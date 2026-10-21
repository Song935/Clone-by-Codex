@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-CloneCad.ps1" -Launch
if errorlevel 1 (
    echo Installation failed. Please copy the error above.
    pause
    exit /b 1
)
