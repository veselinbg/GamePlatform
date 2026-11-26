using System.Text.Json;
using GamePlatform.GameServer.Domain;
using GamePlatform.GameServer.Domain.Enums;
using GamePlatform.GameServer.Infrastructure.Persistence;
using GamePlatform.GameServer.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace GamePlatform.GameServer.Application.Bets;

public sealed class PlaceBetService
{
    private readonly FinancialTransactionExecutor _transactions;
    private readonly ILogger<PlaceBetService> _logger;

    public PlaceBetService(
        FinancialTransactionExecutor transactions,
        ILogger<PlaceBetService> logger)
    {
        _transactions = transactions;
        _logger = logger;
    }

    public async Task<BetResult> ExecuteAsync(
        PlaceBetRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException(
                "Idempotency-Key is required.");
        }

        if (request.Amount <= 0)
        {
            throw new ArgumentException(
                "Bet amount must be greater than zero.");
        }

        return await _transactions.ExecuteAsync(
            async (db, ct) =>
            {
                /*
                 * IMPORTANT:
                 *
                 * The idempotency record is checked inside the
                 * same financial transaction as the wallet update.
                 *
                 * The database UNIQUE constraint on IdempotencyKey
                 * is the final authority when two different
                 * Game Server processes race.
                 */

                var existing =
                    await db.IdempotencyRecords
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            x => x.IdempotencyKey == idempotencyKey,
                            ct);

                if (existing is not null)
                {
                    var existingResult =
                        JsonSerializer.Deserialize<BetResult>(
                            existing.ResponsePayload);

                    if (existingResult is null)
                    {
                        throw new InvalidOperationException(
                            "Stored idempotency result is invalid.");
                    }

                    _logger.LogInformation(
                        "Returning existing idempotent result. " +
                        "IdempotencyKey={IdempotencyKey}, " +
                        "OperationId={OperationId}",
                        idempotencyKey,
                        existing.OperationId);

                    return existingResult;
                }

                /*
                 * Load the wallet as a tracked entity.
                 *
                 * Version is configured as an EF Core concurrency
                 * token. EF will therefore include the original
                 * Version value in the UPDATE WHERE clause.
                 */

                var wallet =
                    await db.Wallets
                        .SingleOrDefaultAsync(
                            x => x.Id == request.WalletId,
                            ct);

                if (wallet is null)
                {
                    throw new KeyNotFoundException(
                        "Wallet not found.");
                }

                if (wallet.Balance < request.Amount)
                {
                    throw new InsufficientBalanceException(
                        wallet.Id,
                        request.Amount,
                        wallet.Balance);
                }

                var operationId =
                    Guid.NewGuid().ToString("N");

                var now = DateTime.UtcNow;

                var bet = new Bet
                {
                    Id = Guid.NewGuid(),
                    WalletId = wallet.Id,
                    Amount = request.Amount,
                    OperationId = operationId,
                    Status = BetStatus.Accepted,
                    CreatedAt = now
                };

                var ledger = new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    WalletId = wallet.Id,
                    Amount = -request.Amount,
                    TransactionType =
                        LedgerTransactionType.Bet,
                    OperationId = operationId,
                    IdempotencyKey = idempotencyKey,
                    CreatedAt = now
                };

                /*
                 * Update the balance projection.
                 *
                 * The original wallet.Version is retained by EF
                 * as the concurrency value.
                 */

                wallet.Balance -= request.Amount;
                wallet.Version++;
                wallet.UpdatedAt = now;

                db.Bets.Add(bet);
                db.LedgerEntries.Add(ledger);

                var result = new BetResult(
                    bet.Id,
                    wallet.Id,
                    request.Amount,
                    wallet.Balance,
                    operationId,
                    bet.Status.ToString());

                db.IdempotencyRecords.Add(
                    new IdempotencyRecord
                    {
                        Id = Guid.NewGuid(),
                        IdempotencyKey = idempotencyKey,
                        OperationId = operationId,
                        Status = "Completed",
                        ResponsePayload =
                            JsonSerializer.Serialize(result),
                        CreatedAt = now,
                        CompletedAt = now
                    });

                return result;
            },
            cancellationToken);
    }
}

public sealed class InsufficientBalanceException : Exception
{
    public Guid WalletId { get; }

    public decimal RequestedAmount { get; }

    public decimal AvailableBalance { get; }

    public InsufficientBalanceException(
        Guid walletId,
        decimal requestedAmount,
        decimal availableBalance)
        : base("Insufficient wallet balance.")
    {
        WalletId = walletId;
        RequestedAmount = requestedAmount;
        AvailableBalance = availableBalance;
    }
}
