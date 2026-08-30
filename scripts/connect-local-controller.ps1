param(
    [string]$AgentDirectory = 'C:\ClubPay\Agent',
    [string]$ControllerDirectory = 'C:\ClubPay\Controller',
    [string]$ExternalPcId = 'pc-001'
)

$ErrorActionPreference = 'Stop'

$controllerConfigPath = Join-Path $ControllerDirectory 'controller.env'
$agentConfigPath = Join-Path $AgentDirectory 'appsettings.Local.json'
$agentExecutablePath = Join-Path $AgentDirectory 'ClubPay.Agent.Client.exe'

if (-not (Test-Path $controllerConfigPath)) {
    throw "Controller config was not found: $controllerConfigPath"
}
if (-not (Test-Path $agentExecutablePath)) {
    throw "Agent executable was not found: $agentExecutablePath"
}

$coreTokenLine = Get-Content $controllerConfigPath |
    Where-Object { $_ -match '^CORE_TOKEN=' } |
    Select-Object -First 1
$coreToken = ($coreTokenLine -replace '^CORE_TOKEN=', '').Trim()
if ([string]::IsNullOrWhiteSpace($coreToken) -or $coreToken -eq 'CHANGE_ME_SHARED_CORE_TOKEN') {
    throw 'CORE_TOKEN is not configured in the local Controller. Run Controller activation first.'
}

$headers = @{ Authorization = "Bearer $coreToken" }
$bootstrapUri = "http://127.0.0.1:8080/api/core/bootstrap?external_pc_id=$([uri]::EscapeDataString($ExternalPcId))"
try {
    $bootstrap = Invoke-RestMethod -Method Get -Uri $bootstrapUri -Headers $headers -TimeoutSec 10
}
catch {
    throw "Local Controller could not bootstrap $ExternalPcId on port 8080. Check that Controller is running and that this PC exists in its club. $($_.Exception.Message)"
}

if ($bootstrap.pc.external_pc_id -ne $ExternalPcId) {
    throw "Local Controller returned a different PC: $($bootstrap.pc.external_pc_id)"
}

if (Test-Path $agentConfigPath) {
    $backupPath = "$agentConfigPath.before-local-controller"
    if (-not (Test-Path $backupPath)) {
        Copy-Item $agentConfigPath $backupPath
    }
    $config = Get-Content $agentConfigPath -Raw | ConvertFrom-Json
}
else {
    $config = [pscustomobject]@{}
}

if ($null -eq $config.Controller) {
    $config | Add-Member -NotePropertyName Controller -NotePropertyValue ([pscustomobject]@{})
}

function Set-ConfigValue {
    param(
        [Parameter(Mandatory)] [object]$Target,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [object]$Value
    )

    $Target | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
}

$controller = $config.Controller
Set-ConfigValue $controller 'ExternalPcId' $ExternalPcId
Set-ConfigValue $controller 'AgentToken' $coreToken

# The local Controller is first for this installation. Cloud stays a fallback,
# so this VM can test the same path that the club uses during a Cloud outage.
Set-ConfigValue $controller 'WebSocketUrl' 'ws://127.0.0.1:8080/api/core/ws'
Set-ConfigValue $controller 'BootstrapUrl' 'http://127.0.0.1:8080/api/core/bootstrap'
Set-ConfigValue $controller 'SessionEndUrl' 'http://127.0.0.1:8080/api/core/agent/session/end'
Set-ConfigValue $controller 'FallbackWebSocketUrls' @('wss://api-clubpay.justix.uz/api/core/ws')
Set-ConfigValue $controller 'FallbackBootstrapUrls' @('https://api-clubpay.justix.uz/api/core/bootstrap')
Set-ConfigValue $controller 'FallbackSessionEndUrls' @('https://api-clubpay.justix.uz/api/core/agent/session/end')

$config | ConvertTo-Json -Depth 12 | Set-Content -Path $agentConfigPath -Encoding utf8

Get-Process -Name 'ClubPay.Agent.Client' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Start-Process -FilePath $agentExecutablePath

Write-Host "Ready: Agent $ExternalPcId now uses the local Controller first." -ForegroundColor Green
Write-Host "Bootstrap verified: $($bootstrap.club.name) / $($bootstrap.pc.label)." -ForegroundColor Green
Write-Host "Cloud remains the fallback. A backup of appsettings.Local.json was saved once next to it." -ForegroundColor Yellow
