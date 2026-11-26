using GamePlatform.GameServer.Application.Bets;
using GamePlatform.GameServer.Health;
using GamePlatform.GameServer.Infrastructure.Persistence;
using GamePlatform.GameServer.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var connectionString =
    builder.Configuration.GetConnectionString("GameDatabase")
    ?? "Data Source=game.db;Cache=Shared";

builder.Services.AddDbContext<GameDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

builder.Services.AddDbContextFactory<GameDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

builder.Services.AddScoped<PlaceBetService>();
builder.Services.AddScoped<FinancialTransactionExecutor>();

builder.Services
    .AddHealthChecks()
    .AddCheck<DatabaseReadinessCheck>(
        "database",
        HealthStatus.Unhealthy);

var app = builder.Build();

app.Use(async (context, next) =>
{
    var requestId =
        context.Request.Headers["X-Request-Id"].FirstOrDefault()
        ?? Guid.NewGuid().ToString("N");

    context.Response.Headers["X-Request-Id"] = requestId;

    using var scope = app.Logger.BeginScope(
        new Dictionary<string, object>
        {
            ["RequestId"] = requestId,
            ["ServerInstance"] =
                Environment.GetEnvironmentVariable("GAME_SERVER_NAME")
                ?? "unknown"
        });

    await next();
});

app.MapControllers();

app.MapHealthChecks(
    "/health/live",
    new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false
    });

app.MapHealthChecks(
    "/health/ready",
    new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => true
    });

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();

    await db.Database.EnsureCreatedAsync();

    await db.Database.ExecuteSqlRawAsync(
        "PRAGMA journal_mode = WAL;");

    await db.Database.ExecuteSqlRawAsync(
        "PRAGMA busy_timeout = 5000;");
}

app.Run();

public partial class Program;
