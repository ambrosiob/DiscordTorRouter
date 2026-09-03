using System.ComponentModel;
using DiscordTorRouter.Application;
using DiscordTorRouter.Settings;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace DiscordTorRouter.UI;

public sealed partial class SettingsWindow : Window
{
    private readonly AppController _controller;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly DispatcherQueueTimer _discordStatusTimer;
    private CancellationTokenSource? _pendingSave;
    private bool _loading = true;
    private bool _identityOperationActive;
    private bool _discordOperationActive;

    public SettingsWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        _discordStatusTimer = DispatcherQueue.CreateTimer();
        _discordStatusTimer.Interval = TimeSpan.FromSeconds(2);
        _discordStatusTimer.IsRepeating = true;
        _discordStatusTimer.Tick += (_, _) =>
        {
            if (_discordOperationActive) return;
            _controller.RefreshDiscordStatus();
            UpdateDiscordButtonState();
        };
        RootGrid.DataContext = controller.State;
        ConfigureWindow();
        LoadSettings();
        _loading = false;
        controller.State.PropertyChanged += StateOnPropertyChanged;
        Closed += (_, _) =>
        {
            _discordStatusTimer.Stop();
            controller.State.PropertyChanged -= StateOnPropertyChanged;
        };
        UpdateVisualState();
        _discordStatusTimer.Start();
    }

    private void ConfigureWindow()
    {
        Title = "Discord Tor Router";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        try { SystemBackdrop = new MicaBackdrop(); }
        catch { /* O fundo padrão continua disponível em versões sem Mica. */ }

        var windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(780, 820));
        appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "link.ico"));
        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        var display = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var x = display.WorkArea.X + Math.Max(0, (display.WorkArea.Width - 780) / 2);
        var y = display.WorkArea.Y + Math.Max(0, (display.WorkArea.Height - 820) / 2);
        appWindow.Move(new PointInt32(x, y));
    }

    private void LoadSettings()
    {
        var settings = _controller.Settings;
        OpenDiscordSwitch.IsOn = settings.OpenDiscordAutomatically;
        RestartDiscordSwitch.IsOn = settings.RestartDiscordIfAlreadyOpenAtStartup;
        StartMinimizedSwitch.IsOn = settings.StartMinimized;
        DestinationsTextBox.Text = string.Join(Environment.NewLine, settings.TorDestinations.Select(x => x.ToString()));
    }

    private void SettingChanged(object sender, RoutedEventArgs e) => QueueAutomaticSave();

    private void DestinationsTextBox_TextChanged(object sender, TextChangedEventArgs e) => QueueAutomaticSave();

    private void QueueAutomaticSave()
    {
        if (_loading) return;
        var pending = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _pendingSave, pending);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var destinations = DestinationParser.ParseLines(DestinationsTextBox.Text).ToList();
            var updated = new AppSettings
            {
                StartWithWindows = _controller.Settings.StartWithWindows,
                OpenDiscordAutomatically = OpenDiscordSwitch.IsOn,
                RestartDiscordIfAlreadyOpenAtStartup = RestartDiscordSwitch.IsOn,
                StartMinimized = StartMinimizedSwitch.IsOn,
                TorDestinations = destinations,
                DiscordAutoStartBackup = _controller.Settings.DiscordAutoStartBackup
            };
            ValidationText.Foreground = new SolidColorBrush(Colors.Gray);
            ValidationText.Text = "Salvando alterações...";
            _ = SaveAfterDelayAsync(updated, pending.Token);
        }
        catch (Exception ex)
        {
            ValidationText.Foreground = new SolidColorBrush(Colors.IndianRed);
            ValidationText.Text = ex.Message;
        }
    }

    private async Task SaveAfterDelayAsync(AppSettings updated, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(400, cancellationToken);
            await _saveGate.WaitAsync(cancellationToken);
            try
            {
                await _controller.ApplySettingsAsync(updated);
                ValidationText.Foreground = new SolidColorBrush(Colors.ForestGreen);
                ValidationText.Text = "Alterações salvas.";
            }
            finally { _saveGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ValidationText.Foreground = new SolidColorBrush(Colors.IndianRed);
            ValidationText.Text = ex.Message;
        }
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleButton.IsEnabled = false;
        if (_controller.State.IsEnabled) await _controller.DisableAsync();
        else await _controller.EnableAsync(
            _controller.Settings.OpenDiscordAutomatically,
            _controller.Settings.RestartDiscordIfAlreadyOpenAtStartup);
        ToggleButton.IsEnabled = true;
        UpdateVisualState();
    }

    private async void OpenDiscordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.IsDiscordRunning) return;
        _discordOperationActive = true;
        OpenDiscordButton.IsEnabled = false;
        OpenDiscordIcon.Visibility = Visibility.Collapsed;
        OpenDiscordProgress.Visibility = Visibility.Visible;
        OpenDiscordProgress.IsActive = true;
        OpenDiscordText.Text = "Abrindo Discord...";
        try
        {
            _controller.OpenDiscord();
            for (var attempt = 0; attempt < 20 && !_controller.IsDiscordRunning; attempt++)
                await Task.Delay(250);
            _controller.RefreshDiscordStatus();
        }
        catch (Exception ex)
        {
            _controller.State.LastError = ex.Message;
            OpenDiscordProgress.IsActive = false;
            OpenDiscordProgress.Visibility = Visibility.Collapsed;
            OpenDiscordIcon.Glyph = "\uE783";
            OpenDiscordIcon.Visibility = Visibility.Visible;
            OpenDiscordText.Text = "Falha ao abrir Discord";
            await Task.Delay(1800);
        }
        finally
        {
            _discordOperationActive = false;
            UpdateDiscordButtonState();
        }
    }

    private void UpdateDiscordButtonState()
    {
        var discordRunning = _controller.IsDiscordRunning;
        OpenDiscordProgress.IsActive = false;
        OpenDiscordProgress.Visibility = Visibility.Collapsed;
        OpenDiscordIcon.Glyph = discordRunning ? "\uE73E" : "\uE8A7";
        OpenDiscordIcon.Visibility = Visibility.Visible;
        OpenDiscordText.Text = discordRunning ? "Discord já está aberto" : "Abrir Discord";
        OpenDiscordButton.IsEnabled = !discordRunning;
    }

    private async void NewIdentityButton_Click(object sender, RoutedEventArgs e)
    {
        _identityOperationActive = true;
        NewIdentityButton.IsEnabled = false;
        NewIdentityIcon.Visibility = Visibility.Collapsed;
        NewIdentityProgress.Visibility = Visibility.Visible;
        NewIdentityProgress.IsActive = true;
        NewIdentityText.Text = "Solicitando nova identidade...";
        try
        {
            await _controller.RequestNewIdentityAsync();
            NewIdentityProgress.IsActive = false;
            NewIdentityProgress.Visibility = Visibility.Collapsed;
            NewIdentityIcon.Glyph = "\uE73E";
            NewIdentityIcon.Visibility = Visibility.Visible;
            NewIdentityText.Text = "Nova identidade solicitada";
        }
        catch (Exception ex)
        {
            _controller.State.LastError = ex.Message;
            NewIdentityProgress.IsActive = false;
            NewIdentityProgress.Visibility = Visibility.Collapsed;
            NewIdentityIcon.Glyph = "\uE783";
            NewIdentityIcon.Visibility = Visibility.Visible;
            NewIdentityText.Text = "Falha ao trocar identidade";
        }

        await Task.Delay(1800);
        NewIdentityProgress.IsActive = false;
        NewIdentityProgress.Visibility = Visibility.Collapsed;
        NewIdentityIcon.Glyph = "\uE777";
        NewIdentityIcon.Visibility = Visibility.Visible;
        NewIdentityText.Text = "Nova identidade Tor";
        _identityOperationActive = false;
        NewIdentityButton.IsEnabled = _controller.State.TorStatus == Tor.TorStatus.Connected;
    }

    private void StateOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ApplicationState.IsEnabled)
            or nameof(ApplicationState.RouterStatus)
            or nameof(ApplicationState.LastError))
            DispatcherQueue.TryEnqueue(UpdateVisualState);
    }

    private void UpdateVisualState()
    {
        var state = _controller.State;
        ToggleButtonText.Text = state.IsEnabled ? "Desligar" : "Ligar";
        RouterProgressRing.IsActive = state.RouterStatus is RouterStatus.Starting or RouterStatus.Stopping;
        RouterProgressRing.Visibility = RouterProgressRing.IsActive ? Visibility.Visible : Visibility.Collapsed;
        if (!_identityOperationActive)
            NewIdentityButton.IsEnabled = state.TorStatus == Tor.TorStatus.Connected;
        if (!_discordOperationActive)
            UpdateDiscordButtonState();
        ErrorInfoBar.Message = state.LastError ?? string.Empty;
        ErrorInfoBar.IsOpen = !string.IsNullOrWhiteSpace(state.LastError);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
