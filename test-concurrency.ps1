param(
    [string]$BaseUrl = "http://localhost:5000",
    [int]$Amount = 80,
    [int]$StartingBalance = 100
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host " GamePlatform Concurrency Test" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# ============================================================
# Configuration
# ============================================================

$walletId = [guid]::NewGuid().ToString()

$createRequestId = "CREATE-" + [guid]::NewGuid().ToString()
$requestId1 = "BET-1-" + [guid]::NewGuid().ToString()
$requestId2 = "BET-2-" + [guid]::NewGuid().ToString()

$idempotency1 = [guid]::NewGuid().ToString()
$idempotency2 = [guid]::NewGuid().ToString()

$walletUrl = "$BaseUrl/api/wallets"
$betUrl = "$BaseUrl/api/bets"

Write-Host "Load Balancer : $BaseUrl"
Write-Host "Wallet        : $walletId"
Write-Host "Balance       : $StartingBalance"
Write-Host "Bet amount    : $Amount"
Write-Host ""

# ============================================================
# Check curl
# ============================================================

$curlCommand = Get-Command curl.exe -ErrorAction SilentlyContinue

if (-not $curlCommand) {
    Write-Host "ERROR: curl.exe was not found." -ForegroundColor Red
    exit 1
}

Write-Host "Using curl    : $($curlCommand.Source)" -ForegroundColor DarkGray
Write-Host ""

# ============================================================
# Helper - POST using a temporary JSON file
# ============================================================

function Invoke-CurlPost {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,

        [Parameter(Mandatory = $true)]
        [string]$Json,

        [Parameter(Mandatory = $true)]
        [string]$RequestId,

        [string]$IdempotencyKey
    )

    $tempFile = [System.IO.Path]::GetTempFileName()

    try {

        # Write UTF-8 JSON WITHOUT BOM.
        # This avoids PowerShell 5.1/native curl quoting problems.
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)

        [System.IO.File]::WriteAllText(
            $tempFile,
            $Json,
            $utf8NoBom
        )

        $arguments = @(
            "--silent"
            "--show-error"
            "--request"
            "POST"
            "--url"
            $Url
            "--header"
            "Content-Type: application/json"
            "--header"
            "X-Request-Id: $RequestId"
            "--data-binary"
            "@$tempFile"
            "--write-out"
            "`nHTTP_STATUS:%{http_code}"
        )

        if ($IdempotencyKey) {
            $arguments += @(
                "--header"
                "Idempotency-Key: $IdempotencyKey"
            )
        }

        $output = & curl.exe @arguments

        $exitCode = $LASTEXITCODE

        $text = ($output -join "`n")

        $statusCode = -1
        $body = $text

        if ($text -match "HTTP_STATUS:(\d+)\s*$") {

            $statusCode = [int]$matches[1]

            $body = $text -replace `
                "`nHTTP_STATUS:\d+\s*$", ""
        }

        return [PSCustomObject]@{
            StatusCode     = $statusCode
            Body           = $body
            ExitCode       = $exitCode
            RequestId      = $RequestId
            IdempotencyKey = $IdempotencyKey
        }
    }
    catch {

        return [PSCustomObject]@{
            StatusCode     = -1
            Body           = $_.Exception.Message
            ExitCode       = -1
            RequestId      = $RequestId
            IdempotencyKey = $IdempotencyKey
        }
    }
    finally {

        if (Test-Path $tempFile) {
            Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        }
    }
}

# ============================================================
# STEP 1 - Create wallet
# ============================================================

Write-Host "STEP 1 - Creating wallet..." -ForegroundColor Yellow
Write-Host ""

$walletBody = @{
    id      = $walletId
    balance = $StartingBalance
    type    = "Player"
} | ConvertTo-Json -Compress

Write-Host "POST $walletUrl" -ForegroundColor DarkGray
Write-Host "RequestId: $createRequestId" -ForegroundColor DarkGray
Write-Host "Body: $walletBody" -ForegroundColor DarkGray
Write-Host ""

$walletResult = Invoke-CurlPost `
    -Url $walletUrl `
    -Json $walletBody `
    -RequestId $createRequestId `
    -IdempotencyKey ([guid]::NewGuid().ToString())

