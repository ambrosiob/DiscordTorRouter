using System.Collections.Immutable;
using System.Net;
using DiscordTorRouter.Infrastructure;
using DiscordTorRouter.Settings;

namespace DiscordTorRouter.Routing;

public sealed class GatewayResolver : IAsyncDisposable
{
    private readonly object _sync = new();
    private ImmutableArray<RouteDestination> _configured = [];
    private ImmutableArray<ResolvedDestination> _resolved = [];
    private CancellationTokenSource? _loopCancellation;
    private Task? _loop;

    public IReadOnlyList<ResolvedDestination> Resolved { get { lock (_sync) return _resolved; } }
    public event EventHandler? Updated;

    public void Configure(IEnumerable<RouteDestination> destinations)
    {
        lock (_sync) _configured = destinations.Where(x => x.Enabled).ToImmutableArray();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ImmutableArray<RouteDestination> configured;
        lock (_sync) configured = _configured;
        var resolved = new List<ResolvedDestination>();
        foreach (var destination in configured)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var addresses = IPAddress.TryParse(destination.Host, out var literal)
                    ? [literal]
                    : await Dns.GetHostAddressesAsync(destination.Host, cancellationToken);
                resolved.AddRange(addresses.Distinct().Select(address => new ResolvedDestination(destination.Host, destination.Port, Normalize(address))));
                AppLog.Info($"Resolved {destination}: {string.Join(", ", addresses.Select(x => x.ToString()))}");
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                AppLog.Error($"Could not resolve {destination}", ex);
            }
        }
        lock (_sync) _resolved = resolved.DistinctBy(x => (x.Address, x.Port)).ToImmutableArray();
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public bool Contains(IPAddress address, ushort port)
    {
        address = Normalize(address);
        lock (_sync) return _resolved.Any(x => x.Port == port && x.Address.Equals(address));
    }

    public string? FindHost(IPAddress address, ushort port)
    {
        address = Normalize(address);
        lock (_sync) return _resolved.FirstOrDefault(x => x.Port == port && x.Address.Equals(address))?.Host;
    }

    public void StartPeriodicRefresh(TimeSpan? interval = null)
    {
        if (_loop is not null) return;
        _loopCancellation = new CancellationTokenSource();
        _loop = RefreshLoopAsync(interval ?? TimeSpan.FromMinutes(5), _loopCancellation.Token);
    }

    private async Task RefreshLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try { while (await timer.WaitForNextTickAsync(cancellationToken)) await RefreshAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    public async ValueTask DisposeAsync()
    {
        if (_loopCancellation is not null) await _loopCancellation.CancelAsync();
        if (_loop is not null) await _loop;
        _loopCancellation?.Dispose();
    }
}
