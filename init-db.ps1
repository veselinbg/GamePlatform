param(
    [string]$ConnectionString = "Data Source=game.db"
)

$env:ConnectionStrings__GameDatabase = $ConnectionString

dotnet run --project ../GamePlatform.GameServer
