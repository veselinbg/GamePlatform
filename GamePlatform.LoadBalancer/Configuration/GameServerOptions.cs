namespace GamePlatform.LoadBalancer.Configuration;

public sealed class GameServerOptions
{
    public List<GameServerEndpoint> GameServers { get; set; } = [];

    public LoadBalancerOptions LoadBalancer { get; set; } = new();
}

public sealed class GameServerEndpoint
{
    public string Name { get; set; } = null!;

    public string BaseUrl { get; set; } = null!;
}

public sealed class LoadBalancerOptions
{
    public int RequestTimeoutSeconds { get; set; } = 10;

    public int HealthCheckIntervalSeconds { get; set; } = 5;

    public string Strategy { get; set; } = "RoundRobin";
}
