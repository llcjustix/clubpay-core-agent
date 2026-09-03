param(
    [string]$EnrollmentPath
)

$ErrorActionPreference = 'Stop'

$bundleDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$agentExecutable = Join-Path $bundleDirectory 'ClubPay.Agent.Client.exe'
$agentConfig = Join-Path $bundleDirectory 'appsettings.Local.json'

if ([string]::IsNullOrWhiteSpace($EnrollmentPath)) {
    $EnrollmentPath = Join-Path $bundleDirectory 'clubpay-agent-enrollment.json'
}

if (-not (Test-Path $agentExecutable)) {
    throw "ClubPay Agent was not found next to the installer: $agentExecutable"
}

Write-Host ''
Write-Host 'ClubPay Agent — настройка игрового ПК' -ForegroundColor Cyan

$coreToken = ''
if (Test-Path $EnrollmentPath) {
    try {
        $enrollment = Get-Content -Path $EnrollmentPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "Не удалось прочитать файл подготовки Agent: $EnrollmentPath"
    }

    $externalPcId = ([string]$enrollment.external_pc_id).Trim()
    $controllerAddress = ([string]$enrollment.controller_url).Trim()
    $coreToken = ([string]$enrollment.core_token).Trim()
    if ([string]::IsNullOrWhiteSpace($externalPcId) -or [string]::IsNullOrWhiteSpace($controllerAddress) -or [string]::IsNullOrWhiteSpace($coreToken)) {
        throw 'Файл подготовки Agent должен содержать external_pc_id, controller_url и core_token.'
    }
    Write-Host "Используется подготовленный пакет для $externalPcId." -ForegroundColor Green
}
else {
    # Compatibility path for older packages. New club deployments must ship a
    # per-PC enrollment file so operators never copy a Controller secret by hand.
    Write-Host 'Подготовленный файл не найден. Используется устаревший ручной режим.' -ForegroundColor Yellow
    Write-Host ''
    do {
        $externalPcId = (Read-Host 'ID компьютера (например, pilot-real-network-pc-001)').Trim()
    } while ([string]::IsNullOrWhiteSpace($externalPcId))

    do {
        $controllerAddress = (Read-Host 'IP-адрес основного Controller (например, 192.168.1.10)').Trim()
    } while ([string]::IsNullOrWhiteSpace($controllerAddress))
}

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
$controllerPort = if ($controllerUri.IsDefaultPort) { 8080 } else { $controllerUri.Port }
$portSuffix = ":$controllerPort"
$controllerBaseUrl = '{0}://{1}{2}' -f $httpScheme, $controllerUri.Host, $portSuffix
$controllerWebSocketUrl = '{0}://{1}{2}/api/core/ws' -f $webSocketScheme, $controllerUri.Host, $portSuffix

$controllerReachable = Test-NetConnection -ComputerName $controllerUri.Host -Port $controllerPort -InformationLevel Quiet -WarningAction SilentlyContinue
if (-not $controllerReachable) {
    throw "Основной Controller недоступен по $($controllerUri.Host):$controllerPort. Проверьте сеть и Windows Firewall, затем запустите установку снова."
}

if ([string]::IsNullOrWhiteSpace($coreToken)) {
    $secureCoreToken = Read-Host 'CORE_TOKEN из защищённого файла controller.env' -AsSecureString
    $coreTokenPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureCoreToken)
    try {
        $coreToken = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($coreTokenPointer).Trim()
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($coreTokenPointer)
    }
}
# Clipboard managers and remote-desktop clients can insert invisible control
# characters into a pasted secret. They are forbidden in HTTP headers and used
# to produce an apparently successful installation with a permanently blank QR.
$coreToken = (-join ($coreToken.ToCharArray() | Where-Object { -not [char]::IsControl($_) })).Trim()
if ([string]::IsNullOrWhiteSpace($coreToken)) {
    throw 'CORE_TOKEN пустой или содержит только служебные символы. Скопируйте только значение после CORE_TOKEN= из controller.env.'
}

# Do not install an Agent that merely opens its local fallback screen. A real
# installation must authenticate against the selected Controller and receive a
# checkout QR before any files are copied into C:\ClubPay\Agent.
$escapedExternalPcId = [uri]::EscapeDataString($externalPcId)
try {
    $bootstrap = Invoke-RestMethod -Method Get -Uri "$controllerBaseUrl/api/core/bootstrap?external_pc_id=$escapedExternalPcId" -Headers @{ Authorization = "Bearer $coreToken" } -TimeoutSec 15
}
catch {
    throw "Controller отклонил подготовку $externalPcId. Проверьте подготовленный пакет и Controller. $($_.Exception.Message)"
}
if ($bootstrap.pc.external_pc_id -ne $externalPcId) {
    throw "Controller вернул другой игровой ПК: $($bootstrap.pc.external_pc_id)"
}
if ([string]::IsNullOrWhiteSpace([string]$bootstrap.qr_url)) {
    throw 'Controller не выдал QR для оплаты. Установка остановлена: сначала создайте активный статический QR для этого ПК в ClubPay.'
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
