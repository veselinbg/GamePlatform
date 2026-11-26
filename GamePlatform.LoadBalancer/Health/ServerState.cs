namespace GamePlatform.LoadBalancer.Health;

public sealed class ServerState
{
    public required string Name { get; init; }

    public required string BaseUrl { get; init; }

    public bool Healthy { get; set; }
}
