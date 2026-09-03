using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using DiscordTorRouter.Infrastructure;
using DiscordTorRouter.Network;

namespace DiscordTorRouter.Routing;

public sealed class FlowTracker : IAsyncDisposable
{
    private readonly ConcurrentDictionary<FlowKey, ConnectionInfo> _flows = new();
    private readonly ConcurrentDictionary<ulong, FlowKey> _endpoints = new();
    private CancellationTokenSource? _cancellation;
    private WinDivertHandle? _handle;
    private Task? _worker;

    public void Start()
    {
        if (_worker is not null) return;
        _cancellation = new CancellationTokenSource();
        _handle = WinDivertNative.OpenChecked(
            "tcp and (event == CONNECT or event == CLOSE)",
            WinDivertLayer.Socket,
            flags: WinDivertNative.FlagSniff | WinDivertNative.FlagReceiveOnly);
        _worker = Task.Run(() => TrackLoop(_cancellation.Token));
        AppLog.Info("Flow tracker started");
    }

    public bool TryGet(FlowKey key, out ConnectionInfo connection) => _flows.TryGetValue(Normalize(key), out connection!);

    private void TrackLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var address = WinDivertAddress.Create();
            if (!WinDivertNative.WinDivertRecv(_handle!, IntPtr.Zero, 0, out _, ref address))
            {
                if (cancellationToken.IsCancellationRequested) break;
                AppLog.Warn($"WinDivert socket receive failed: {Marshal.GetLastWin32Error()}");
                continue;
            }
            var endpoint = BitConverter.ToUInt64(address.Data, 0);
            if (address.Event == 7)
            {
                if (_endpoints.TryRemove(endpoint, out var oldKey)) _flows.TryRemove(oldKey, out _);
                continue;
            }
            if (address.Event != 4) continue;
            var processId = BitConverter.ToInt32(address.Data, 16);
            var local = ReadAddress(address.Data, 20);
            var remote = ReadAddress(address.Data, 36);
            var localPort = BitConverter.ToUInt16(address.Data, 52);
            var remotePort = BitConverter.ToUInt16(address.Data, 54);
            var protocol = address.Data[56];
            if (localPort == 0 || remotePort == 0) continue;
            var processName = GetProcessName(processId);
            var connection = new ConnectionInfo(processId, processName, local, localPort, remote, remotePort, protocol, DateTimeOffset.UtcNow);
            var key = Normalize(new FlowKey(local, localPort, remote, remotePort, protocol));
            _flows[key] = connection;
            _endpoints[endpoint] = key;
        }
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.ProcessName.Equals("Discord", StringComparison.OrdinalIgnoreCase)) return process.ProcessName;
            var executable = process.MainModule?.FileName;
            var expectedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord") + Path.DirectorySeparatorChar;
            return executable is not null && Path.GetFullPath(executable).StartsWith(Path.GetFullPath(expectedRoot), StringComparison.OrdinalIgnoreCase)
                ? "Discord"
                : string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return string.Empty; }
    }

    private static IPAddress ReadAddress(byte[] data, int offset)
    {
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var text = new StringBuilder(64);
            var pointer = IntPtr.Add(pinned.AddrOfPinnedObject(), offset);
            if (!WinDivertNative.WinDivertHelperFormatIPv6Address(pointer, text, 64))
                throw new InvalidOperationException("WinDivert não pôde formatar um endereço de flow.");
            return GatewayResolver.Normalize(IPAddress.Parse(text.ToString()));
        }
        finally { pinned.Free(); }
    }

    private static FlowKey Normalize(FlowKey key) => new(
        GatewayResolver.Normalize(key.LocalAddress), key.LocalPort,
        GatewayResolver.Normalize(key.RemoteAddress), key.RemotePort, key.Protocol);

    public async ValueTask DisposeAsync()
    {
        if (_cancellation is null) return;
        await _cancellation.CancelAsync();
        if (_handle is not null && !_handle.IsInvalid) WinDivertNative.WinDivertShutdown(_handle, WinDivertShutdown.Both);
        if (_worker is not null) await _worker;
        _handle?.Dispose();
        _cancellation.Dispose();
        _handle = null; _cancellation = null; _worker = null;
        _flows.Clear(); _endpoints.Clear();
        AppLog.Info("Flow tracker stopped");
    }
}
