@echo off
title Antigravity Auto Pilot
cd /d "%~dp0"

echo ===================================================
echo           ANTIGRAVITY AUTO PILOT (MULTI-WINDOW)
echo ===================================================

echo [1/3] Dong cac tien trinh Auto Pilot cu...
taskkill /F /IM AntigravityAutoPilot.exe >nul 2>&1
ping -n 2 127.0.0.1 >nul

echo [2/3] Bien dich phien ban moi nhat...
dotnet build -c Release >nul 2>&1

echo [3/3] Khoi dong phien ban moi nhat...
if exist "bin\Release\net8.0-windows\AntigravityAutoPilot.exe" (
    start "" "bin\Release\net8.0-windows\AntigravityAutoPilot.exe"
    exit /b 0
)

if exist "bin\Release\net8.0-windows\win-x64\publish\AntigravityAutoPilot.exe" (
    start "" "bin\Release\net8.0-windows\win-x64\publish\AntigravityAutoPilot.exe"
    exit /b 0
)

dotnet run
