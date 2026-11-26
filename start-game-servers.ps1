$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "GamePlatform.GameServer"

$servers = @(
    @{ Name = "GameServer1"; Port = 6001 },
    @{ Name = "GameServer2"; Port = 6002 },
    @{ Name = "GameServer3"; Port = 6003 },
    @{ Name = "GameServer4"; Port = 6004 },
    @{ Name = "GameServer5"; Port = 6005 }
)

foreach ($server in $servers) {

    Write-Host "Starting $($server.Name) on port $($server.Port)..." -ForegroundColor Green

    $env:GAME_SERVER_NAME = $server.Name

    Start-Process `
        -FilePath "dotnet" `
        -ArgumentList @(
            "run",
            "--project",
            "`"$project`"",
            "--no-launch-profile",
            "--urls",
            "http://localhost:$($server.Port)"
        ) `
        -WorkingDirectory $PSScriptRoot `
        -WindowStyle Normal
}

Write-Host ""
Write-Host "Five Game Servers started." -ForegroundColor Cyan
Write-Host "6001  GameServer1"
Write-Host "6002  GameServer2"
Write-Host "6003  GameServer3"
Write-Host "6004  GameServer4"
Write-Host "6005  GameServer5"
