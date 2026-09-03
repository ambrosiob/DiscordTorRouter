using System.Net;
using DiscordTorRouter.Routing;
using DiscordTorRouter.Settings;

namespace DiscordTorRouter.Tests;

public sealed class RoutingPolicyTests
{
    [Fact]
    public async Task RoutesOnlyDiscordTcpAndConfiguredAddress()
    {
        await using var resolver = new GatewayResolver();
        resolver.Configure([new RouteDestination("127.0.0.1", 443)]);
        await resolver.RefreshAsync();
        var policy = new RoutingPolicy(resolver);
        var matching = new ConnectionInfo(10, "Discord", IPAddress.Loopback, 50000, IPAddress.Loopback, 443, 6, DateTimeOffset.UtcNow);
        Assert.True(policy.ShouldRouteThroughTor(matching));
        Assert.False(policy.ShouldRouteThroughTor(matching with { ProcessName = "chrome" }));
        Assert.False(policy.ShouldRouteThroughTor(matching with { RemotePort = 80 }));
        Assert.False(policy.ShouldRouteThroughTor(matching with { Protocol = 17 }));
    }
}
