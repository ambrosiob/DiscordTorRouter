using System.Buffers.Binary;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using DiscordTorRouter.Infrastructure;
using DiscordTorRouter.Routing;

namespace DiscordTorRouter.Network;

public sealed class TcpRedirector(
    FlowTracker flowTracker,
    RoutingPolicy policy,
    GatewayResolver resolver,
    RedirectRegistry registry,
    int relayPort = 15000) : IAsyncDisposable
{
    private const int MaximumPacket = 0xFFFF;
    private WinDivertHandle? _handle;
    private CancellationTokenSource? _cancellation;
    private Task? _worker;

    public void Start(IEnumerable<ushort> destinationPorts)
    {
        if (_worker is not null) return;
        var filter = BuildFilter(destinationPorts, checked((ushort)relayPort));
        _cancellation = new CancellationTokenSource();
        _handle = WinDivertNative.OpenChecked(filter, WinDivertLayer.Network);
        _worker = Task.Run(() => PacketLoop(_cancellation.Token));
        AppLog.Info($"TCP redirector started with filter: {filter}");
    }

    internal static string BuildFilter(IEnumerable<ushort> destinationPorts, ushort localRelayPort)
    {
        var ports = destinationPorts.Append(localRelayPort).Distinct().Order().ToArray();
        var portFilter = string.Join(" or ", ports.Select(port => $"tcp.DstPort == {port}"));
        return $"tcp and !loopback and outbound and (({portFilter}) or tcp.SrcPort == {localRelayPort})";
    }

    private void PacketLoop(CancellationToken cancellationToken)
    {
        var packet = Marshal.AllocHGlobal(MaximumPacket);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var address = WinDivertAddress.Create();
                if (!WinDivertNative.WinDivertRecv(_handle!, packet, MaximumPacket, out var length, ref address))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "WinDivertRecv falhou.");
                }
                ProcessPacket(packet, length, ref address);
                if (!WinDivertNative.WinDivertSend(_handle!, packet, length, out _, ref address) && !cancellationToken.IsCancellationRequested)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "WinDivertSend falhou.");
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Error("TCP redirector stopped unexpectedly; matching packets remain blocked while the handle is open", ex);
        }
        finally { Marshal.FreeHGlobal(packet); }
    }

    private void ProcessPacket(IntPtr packet, uint length, ref WinDivertAddress address)
    {
        if (!WinDivertNative.WinDivertHelperParsePacket(packet, length, out var ipv4, out var ipv6, out var protocol, out _, out _, out var tcp, out _, out _, out _, out _, out _) || tcp == IntPtr.Zero)
            return;
        var sourcePort = ReadNetworkPort(tcp, 0);
        var destinationPort = ReadNetworkPort(tcp, 2);
        var sourceAddress = ReadAddress(ipv4, ipv6, source: true);
        var destinationAddress = ReadAddress(ipv4, ipv6, source: false);

        if (sourcePort == relayPort && registry.TryGet(destinationAddress, destinationPort, out var redirected))
        {
            WriteNetworkPort(tcp, 0, redirected.DestinationPort);
            SwapAddresses(ipv4, ipv6);
            address.SetOutbound(false);
            WinDivertNative.WinDivertHelperCalcChecksums(packet, length, ref address, 0);
            return;
        }

        if (!resolver.Contains(destinationAddress, destinationPort)) return;
        var key = new FlowKey(sourceAddress, sourcePort, destinationAddress, destinationPort, protocol);
        if (!flowTracker.TryGet(key, out var connection) || !policy.ShouldRouteThroughTor(connection)) return;
        var host = resolver.FindHost(destinationAddress, destinationPort) ?? destinationAddress.ToString();
        registry.Record(new RedirectedConnection(host, destinationAddress, destinationPort, sourcePort, DateTimeOffset.UtcNow));
        WriteNetworkPort(tcp, 2, checked((ushort)relayPort));
        SwapAddresses(ipv4, ipv6);
        address.SetOutbound(false);
        WinDivertNative.WinDivertHelperCalcChecksums(packet, length, ref address, 0);
        AppLog.Info($"Discord destination intercepted: {host}:{destinationPort}");
    }

    private static ushort ReadNetworkPort(IntPtr tcp, int offset)
    {
        var copy = new byte[2];
        Marshal.Copy(IntPtr.Add(tcp, offset), copy, 0, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(copy);
    }

    private static void WriteNetworkPort(IntPtr tcp, int offset, ushort port)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, port);
        Marshal.Copy(bytes, 0, IntPtr.Add(tcp, offset), 2);
    }

    private static IPAddress ReadAddress(IntPtr ipv4, IntPtr ipv6, bool source)
    {
        var length = ipv4 != IntPtr.Zero ? 4 : 16;
        var offset = ipv4 != IntPtr.Zero ? (source ? 12 : 16) : (source ? 8 : 24);
        var bytes = new byte[length];
        Marshal.Copy(IntPtr.Add(ipv4 != IntPtr.Zero ? ipv4 : ipv6, offset), bytes, 0, length);
        return GatewayResolver.Normalize(new IPAddress(bytes));
    }

    private static void SwapAddresses(IntPtr ipv4, IntPtr ipv6)
    {
        var header = ipv4 != IntPtr.Zero ? ipv4 : ipv6;
        var length = ipv4 != IntPtr.Zero ? 4 : 16;
        var sourceOffset = ipv4 != IntPtr.Zero ? 12 : 8;
        var destinationOffset = ipv4 != IntPtr.Zero ? 16 : 24;
        var source = new byte[length]; var destination = new byte[length];
        Marshal.Copy(IntPtr.Add(header, sourceOffset), source, 0, length);
        Marshal.Copy(IntPtr.Add(header, destinationOffset), destination, 0, length);
        Marshal.Copy(destination, 0, IntPtr.Add(header, sourceOffset), length);
        Marshal.Copy(source, 0, IntPtr.Add(header, destinationOffset), length);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cancellation is null) return;
        await _cancellation.CancelAsync();
        if (_handle is not null && !_handle.IsInvalid) WinDivertNative.WinDivertShutdown(_handle, WinDivertShutdown.Both);
        if (_worker is not null) await _worker;
        _handle?.Dispose(); _cancellation.Dispose();
        _handle = null; _cancellation = null; _worker = null;
        registry.Clear();
        AppLog.Info("TCP redirector stopped");
    }
}
