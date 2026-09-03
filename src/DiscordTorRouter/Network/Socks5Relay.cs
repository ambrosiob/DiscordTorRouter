using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DiscordTorRouter.Infrastructure;

namespace DiscordTorRouter.Network;

public sealed class Socks5Relay(RedirectRegistry registry, int listenPort = 15000) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, Task> _connections = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Task? _acceptLoop;
    private int _connectionId;
    public int ListenPort { get; } = listenPort;
    public bool IsRunning => _listener is not null;

    public void Start()
    {
        if (_listener is not null) return;
        _cancellation = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.IPv6Any, ListenPort);
        _listener.Server.DualMode = true;
        _listener.Start(128);
        _acceptLoop = AcceptLoopAsync(_cancellation.Token);
        AppLog.Info($"SOCKS5 relay listening on local port {ListenPort}");
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                var id = Interlocked.Increment(ref _connectionId);
                var task = HandleConnectionAsync(client, cancellationToken);
                _connections[id] = task;
                _ = task.ContinueWith(completed => _connections.TryRemove(id, out var removed), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            if (client.Client.RemoteEndPoint is not IPEndPoint peer || !registry.TryGet(peer.Address, checked((ushort)peer.Port), out var destination))
            {
                AppLog.Warn($"Relay rejected an uncorrelated connection from {client.Client.RemoteEndPoint}");
                return;
            }
            try
            {
                using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                using var tor = await Socks5Client.ConnectAsync("127.0.0.1", 9050, destination, handshakeTimeout.Token);
                AppLog.Info($"Routing Discord connection to {destination.Host}:{destination.DestinationPort} through Tor");
                var source = client.GetStream();
                var target = tor.GetStream();
                var upstream = CopyAndHalfCloseAsync(source, target, tor.Client, cancellationToken);
                var downstream = CopyAndHalfCloseAsync(target, source, client.Client, cancellationToken);
                await Task.WhenAll(upstream, downstream);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
            {
                AppLog.Error($"Relay connection to {destination.Host}:{destination.DestinationPort} failed", ex);
            }
        }
    }

    private static async Task CopyAndHalfCloseAsync(NetworkStream source, NetworkStream destination, Socket destinationSocket, CancellationToken cancellationToken)
    {
        try { await source.CopyToAsync(destination, 81920, cancellationToken); }
        catch (IOException) when (cancellationToken.IsCancellationRequested) { }
        finally { try { destinationSocket.Shutdown(SocketShutdown.Send); } catch (SocketException) { } }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cancellation is null) return;
        await _cancellation.CancelAsync();
        _listener?.Stop();
        if (_acceptLoop is not null) await _acceptLoop;
        await Task.WhenAll(_connections.Values);
        _cancellation.Dispose();
        _listener = null; _acceptLoop = null; _cancellation = null;
        AppLog.Info("SOCKS5 relay stopped");
    }
}
