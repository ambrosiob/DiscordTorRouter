using System.Net;

namespace DiscordTorRouter.Routing;

public sealed record ConnectionInfo(
    int ProcessId,
    string ProcessName,
    IPAddress LocalAddress,
    ushort LocalPort,
    IPAddress RemoteAddress,
    ushort RemotePort,
    byte Protocol,
    DateTimeOffset ObservedAt);

public readonly record struct FlowKey(IPAddress LocalAddress, ushort LocalPort, IPAddress RemoteAddress, ushort RemotePort, byte Protocol);
public readonly record struct RedirectKey(IPAddress RemoteAddress, ushort ClientPort);
