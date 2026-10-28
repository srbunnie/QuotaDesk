# Cria (ou atualiza) os atalhos do QuotaDesk no Menu Iniciar (aparece na pesquisa do Windows) e na Área de Trabalho.
# Uso:  powershell -NoProfile -ExecutionPolicy Bypass -File .\install-shortcuts.ps1 [-NoDesktop]
param([switch]$NoDesktop, [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\QuotaDesk'))
$ErrorActionPreference = 'Stop'
$exe = Join-Path $InstallDirectory 'QuotaDesk.exe'
if (-not (Test-Path $exe)) { throw "QuotaDesk.exe não encontrado em $exe" }

$targets = @([Environment]::GetFolderPath('Programs'))
if (-not $NoDesktop) { $targets += [Environment]::GetFolderPath('Desktop') }

$shell = New-Object -ComObject WScript.Shell
foreach ($folder in $targets) {
    $link = $shell.CreateShortcut((Join-Path $folder 'QuotaDesk.lnk'))
    $link.TargetPath = $exe
    $link.Arguments = '--voxel --open'
    $link.WorkingDirectory = Split-Path $exe
    $link.IconLocation = "$exe,0"
    $link.Description = 'QuotaDesk voxel - escritório e cotas de seis ferramentas'
    $link.Save()
    "Atalho: $(Join-Path $folder 'QuotaDesk.lnk')"
}
