param(
    [string]$BaseUrl = "http://localhost:5000"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host " GamePlatform POST Diagnostic" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$walletId = [guid]::NewGuid().ToString()
$requestId = "DEBUG-" + [guid]::NewGuid().ToString()

Write-Host "URL       : $BaseUrl/api/wallets"
Write-Host "Method    : POST"
Write-Host "Wallet ID : $walletId"
Write-Host "RequestId : $requestId"
Write-Host ""

$walletBody = @{
    id      = $walletId
    balance = 100
    type    = "Player"
}

$json = $walletBody | ConvertTo-Json -Depth 10

Write-Host "Request body:" -ForegroundColor DarkGray
Write-Host $json
Write-Host ""

Write-Host "Sending POST without following redirects..." -ForegroundColor Yellow
Write-Host ""

try {

    $response = Invoke-WebRequest `
        -Uri "$BaseUrl/api/wallets" `
        -Method POST `
        -ContentType "application/json" `
        -Headers @{
            "X-Request-Id" = $requestId
        } `
        -Body $json `
        -TimeoutSec 30 `
        -MaximumRedirection 0

    Write-Host "HTTP STATUS: $($response.StatusCode)" -ForegroundColor Green
    Write-Host ""

    Write-Host "Response headers:" -ForegroundColor Cyan

    foreach ($header in $response.Headers.GetEnumerator()) {
        Write-Host "$($header.Key): $($header.Value)"
    }

    Write-Host ""

    if ($response.Content) {
        Write-Host "Response body:" -ForegroundColor Cyan
        Write-Host $response.Content
    }
}
catch {

    Write-Host ""
    Write-Host "REQUEST EXCEPTION" -ForegroundColor Red
    Write-Host "-----------------" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red

    if ($_.Exception.Response) {

        Write-Host ""
        Write-Host "HTTP response received:" -ForegroundColor Yellow

        $httpResponse = $_.Exception.Response

        Write-Host "Status: $([int]$httpResponse.StatusCode)"
        Write-Host "Description: $($httpResponse.StatusDescription)"

        Write-Host ""
        Write-Host "Headers:" -ForegroundColor Cyan

        foreach ($header in $httpResponse.Headers.GetEnumerator()) {
            Write-Host "$($header.Key): $($header.Value)"
        }
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host " Check Load Balancer log for:" -ForegroundColor Cyan
Write-Host " RAW REQUEST: Method=POST" -ForegroundColor White
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
