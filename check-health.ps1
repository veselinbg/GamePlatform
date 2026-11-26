$ErrorActionPreference = "Stop"

Write-Host "Checking Load Balancer..." -ForegroundColor Cyan

try {
    Invoke-WebRequest `
        -Uri "http://localhost:5000/health/ready" `
        -UseBasicParsing `
        -TimeoutSec 3
}
catch {
    Write-Host "Load Balancer health endpoint is not exposed directly." `
        -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Checking Game Servers..." -ForegroundColor Cyan

1..5 | ForEach-Object {

    $port = 6000 + $_

    try {
        $response = Invoke-WebRequest `
            -Uri "http://localhost:$port/health/ready" `
            -UseBasicParsing `
            -TimeoutSec 3

        Write-Host "GameServer$_ : OK" -ForegroundColor Green
    }
    catch {
        Write-Host "GameServer$_ : FAILED" -ForegroundColor Red
    }
}
