# Compila o código atual e abre o QuotaDesk num ambiente isolado, ao lado da versão instalada.
#
#   dev      pasta de dados própria e vazia: nenhum token, configuração ou histórico real.
#   homolog  cópia dos seus dados reais, feita na primeira vez (ou com -FreshData). Testa a versão
#            candidata com dados de verdade sem tocar nos originais. Os logins (*.dat) não são copiados:
#            o serviço pode trocar o refresh token a cada renovação, e duas cópias do mesmo login
#            acabariam derrubando a sessão da versão pessoal. Conecte a homologação uma vez; o login
#            dela é mantido entre execuções, inclusive com -FreshData.
#
# A versão pessoal (%LOCALAPPDATA%\Programs\QuotaDesk e %LOCALAPPDATA%\AIUsageMonitor) nunca é alterada.
#
# Uso:  scripts\env.ps1 dev | homolog [-FreshData] [-NoBuild] [-Stop]
param(
    [Parameter(Position = 0)][ValidateSet('dev', 'homolog')][string]$Name = 'dev',
    [switch]$FreshData,
    [switch]$NoBuild,
    [switch]$Stop
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $env:LOCALAPPDATA "Programs\QuotaDesk-$Name"
$data = Join-Path $env:LOCALAPPDATA "AIUsageMonitor-$Name"
$personal = Join-Path $env:LOCALAPPDATA 'AIUsageMonitor'
$exe = Join-Path $app 'QuotaDesk.exe'

function Stop-Environment {
    Get-Process QuotaDesk -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and [string]::Equals([IO.Path]::GetFullPath($_.Path), $exe, [StringComparison]::OrdinalIgnoreCase)
    } | Stop-Process -Force
    Start-Sleep -Milliseconds 600
}

Stop-Environment
if ($Stop) { "Ambiente $Name encerrado."; return }

if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'setup.ps1') -NoInstall
    if (-not $?) { throw 'Falha na compilação.' }
    if (Test-Path $app) { Remove-Item -LiteralPath $app -Recurse -Force }
    New-Item -ItemType Directory -Force $app | Out-Null
    Copy-Item -Path (Join-Path $root 'src\QuotaDesk\publish-staging\*') -Destination $app -Recurse -Force
}
if (-not (Test-Path $exe)) { throw "Nada compilado em $app. Rode sem -NoBuild." }

if ($Name -eq 'homolog' -and ($FreshData -or -not (Test-Path $data))) {
    New-Item -ItemType Directory -Force $data | Out-Null
    Get-ChildItem -LiteralPath $data -File | Where-Object Extension -ne '.dat' | Remove-Item -Force
    if (Test-Path $personal) {
        Get-ChildItem -LiteralPath $personal -File |
            Where-Object { $_.Extension -ne '.dat' -and $_.Name -notin 'error.log', 'claude-limits.json' } |
            Copy-Item -Destination $data
    }
    "Dados pessoais copiados para $data"
}
New-Item -ItemType Directory -Force $data | Out-Null

# O processo filho herda estas variáveis; o app as repassa ao se reabrir.
$env:QUOTADESK_DATA_DIR = $data
$env:QUOTADESK_ENV = $Name
Start-Process -FilePath $exe -ArgumentList '--test-instance', '--voxel', '--open' -WorkingDirectory $app
"QuotaDesk [$Name] aberto: app em $app, dados em $data"
