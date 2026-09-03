using DiscordTorRouter.Application;
using DiscordTorRouter.Infrastructure;
using DiscordTorRouter.UI;
using DiscordTorRouter.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DiscordTorRouter;

public partial class App : Microsoft.UI.Xaml.Application
{
    private SingleInstanceManager? _singleInstance;
    private AppController? _controller;
    private TrayManager? _tray;
    private SettingsWindow? _settingsWindow;
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private int _exiting;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => AppLog.Error("Unhandled UI exception", args.Exception);
        DebugSettings.XamlResourceReferenceFailed += (_, args) => AppLog.Error($"XAML resource failed: {args.Message}");
        DebugSettings.BindingFailed += (_, args) => AppLog.Error($"XAML binding failed: {args.Message}");
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var isSmokeTest = commandLine.Any(argument => argument.EndsWith("-smoke-test", StringComparison.OrdinalIgnoreCase));
        var instanceName = isSmokeTest
            ? $"DiscordTorRouter.SingleInstance.SmokeTest.{Environment.ProcessId}"
            : "DiscordTorRouter.SingleInstance";
        _singleInstance = new SingleInstanceManager(instanceName);
        if (!_singleInstance.IsPrimary)
        {
            _singleInstance.SignalPrimary();
            Exit();
            return;
        }

        AppLog.Initialize(AppPaths.LogDirectory);
        AppLog.Info("Discord Tor Router starting");
        if (commandLine.Contains("--tor-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            await using (var tor = new Tor.TorManager())
            {
                await tor.StartAsync();
                await tor.WaitUntilReadyAsync();
                AppLog.Info("Tor smoke test reached Bootstrapped 100%");
            }
            _singleInstance.Dispose();
            AppLog.Info("Tor smoke test completed cleanly");
            AppLog.Dispose();
            Exit();
            return;
        }

        _controller = await AppController.CreateAsync();
        if (_controller.Settings.StartWithWindows && !isSmokeTest)
            await new StartupTaskManager().SetEnabledAsync(true);
        if (commandLine.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            await _controller.DisposeAsync();
            _singleInstance.Dispose();
            AppLog.Info("Application smoke test completed");
            AppLog.Dispose();
            Exit();
            return;
        }

        _singleInstance.Activated += (_, _) => _dispatcherQueue.TryEnqueue(ShowSettings);
        _singleInstance.StartListening();

        _tray = new TrayManager(_controller, ShowSettings, ExitAsync);
        if (commandLine.Contains("--tray-menu-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            _tray.ShowMenu();
            await Task.Delay(1500);
            await ExitAsync();
            return;
        }
        if (commandLine.Contains("--ui-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            ShowSettings();
            await Task.Run(() =>
            {
                _controller.State.TorStatusText = "Conectando (50%)";
                _controller.State.RouterStatusText = "Iniciando — aguardando Tor (50%)";
                _controller.State.DestinationStatus = "Resolvendo...";
            });
            await Task.Delay(1500);
            await ExitAsync();
            return;
        }
        if (!_controller.Settings.StartMinimized && !commandLine.Contains("--startup", StringComparer.OrdinalIgnoreCase))
            ShowSettings();

        await _controller.EnableAsync(
            openDiscordWhenReady: _controller.Settings.OpenDiscordAutomatically,
            restartDiscordIfRunning: _controller.Settings.RestartDiscordIfAlreadyOpenAtStartup);
    }

    private void ShowSettings()
    {
        if (_controller is null) return;
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_controller);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Activate();
    }

    private async Task ExitAsync()
    {
        if (Interlocked.Exchange(ref _exiting, 1) != 0) return;
        if (_controller is not null) await _controller.DisposeAsync();
        _tray?.Dispose();
        _settingsWindow?.Close();
        _singleInstance?.Dispose();
        AppLog.Info("Discord Tor Router stopped");
        AppLog.Dispose();
        Exit();
    }
}
