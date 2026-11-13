@echo off
title Scan Drop - Enable Windows Auto-Start
cd /d "%~dp0"
if not exist "ScanDrop.exe" (
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\build-exe.ps1" -SkipTests
)
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "ScanDropStation" /t REG_SZ /d "\"%~dp0ScanDrop.exe\" --minimized" /f
echo Scan Drop Station is now configured to start automatically with Windows!
pause
