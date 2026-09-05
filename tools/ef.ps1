<#
.SYNOPSIS
  EF Core migration helper for the Klara Home modular monolith.

.DESCRIPTION
  Every module owns its own DbContext and its own migration history, so `dotnet ef` needs three
  arguments every time and gets them wrong quietly if you guess. This script derives them from a
  module name.

  Modules are registered in $Contexts below; adding a module means adding one line there.

.EXAMPLE
  ./tools/ef.ps1 add Platform AddTenantTable
    Creates a migration in the Platform module.

.EXAMPLE
  ./tools/ef.ps1 script Platform
    Writes an idempotent SQL script to artifacts/migrations/. This is the file reviewed in the
    PR — docs/03-database-design.md §7 requires the SQL to be read, not just the C#.

.EXAMPLE
  ./tools/ef.ps1 list Platform
    Lists the module's migrations and which are applied.

.EXAMPLE
  ./tools/ef.ps1 remove Platform
    Removes the most recent migration, if it has not been applied.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('add', 'remove', 'script', 'list')]
    [string]$Command,

    [Parameter(Mandatory, Position = 1)]
    [string]$Module,

    [Parameter(Position = 2)]
    [string]$Name
)

$ErrorActionPreference = 'Stop'

# module -> context type. One line per module.
$Contexts = @{
    'Platform' = 'PlatformDbContext'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$backend = Join-Path $repoRoot 'src/backend'

if (-not $Contexts.ContainsKey($Module)) {
    throw "Unknown module '$Module'. Known: $($Contexts.Keys -join ', '). Add it to `$Contexts in tools/ef.ps1."
}

$context = $Contexts[$Module]
$project = Join-Path $backend "modules/KlaraHome.Modules.$Module"

if (-not (Test-Path $project)) {
    throw "Module project not found: $project"
}

# Migrations live beside the DbContext, inside the module's Infrastructure layer.
$outputDir = 'Infrastructure/Persistence/Migrations'
$common = @('--project', $project, '--context', $context)

Push-Location $backend
try {
    switch ($Command) {
        'add' {
            if ([string]::IsNullOrWhiteSpace($Name)) {
                throw 'A migration name is required: ./tools/ef.ps1 add <Module> <MigrationName>'
            }
            dotnet dotnet-ef migrations add $Name @common --output-dir $outputDir
        }
        'remove' {
            dotnet dotnet-ef migrations remove @common
        }
        'list' {
            dotnet dotnet-ef migrations list @common
        }
        'script' {
            $artifacts = Join-Path $repoRoot 'artifacts/migrations'
            New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

            $stamp = Get-Date -Format 'yyyyMMddHHmmss'
            $output = Join-Path $artifacts "$Module-$stamp.sql"

            # --idempotent guards every statement with a check against the history table, so the
            # same file can be applied to a database at any migration level. That is what makes it
            # safe to hand to a DBA who does not know which version production is on.
            dotnet dotnet-ef migrations script @common --idempotent --output $output
            Write-Host "Idempotent script written to $output" -ForegroundColor Green
        }
    }

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet ef exited with $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
