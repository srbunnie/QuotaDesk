# QuotaDesk de uso pessoal, na pasta central ao lado do repositório (pessoal\app).
#
#   open     abre o app pessoal instalado.
#   update   compila o código atual do opensource e atualiza o pessoal (scripts\update-personal.ps1).
#
# Os dados (pessoal\dados) nunca são apagados: só o programa é trocado.
param(
    [Parameter(Position = 0)][ValidateSet('open', 'update')][string]$Action = 'open',
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path (Split-Path $root -Parent) 'pessoal\app'
$exe = Join-Path $app 'QuotaDesk.exe'

if ($Action -eq 'update' -or $Force) {
    & (Join-Path $PSScriptRoot 'update-personal.ps1')
    return
}

if (-not (Test-Path $exe)) {
    'QuotaDesk pessoal ainda não foi compilado. Compilando a partir do opensource...'
    & (Join-Path $PSScriptRoot 'update-personal.ps1')
    return
}
Start-Process -FilePath $exe -ArgumentList '--voxel', '--open' -WorkingDirectory $app
'QuotaDesk pessoal aberto.'
