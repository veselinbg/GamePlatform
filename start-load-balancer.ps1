$ErrorActionPreference = "Stop"

Write-Host "Starting Load Balancer on port 5000..." -ForegroundColor Cyan

dotnet run `
    --project "$PSScriptRoot\GamePlatform.LoadBalancer" `
    --no-launch-profile `
    --urls "http://localhost:5000"
