@echo off
echo Launching setup script...
powershell -NoProfile -ExecutionPolicy Bypass -File "setup_tts.ps1"
if %errorlevel% neq 0 (
    echo.
    echo Script execution failed or was restricted.
    echo Please right-click 'setup_tts.ps1' and select 'Run with PowerShell'.
)
pause