Write-Host "HTTP Status: $($walletResult.StatusCode)"
Write-Host "curl Exit  : $($walletResult.ExitCode)"

if ($walletResult.Body) {
    Write-Host "Response   : $($walletResult.Body)"
}

Write-Host ""

if ($walletResult.StatusCode -ne 201) {

    Write-Host "FAILED: Wallet was not created." -ForegroundColor Red
    Write-Host ""

    Write-Host "Expected:" -ForegroundColor Yellow
    Write-Host "  HTTP 201 Created"
    Write-Host ""

    Write-Host "Check Load Balancer logs for:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "RAW REQUEST: Method=POST, Path=/api/wallets"
    Write-Host "FORWARD POST api/wallets ..."
    Write-Host ""

    exit 1
}

Write-Host "Wallet created successfully." -ForegroundColor Green
Write-Host ""

# ============================================================
# STEP 2 - Prepare concurrent bets
# ============================================================

Write-Host "STEP 2 - Preparing two simultaneous bets..." -ForegroundColor Yellow
Write-Host ""

$betBody = @{
    walletId = $walletId
    amount   = $Amount
} | ConvertTo-Json -Compress

Write-Host "POST $betUrl" -ForegroundColor DarkGray
Write-Host "Body: $betBody" -ForegroundColor DarkGray
Write-Host ""

# ============================================================
# STEP 3 - Concurrent curl script
# ============================================================

$betScript = {

    param(
        [string]$Url,
        [string]$Json,
        [string]$RequestId,
        [string]$IdempotencyKey
    )

    $tempFile = [System.IO.Path]::GetTempFileName()

    try {

        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)

        [System.IO.File]::WriteAllText(
            $tempFile,
            $Json,
            $utf8NoBom
        )

        $arguments = @(
            "--silent"
            "--show-error"
            "--request"
            "POST"
            "--url"
            $Url

            "--header"
            "Content-Type: application/json"

            "--header"
            "X-Request-Id: $RequestId"

            "--header"
            "Idempotency-Key: $IdempotencyKey"

            "--data-binary"
            "@$tempFile"

            "--write-out"
            "`nHTTP_STATUS:%{http_code}"
        )

        $output = & curl.exe @arguments

        $exitCode = $LASTEXITCODE

        $text = ($output -join "`n")

        $statusCode = -1
        $body = $text

        if ($text -match "HTTP_STATUS:(\d+)\s*$") {

            $statusCode = [int]$matches[1]

            $body = $text -replace `
                "`nHTTP_STATUS:\d+\s*$", ""
        }

        [PSCustomObject]@{
            StatusCode     = $statusCode
            Body           = $body
            ExitCode       = $exitCode
            RequestId      = $RequestId
            IdempotencyKey = $IdempotencyKey
        }
    }
    catch {

        [PSCustomObject]@{
            StatusCode     = -1
            Body           = $_.Exception.Message
            ExitCode       = -1
            RequestId      = $RequestId
            IdempotencyKey = $IdempotencyKey
        }
    }
    finally {

        if (Test-Path $tempFile) {
            Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        }
    }
}

# ============================================================
# STEP 4 - Start both requests
# ============================================================

Write-Host "Starting BET #1..." -ForegroundColor Cyan

$job1 = Start-Job `
    -ScriptBlock $betScript `
    -ArgumentList `
        $betUrl,
        $betBody,
        $requestId1,
        $idempotency1

Write-Host "Starting BET #2..." -ForegroundColor Cyan

$job2 = Start-Job `
    -ScriptBlock $betScript `
    -ArgumentList `
        $betUrl,
        $betBody,
        $requestId2,
        $idempotency2

$jobs = @(
    $job1
    $job2
)

Write-Host ""
Write-Host "Both requests started." -ForegroundColor Green
Write-Host "Waiting for responses..." -ForegroundColor DarkGray
Write-Host ""

Wait-Job -Job $jobs | Out-Null

$result1 = Receive-Job -Job $job1
$result2 = Receive-Job -Job $job2

Remove-Job -Job $job1 -Force
Remove-Job -Job $job2 -Force

# ============================================================
# STEP 5 - Display HTTP results
# ============================================================

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host " HTTP RESULTS" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

