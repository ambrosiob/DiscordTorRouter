using DiscordTorRouter.Discord;
using DiscordTorRouter.Infrastructure;
using DiscordTorRouter.Routing;
using DiscordTorRouter.Settings;
using DiscordTorRouter.Tor;
using DiscordTorRouter.Windows;

namespace DiscordTorRouter.Application;

public sealed class AppController : IAsyncDisposable
{
    private readonly SettingsManager _settingsManager = new();
    private readonly StartupTaskManager _startupTaskManager = new();
    private readonly DiscordStartupManager _discordStartupManager = new();
    private readonly DiscordProcessManager _discord = new(new DiscordLocator());
    private readonly GatewayResolver _resolver = new();
    private readonly TorManager _tor = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RoutingEngine? _routing;

    public AppSettings Settings { get; private set; } = new();
    public ApplicationState State { get; } = new();

    private AppController()
    {
        _tor.StatusChanged += (_, status) =>
        {
            State.TorStatus = status;
            State.TorStatusText = status switch
            {
                TorStatus.Stopped => "Parado",
                TorStatus.Starting => "Iniciando",
                TorStatus.Bootstrapping => $"Conectando ({_tor.BootstrapProgress}%)",
                TorStatus.Connected => "Conectado",
                TorStatus.Stopping => "Parando",
                _ => "Erro"
            };
            if (status == TorStatus.Error && State.IsEnabled)
            {
                State.RouterStatus = RouterStatus.Error;
                State.RouterStatusText = "Erro — Tor indisponível";
                State.DestinationStatus = "Bloqueado — Tor indisponível";
            }
        };
        _tor.BootstrapProgressChanged += (_, progress) =>
        {
            State.TorStatusText = progress >= 100 ? "Conectado" : $"Conectando ({progress}%)";
            if (State.RouterStatus == RouterStatus.Starting)
                State.RouterStatusText = $"Iniciando — aguardando Tor ({progress}%)";
        };
        _resolver.Updated += (_, _) =>
        {
            State.ResolvedDestinations = _resolver.Resolved.Select(x => x.ToString()).ToArray();
            State.DestinationStatus = State.IsEnabled ? $"Protegidos: {_resolver.Resolved.Count}" : "Desprotegido";
        };
    }

    public static async Task<AppController> CreateAsync()
    {
        var controller = new AppController();
        controller.Settings = await controller._settingsManager.LoadAsync();
        controller._resolver.Configure(controller.Settings.TorDestinations);
        controller.UpdateDiscordStatus();
        return controller;
    }

