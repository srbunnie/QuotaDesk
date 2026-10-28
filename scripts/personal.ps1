# QuotaDesk de uso pessoal: a versão publicada, instalada em %LOCALAPPDATA%\Programs\QuotaDesk.
#
#   open     abre o app instalado; se ainda não houver um, instala a última Release.
#   update   compara com a última Release e instala se houver versão nova (-Force reinstala mesmo assim).
#
# Os dados (%LOCALAPPDATA%\AIUsageMonitor) nunca são apagados: só a pasta do programa é trocada.
param(
    [Parameter(Position = 0)][ValidateSet('open', 'update')][string]$Action = 'open',
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $env:LOCALAPPDATA 'Programs\QuotaDesk'
$exe = Join-Path $app 'QuotaDesk.exe'

function Get-InstalledVersion {
    if (-not (Test-Path $exe)) { return $null }
    return ((Get-Item $exe).VersionInfo.ProductVersion -split '\+')[0]
}

function Install-Release {
    # O mesmo install.ps1 do "irm | iex", lido como UTF-8 a partir desta cópia do repositório.
    & ([scriptblock]::Create([IO.File]::ReadAllText((Join-Path $root 'install.ps1'))))
}

function Open-Installed {
    Start-Process -FilePath $exe -ArgumentList '--voxel', '--open' -WorkingDirectory $app
    "QuotaDesk $(Get-InstalledVersion) aberto."
}

$installed = Get-InstalledVersion
if ($Action -eq 'open') {
    if ($installed) { Open-Installed } else { 'QuotaDesk ainda não está instalado. Instalando a última versão publicada...'; Install-Release }
    return
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
try {
    $latest = (Invoke-RestMethod -UseBasicParsing -Uri 'https://api.github.com/repos/srbunnie/QuotaDesk/releases/latest' -TimeoutSec 15).tag_name.TrimStart('v')
}
catch {
    Write-Host "Não foi possível consultar o GitHub ($($_.Exception.Message)). A versão instalada continua como está." -ForegroundColor Yellow
    if ($installed) { Open-Installed }
    return
}

"Instalada: $(if ($installed) { $installed } else { 'nenhuma' })  ·  Publicada: $latest"
if ($installed -and -not $Force -and [version]$installed -ge [version]$latest) {
    'Já está na última versão.'
    Open-Installed
    return
}
Install-Release
