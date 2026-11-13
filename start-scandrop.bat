@echo off
title Scan Drop - Local PC Station & Tunnel
cd /d "%~dp0"
if not exist "ScanDrop.exe" (
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\build-exe.ps1" -SkipTests
)
start "" "%~dp0ScanDrop.exe"
