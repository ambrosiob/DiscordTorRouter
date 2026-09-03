namespace DiscordTorRouter.Routing;

public sealed class RoutingPolicy(GatewayResolver resolver)
{
    public bool ShouldRouteThroughTor(ConnectionInfo connection) =>
        connection.Protocol == 6 &&
        connection.RemotePort > 0 &&
        connection.ProcessName.Equals("Discord", StringComparison.OrdinalIgnoreCase) &&
        resolver.Contains(connection.RemoteAddress, connection.RemotePort);
}
