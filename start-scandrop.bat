@echo off
title Scan Drop - Local PC Station & Tunnel
cd /d "%~dp0"
echo ========================================================
echo   Starting Scan Drop Local SSD + Tunnel Station...
echo ========================================================
if not exist "ScanDrop.exe" (
    echo Compiling ScanDrop.exe...
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\build-exe.ps1" -SkipTests
    if errorlevel 1 (
        echo Failed to build ScanDrop.exe.
        pause
        exit /b 1
    )
)
start "" "%~dp0ScanDrop.exe"
timeout /t 2 /nobreak >nul
tasklist /FI "IMAGENAME eq ScanDrop.exe" 2>NUL | find /I /N "ScanDrop.exe" >NUL
if errorlevel 1 (
    echo ScanDrop.exe exited unexpectedly. Check ScanDrop_Local_Inbox\startup_error.log
    pause
    exit /b 1
)
echo Scan Drop Station is running! Opening workspace...
timeout /t 2 /nobreak >nul
