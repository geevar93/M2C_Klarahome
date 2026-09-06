#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Exports the API's OpenAPI document and regenerates the Angular client from it.

.DESCRIPTION
    Step 22 of docs/IMPLEMENTATION_PLAN.md, and the CI step described in
    docs/04-api-specification.md section 7:

        build the API  ->  export openapi.json  ->  generate the client  ->  fail on drift

    The export starts the host in-process to read its route table, so it needs the configuration a
    host needs. That is why it is NOT part of an ordinary `dotnet build`: `OpenApiGenerateDocuments`
    defaults to false in KlaraHome.Api.csproj and this script is the only thing that turns it on.
    Nothing is connected to - no database, no Redis, no gateway - because the host is built and
    never started.

    -Check is the gate. It regenerates into a temporary directory and compares; a difference means
    somebody changed an endpoint without regenerating the client, and the build stops. That is what
    makes a breaking API change impossible to merge unnoticed.

.PARAMETER Check
    Verify only. Writes nothing and exits 1 if the committed client is out of date.

.PARAMETER SkipBuild
    Reuse the openapi.json already in artifacts/openapi instead of rebuilding the API. For
    iterating on the generator itself.

.PARAMETER Configuration
    Build configuration for the API project. Debug by default; CI uses Release.

.EXAMPLE
    pwsh tools/generate-api-client.ps1
    Rebuild the document and regenerate the client.

.EXAMPLE
    pwsh tools/generate-api-client.ps1 -Check
    What CI runs. Fails if the committed client has drifted from the contract.
#>
[CmdletBinding()]
param(
    [switch] $Check,
    [switch] $SkipBuild,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repoRoot 'src/backend/host/KlaraHome.Api/KlaraHome.Api.csproj'
$documentPath = Join-Path $repoRoot 'artifacts/openapi/KlaraHome.Api.json'
$generator = Join-Path $repoRoot 'tools/generate-api-client.mjs'

function Write-Step([string] $Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

if (-not $SkipBuild) {
    Write-Step "Exporting the OpenAPI document ($Configuration)"

    # The environment is deliberately NOT forced to Development. The export starts the host
    # in-process to read its route table, and Development turns on ValidateOnBuild, which walks
    # every registration and today finds unregistered services left over from the build sprint
    # (PARKING_LOT: "DI validation fails under ValidateOnBuild"). Fixing those is Step 28A's whole
    # job. The route table - the only thing this step needs - does not depend on any of it.
    $env:ASPNETCORE_ENVIRONMENT = $null

    # The export target is incremental against this cache, not against the document, so a deleted
    # or hand-edited openapi.json would otherwise be quietly left as it was. Removing the cache is
    # what makes "run the script" and "have a current document" the same thing.
    $exportCache = Join-Path (Split-Path -Parent $apiProject) "obj/KlaraHome.Api.OpenApiFiles.cache"
    if (Test-Path $exportCache) { Remove-Item $exportCache -Force }

    dotnet build $apiProject -c $Configuration -p:OpenApiGenerateDocuments=true
    if ($LASTEXITCODE -ne 0) {
        Write-Error 'The API build failed, so no document was exported.'
    }
}

if (-not (Test-Path $documentPath)) {
    Write-Error "No OpenAPI document at $documentPath. Run without -SkipBuild."
}

Write-Step $(if ($Check) { 'Checking the committed client against the document' } else { 'Generating the client' })

$generatorArgs = @($generator)
if ($Check) { $generatorArgs += '--check' }

node @generatorArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host 'OK' -ForegroundColor Green
