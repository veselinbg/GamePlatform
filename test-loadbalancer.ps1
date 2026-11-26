$ErrorActionPreference = "Stop"

$loadBalancer = "http://localhost:5000"
$walletId = [Guid]::NewGuid()

Write-Host ""
Write-Host "============================================"
Write-Host " GamePlatform Load Balancer Test"
Write-Host "============================================"
Write-Host ""
Write-Host "Load Balancer : $loadBalancer"
Write-Host "Wallet : $walletId"
Write-Host ""

$body = @{
id = $walletId
balance = 100
type = "Player"
} | ConvertTo-Json

Write-Host "Creating wallet through Load Balancer..."

try {
$response = Invoke-WebRequest -Uri "$loadBalancer/api/wallets"
-Method POST -ContentType "application/json"
-Headers @{
"X-Request-Id" = [Guid]::NewGuid().ToString("N")
} `
-Body $body

Write-Host ""
Write-Host "Wallet creation succeeded." -ForegroundColor Green
Write-Host $response.Content


}
catch {
Write-Host ""
Write-Host "Wallet creation failed." -ForegroundColor Red
Write-Host $_.Exception.Message
exit 1
}

Write-Host ""
Write-Host "Reading wallet through Load Balancer..."

try {
$response = Invoke-WebRequest -Uri "$loadBalancer/api/wallets/$walletId"
-Method GET `
-UseBasicParsing

Write-Host ""
Write-Host "Wallet read succeeded." -ForegroundColor Green
Write-Host $response.Content


}
catch {
Write-Host ""
Write-Host "Wallet read failed." -ForegroundColor Red
Write-Host $_.Exception.Message
exit 1
}

Write-Host ""
Write-Host "Verifying balance through Load Balancer..."

try {
$response = Invoke-WebRequest -Uri "$loadBalancer/api/wallets/$walletId/balance/verify"
-Method GET `
-UseBasicParsing

Write-Host ""
Write-Host "Balance verification succeeded." -ForegroundColor Green
Write-Host $response.Content


}
catch {
Write-Host ""
Write-Host "Balance verification failed." -ForegroundColor Red
Write-Host $_.Exception.Message
exit 1
}

Write-Host ""
Write-Host "============================================"
Write-Host " Load Balancer test completed"
Write-Host "============================================"
Write-Host ""