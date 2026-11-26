using GamePlatform.GameServer.Domain;
using GamePlatform.GameServer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GamePlatform.Tests.Concurrency;

public sealed class WalletConcurrencyTests
{
    [Fact]
    public async Task LedgerBalanceShouldMatchWalletBalance()
    {
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        await using var db = new GameDbContext(options);

        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        var wallet = new Wallet
        {
            Id = Guid.NewGuid(),
            Balance = 100m,
            Version = 1,
            Type = GameServer.Domain.Enums.WalletType.Player,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Wallets.Add(wallet);

        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Amount = 100m,
            TransactionType =
                GameServer.Domain.Enums.LedgerTransactionType.Deposit,
            OperationId = Guid.NewGuid().ToString("N"),
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var ledgerBalance =
            await db.LedgerEntries
                .Where(x => x.WalletId == wallet.Id)
                .SumAsync(x => x.Amount);

        Assert.Equal(wallet.Balance, ledgerBalance);
    }
}
