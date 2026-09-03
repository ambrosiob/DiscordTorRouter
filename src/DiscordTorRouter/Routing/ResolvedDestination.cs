using System.Net;

namespace DiscordTorRouter.Routing;

public sealed record ResolvedDestination(string Host, ushort Port, IPAddress Address)
{
    public override string ToString() => $"{Host}:{Port} ({Address})";
}
