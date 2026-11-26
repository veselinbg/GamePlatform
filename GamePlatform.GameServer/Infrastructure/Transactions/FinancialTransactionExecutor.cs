using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GamePlatform.GameServer.Infrastructure.Transactions;

public sealed class FinancialTransactionExecutor
{
    private readonly IDbContextFactory<Persistence.GameDbContext> _factory;
    private readonly ILogger<FinancialTransactionExecutor> _logger;

    public FinancialTransactionExecutor(
        IDbContextFactory<Persistence.GameDbContext> factory,
        ILogger<FinancialTransactionExecutor> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(
        Func<Persistence.GameDbContext, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken);

            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.ReadCommitted,
                    cancellationToken);

            try
            {
                var result = await operation(db, cancellationToken);

                await db.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return result;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < maxAttempts)
            {
                await transaction.RollbackAsync(CancellationToken.None);

                _logger.LogWarning(
                    ex,
                    "Optimistic concurrency conflict. Attempt {Attempt}/{MaxAttempts}",
                    attempt,
                    maxAttempts);

                await Task.Delay(
                    TimeSpan.FromMilliseconds(25 * attempt),
                    cancellationToken);
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
                when ((ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6)
                       && attempt < maxAttempts)
            {
                await transaction.RollbackAsync(CancellationToken.None);

                _logger.LogWarning(
                    ex,
                    "SQLite busy/locked. Attempt {Attempt}/{MaxAttempts}",
                    attempt,
                    maxAttempts);

                await Task.Delay(
                    TimeSpan.FromMilliseconds(50 * attempt),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            "Financial transaction failed after maximum retry attempts.");
    }
}
