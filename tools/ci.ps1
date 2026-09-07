#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the Klara Home quality gates - the same ones CI runs, in the same order.

.DESCRIPTION
    Step 5 of docs/IMPLEMENTATION_PLAN.md. This script IS the pipeline: .github/workflows/ci.yml
    calls it stage by stage rather than restating the commands, so "it passed locally" and "it
    passed in CI" cannot mean two different things.

    Stages, in dependency order:

      restore    dotnet restore with --locked-mode, and npm ci
      build      dotnet build -c Release with ContinuousIntegrationBuild=true, so every analyzer
                 warning is an error (Directory.Build.props conditions that on CI)
      format     dotnet format --verify-no-changes, and prettier --check
      test       the three backend suites, with coverage, and the frontend unit tests
      lint       eslint across every Nx project
      audit      npm audit, and dotnet list package --vulnerable
      codegen    re-export the OpenAPI document; fail if the committed API client has drifted
      frontend   the storefront and admin production builds
      package    docker build of the api and migrator images, then a Trivy scan

    Backend tests are run BY EXECUTING THE TEST EXECUTABLES DIRECTLY, not through `dotnet test`.
    That is deliberate and documented in docs/dev-setup.md section 7: the Microsoft.Testing.Platform
    orchestrator reaches the test host over a loopback JSON-RPC connection, and when an
    endpoint-security agent blocks that connection it reports "Zero tests ran" and exit code 0 - a
    silent pass on a suite that never ran. Running the executable is the same runner without the
    RPC hop. On top of that, this script asserts a MINIMUM TEST COUNT per suite, so a suite that
    somehow discovers nothing fails the build instead of passing it.

.PARAMETER Stage
    Which stage to run. 'all' runs every stage in order. Repeatable.

.PARAMETER CoverageMinimum
    Minimum total line coverage percentage over our own assemblies. Defaults to 70, the figure
    agreed in docs/09-nfr-testing-observability.md section 1.4.

    The default is deliberately still 70 even though the build sprint left coverage far below it.
    Measured at Step 28A over the two suites that run without Docker: 15.84% line, 29.08% branch,
    on 941 unit tests and 14 architecture tests. Step 29 restores it to 70% by paying down
    TEST_DEBT.md; until then a full local sweep needs -CoverageMinimum passed on the command line,
    never lowered here.

.PARAMETER SkipIntegrationTests
    Skip the suites that need a Docker daemon. They skip themselves gracefully, but this makes the
    intent explicit and keeps the test-count assertion honest.

.EXAMPLE
    ./tools/ci.ps1
    Everything except `package`, which needs Docker and Trivy.

.EXAMPLE
    ./tools/ci.ps1 -Stage format,test
    Just the gates a code change is most likely to trip.
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'restore', 'build', 'format', 'test', 'lint', 'audit', 'codegen', 'frontend', 'package')]
    [string[]] $Stage = @('all'),

    [ValidateRange(0, 100)]
    [double] $CoverageMinimum = 70,

    [switch] $SkipIntegrationTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

# -----------------------------------------------------------------------------------------------
# Layout and configuration
# -----------------------------------------------------------------------------------------------

$RepoRoot = Split-Path -Parent $PSScriptRoot
$BackendDir = Join-Path $RepoRoot 'src/backend'
$FrontendDir = Join-Path $RepoRoot 'src/frontend'
$ArtifactsDir = Join-Path $RepoRoot 'artifacts'
$CoverageDir = Join-Path $ArtifactsDir 'coverage'
$TestResultsDir = Join-Path $ArtifactsDir 'test-results'

# Suite name -> the smallest number of tests that suite is allowed to report. Not a target: a
# tripwire. It exists so a suite that silently discovers nothing cannot be mistaken for a pass.
# Raise these when a step adds tests; never lower one to make a build go green.
#
# Restored at Step 28A to the counts the sprint actually ended on. They were frozen for Steps 9-28
# (IMPLEMENTATION_PLAN.md section 3.2 rule 6) because the sprint wrote production code and deferred
# tests, so a rising floor would have failed every step. The integration floor is unchanged: that
# suite is Step 29's work and has not grown since Step 8.
$TestSuites = [ordered]@{
    'UnitTests'         = @{ Minimum = 941; NeedsDocker = $false }
    'ArchitectureTests' = @{ Minimum = 14; NeedsDocker = $false }
    'IntegrationTests'  = @{ Minimum = 180; NeedsDocker = $true }
}

