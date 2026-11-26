$processes = Get-CimInstance Win32_Process |
    Where-Object {
        $_.Name -eq "dotnet.exe" -and
        $_.CommandLine -like "*GamePlatform*"
    }

foreach ($process in $processes) {
    Write-Host "Stopping PID $($process.ProcessId)" -ForegroundColor Yellow
    Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
}

Write-Host "GamePlatform processes stopped." -ForegroundColor Green
