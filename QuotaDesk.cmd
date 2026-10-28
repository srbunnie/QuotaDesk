@echo off
rem Inicializador do QuotaDesk. Dois cliques abrem o menu; sem escolha em 8 segundos, abre o seu QuotaDesk.
rem Direto pela linha de comando:  QuotaDesk.cmd abrir ^| atualizar ^| reinstalar ^| codigo ^| dev ^| homolog ^| parar
rem Nada precisa de administrador; tudo vai para a sua pasta de usuario.
setlocal
title QuotaDesk
set "PS=powershell -NoProfile -ExecutionPolicy Bypass -File"
set "HERE=%~dp0"

rem Compatibilidade: QuotaDesk.cmd -NoInstall / -Package continuam compilando a partir do codigo.
rem (o ponto garante que FIRST nunca fica vazia: o recorte de uma variavel vazia quebra a linha)
set "FIRST=%~1."
if "%FIRST:~0,1%"=="-" ( %PS% "%HERE%scripts\setup.ps1" %* & goto :done )
if not "%~1"=="" ( set "CHOICE=%~1" & goto :run )

echo.
echo   QuotaDesk
echo.
echo   1  Abrir meu QuotaDesk (uso pessoal)
echo   2  Atualizar para a ultima versao publicada
echo   3  Desenvolvimento: codigo atual com dados vazios
echo   4  Homologacao: codigo atual com copia dos seus dados
echo   5  Instalar a versao pessoal a partir deste codigo
echo   6  Fechar desenvolvimento e homologacao
echo   0  Sair
echo.
choice /c 1234560 /t 8 /d 1 /n /m "Escolha [1]: "
set "CHOICE=%errorlevel%"
if "%CHOICE%"=="1" set "CHOICE=abrir"
if "%CHOICE%"=="2" set "CHOICE=atualizar"
if "%CHOICE%"=="3" set "CHOICE=dev"
if "%CHOICE%"=="4" set "CHOICE=homolog"
if "%CHOICE%"=="5" set "CHOICE=codigo"
if "%CHOICE%"=="6" set "CHOICE=parar"
if "%CHOICE%"=="7" exit /b 0

:run
if /i "%CHOICE%"=="abrir"      ( %PS% "%HERE%scripts\personal.ps1" open & goto :done )
if /i "%CHOICE%"=="atualizar"  ( %PS% "%HERE%scripts\personal.ps1" update & goto :done )
if /i "%CHOICE%"=="reinstalar" ( %PS% "%HERE%scripts\personal.ps1" update -Force & goto :done )
if /i "%CHOICE%"=="codigo"     ( %PS% "%HERE%scripts\setup.ps1" & goto :done )
if /i "%CHOICE%"=="dev"        ( %PS% "%HERE%scripts\env.ps1" dev & goto :done )
if /i "%CHOICE%"=="homolog"    ( %PS% "%HERE%scripts\env.ps1" homolog & goto :done )
if /i "%CHOICE%"=="parar"      ( %PS% "%HERE%scripts\env.ps1" dev -Stop & %PS% "%HERE%scripts\env.ps1" homolog -Stop & goto :done )
echo Opcao desconhecida: %CHOICE%
exit /b 2

:done
if errorlevel 1 (
  echo.
  echo Algo deu errado. Leia a mensagem acima.
  pause
  exit /b 1
)
exit /b 0
