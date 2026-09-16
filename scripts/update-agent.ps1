param(
    [switch]$NoPrompt
)

$ErrorActionPreference = 'Stop'

$bundleDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceExecutable = Join-Path $bundleDirectory 'ClubPay.Agent.Client.exe'
$installDirectory = 'C:\ClubPay\Agent'
$installedExecutable = Join-Path $installDirectory 'ClubPay.Agent.Client.exe'
$installedConfig = Join-Path $installDirectory 'appsettings.Local.json'

if (-not (Test-Path $sourceExecutable)) {
    throw "ClubPay Agent was not found next to the updater: $sourceExecutable"
}
if (-not (Test-Path $installedExecutable)) {
    throw 'Configured ClubPay Agent was not found. Use the per-PC first-install file from ClubPay Web Admin.'
}
if (-not (Test-Path $installedConfig)) {
    throw 'The Agent configuration is missing. Use the per-PC first-install file from ClubPay Web Admin.'
}

Write-Host 'Updating ClubPay Agent…' -ForegroundColor Cyan

# The enrollment is the identity of this physical PC. Back it up before files
# are replaced; the updater must never turn a configured kiosk into an
# unbound Agent.
$configBackup = Join-Path $env:TEMP ('clubpay-agent-config-' + [guid]::NewGuid().ToString() + '.json')
# Retain the last known good build beside the Agent. It is independent of the
# downloaded release and remains available without Internet access.
$programBackup = Join-Path $installDirectory 'updates\versions\previous'
if (Test-Path $programBackup) { Remove-Item -Path $programBackup -Recurse -Force }
Copy-Item -Path $installedConfig -Destination $configBackup -Force
New-Item -ItemType Directory -Path $programBackup -Force | Out-Null
Get-ChildItem -Path $installDirectory -Force | Where-Object {
    $_.Name -notin @('appsettings.Local.json', 'updates', 'runtime')
} | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination $programBackup -Recurse -Force
}
try {
    Get-Process -Name 'ClubPay.Agent.Client' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1

    Copy-Item -Path (Join-Path $bundleDirectory '*') -Destination $installDirectory -Recurse -Force
    Copy-Item -Path $configBackup -Destination $installedConfig -Force

    # The first failover-capable package may replace an older Agent whose local
    # config only knew its current Controller and Cloud.  The authenticated
    # Cloud bootstrap is the source of truth for this PC's primary + Manager
    # pair, so hydrate it here before the replacement reconnects.  This is
    # deliberately best-effort: a network outage must roll back neither a
    # healthy binary update nor a known-good endpoint configuration.
    try {
        $config = Get-Content -LiteralPath $installedConfig -Raw | ConvertFrom-Json
        $controller = $config.Controller
        $externalPcId = ([string]$controller.ExternalPcId).Trim()
        $agentToken = ([string]$controller.AgentToken).Trim()
        $bootstrapUrls = @()
        if (-not [string]::IsNullOrWhiteSpace([string]$controller.BootstrapUrl)) { $bootstrapUrls += [string]$controller.BootstrapUrl }
        foreach ($item in @($controller.FallbackBootstrapUrls)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$item)) { $bootstrapUrls += [string]$item }
        }
        # Cloud is included explicitly so an old local Controller that does
        # not yet mirror controller_routes cannot hide the new configuration.
        $bootstrapUrls += 'https://api-clubpay.justix.uz/api/core/bootstrap'
        $bootstrapUrls = @($bootstrapUrls | Select-Object -Unique)
        $routes = $null
        foreach ($bootstrapUrl in $bootstrapUrls) {
            try {
                $separator = if ($bootstrapUrl.Contains('?')) { '&' } else { '?' }
                $bootstrap = Invoke-RestMethod -Method Get -Uri ($bootstrapUrl + $separator + 'external_pc_id=' + [uri]::EscapeDataString($externalPcId)) -Headers @{ Authorization = "Bearer $agentToken" } -TimeoutSec 10
                if ($null -ne $bootstrap.controller_routes -and -not [string]::IsNullOrWhiteSpace([string]$bootstrap.controller_routes.primary_controller_url)) {
                    $routes = $bootstrap.controller_routes
                    break
                }
            }
            catch { }
        }
        if ($null -ne $routes) {
            $primary = ([string]$routes.primary_controller_url).Trim().TrimEnd('/')
            $fallback = ([string]$routes.fallback_controller_url).Trim().TrimEnd('/')
            $primaryUri = [Uri]$primary
            if (-not [string]::IsNullOrWhiteSpace($primaryUri.Host)) {
                $primaryHttp = if ($primaryUri.Scheme -eq 'https') { 'https' } else { 'http' }
                $primaryWs = if ($primaryHttp -eq 'https') { 'wss' } else { 'ws' }
                $primaryPort = if ($primaryUri.IsDefaultPort) { 8080 } else { $primaryUri.Port }
                $controller.WebSocketUrl = '{0}://{1}:{2}/api/core/ws' -f $primaryWs, $primaryUri.Host, $primaryPort
                $controller.BootstrapUrl = '{0}://{1}:{2}/api/core/bootstrap' -f $primaryHttp, $primaryUri.Host, $primaryPort
                $controller.SessionEndUrl = '{0}://{1}:{2}/api/core/agent/session/end' -f $primaryHttp, $primaryUri.Host, $primaryPort
                $fallbackWs = @()
                $fallbackBootstrap = @()
                $fallbackEnd = @()
                if (-not [string]::IsNullOrWhiteSpace($fallback)) {
                    $fallbackUri = [Uri]$fallback
                    if (-not [string]::IsNullOrWhiteSpace($fallbackUri.Host)) {
                        $fallbackHttp = if ($fallbackUri.Scheme -eq 'https') { 'https' } else { 'http' }
                        $fallbackWsScheme = if ($fallbackHttp -eq 'https') { 'wss' } else { 'ws' }
                        $fallbackPort = if ($fallbackUri.IsDefaultPort) { 8080 } else { $fallbackUri.Port }
                        $fallbackWs += '{0}://{1}:{2}/api/core/ws' -f $fallbackWsScheme, $fallbackUri.Host, $fallbackPort
                        $fallbackBootstrap += '{0}://{1}:{2}/api/core/bootstrap' -f $fallbackHttp, $fallbackUri.Host, $fallbackPort
                        $fallbackEnd += '{0}://{1}:{2}/api/core/agent/session/end' -f $fallbackHttp, $fallbackUri.Host, $fallbackPort
                    }
                }
                $fallbackWs += 'wss://api-clubpay.justix.uz/api/core/ws'
                $fallbackBootstrap += 'https://api-clubpay.justix.uz/api/core/bootstrap'
                $fallbackEnd += 'https://api-clubpay.justix.uz/api/core/agent/session/end'
                $controller.FallbackWebSocketUrls = @($fallbackWs | Select-Object -Unique)
                $controller.FallbackBootstrapUrls = @($fallbackBootstrap | Select-Object -Unique)
                $controller.FallbackSessionEndUrls = @($fallbackEnd | Select-Object -Unique)
                $config | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $installedConfig -Encoding utf8
            }
        }
    }
    catch { }

    $healthPath = Join-Path $installDirectory 'runtime\agent-health.json'
    New-Item -ItemType Directory -Path (Split-Path -Parent $healthPath) -Force | Out-Null
    Remove-Item -Path $healthPath -Force -ErrorAction SilentlyContinue
    $startedAt = [DateTime]::UtcNow

    Start-Process -FilePath $installedExecutable
    # The replacement is accepted only after it has re-established its
    # Controller channel. A process that merely starts and crash-loops must
    # roll back just like a failed download.
    $healthy = $false
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        if ((Get-Process -Name 'ClubPay.Agent.Client' -ErrorAction SilentlyContinue) -and
            (Test-Path $healthPath) -and ((Get-Item $healthPath).LastWriteTimeUtc -ge $startedAt)) {
            $healthy = $true
            break
        }
        Start-Sleep -Seconds 3
    }
    if (-not $healthy) {
        throw 'Agent did not reconnect to the Controller within 60 seconds.'
    }
    @{ status = 'healthy'; updated_at = [DateTime]::UtcNow.ToString('O') } | ConvertTo-Json | Set-Content -Path (Join-Path $installDirectory 'updates\last-update.json') -Encoding UTF8
    Write-Host 'ClubPay Agent updated and reconnected to Controller.' -ForegroundColor Green
}
catch {
    $updateError = $_
    Write-Host 'Agent update failed; restoring the previous build…' -ForegroundColor Yellow
    Get-Process -Name 'ClubPay.Agent.Client' -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-ChildItem -Path $programBackup -Force | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $installDirectory -Recurse -Force
    }
    Copy-Item -Path $configBackup -Destination $installedConfig -Force
    Start-Process -FilePath $installedExecutable -ErrorAction SilentlyContinue
    throw $updateError
}
finally {
    Remove-Item -Path $configBackup -Force -ErrorAction SilentlyContinue
}

if (-not $NoPrompt) {
    Read-Host 'Press Enter to close'
}