    public async Task EnableAsync(bool openDiscordWhenReady = false, bool restartDiscordIfRunning = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (State.IsEnabled && State.RouterStatus == RouterStatus.Ready) return;
            var reopenDiscordWhenReady = restartDiscordIfRunning && _discord.IsRunning;
            State.IsEnabled = true;
            State.RouterStatus = RouterStatus.Starting;
            State.RouterStatusText = "Iniciando — resolvendo destinos";
            State.LastError = null;
            State.DestinationStatus = "Resolvendo...";
            if (reopenDiscordWhenReady)
            {
                State.DiscordStatus = "Fechando até a proteção ficar pronta";
                await _discord.StopAsync(cancellationToken);
                UpdateDiscordStatus();
            }
            if (_routing is null)
            {
                _resolver.Configure(Settings.TorDestinations);
                await _resolver.RefreshAsync(cancellationToken);
                if (_resolver.Resolved.Count == 0) throw new InvalidOperationException("Nenhum destino configurado pôde ser resolvido.");
                _resolver.StartPeriodicRefresh();
                State.RouterStatusText = "Iniciando — ativando WinDivert e ponte SOCKS5";
                _routing = new RoutingEngine(_resolver);
                await _routing.StartAsync(cancellationToken);
            }
            State.RouterStatusText = "Iniciando — aguardando Tor (0%)";
            await _tor.StartAsync(cancellationToken);
            await _tor.WaitUntilReadyAsync(cancellationToken);
            State.RouterStatus = RouterStatus.Ready;
            State.RouterStatusText = "Pronto";
            State.DestinationStatus = $"Protegidos: {_resolver.Resolved.Count}";
            AppLog.Info("Router ready");
            if (_discord.IsRunning && restartDiscordIfRunning)
            {
                await _discord.RestartAsync(cancellationToken);
            }
            else if (!_discord.IsRunning && (openDiscordWhenReady || reopenDiscordWhenReady)) _discord.Start();
            UpdateDiscordStatus();
        }
        catch (Exception ex)
        {
            State.RouterStatus = RouterStatus.Error;
            State.RouterStatusText = "Erro";
            State.LastError = ex.Message;
            State.DestinationStatus = "Erro — conexão protegida bloqueada";
            AppLog.Error("Could not enable routing", ex);
            if (_routing is not { IsReady: true })
            {
                if (_routing is not null) await _routing.DisposeAsync();
                _routing = null;
                State.IsEnabled = false;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!State.IsEnabled && State.RouterStatus == RouterStatus.Stopped) return;
            State.RouterStatus = RouterStatus.Stopping;
            State.RouterStatusText = "Parando";
            State.IsEnabled = false;
            if (_routing is not null) await _routing.DisposeAsync();
            _routing = null;
            await _tor.StopAsync(cancellationToken);
            State.RouterStatus = RouterStatus.Stopped;
            State.RouterStatusText = "Parado";
            State.DestinationStatus = "Desprotegido";
            State.LastError = null;
        }
        finally { _gate.Release(); }
    }

    public async Task ApplySettingsAsync(AppSettings updated, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var wasEnabled = State.IsEnabled && _routing is { IsReady: true };
            var destinationsChanged = !Settings.TorDestinations.SequenceEqual(updated.TorDestinations);
            var startupOptionsChanged = Settings.StartWithWindows != updated.StartWithWindows
                || Settings.OpenDiscordAutomatically != updated.OpenDiscordAutomatically;

            if (Settings.StartWithWindows != updated.StartWithWindows)
                await _startupTaskManager.SetEnabledAsync(updated.StartWithWindows, cancellationToken);
            if (startupOptionsChanged)
            {
                if (updated.OpenDiscordAutomatically && updated.StartWithWindows) _discordStartupManager.DisableNativeAutoStart(updated);
                else _discordStartupManager.RestoreNativeAutoStart(updated);
            }

            Settings = updated;
            await _settingsManager.SaveAsync(Settings, cancellationToken);
            if (destinationsChanged)
            {
                _resolver.Configure(Settings.TorDestinations);
                if (!wasEnabled) return;
                if (_routing is not null) await _routing.DisposeAsync();
                await _resolver.RefreshAsync(cancellationToken);
                if (_resolver.Resolved.Count == 0) throw new InvalidOperationException("Nenhum destino configurado pôde ser resolvido.");
                _routing = new RoutingEngine(_resolver);
                await _routing.StartAsync(cancellationToken);
                State.DestinationStatus = $"Protegidos: {_resolver.Resolved.Count}";
            }
        }
        finally { _gate.Release(); }
    }

    public void OpenDiscord() { _discord.Start(); UpdateDiscordStatus(); }
    public async Task RestartDiscordAsync(CancellationToken cancellationToken = default) { await _discord.RestartAsync(cancellationToken); UpdateDiscordStatus(); }
    public Task RequestNewIdentityAsync(CancellationToken cancellationToken = default) => _tor.RequestNewCircuitAsync(cancellationToken);
    public bool IsDiscordRunning => _discord.IsRunning;
    public void RefreshDiscordStatus() => UpdateDiscordStatus();
    private void UpdateDiscordStatus() => State.DiscordStatus = _discord.IsRunning ? "Executando" : "Fechado";

    public async ValueTask DisposeAsync()
    {
        await DisableAsync();
        await _resolver.DisposeAsync();
        await _tor.DisposeAsync();
        _gate.Dispose();
    }
}
