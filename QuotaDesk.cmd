@echo off
rem Um clique: instala o SDK do .NET 10 se faltar, compila, instala e abre o QuotaDesk.
rem Nada precisa de administrador; tudo vai para a sua pasta de usuario.
title QuotaDesk - instalador
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup.ps1" %*
if errorlevel 1 (
  echo.
  echo Algo deu errado. Leia a mensagem acima.
  pause
)
