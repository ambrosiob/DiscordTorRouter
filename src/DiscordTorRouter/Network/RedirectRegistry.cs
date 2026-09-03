using System.Collections.Concurrent;
using System.Net;
using DiscordTorRouter.Routing;

namespace DiscordTorRouter.Network;

public sealed record RedirectedConnection(string Host, IPAddress Address, ushort DestinationPort, ushort ClientPort, DateTimeOffset CreatedAt);

public sealed class RedirectRegistry
{
    private readonly ConcurrentDictionary<RedirectKey, RedirectedConnection> _items = new();

    public void Record(RedirectedConnection connection)
    {
        Cleanup();
        _items[new RedirectKey(GatewayResolver.Normalize(connection.Address), connection.ClientPort)] = connection;
    }

    public bool TryGet(IPAddress remoteAddress, ushort clientPort, out RedirectedConnection connection) =>
        _items.TryGetValue(new RedirectKey(GatewayResolver.Normalize(remoteAddress), clientPort), out connection!);

    private void Cleanup()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-10);
        foreach (var item in _items.Where(x => x.Value.CreatedAt < cutoff)) _items.TryRemove(item.Key, out _);
    }

    public void Clear() => _items.Clear();
}
