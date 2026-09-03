using DiscordTorRouter.Infrastructure;
using DiscordTorRouter.Network;

namespace DiscordTorRouter.Routing;

public sealed class RoutingEngine(GatewayResolver resolver) : IAsyncDisposable
{
    private readonly RedirectRegistry _registry = new();
    private FlowTracker? _flowTracker;
    private Socks5Relay? _relay;
    private TcpRedirector? _redirector;
    public bool IsReady { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsReady) return;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _flowTracker = new FlowTracker();
            try { _flowTracker.Start(); }
            catch (Exception ex) { throw new InvalidOperationException($"Falha ao iniciar a correlação de conexões do WinDivert: {ex.Message}", ex); }
            _relay = new Socks5Relay(_registry);
            try { _relay.Start(); }
            catch (Exception ex) { throw new InvalidOperationException($"Falha ao abrir o relay TCP local na porta 15000: {ex.Message}", ex); }
            _redirector = new TcpRedirector(_flowTracker, new RoutingPolicy(resolver), resolver, _registry);
            try { _redirector.Start(resolver.Resolved.Select(x => x.Port)); }
            catch (Exception ex) { throw new InvalidOperationException($"Falha ao iniciar o redirecionamento de pacotes do WinDivert: {ex.Message}", ex); }
            IsReady = true;
            AppLog.Info("Routing engine ready");
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        IsReady = false;
        if (_redirector is not null) await _redirector.DisposeAsync();
        if (_relay is not null) await _relay.DisposeAsync();
        if (_flowTracker is not null) await _flowTracker.DisposeAsync();
        _redirector = null; _relay = null; _flowTracker = null;
        AppLog.Info("Routing engine stopped");
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
