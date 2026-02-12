Write-Host "Audio Setup Helper" -ForegroundColor Cyan
Write-Host "=================="

# Check for Python
try {
    python --version
    Write-Host "Python found." -ForegroundColor Green
}
catch {
    Write-Host "Python not found. Installing..." -ForegroundColor Yellow
    winget install Python.Python.3.11 --accept-source-agreements --accept-package-agreements
    
    if ($?) {
        Write-Host "Python installed." -ForegroundColor Green
        # Refresh path roughly
        $env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path', 'User')
    }
    else {
        Write-Host "Failed to install Python." -ForegroundColor Red
        Read-Host "Press Enter"
        exit
    }
}

# Check for edge-tts
Write-Host "Checking for edge-tts..."
try {
    Get-Command edge-tts -ErrorAction Stop | Out-Null
    Write-Host "edge-tts is installed." -ForegroundColor Green
}
catch {
    Write-Host "Installing edge-tts..."
    python -m pip install edge-tts
    
    if ($?) {
        Write-Host "edge-tts installed." -ForegroundColor Green
    }
    else {
        Write-Host "Failed to install edge-tts." -ForegroundColor Red
    }
}

Write-Host "Done." -ForegroundColor Cyan
Read-Host "Press Enter to exit"
