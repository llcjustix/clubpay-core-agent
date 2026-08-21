param(
    [string]$CoreToken = $env:CORE_TOKEN,
    [string]$ExternalPcId = 'pc-001',
    [string]$PcLabel = 'VM PC #01',
    [string]$ClubName = 'ClubPay Test VM'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($CoreToken)) {
    throw 'CORE_TOKEN must be supplied through CORE_TOKEN or -CoreToken.'
}

$root = 'C:\ClubPay'
$sourceZip = Join-Path $root 'agent-source.zip'
$sourceRoot = Join-Path $root 'clubpay-core-agent-main'
$dotnetRoot = Join-Path $root 'dotnet'
$output = Join-Path $root 'Agent'

New-Item -ItemType Directory -Force -Path $root | Out-Null

Invoke-WebRequest -UseBasicParsing `
    -Uri 'https://github.com/llcjustix/clubpay-core-agent/archive/refs/heads/main.zip' `
    -OutFile $sourceZip

if (Test-Path $sourceRoot) {
    Remove-Item -Recurse -Force $sourceRoot
}
Expand-Archive -LiteralPath $sourceZip -DestinationPath $root -Force

Invoke-WebRequest -UseBasicParsing `
    -Uri 'https://dot.net/v1/dotnet-install.ps1' `
    -OutFile (Join-Path $root 'dotnet-install.ps1')

& (Join-Path $root 'dotnet-install.ps1') -Channel 10.0 -InstallDir $dotnetRoot -NoPath

& (Join-Path $dotnetRoot 'dotnet.exe') publish `
    (Join-Path $sourceRoot 'src\ClubPay.Agent.Client\ClubPay.Agent.Client.csproj') `
    -Configuration Release `
    -Runtime win-x64 `
    --self-contained true `
    -Output $output

$config = @{
    Agent = @{
        PcId = $PcLabel
        ClubName = $ClubName
        Zone = 'Standard'
    }
    Controller = @{
        ExternalPcId = $ExternalPcId
        AgentToken = $CoreToken
    }
} | ConvertTo-Json -Depth 4

Set-Content -LiteralPath (Join-Path $output 'appsettings.Local.json') -Value $config -Encoding utf8
Start-Process -FilePath (Join-Path $output 'ClubPay.Agent.Client.exe')

Write-Host 'ClubPay Agent started.'
