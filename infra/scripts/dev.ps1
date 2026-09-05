#Requires -Version 7.0
<#
.SYNOPSIS
    Drives the Klara Home local development stack.

.DESCRIPTION
    Thin wrapper over docker compose so the day-to-day commands are one word.
    Run from anywhere - the script resolves the repository root itself.

.EXAMPLE
    ./infra/scripts/dev.ps1 up
    ./infra/scripts/dev.ps1 logs postgres
    ./infra/scripts/dev.ps1 reset
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('up', 'down', 'restart', 'status', 'logs', 'reset', 'urls')]
    [string]$Command = 'up',

    [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$Rest
)

$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$ComposeFile = Join-Path $RepoRoot 'infra/compose/docker-compose.dev.yml'
$EnvFile = Join-Path $RepoRoot '.env'

$compose = @('compose', '-f', $ComposeFile)
if (Test-Path $EnvFile) {
    $compose += @('--env-file', $EnvFile)
}
else {
    Write-Host 'No .env found - using the defaults baked into the compose file.' -ForegroundColor DarkGray
}

function Invoke-Compose {
    param([string[]]$ComposeArgs)
    Push-Location $RepoRoot
    try {
        & docker @compose @ComposeArgs
        if ($LASTEXITCODE -ne 0) { throw "docker compose exited with $LASTEXITCODE" }
    }
    finally { Pop-Location }
}

function Get-EnvValue {
    param([string]$Key, [string]$Default)
    if (Test-Path $EnvFile) {
        $match = Select-String -Path $EnvFile -Pattern "^\s*$Key\s*=\s*(.+?)\s*$" | Select-Object -First 1
        if ($match) { return $match.Matches[0].Groups[1].Value }
    }
    return $Default
}

function Show-Urls {
    $domain = Get-EnvValue 'DEV_DOMAIN' 'klarahome.localhost'
    $bind = Get-EnvValue 'BIND_ADDRESS' '127.0.0.1'
    $pg = Get-EnvValue 'POSTGRES_PORT' '5432'
    $redis = Get-EnvValue 'REDIS_PORT' '6379'
    $s3 = Get-EnvValue 'MINIO_API_PORT' '9000'
    $console = Get-EnvValue 'MINIO_CONSOLE_PORT' '9001'
    $smtp = Get-EnvValue 'MAILPIT_SMTP_PORT' '1025'
    $mailUi = Get-EnvValue 'MAILPIT_UI_PORT' '8025'

    Write-Host ''
    Write-Host 'Klara Home dev services' -ForegroundColor Cyan
    Write-Host "  Traefik dashboard  https://traefik.$domain"
    Write-Host "  MinIO console      https://minio.$domain   (or http://${bind}:$console)"
    Write-Host "  MinIO S3 API       https://s3.$domain      (or http://${bind}:$s3)"
    Write-Host "  Mailpit            https://mail.$domain    (or http://${bind}:$mailUi)"
    Write-Host "  PostgreSQL         ${bind}:$pg"
    Write-Host "  Redis              ${bind}:$redis"
    Write-Host "  SMTP (Mailpit)     ${bind}:$smtp"
    Write-Host ''
    Write-Host 'The TLS certificate is self-signed - accept the browser warning, or see docs/dev-setup.md.' -ForegroundColor DarkGray
}

switch ($Command) {
    'up' {
        # Two passes on purpose: `--wait` stops as soon as ANY container exits, and
        # minio-init is a one-shot that exits 0. Start everything first, then wait
        # only on the long-running services.
        Invoke-Compose (@('up', '-d', '--remove-orphans') + $Rest)
        Invoke-Compose @('up', '-d', '--no-recreate', '--wait', 'traefik', 'postgres', 'redis', 'minio', 'mailpit')
        Invoke-Compose @('ps')
        Show-Urls
    }
    'down' { Invoke-Compose (@('down', '--remove-orphans') + $Rest) }
    'restart' { Invoke-Compose (@('restart') + $Rest) }
    'status' { Invoke-Compose (@('ps') + $Rest) }
    'logs' { Invoke-Compose (@('logs', '-f', '--tail', '100') + $Rest) }
    'urls' { Show-Urls }
    'reset' {
        Write-Host 'This deletes every dev volume: database, object storage and captured mail.' -ForegroundColor Yellow
        $answer = Read-Host 'Type the word DELETE to continue'
        if ($answer -ceq 'DELETE') {
            Invoke-Compose @('down', '--volumes', '--remove-orphans')
            Write-Host 'Volumes removed. Run `up` to rebuild a clean environment.' -ForegroundColor Green
        }
        else { Write-Host 'Cancelled.' }
    }
}
