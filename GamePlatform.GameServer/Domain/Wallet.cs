using GamePlatform.GameServer.Domain.Enums;

namespace GamePlatform.GameServer.Domain;

public sealed class Wallet
{
    public Guid Id { get; set; }

    public decimal Balance { get; set; }

    public WalletType Type { get; set; }

    public long Version { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();

    public ICollection<Bet> Bets { get; set; } = new List<Bet>();
}
