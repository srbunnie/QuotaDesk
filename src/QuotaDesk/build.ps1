# Compila o QuotaDesk e mantém uma instalação estável por usuário.
# Uso:  powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 [-NoInstall] [-Package] [-Dotnet <caminho do dotnet.exe>]
#       -Package também grava dist\QuotaDesk-<versão>-win-x64.zip, pronto para copiar para outra máquina.
param([switch]$NoInstall, [switch]$Package, [string]$Dotnet = '')
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot

# SDK: parâmetro -Dotnet, variável QUOTADESK_DOTNET ou o 'dotnet' do PATH (precisa do .NET 10 SDK)
if (-not $Dotnet) { $Dotnet = if ($env:QUOTADESK_DOTNET) { $env:QUOTADESK_DOTNET } else { 'dotnet' } }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$staging = Join-Path $project 'publish-staging'

& $Dotnet publish (Join-Path $project 'AIUsageMonitor.csproj') -c Release -r win-x64 --self-contained -o $staging
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }

# Confere se o Windows deixa a compilação nova rodar (Smart App Control / antivírus podem bloquear
# arquivos sem assinatura). Se bloquear, pare aqui: não crie exclusões nem tente contornar o filtro.
$probe = Join-Path $env:TEMP ("quotadesk-probe-" + [guid]::NewGuid().ToString('N') + '.ico')
$test = Start-Process -FilePath (Join-Path $staging 'QuotaDesk.exe') -ArgumentList '--export-icon', $probe -WindowStyle Hidden -PassThru -Wait
if (-not (Test-Path $probe)) {
    throw "O Windows bloqueou a compilação nova (código $($test.ExitCode)). Veja o log 'Microsoft-Windows-CodeIntegrity/Operational' ou o antivírus. A versão instalada não foi alterada."
}
if ($Package) {
    $version = ([xml](Get-Content (Join-Path $project 'AIUsageMonitor.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $dist = [IO.Path]::GetFullPath((Join-Path $project '..\..\dist'))
    New-Item -ItemType Directory -Force $dist | Out-Null
    $zip = Join-Path $dist "QuotaDesk-$version-win-x64.zip"
    if (Test-Path $zip) { Remove-Item -LiteralPath $zip }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
    # Same file under a fixed name: the release asset that releases/latest/download/ and install.ps1 point to.
    Copy-Item -LiteralPath $zip -Destination (Join-Path $dist 'QuotaDesk-win-x64.zip') -Force
    "Pacote: $zip"
}
if ($NoInstall) { "Compilado e testado em $staging"; return }

$app = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\QuotaDesk'))
$previous = Join-Path ([Environment]::GetFolderPath('UserProfile')) ('QuotaDesk-archive\installations\app-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
Get-Process QuotaDesk -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and [string]::Equals([IO.Path]::GetFullPath($_.Path), (Join-Path $app 'QuotaDesk.exe'), [StringComparison]::OrdinalIgnoreCase)
} | Stop-Process -Force
Start-Sleep -Milliseconds 800
if (Test-Path $app) { New-Item -ItemType Directory -Force (Split-Path $previous) | Out-Null; Copy-Item -LiteralPath $app -Destination $previous -Recurse }
New-Item -ItemType Directory -Force $app | Out-Null
Copy-Item -Path (Join-Path $staging '*') -Destination $app -Recurse -Force
& (Join-Path $project 'install-shortcuts.ps1') -InstallDirectory $app

# Preserve the user's existing startup choice, but open the main panel directly.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startup = Get-ItemProperty -Path $runKey -Name 'QuotaDesk' -ErrorAction SilentlyContinue
if ($null -ne $startup -and $startup.QuotaDesk) {
    Set-ItemProperty -Path $runKey -Name 'QuotaDesk' -Value "`"$app\QuotaDesk.exe`" --voxel --open"
}

# Inicia pelo WMI: assim o QuotaDesk não fica preso ao "grupo de processos" de quem rodou este script.
# (Um terminal dentro de outro app, como o app desktop do Claude, faz os filhos morrerem junto quando esse app fecha ou se atualiza.)
$exe = Join-Path $app 'QuotaDesk.exe'
$started = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = "`"$exe`" --voxel --open"; CurrentDirectory = $app }
if ($started.ReturnValue -ne 0) { Start-Process -FilePath $exe -ArgumentList '--voxel', '--open' -WindowStyle Hidden }
"Instalado em $app" + ($(if (Test-Path $previous) { " (anterior preservado em $previous)" } else { '' }))
