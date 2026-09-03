using DiscordTorRouter.Settings;

namespace DiscordTorRouter.Tests;

public sealed class DestinationParserTests
{
    [Fact]
    public void ParsesDefaultDestination()
    {
        var destination = Assert.Single(DestinationParser.ParseLines("gateway.discord.gg:443"));
        Assert.Equal("gateway.discord.gg", destination.Host);
        Assert.Equal((ushort)443, destination.Port);
    }

    [Fact]
    public void ParsesDnsIpv4AndIpv6AndRemovesDuplicates()
    {
        var destinations = DestinationParser.ParseLines("example.com:443\n1.1.1.1:8443\n[2606:4700:4700::1111]:443\nEXAMPLE.COM:443");
        Assert.Equal(3, destinations.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("example.com:0")]
    [InlineData("example.com:99999")]
    [InlineData("bad host:443")]
    public void RejectsInvalidDestinations(string value) => Assert.Throws<FormatException>(() => DestinationParser.ParseLines(value));
}
