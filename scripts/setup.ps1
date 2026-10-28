# Prepara e instala o QuotaDesk a partir do código-fonte, sem administrador.
# 1. Procura um SDK do .NET 10 (PATH, %LOCALAPPDATA%\Microsoft\dotnet, ~\.dotnet).
# 2. Se não houver, baixa o instalador oficial da Microsoft (https://dot.net/v1/dotnet-install.ps1),
#    confere a assinatura digital dele e instala o SDK 10 só para o seu usuário.
# 3. Roda src\QuotaDesk\build.ps1, que publica, instala em %LOCALAPPDATA%\Programs\QuotaDesk,
#    cria os atalhos e abre o painel. -NoInstall e -Package são repassados ao build.
param([switch]$NoInstall, [switch]$Package)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$userDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'

function Test-Sdk10([string]$exe) {
    if (-not $exe -or -not (Test-Path $exe)) { return $false }
    try { return [bool](& $exe --list-sdks 2>$null | Where-Object { $_ -match '^10\.' }) } catch { return $false }
}

$candidates = @(
    (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    (Join-Path $userDotnet 'dotnet.exe'),
    (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe')
)
$dotnet = $candidates | Where-Object { Test-Sdk10 $_ } | Select-Object -First 1

if (-not $dotnet) {
    Write-Host 'SDK do .NET 10 não encontrado. Baixando o instalador oficial da Microsoft...' -ForegroundColor Cyan
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $installer = Join-Path $env:TEMP ('dotnet-install-' + [guid]::NewGuid().ToString('N') + '.ps1')
    try {
        Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
        $signature = Get-AuthenticodeSignature $installer
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft') {
            throw "A assinatura do instalador do .NET não confere ($($signature.Status)). Instale o SDK 10 manualmente: https://dotnet.microsoft.com/download/dotnet/10.0"
        }
        & $installer -Channel 10.0 -InstallDir $userDotnet -NoPath
    }
    finally { Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue }
    $dotnet = Join-Path $userDotnet 'dotnet.exe'
    if (-not (Test-Sdk10 $dotnet)) { throw 'A instalação do SDK do .NET 10 não foi concluída.' }
}

Write-Host "Usando $dotnet" -ForegroundColor Cyan
$build = Join-Path $root 'src\QuotaDesk\build.ps1'
$arguments = @{ Dotnet = $dotnet }
if ($NoInstall) { $arguments.NoInstall = $true }
if ($Package) { $arguments.Package = $true }
& $build @arguments
