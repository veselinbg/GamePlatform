using GamePlatform.GameServer.Domain.Enums;

namespace GamePlatform.GameServer.Domain;

public sealed class Bet
{
    public Guid Id { get; set; }

    public Guid WalletId { get; set; }

    public decimal Amount { get; set; }

    public string OperationId { get; set; } = null!;

    public BetStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public Wallet Wallet { get; set; } = null!;
}
