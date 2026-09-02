param()

$ErrorActionPreference = 'Stop'

$bundleDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$agentExecutable = Join-Path $bundleDirectory 'ClubPay.Agent.Client.exe'
$agentConfig = Join-Path $bundleDirectory 'appsettings.Local.json'

if (-not (Test-Path $agentExecutable)) {
    throw "ClubPay Agent was not found next to the installer: $agentExecutable"
}

Write-Host ''
Write-Host 'ClubPay Agent — настройка игрового ПК' -ForegroundColor Cyan
Write-Host 'Заполните данные, выданные при установке основного ClubPay Controller.' -ForegroundColor DarkGray
Write-Host ''

do {
    $externalPcId = (Read-Host 'ID компьютера (например, pilot-real-network-pc-001)').Trim()
} while ([string]::IsNullOrWhiteSpace($externalPcId))

do {
    $controllerAddress = (Read-Host 'IP-адрес основного Controller (например, 192.168.1.10)').Trim()
} while ([string]::IsNullOrWhiteSpace($controllerAddress))

if ($controllerAddress -notmatch '^https?://') {
    $controllerAddress = "http://$controllerAddress"
}

try {
    $controllerUri = [Uri]$controllerAddress
    if ([string]::IsNullOrWhiteSpace($controllerUri.Host)) { throw 'host is empty' }
}
catch {
    throw 'Указан некорректный адрес Controller. Пример: 192.168.1.10 или http://192.168.1.10:8080'
}

$httpScheme = if ($controllerUri.Scheme -eq 'https') { 'https' } else { 'http' }
$webSocketScheme = if ($httpScheme -eq 'https') { 'wss' } else { 'ws' }
$portSuffix = if ($controllerUri.IsDefaultPort) { ':8080' } else { ":$($controllerUri.Port)" }
$controllerBaseUrl = '{0}://{1}{2}' -f $httpScheme, $controllerUri.Host, $portSuffix
$controllerWebSocketUrl = '{0}://{1}{2}/api/core/ws' -f $webSocketScheme, $controllerUri.Host, $portSuffix

$secureCoreToken = Read-Host 'CORE_TOKEN из защищённого файла controller.env' -AsSecureString
$coreTokenPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureCoreToken)
try {
    $coreToken = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($coreTokenPointer).Trim()
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($coreTokenPointer)
}
if ([string]::IsNullOrWhiteSpace($coreToken)) {
    throw 'CORE_TOKEN обязателен.'
}

# Pilot mode deliberately leaves a maintenance route available. Switch to the
# dedicated kiosk account only after the complete payment/session flow passed.
$config = [ordered]@{
    Agent = [ordered]@{
        PcId = "PC $externalPcId"
        DataDirectory = "C:\ProgramData\ClubPay\Agent\state\$externalPcId"
        KioskLockdownEnabled = $false
        HideWindowsTaskbar = $false
        MaintenanceExitEnabled = $true
    }
    Controller = [ordered]@{
        ExternalPcId = $externalPcId
        AgentToken = $coreToken
        WebSocketUrl = $controllerWebSocketUrl
        BootstrapUrl = "$controllerBaseUrl/api/core/bootstrap"
        SessionEndUrl = "$controllerBaseUrl/api/core/agent/session/end"
        FallbackWebSocketUrls = @('wss://api-clubpay.justix.uz/api/core/ws')
        FallbackBootstrapUrls = @('https://api-clubpay.justix.uz/api/core/bootstrap')
        FallbackSessionEndUrls = @('https://api-clubpay.justix.uz/api/core/agent/session/end')
    }
}

$config | ConvertTo-Json -Depth 8 | Set-Content -Path $agentConfig -Encoding utf8

Write-Host ''
Write-Host 'Файл настроек создан. Подтвердите запрос Windows на установку.' -ForegroundColor Yellow
$installProcess = Start-Process -FilePath $agentExecutable -ArgumentList '--install' -Verb RunAs -Wait -PassThru
if ($installProcess.ExitCode -ne 0) {
    throw "Установка Agent завершилась с кодом $($installProcess.ExitCode)."
}

$installedAgent = 'C:\ClubPay\Agent\ClubPay.Agent.Client.exe'
if (-not (Test-Path $installedAgent)) {
    throw "Установленный Agent не найден: $installedAgent"
}

Start-Process -FilePath $installedAgent
Write-Host ''
Write-Host 'Готово. Agent запущен и будет запускаться при следующем входе в Windows.' -ForegroundColor Green
Write-Host 'Не удаляйте controller.env и не передавайте CORE_TOKEN игрокам.' -ForegroundColor Yellow
Read-Host 'Нажмите Enter для закрытия'
