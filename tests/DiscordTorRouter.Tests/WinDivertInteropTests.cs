using System.Runtime.InteropServices;
using DiscordTorRouter.Network;

namespace DiscordTorRouter.Tests;

public sealed class WinDivertInteropTests
{
    [Fact]
    public void AddressStructureMatchesNativeAbi() => Assert.Equal(80, Marshal.SizeOf<WinDivertAddress>());

    [Fact]
    public void NativeConstantsMatchWinDivert22Header()
    {
        Assert.Equal(0x0001UL, WinDivertNative.FlagSniff);
        Assert.Equal(0x0004UL, WinDivertNative.FlagReceiveOnly);
        Assert.Equal(1, (int)WinDivertShutdown.Receive);
        Assert.Equal(2, (int)WinDivertShutdown.Send);
        Assert.Equal(3, (int)WinDivertShutdown.Both);
    }

    [Fact]
    public void SocketAndNetworkFiltersCompileWithOfficialLibrary()
    {
        WinDivertNative.ValidateFilter("tcp and (event == CONNECT or event == CLOSE)", WinDivertLayer.Socket);
        WinDivertNative.ValidateFilter(TcpRedirector.BuildFilter([443, 8443], 15000), WinDivertLayer.Network);
    }
}
