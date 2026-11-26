using GamePlatform.LoadBalancer.Configuration;
using GamePlatform.LoadBalancer.Health;
using GamePlatform.LoadBalancer.Routing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<List<GameServerEndpoint>>(
    builder.Configuration.GetSection("GameServers"));

builder.Services.AddControllers();

builder.Services.AddSingleton<IServerSelector, RoundRobinServerSelector>();

builder.Services.AddSingleton<ServerHealthMonitor>();

builder.Services.AddHttpClient("health", client =>
{
    client.Timeout = TimeSpan.FromSeconds(2);
});

builder.Services.AddHttpClient("proxy", client =>
{
    var timeout =
        builder.Configuration
            .GetValue<int?>("LoadBalancer:RequestTimeoutSeconds")
        ?? 10;

    client.Timeout = TimeSpan.FromSeconds(timeout);
});

builder.Services.AddHostedService(
    provider => provider.GetRequiredService<ServerHealthMonitor>());

var app = builder.Build();

app.Use(async (context, next) =>
{
    var requestId =
        context.Request.Headers["X-Request-Id"]
            .FirstOrDefault()
        ?? Guid.NewGuid().ToString("N");

    context.Response.Headers["X-Request-Id"] = requestId;

    app.Logger.LogWarning(
        "RAW REQUEST: Method={Method}, Path={Path}, RequestId={RequestId}",
        context.Request.Method,
        context.Request.Path,
        requestId);

    await next();
});


app.MapControllers();

app.Run();
