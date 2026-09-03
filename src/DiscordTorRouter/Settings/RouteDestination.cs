using System.Net;

namespace DiscordTorRouter.Settings;

public sealed record RouteDestination(string Host, ushort Port = 443, bool Enabled = true)
{
    public static RouteDestination Default { get; } = new("gateway.discord.gg", 443);
    public override string ToString() => Host.Contains(':') && IPAddress.TryParse(Host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
        ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
}
