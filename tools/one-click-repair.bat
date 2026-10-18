@echo off
rem One-click repair launcher for DefenderRestore
cd /d "%~dp0"
start "" "%~dp0DefenderRestore.exe" --repair --yes
