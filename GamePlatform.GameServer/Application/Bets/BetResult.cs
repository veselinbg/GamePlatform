namespace GamePlatform.GameServer.Application.Bets;

public sealed record BetResult(
    Guid BetId,
    Guid WalletId,
    decimal Amount,
    decimal Balance,
    string OperationId,
    string Status);
