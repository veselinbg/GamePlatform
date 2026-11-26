using GamePlatform.GameServer.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GamePlatform.GameServer.Health;

public sealed class DatabaseReadinessCheck : IHealthCheck
{
    private readonly GameDbContext _db;

    public DatabaseReadinessCheck(GameDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var healthy =
                await _db.Database.CanConnectAsync(cancellationToken);

            return healthy
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database unavailable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Database unavailable.",
                ex);
        }
    }
}
