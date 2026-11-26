using GamePlatform.LoadBalancer.Configuration;

namespace GamePlatform.LoadBalancer.Health;

public sealed class ServerHealthMonitor : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ServerHealthMonitor> _logger;

    private readonly List<ServerState> _servers = [];
    private readonly object _sync = new();

    public ServerHealthMonitor(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ServerHealthMonitor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;

        var servers =
            configuration
                .GetSection("GameServers")
                .Get<List<GameServerEndpoint>>()
            ?? [];

        foreach (var server in servers)
        {
            _servers.Add(new ServerState
            {
                Name = server.Name,
                BaseUrl = server.BaseUrl,
                Healthy = false
            });
        }
    }

    public IReadOnlyList<GameServerEndpoint> GetHealthyServers()
    {
        lock (_sync)
        {
            return _servers
                .Where(x => x.Healthy)
                .Select(x => new GameServerEndpoint
                {
                    Name = x.Name,
                    BaseUrl = x.BaseUrl
                })
                .ToList();
        }
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var interval =
            _configuration
                .GetValue<int?>(
                    "LoadBalancer:HealthCheckIntervalSeconds")
            ?? 5;

        // Perform an immediate health check rather than
        // waiting for the first interval.
        await CheckAllServers(stoppingToken);

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(interval));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CheckAllServers(stoppingToken);
        }
    }

    private async Task CheckAllServers(
        CancellationToken cancellationToken)
    {
        var client =
            _httpClientFactory.CreateClient("health");

        foreach (var server in _servers)
        {
            try
            {
                using var response =
                    await client.GetAsync(
                        $"{server.BaseUrl.TrimEnd('/')}/health/ready",
                        cancellationToken);

                var healthy = response.IsSuccessStatusCode;

                lock (_sync)
                {
                    server.Healthy = healthy;
                }

                if (!healthy)
                {
                    _logger.LogWarning(
                        "Game server {Server} returned unhealthy status {StatusCode}.",
                        server.Name,
                        (int)response.StatusCode);
                }
                else
                {
                    _logger.LogDebug(
                        "Game server {Server} is healthy.",
                        server.Name);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    server.Healthy = false;
                }

                _logger.LogWarning(
                    ex,
                    "Game server {Server} is unavailable.",
                    server.Name);
            }
        }
    }
}