Write-Host ""
Write-Host "REQUEST #1" -ForegroundColor White
Write-Host "RequestId      : $($result1.RequestId)"
Write-Host "IdempotencyKey : $($result1.IdempotencyKey)"
Write-Host "HTTP Status    : $($result1.StatusCode)"
Write-Host "curl ExitCode  : $($result1.ExitCode)"
Write-Host "Response       : $($result1.Body)"

Write-Host ""
Write-Host "REQUEST #2" -ForegroundColor White
Write-Host "RequestId      : $($result2.RequestId)"
Write-Host "IdempotencyKey : $($result2.IdempotencyKey)"
Write-Host "HTTP Status    : $($result2.StatusCode)"
Write-Host "curl ExitCode  : $($result2.ExitCode)"
Write-Host "Response       : $($result2.Body)"

# ============================================================
# STEP 6 - Validate concurrency
# ============================================================

$results = @(
    $result1
    $result2
)

$successful = @(
    $results |
        Where-Object {
            $_.StatusCode -eq 200 -or
            $_.StatusCode -eq 201
        }
)

$rejected = @(
    $results |
        Where-Object {
            $_.StatusCode -eq 409
        }
)

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host " CONCURRENCY RESULT" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Starting balance : $StartingBalance"
Write-Host "Bet #1           : $Amount"
Write-Host "Bet #2           : $Amount"
Write-Host "Expected balance : $($StartingBalance - $Amount)"
Write-Host ""

# ============================================================
# PASS
# ============================================================

if ($successful.Count -eq 1 -and $rejected.Count -eq 1) {

    Write-Host "PASS" -ForegroundColor Green
    Write-Host ""

    Write-Host "Exactly ONE bet succeeded." -ForegroundColor Green
    Write-Host "Exactly ONE bet was rejected with HTTP 409." -ForegroundColor Green

    Write-Host ""
    Write-Host "Expected final balance: $($StartingBalance - $Amount)" `
        -ForegroundColor Green

}
# ============================================================
# DOUBLE SPEND
# ============================================================

elseif ($successful.Count -eq 2) {

    Write-Host "FAIL" -ForegroundColor Red
    Write-Host ""

    Write-Host "BOTH bets succeeded!" -ForegroundColor Red
    Write-Host ""

    Write-Host "Possible double-spending/concurrency bug." `
        -ForegroundColor Red

    Write-Host ""

    exit 2
}
# ============================================================
# UNEXPECTED
# ============================================================

else {

    Write-Host "FAIL" -ForegroundColor Red
    Write-Host ""

    Write-Host "Unexpected result." -ForegroundColor Red
    Write-Host ""
    Write-Host "Successful requests : $($successful.Count)"
    Write-Host "Rejected requests   : $($rejected.Count)"
    Write-Host ""

    exit 3
}

# ============================================================
# STEP 7 - Expected Load Balancer logs
# ============================================================

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host " EXPECTED LOAD BALANCER LOG" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "For wallet creation:" -ForegroundColor Yellow
Write-Host ""
Write-Host "RAW REQUEST: Method=POST, Path=/api/wallets"
Write-Host "FORWARD POST api/wallets ..."
Write-Host ""

Write-Host "For the two bets:" -ForegroundColor Yellow
Write-Host ""
Write-Host "RAW REQUEST: Method=POST, Path=/api/bets"
Write-Host "FORWARD POST api/bets ..."
Write-Host ""
Write-Host "RAW REQUEST: Method=POST, Path=/api/bets"
Write-Host "FORWARD POST api/bets ..."
Write-Host ""

Write-Host "The two bets may be routed to different GameServers"
Write-Host "depending on the current round-robin state."
Write-Host ""

# ============================================================
# STEP 8 - Database reminder
# ============================================================

Write-Host "============================================" -ForegroundColor Cyan
Write-Host " DATABASE VERIFICATION" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "After PASS, verify SQLite directly:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Wallet balance should be $($StartingBalance - $Amount)"
Write-Host "  Exactly one bet should be recorded"
Write-Host "  Exactly one successful ledger/debit operation"
Write-Host "  No duplicate/idempotency records"
Write-Host ""

Write-Host "Test completed." -ForegroundColor Cyan
Write-Host ""
