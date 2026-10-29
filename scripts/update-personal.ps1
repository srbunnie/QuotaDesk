# Atualiza a versão pessoal com o código atual do opensource.
#
# Compila o repositório e copia o resultado para a instalação pessoal. A pasta de dados
# (AIUsageMonitor, com suas chaves e logins) nunca é tocada.
#
# Uso:  scripts\update-personal.ps1 [-NoBuild]
param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $env:LOCALAPPDATA 'Programs\QuotaDesk'
$exe = Join-Path $app 'QuotaDesk.exe'
$staging = Join-Path $root 'src\QuotaDesk\publish-staging'

if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'setup.ps1') -NoInstall
    if (-not $?) { throw 'Falha na compilação.' }
}
if (-not (Test-Path (Join-Path $staging 'QuotaDesk.exe'))) { throw 'Nada compilado em publish-staging.' }

# The personal folder is a junction into the central QuotaDesk folder, so the running
# process may report either path. Match both to stop the right instance only.
Get-Process QuotaDesk -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and ($_.Path -eq $exe -or $_.Path -like '*\QuotaDesk\pessoal\app\QuotaDesk.exe')
} | Stop-Process -Force
Start-Sleep -Milliseconds 600

New-Item -ItemType Directory -Force $app | Out-Null
Copy-Item -Path (Join-Path $staging '*') -Destination $app -Recurse -Force

$commit = git -C $root rev-parse --short HEAD
Start-Process -FilePath $exe -ArgumentList '--voxel', '--open' -WorkingDirectory $app
"QuotaDesk pessoal atualizado com o commit $commit e aberto."
