using GamePlatform.GameServer.Domain;
using GamePlatform.GameServer.Domain.Enums;
using GamePlatform.GameServer.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GamePlatform.GameServer.Api.Controllers;

[ApiController]
[Route("api/wallets")]
public sealed class WalletsController : ControllerBase
{
    private readonly GameDbContext _db;

    public WalletsController(GameDbContext db)
    {
        _db = db;
    }

    [HttpGet("{walletId:guid}/balance/verify")]
    public async Task<IActionResult> VerifyBalance(
        Guid walletId,
        CancellationToken cancellationToken)
    {
        var wallet = await _db.Wallets
            .SingleOrDefaultAsync(x => x.Id == walletId, cancellationToken);

        if (wallet is null)
            return NotFound();

        var ledgerBalance = await _db.LedgerEntries
            .Where(x => x.WalletId == walletId)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        return Ok(new
        {
            walletId,
            walletBalance = wallet.Balance,
            ledgerBalance,
            consistent = wallet.Balance == ledgerBalance,
            version = wallet.Version
        });
    }
    [HttpPost]
    public async Task<IActionResult> Create(
           [FromBody] CreateWalletRequest request,
           CancellationToken cancellationToken)
    {
        if (request.Id == Guid.Empty)
        {
            return BadRequest(new
            {
                error = "Wallet id is required."
            });
        }

        if (request.Balance < 0)
        {
            return BadRequest(new
            {
                error = "Initial balance cannot be negative."
            });
        }

        var existing =
            await _db.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.Id,
                    cancellationToken);

        if (existing is not null)
        {
            return Conflict(new
            {
                error = "Wallet already exists.",
                walletId = existing.Id
            });
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        var wallet = new Wallet
        {
            Id = request.Id,
            Balance = request.Balance,
            Type = Enum.TryParse<WalletType>(request.Type, true, out var walletType)
    ? walletType
    : WalletType.Player,
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Wallets.Add(wallet);

        /*
         * The initial balance must also exist in the ledger.
         * Otherwise the wallet projection cannot be reconciled
         * against the ledger.
         */
        if (request.Balance > 0)
        {
            _db.LedgerEntries.Add(
                new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    WalletId = wallet.Id,
                    Amount = request.Balance,
                    TransactionType =
                        Domain.Enums.LedgerTransactionType.Deposit,
                    OperationId =
                        $"wallet-init-{wallet.Id:N}",
                    IdempotencyKey =
                        $"wallet-init-{wallet.Id:N}",
                    CreatedAt = DateTime.UtcNow
                });
        }

        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return Created(
            $"/api/wallets/{wallet.Id}",
            new
            {
                wallet.Id,
                wallet.Balance,
                wallet.Type,
                wallet.Version
            });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        var wallet =
            await _db.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);

        if (wallet is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            wallet.Id,
            wallet.Balance,
            wallet.Type,
            wallet.Version
        });
    }
}

public sealed class CreateWalletRequest
{
    public Guid Id { get; set; }

    public decimal Balance { get; set; }

    public string? Type { get; set; }
}
