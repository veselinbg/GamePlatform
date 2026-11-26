namespace GamePlatform.GameServer.Application.Bets;

public sealed record PlaceBetRequest(
    Guid WalletId,
    decimal Amount);
