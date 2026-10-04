@echo off
chcp 65001 > nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0New-WeeklyRelease.ps1" %*
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] El script finalizo con errores.
)
pause
