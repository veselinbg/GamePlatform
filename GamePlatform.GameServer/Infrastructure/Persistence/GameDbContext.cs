using GamePlatform.GameServer.Domain;
using Microsoft.EntityFrameworkCore;

namespace GamePlatform.GameServer.Infrastructure.Persistence;

public sealed class GameDbContext : DbContext
{
    public GameDbContext(DbContextOptions<GameDbContext> options)
        : base(options)
    {
    }

    public DbSet<Wallet> Wallets => Set<Wallet>();

    public DbSet<Bet> Bets => Set<Bet>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Wallet>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Balance)
                .HasPrecision(18, 2);

            entity.Property(x => x.Version)
                 .IsConcurrencyToken()
                 .ValueGeneratedNever();

            entity.Property(x => x.Type)
                .IsRequired();

            entity.HasIndex(x => x.Id);
        });

        modelBuilder.Entity<Bet>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Amount)
                .HasPrecision(18, 2);

            entity.Property(x => x.OperationId)
                .IsRequired();

            entity.HasIndex(x => x.OperationId)
                .IsUnique();

            entity.HasOne(x => x.Wallet)
                .WithMany(x => x.Bets)
                .HasForeignKey(x => x.WalletId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Amount)
                .HasPrecision(18, 2);

            entity.Property(x => x.OperationId)
                .IsRequired();

            entity.Property(x => x.IdempotencyKey)
                .IsRequired();

            entity.HasIndex(x => x.OperationId)
                .IsUnique();

            entity.HasIndex(x => x.IdempotencyKey)
                .IsUnique();

            entity.HasOne(x => x.Wallet)
                .WithMany(x => x.LedgerEntries)
                .HasForeignKey(x => x.WalletId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.IdempotencyKey)
                .IsRequired();

            entity.Property(x => x.OperationId)
                .IsRequired();

            entity.HasIndex(x => x.IdempotencyKey)
                .IsUnique();
        });
    }
}
