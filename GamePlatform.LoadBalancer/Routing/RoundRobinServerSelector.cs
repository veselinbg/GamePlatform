using System.Threading;
using GamePlatform.LoadBalancer.Configuration;

namespace GamePlatform.LoadBalancer.Routing;

public interface IServerSelector
{
    GameServerEndpoint Select(
        IReadOnlyList<GameServerEndpoint> servers);
}

public sealed class RoundRobinServerSelector : IServerSelector
{
    private long _counter = -1;

    public GameServerEndpoint Select(
        IReadOnlyList<GameServerEndpoint> servers)
    {
        if (servers.Count == 0)
        {
            throw new InvalidOperationException(
                "No healthy game servers are available.");
        }

        var index =
            (int)(
                (ulong)Interlocked.Increment(ref _counter)
                % (ulong)servers.Count);

        return servers[index];
    }
}
