# Instala a versão mais recente do QuotaDesk publicada no GitHub, sem compilar e sem administrador.
# Uso (PowerShell):  irm https://raw.githubusercontent.com/srbunnie/QuotaDesk/main/install.ps1 | iex
$ErrorActionPreference = 'Stop'
& {
    $url = 'https://github.com/srbunnie/QuotaDesk/releases/latest/download/QuotaDesk-win-x64.zip'
    $app = Join-Path $env:LOCALAPPDATA 'Programs\QuotaDesk'
    $zip = Join-Path $env:TEMP ('QuotaDesk-' + [guid]::NewGuid().ToString('N') + '.zip')
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Write-Host 'Baixando o QuotaDesk...' -ForegroundColor Cyan
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $zip
        # Fecha só a cópia instalada nesta pasta antes de substituir os arquivos.
        Get-Process QuotaDesk -ErrorAction SilentlyContinue | Where-Object {
            $_.Path -and $_.Path.StartsWith($app, [StringComparison]::OrdinalIgnoreCase)
        } | Stop-Process -Force
        Start-Sleep -Milliseconds 800
        if (Test-Path $app) { Remove-Item -LiteralPath $app -Recurse -Force }
        New-Item -ItemType Directory -Force $app | Out-Null
        Expand-Archive -LiteralPath $zip -DestinationPath $app -Force
    }
    finally { Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue }

    $exe = Join-Path $app 'QuotaDesk.exe'
    if (-not (Test-Path $exe)) { throw 'O pacote baixado não contém QuotaDesk.exe.' }
    $shell = New-Object -ComObject WScript.Shell
    foreach ($folder in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
        $link = $shell.CreateShortcut((Join-Path $folder 'QuotaDesk.lnk'))
        $link.TargetPath = $exe
        $link.Arguments = '--voxel --open'
        $link.WorkingDirectory = $app
        $link.IconLocation = "$exe,0"
        $link.Description = 'QuotaDesk - cotas das ferramentas de IA'
        $link.Save()
    }
    Start-Process -FilePath $exe -ArgumentList '--voxel', '--open'
    Write-Host "QuotaDesk instalado em $app e aberto. Atalhos criados no Menu Iniciar e na Área de Trabalho." -ForegroundColor Green
}