$DockerImages = [ordered]@{
    'klarahome/api'      = 'infra/docker/api.Dockerfile'
    'klarahome/worker'   = 'infra/docker/worker.Dockerfile'
    'klarahome/migrator' = 'infra/docker/migrator.Dockerfile'
}

# The VS Code JavaScript debugger injects a --require bootloader through NODE_OPTIONS, which
# attaches a debugger to every node process and writes to stdout. Anything that parses npm or Nx
# output then sees the bootloader's chatter as data. Cleared for the whole run.
$env:NODE_OPTIONS = ''
# npm ci and Nx are noisy about being non-interactive otherwise.
$env:CI = 'true'
$env:DOTNET_COVERAGE_NOLOGO = '1'
$env:DOTNET_NOLOGO = '1'

$script:Failures = @()
$script:StageResults = [ordered]@{}

# -----------------------------------------------------------------------------------------------
# Output helpers
# -----------------------------------------------------------------------------------------------

$script:InGitHubActions = [bool] $env:GITHUB_ACTIONS

function Write-Stage([string] $Name, [string] $Description) {
    if ($script:InGitHubActions) {
        Write-Host "::group::$Name - $Description"
    }
    else {
        Write-Host ''
        Write-Host ('=' * 96) -ForegroundColor DarkCyan
        Write-Host ("  {0,-10} {1}" -f $Name.ToUpperInvariant(), $Description) -ForegroundColor Cyan
        Write-Host ('=' * 96) -ForegroundColor DarkCyan
    }
}

function Write-StageEnd {
    if ($script:InGitHubActions) { Write-Host '::endgroup::' }
}

function Write-Ok([string] $Message) { Write-Host "  PASS  $Message" -ForegroundColor Green }
function Write-Note([string] $Message) { Write-Host "  ..    $Message" -ForegroundColor DarkGray }

function Write-Fail([string] $Message) {
    $script:Failures += $Message
    if ($script:InGitHubActions) { Write-Host "::error::$Message" }
    Write-Host "  FAIL  $Message" -ForegroundColor Red
}

