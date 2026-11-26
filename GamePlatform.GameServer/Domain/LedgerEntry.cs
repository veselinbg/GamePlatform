using GamePlatform.GameServer.Domain.Enums;

namespace GamePlatform.GameServer.Domain;

public sealed class LedgerEntry
{
    public Guid Id { get; set; }

    public Guid WalletId { get; set; }

    public decimal Amount { get; set; }

    public LedgerTransactionType TransactionType { get; set; }

    public string OperationId { get; set; } = null!;

    public string IdempotencyKey { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public Wallet Wallet { get; set; } = null!;
}