# Runs a native command and throws on a non-zero exit code, naming the command that failed rather
# than leaving a bare exit code in the log.
function Invoke-Step {
    param(
        [Parameter(Mandatory)] [string] $Label,
        [Parameter(Mandatory)] [string] $Command,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [string] $WorkingDirectory = $RepoRoot,
        [int[]] $AllowExitCodes = @(0)
    )

    Write-Note "$Label`: $Command $($Arguments -join ' ')"
    Push-Location $WorkingDirectory
    try {
        # Out-Host, not the pipeline: the tool's own output belongs on the console where a developer
        # reads it, and the only thing this function returns is the exit code. Without this, a
        # caller assigning the result captures every line the tool printed along with it.
        & $Command @Arguments 2>&1 | Out-Host
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($AllowExitCodes -notcontains $code) {
        throw "$Label failed (exit $code): $Command $($Arguments -join ' ')"
    }
    return $code
}

function Test-CommandExists([string] $Name) {
    return [bool] (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Test-DockerRunning {
    if (-not (Test-CommandExists 'docker')) { return $false }
    & docker info --format '{{.ServerVersion}}' *> $null
    return $LASTEXITCODE -eq 0
}

# -----------------------------------------------------------------------------------------------
# TRX and cobertura readers
# -----------------------------------------------------------------------------------------------

# Reads the <Counters> element a TRX carries. The count is the point: a suite reporting zero tests
# is the failure mode this whole approach exists to catch.
function Get-TrxCounters([string] $Path) {
    [xml] $trx = Get-Content -LiteralPath $Path -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if (-not $counters) { throw "No <Counters> element in $Path - the TRX is malformed." }

    return [pscustomobject]@{
        Total    = [int] $counters.total
        Executed = [int] $counters.executed
        Passed   = [int] $counters.passed
        Failed   = [int] $counters.failed
        Errors   = [int] $counters.error
        Skipped  = [int] $counters.total - [int] $counters.executed
    }
}

# Cobertura attributes are read through GetAttribute rather than property access. A report from a
# suite with no branches at all omits `branches-covered` entirely, and under Set-StrictMode a
# missing property is a terminating error - so the reader would crash on exactly the narrow run it
# is meant to describe. GetAttribute returns an empty string instead, which casts to zero.
function Get-XmlNumber($Element, [string] $Name) {
    $raw = $Element.GetAttribute($Name)
    if ([string]::IsNullOrWhiteSpace($raw)) { return [double] 0 }
    return [double] $raw
}

function Get-CoberturaSummary([string] $Path) {
    [xml] $doc = Get-Content -LiteralPath $Path -Raw
    $root = $doc.DocumentElement

    $packages = @()
    foreach ($p in $root.SelectNodes('packages/package')) {
        $packages += [pscustomobject]@{
            Name       = $p.GetAttribute('name')
            LineRate   = Get-XmlNumber $p 'line-rate'
            BranchRate = Get-XmlNumber $p 'branch-rate'
        }
    }

    return [pscustomobject]@{
        LinesCovered    = [int] (Get-XmlNumber $root 'lines-covered')
        LinesValid      = [int] (Get-XmlNumber $root 'lines-valid')
        BranchesCovered = [int] (Get-XmlNumber $root 'branches-covered')
        BranchesValid   = [int] (Get-XmlNumber $root 'branches-valid')
        LineRate        = Get-XmlNumber $root 'line-rate'
        BranchRate      = Get-XmlNumber $root 'branch-rate'
        Packages        = $packages | Sort-Object Name
    }
}

# -----------------------------------------------------------------------------------------------
# Stages
# -----------------------------------------------------------------------------------------------

function Invoke-RestoreStage {
    Write-Stage 'restore' 'dotnet restore --locked-mode, dotnet tool restore, npm ci'

    # --locked-mode is enforced HERE and deliberately NOT in the Docker image builds. Step 1 set
    # RestorePackagesWithLockFile, and the image builds restore with a runtime identifier, which
    # produces a different (RID-specific) lock file. Enforcing it in both places would mean
    # committing two lock files per project that must be kept in step by hand. CI is the right
    # place for the guarantee: if a transitive version drifts, this restore fails.
    $null = Invoke-Step -Label 'dotnet restore' -Command 'dotnet' -WorkingDirectory $BackendDir `
        -Arguments @('restore', '--locked-mode')

    $null = Invoke-Step -Label 'dotnet tool restore' -Command 'dotnet' -Arguments @('tool', 'restore')

    $null = Invoke-Step -Label 'npm ci' -Command 'npm' -WorkingDirectory $FrontendDir `
        -Arguments @('ci', '--no-audit', '--no-fund')

    Write-Ok 'dependencies restored from lock files'
    Write-StageEnd
}

function Invoke-BuildStage {
    Write-Stage 'build' 'dotnet build -c Release, analyzer warnings as errors'

    # ContinuousIntegrationBuild=true is what flips TreatWarningsAsErrors on
    # (src/backend/Directory.Build.props). Set explicitly rather than relying on the CI env var, so
    # a developer running this script locally gets exactly the same verdict.
    $null = Invoke-Step -Label 'dotnet build' -Command 'dotnet' -WorkingDirectory $BackendDir `
        -Arguments @(
        'build',
        '--configuration', 'Release',
        '--no-restore',
        '-p:ContinuousIntegrationBuild=true',
        '-warnaserror'
    )

    Write-Ok 'backend builds clean with analyzers as errors'
    Write-StageEnd
}

function Invoke-FormatStage {
    Write-Stage 'format' 'dotnet format --verify-no-changes, prettier --check'

    $formatFailed = $false
    try {
        $null = Invoke-Step -Label 'dotnet format' -Command 'dotnet' -WorkingDirectory $BackendDir `
            -Arguments @('format', '--verify-no-changes', '--no-restore')
    }
    catch {
        $formatFailed = $true
        Write-Fail 'dotnet format found unformatted C#. Run: dotnet format (in src/backend)'
    }
    if (-not $formatFailed) { Write-Ok 'C# formatting and code style clean' }

    $prettierFailed = $false
    try {
        $null = Invoke-Step -Label 'prettier' -Command 'npx' -WorkingDirectory $FrontendDir `
            -Arguments @('prettier', '--check', '.')
    }
    catch {
        $prettierFailed = $true
        Write-Fail 'prettier found unformatted files. Run: npx prettier --write . (in src/frontend)'
    }
    if (-not $prettierFailed) { Write-Ok 'frontend formatting clean' }

    Write-StageEnd
}

function Invoke-TestStage {
    Write-Stage 'test' 'backend suites with coverage, then the frontend unit tests'

    foreach ($dir in @($CoverageDir, $TestResultsDir)) {
        if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    $dockerUp = Test-DockerRunning
    if (-not $dockerUp -and -not $SkipIntegrationTests) {
        Write-Fail 'No Docker daemon: the Testcontainers suites cannot run. Start Docker, or pass -SkipIntegrationTests to accept the gap.'
    }

    $coberturaFiles = @()
    $skippedSuites = @()
    $totalTests = 0

    foreach ($suite in $TestSuites.Keys) {
        $config = $TestSuites[$suite]

        if ($config.NeedsDocker -and ($SkipIntegrationTests -or -not $dockerUp)) {
            Write-Note "$suite skipped (needs Docker)"
            $skippedSuites += $suite
            continue
        }

        $exeName = if ($IsWindows) { "KlaraHome.$suite.exe" } else { "KlaraHome.$suite" }
        $exePath = Join-Path $BackendDir "tests/KlaraHome.$suite/bin/Release/net10.0/$exeName"
        if (-not (Test-Path $exePath)) {
            Write-Fail "$suite executable not found at $exePath - run the build stage first."
            continue
        }

        $cobertura = Join-Path $CoverageDir "$suite.cobertura.xml"
        $trx = Join-Path $TestResultsDir "$suite.trx"

        # dotnet-coverage wraps the process; everything after the bare `--` is the suite's own
        # command line, so -result-trx is xunit's option and not the collector's.
        $exitCode = Invoke-Step -Label "$suite" -Command 'dotnet' -WorkingDirectory $BackendDir `
            -AllowExitCodes @(0, 1) `
            -Arguments @(
            'dotnet-coverage', 'collect',
            '--settings', 'coverage.settings.xml',
            '--output-format', 'cobertura',
            '--output', $cobertura,
            '--', $exePath, '-result-trx', $trx
        )

        if (-not (Test-Path $trx)) {
            Write-Fail "$suite produced no TRX. The suite did not run - treat this as a failure, never as an empty pass."
            continue
        }

        $counters = Get-TrxCounters $trx

        if ($counters.Failed -gt 0 -or $counters.Errors -gt 0 -or $exitCode -ne 0) {
            Write-Fail "${suite}: $($counters.Failed) failed, $($counters.Errors) errored of $($counters.Total)"
            continue
        }

        if ($counters.Total -lt $config.Minimum) {
            Write-Fail "${suite} reported only $($counters.Total) tests, below the floor of $($config.Minimum). A suite that discovers nothing must not pass - see docs/dev-setup.md section 7."
            continue
        }

        $totalTests += $counters.Total
        if (Test-Path $cobertura) { $coberturaFiles += $cobertura }
        Write-Ok "${suite}: $($counters.Passed) passed, $($counters.Skipped) skipped"
    }

    $script:StageResults['Backend tests'] = "$totalTests passed"

    if ($coberturaFiles.Count -gt 0) {
        # A partial run cannot produce a verdict on coverage: without the integration suites the
        # figure drops to roughly half, and failing the build on that would teach developers that
        # the number is noise. Report it, say plainly that it is not the whole picture, and leave
        # the gate to the run that actually measured everything.
        $partial = $skippedSuites.Count -gt 0
        Test-Coverage -CoberturaFiles $coberturaFiles -Advisory:$partial
        if ($partial) {
            Write-Host ("  NOTE  coverage not enforced: {0} did not run. This is not a full verdict." -f ($skippedSuites -join ', ')) -ForegroundColor Yellow
        }
    }
    else {
        Write-Fail 'No coverage data collected - the threshold cannot be checked.'
    }

    Write-Note 'frontend unit tests'
    try {
        $null = Invoke-Step -Label 'nx test' -Command 'npx' -WorkingDirectory $FrontendDir `
            -Arguments @('nx', 'run-many', '--target=test', '--all', '--configuration=ci')
        Write-Ok 'frontend unit tests pass'
    }
    catch {
        Write-Fail 'frontend unit tests failed'
    }

    Write-StageEnd
}

function Test-Coverage {
    param(
        [Parameter(Mandatory)] [string[]] $CoberturaFiles,
        # Measure and report, but do not fail. Used when some suites did not run, so the figure is
        # real but not comparable to the threshold.
        [switch] $Advisory
    )

    $merged = Join-Path $CoverageDir 'merged.cobertura.xml'

    if ($CoberturaFiles.Count -eq 1) {
        Copy-Item $CoberturaFiles[0] $merged -Force
    }
    else {
        $null = Invoke-Step -Label 'merge coverage' -Command 'dotnet' `
            -Arguments (@('dotnet-coverage', 'merge', '--output', $merged, '--output-format', 'cobertura') + $CoberturaFiles)
    }

    $summary = Get-CoberturaSummary $merged
    $linePct = [math]::Round($summary.LineRate * 100, 2)
    $branchPct = [math]::Round($summary.BranchRate * 100, 2)

    Write-Host ''
    Write-Host ("  {0,-34} {1,8} {2,8}" -f 'Assembly', 'line %', 'branch %')
    Write-Host ("  {0}" -f ('-' * 52))
    foreach ($p in $summary.Packages) {
        Write-Host ("  {0,-34} {1,8:N1} {2,8:N1}" -f $p.Name,
            [math]::Round($p.LineRate * 100, 1), [math]::Round($p.BranchRate * 100, 1))
    }
    Write-Host ("  {0}" -f ('-' * 52))
    Write-Host ("  {0,-34} {1,8:N1} {2,8:N1}" -f 'TOTAL', $linePct, $branchPct) -ForegroundColor Cyan
    Write-Host ("  {0} of {1} lines, {2} of {3} branches" -f
        $summary.LinesCovered, $summary.LinesValid, $summary.BranchesCovered, $summary.BranchesValid) -ForegroundColor DarkGray
    Write-Host ''

    $script:StageResults['Line coverage'] = if ($Advisory) {
        "$linePct% (advisory: not every suite ran)"
    }
    else {
        "$linePct% (minimum $CoverageMinimum%)"
    }
    $script:StageResults['Branch coverage'] = "$branchPct%"

    if ($Advisory) {
        Write-Note "line coverage $linePct% measured, threshold not applied to a partial run"
    }
    elseif ($linePct -lt $CoverageMinimum) {
        Write-Fail "Line coverage $linePct% is below the agreed minimum of $CoverageMinimum% (docs/09-nfr-testing-observability.md section 1.4)."
    }
    else {
        Write-Ok "line coverage $linePct% meets the $CoverageMinimum% minimum"
    }

    if ($script:InGitHubActions -and $env:GITHUB_STEP_SUMMARY) {
        $lines = @(
            '### Backend coverage',
            '',
            '| Assembly | Line % | Branch % |',
            '| --- | --: | --: |'
        )
        foreach ($p in $summary.Packages) {
            $lines += ('| {0} | {1:N1} | {2:N1} |' -f $p.Name,
                [math]::Round($p.LineRate * 100, 1), [math]::Round($p.BranchRate * 100, 1))
        }
        $lines += ('| **Total** | **{0:N2}** | **{1:N2}** |' -f $linePct, $branchPct)
        $lines += ''
        $lines += "Minimum enforced: **$CoverageMinimum %** line coverage."
        $lines -join "`n" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    }
}

function Invoke-LintStage {
    Write-Stage 'lint' 'eslint across every Nx project, module boundaries included'

    try {
        $null = Invoke-Step -Label 'nx lint' -Command 'npx' -WorkingDirectory $FrontendDir `
            -Arguments @('nx', 'run-many', '--target=lint', '--all')
        Write-Ok 'eslint clean, module boundaries hold'
    }
    catch {
        Write-Fail 'eslint reported problems'
    }

    Write-StageEnd
}

function Invoke-AuditStage {
    Write-Stage 'audit' 'npm audit, dotnet list package --vulnerable'

    # The gate is high and critical, which is the wording of the Definition of Done in
    # docs/09-nfr-testing-observability.md section 2.4. Moderates are reported, not fatal: they are
    # triaged at a step boundary rather than blocking every unrelated commit.
    try {
        $null = Invoke-Step -Label 'npm audit' -Command 'npm' -WorkingDirectory $FrontendDir `
            -Arguments @('audit', '--audit-level=high')
        Write-Ok 'no high or critical npm advisories'
    }
    catch {
        Write-Fail 'npm audit found high or critical advisories'
    }

    # `dotnet list package --vulnerable` reports through stdout and still exits 0, so the output has
    # to be read rather than the exit code trusted.
    Write-Note 'dotnet list package --vulnerable --include-transitive'
    Push-Location $BackendDir
    try {
        $output = & dotnet list package --vulnerable --include-transitive 2>&1 | Out-String
    }
    finally {
        Pop-Location
    }

    Write-Host $output
    if ($output -match 'has the following vulnerable packages') {
        if ($output -match '\b(High|Critical)\b') {
            Write-Fail 'NuGet packages with high or critical advisories are referenced'
        }
        else {
            Write-Host '  NOTE  vulnerable NuGet packages found, none high or critical' -ForegroundColor Yellow
        }
    }
    else {
        Write-Ok 'no vulnerable NuGet packages'
    }

    Write-StageEnd
}

function Invoke-CodegenStage {
    Write-Stage 'codegen' 'the committed API client still matches the OpenAPI document'

    # The gate described in docs/04-api-specification.md section 7. It runs after 'build', because
    # exporting the document needs the API assembly, and before 'frontend', because a drifted
    # client is a compile error there and a much clearer message here.
    #
    # A failure means somebody changed an endpoint and did not regenerate the client. That is the
    # point: it makes a breaking API change impossible to merge unnoticed.
    try {
        $null = Invoke-Step -Label 'openapi codegen' -Command 'pwsh' -WorkingDirectory $RepoRoot `
            -Arguments @(
            '-NoProfile',
            '-File', (Join-Path $PSScriptRoot 'generate-api-client.ps1'),
            '-Check',
            '-Configuration', 'Release'
        )
        Write-Ok 'the generated API client matches the current contract'
        $script:StageResults['API client'] = 'in sync with the OpenAPI document'
    }
    catch {
        Write-Fail 'the committed API client has drifted. Run: pwsh tools/generate-api-client.ps1'
    }

    Write-StageEnd
}

function Invoke-FrontendStage {
    Write-Stage 'frontend' 'production builds of storefront and admin, then the performance budgets'

    $built = $false
    try {
        $null = Invoke-Step -Label 'nx build' -Command 'npx' -WorkingDirectory $FrontendDir `
            -Arguments @('nx', 'run-many', '--target=build', '--projects=storefront,admin', '--configuration=production')
        Write-Ok 'both Angular apps build for production'
        $built = $true
    }
    catch {
        Write-Fail 'an Angular production build failed'
    }

    if ($built) {
        # The budgets from 05-frontend-architecture.md section 3.4. They are checked here rather
        # than through Angular's own `budgets` for one reason: the document states them GZIPPED and
        # Angular measures RAW bytes, so a literal 180kb budget in project.json would fail a build
        # that is comfortably inside the real limit. A budget that fires spuriously gets raised
        # until it never fires, which is worse than no budget at all.
        Test-BundleBudget -App 'storefront' -InitialKb 180 -RouteChunkKb 80
        Test-BundleBudget -App 'admin' -InitialKb 300 -RouteChunkKb 120
    }

    Write-StageEnd
}

<#
.SYNOPSIS
    Fails the build when an app's gzipped JavaScript exceeds its budget.
.DESCRIPTION
    "Initial" is what the browser must download before the first route can render: the scripts the
    entry document references plus the chunks it preloads. Angular writes both into index.html --
    <script src> for the entry points and <link rel="modulepreload"> for the shared chunks they
    import -- so parsing that file is an exact answer rather than an approximation.

    Everything else in the browser output is a route chunk, and each is measured on its own: the
    sum does not matter, because no visitor downloads every route.
.PARAMETER App
    The Nx project name; its output is expected under dist/apps/<app>/browser.
.PARAMETER InitialKb
    The initial-bundle budget, in gzipped kilobytes.
.PARAMETER RouteChunkKb
    The per-route-chunk budget, in gzipped kilobytes.
#>
function Test-BundleBudget {
    param(
        [Parameter(Mandatory)] [string] $App,
        [Parameter(Mandatory)] [int] $InitialKb,
        [Parameter(Mandatory)] [int] $RouteChunkKb
    )

    $browserDir = Join-Path $FrontendDir "dist/apps/$App/browser"
    # An SSR build writes index.csr.html (the client-side shell); a plain SPA build writes index.html.
    $entryDocument = @('index.csr.html', 'index.html') |
        ForEach-Object { Join-Path $browserDir $_ } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    if (-not $entryDocument) {
        Write-Fail "$App bundle budget: no entry document under $browserDir - the build output is not where it is expected."
        return
    }

    $html = Get-Content -Raw -LiteralPath $entryDocument
    $initialFiles = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($pattern in @('<script[^>]+src="([^"]+\.js)"', '<link[^>]+rel="modulepreload"[^>]+href="([^"]+\.js)"')) {
        foreach ($match in [regex]::Matches($html, $pattern)) {
            $null = $initialFiles.Add(($match.Groups[1].Value -replace '^/', ''))
        }
    }

    if ($initialFiles.Count -eq 0) {
        Write-Fail "$App bundle budget: no scripts found in $(Split-Path -Leaf $entryDocument) - the check would pass vacuously."
        return
    }

    $initialBytes = 0
    foreach ($file in $initialFiles) {
        $initialBytes += Get-GzipSize (Join-Path $browserDir $file)
    }

    # Named so it cannot collide with the $InitialKb parameter: PowerShell variable names are
    # case-insensitive, so $initialKb and $InitialKb are one variable, and the budget would be
    # overwritten by the measurement -- a check that always passes and always looks right.
    $measuredKb = [math]::Round($initialBytes / 1KB, 1)
    if ($measuredKb -gt $InitialKb) {
        Write-Fail "$App initial JavaScript is ${measuredKb} kB gzipped, over the ${InitialKb} kB budget (05-frontend-architecture.md 3.4)."
    }
    else {
        Write-Ok "$App initial JavaScript ${measuredKb} kB gzipped (budget ${InitialKb} kB)"
    }
    $script:StageResults["$App initial JS"] = "$measuredKb kB gzipped"

    $worstChunk = 0.0
    $worstName = ''
    foreach ($chunk in Get-ChildItem -LiteralPath $browserDir -Filter '*.js' -File) {
        if ($initialFiles.Contains($chunk.Name)) { continue }
        $chunkKb = [math]::Round((Get-GzipSize $chunk.FullName) / 1KB, 1)
        if ($chunkKb -gt $worstChunk) {
            $worstChunk = $chunkKb
            $worstName = $chunk.Name
        }
        if ($chunkKb -gt $RouteChunkKb) {
            Write-Fail "$App route chunk $($chunk.Name) is ${chunkKb} kB gzipped, over the ${RouteChunkKb} kB budget."
        }
    }

    if ($worstName) {
        Write-Ok "$App largest route chunk ${worstChunk} kB gzipped (budget ${RouteChunkKb} kB)"
    }
}

<# Compresses a file in memory and answers the number of bytes it would be sent as. #>
function Get-GzipSize {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) { return 0 }

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $buffer = [System.IO.MemoryStream]::new()
    try {
        $gzip = [System.IO.Compression.GZipStream]::new($buffer, [System.IO.Compression.CompressionLevel]::Optimal, $true)
        try { $gzip.Write($bytes, 0, $bytes.Length) } finally { $gzip.Dispose() }
        return $buffer.Length
    }
    finally {
        $buffer.Dispose()
    }
}

function Invoke-PackageStage {
    Write-Stage 'package' 'docker build of the api and migrator images, then Trivy'

    if (-not (Test-DockerRunning)) {
        Write-Fail 'No Docker daemon: images cannot be built or scanned.'
        Write-StageEnd
        return
    }

    $gitSha = (& git -C $RepoRoot rev-parse --short HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $gitSha) { $gitSha = 'unknown' }

    $built = @()
    foreach ($image in $DockerImages.Keys) {
        $dockerfile = $DockerImages[$image]
        $tag = "${image}:ci"
        try {
            # Build context is the repository root: the Dockerfiles need global.json, .editorconfig
            # and the whole src/backend tree.
            $null = Invoke-Step -Label "docker build $image" -Command 'docker' -Arguments @(
                'build', '--file', $dockerfile, '--tag', $tag,
                '--build-arg', "GIT_SHA=$gitSha", '.'
            )
            $built += $tag
            Write-Ok "$tag built"
        }
        catch {
            Write-Fail "docker build failed for $image"
        }
    }

    if (-not (Test-CommandExists 'trivy')) {
        Write-Host '  NOTE  trivy is not installed locally, so the image scan is skipped here. CI always runs it.' -ForegroundColor Yellow
        Write-StageEnd
        return
    }

    foreach ($tag in $built) {
        try {
            $null = Invoke-Step -Label "trivy $tag" -Command 'trivy' -Arguments @(
                'image', '--severity', 'HIGH,CRITICAL', '--ignore-unfixed',
                '--exit-code', '1', '--no-progress', $tag
            )
            Write-Ok "$tag has no fixable high or critical vulnerabilities"
        }
        catch {
            Write-Fail "trivy found high or critical vulnerabilities in $tag"
        }
    }

    Write-StageEnd
}

# -----------------------------------------------------------------------------------------------
# Driver
# -----------------------------------------------------------------------------------------------

# 'package' is out of 'all' on purpose: two container image builds are minutes of work that a
# developer checking a code change does not want, and CI runs it as its own job regardless.
$AllStages = @('restore', 'build', 'format', 'test', 'lint', 'audit', 'codegen', 'frontend')

$requested = if ($Stage -contains 'all') { $AllStages } else {
    # Keep the canonical order however the flags were passed - later stages assume earlier ones.
    @($AllStages + 'package') | Where-Object { $Stage -contains $_ }
}

$started = Get-Date
Write-Host ''
Write-Host 'Klara Home quality gates' -ForegroundColor White
Write-Host "  repository : $RepoRoot" -ForegroundColor DarkGray
Write-Host "  stages     : $($requested -join ', ')" -ForegroundColor DarkGray
Write-Host "  coverage   : minimum $CoverageMinimum% line" -ForegroundColor DarkGray

foreach ($name in $requested) {
    try {
        switch ($name) {
            'restore' { Invoke-RestoreStage }
            'build' { Invoke-BuildStage }
            'format' { Invoke-FormatStage }
            'test' { Invoke-TestStage }
            'lint' { Invoke-LintStage }
            'audit' { Invoke-AuditStage }
            'codegen' { Invoke-CodegenStage }
            'frontend' { Invoke-FrontendStage }
            'package' { Invoke-PackageStage }
        }
    }
    catch {
        # A stage that throws rather than reporting is still a failure, not a crash: record it and
        # keep going, so one run tells the developer everything that is wrong.
        Write-Fail "$name stage: $($_.Exception.Message)"
        Write-StageEnd
    }
}

$elapsed = (Get-Date) - $started

Write-Host ''
Write-Host ('=' * 96) -ForegroundColor DarkCyan
foreach ($key in $script:StageResults.Keys) {
    Write-Host ("  {0,-18} {1}" -f $key, $script:StageResults[$key]) -ForegroundColor DarkGray
}
Write-Host ("  {0,-18} {1:mm\:ss}" -f 'Elapsed', $elapsed) -ForegroundColor DarkGray
Write-Host ('=' * 96) -ForegroundColor DarkCyan

if ($script:Failures.Count -gt 0) {
    Write-Host ''
    Write-Host "FAILED - $($script:Failures.Count) problem(s):" -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  * $f" -ForegroundColor Red }
    Write-Host ''
    exit 1
}

Write-Host ''
Write-Host 'All requested gates passed.' -ForegroundColor Green
Write-Host ''
exit 0
