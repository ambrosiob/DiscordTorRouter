using System.Runtime.InteropServices;
using DiscordTorRouter.Application;
using DiscordTorRouter.Settings;
using DiscordTorRouter.Tor;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace DiscordTorRouter.UI;

public sealed partial class TrayMenuWindow : Window, IDisposable
{
    private const int BaseWidth = 320;
    private const int BaseHeight = 470;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsBorder = 0x00800000L;
    private const long WsDlgFrame = 0x00400000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private readonly AppController _controller;
    private readonly Action _showSettings;
    private readonly Func<Task> _exit;
    private readonly nint _windowHandle;
    private readonly AppWindow _appWindow;
    private readonly int _width;
    private readonly int _height;
    private bool _updatingStartup;
    private bool _identityOperationActive;
    private bool _opening;
    private bool _disposed;

    public TrayMenuWindow(AppController controller, Action showSettings, Func<Task> exit)
    {
        InitializeComponent();
        _controller = controller;
        _showSettings = showSettings;
        _exit = exit;
        Title = "Discord Tor Router";
        SystemBackdrop = new DesktopAcrylicBackdrop();

        _windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        var scale = Math.Max(1d, GetDpiForWindow(_windowHandle) / 96d);
        _width = (int)Math.Round(BaseWidth * scale);
        _height = (int)Math.Round(BaseHeight * scale);
        _appWindow.Resize(new SizeInt32(_width, _height));
        _appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "link.ico"));

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        RemoveNativeFrame();
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && !_opening && !_disposed) _appWindow.Hide();
        };
    }

    public void ShowAtCursor()
    {
        _opening = true;
        UpdateState();
        _ = GetCursorPos(out var cursor);
        var display = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        var x = Math.Clamp(cursor.X - _width + 24, work.X + 8, work.X + work.Width - _width - 8);
        var above = cursor.Y - _height - 12;
        var y = above >= work.Y + 8 ? above : Math.Min(cursor.Y + 12, work.Y + work.Height - _height - 8);
        _appWindow.Move(new PointInt32(x, y));
        _appWindow.Show();
        RemoveNativeFrame();
        Activate();
        _ = SetForegroundWindow(_windowHandle);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _opening = false);
    }

    private void RemoveNativeFrame()
    {
        var style = GetWindowLongPtr(_windowHandle, GwlStyle).ToInt64();
        style &= ~(WsCaption | WsThickFrame | WsBorder | WsDlgFrame);
        _ = SetWindowLongPtr(_windowHandle, GwlStyle, new nint(style));

        var extendedStyle = GetWindowLongPtr(_windowHandle, GwlExStyle).ToInt64();
        _ = SetWindowLongPtr(_windowHandle, GwlExStyle, new nint(extendedStyle | WsExToolWindow));

        var cornerPreference = 2;
        _ = DwmSetWindowAttribute(_windowHandle, 33, ref cornerPreference, sizeof(int));
        var borderColor = unchecked((int)0xFFFFFFFE);
        _ = DwmSetWindowAttribute(_windowHandle, 34, ref borderColor, sizeof(int));
        _ = SetWindowPos(_windowHandle, new nint(-1), 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoActivate | SwpFrameChanged);
    }

    public void UpdateState()
    {
        var state = _controller.State;
        StatusText.Text = $"Status: {StatusTextFor(state.RouterStatus)}";
        ToggleRoutingText.Text = state.IsEnabled ? "Desligar proteção" : "Ligar proteção";
        ToggleRoutingButton.IsEnabled = state.RouterStatus is not RouterStatus.Starting and not RouterStatus.Stopping;
        var discordRunning = _controller.IsDiscordRunning;
        OpenDiscordButton.IsEnabled = !discordRunning;
        OpenDiscordText.Text = discordRunning ? "Discord já está aberto" : "Abrir Discord";
        OpenDiscordIcon.Glyph = discordRunning ? "\uE73E" : "\uE8A7";
        if (!_identityOperationActive) ResetIdentityButton();
        _updatingStartup = true;
        StartupSwitch.IsOn = _controller.Settings.StartWithWindows;
        _updatingStartup = false;
    }

    private async void ToggleRoutingButton_Click(object sender, RoutedEventArgs e) => await RunAndHideAsync(async () =>
    {
        if (_controller.State.IsEnabled) await _controller.DisableAsync();
        else await _controller.EnableAsync(_controller.Settings.OpenDiscordAutomatically, _controller.Settings.RestartDiscordIfAlreadyOpenAtStartup);
    });

    private async void OpenDiscordButton_Click(object sender, RoutedEventArgs e) => await RunAndHideAsync(() =>
    {
        _controller.OpenDiscord();
        return Task.CompletedTask;
    });

    private async void RestartDiscordButton_Click(object sender, RoutedEventArgs e) => await RunAndHideAsync(() => _controller.RestartDiscordAsync());
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
        _identityOperationActive = false;
        ResetIdentityButton();
    }

    private void ResetIdentityButton()
    {
        NewIdentityProgress.IsActive = false;
        NewIdentityProgress.Visibility = Visibility.Collapsed;
        NewIdentityIcon.Glyph = "\uE777";
        NewIdentityIcon.Visibility = Visibility.Visible;
        NewIdentityText.Text = "Nova identidade Tor";
        NewIdentityButton.IsEnabled = _controller.State.TorStatus == TorStatus.Connected;
    }

    private void OpenPanelButton_Click(object sender, RoutedEventArgs e)
    {
        _appWindow.Hide();
        _showSettings();
    }

    private async void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingStartup) return;
        var current = _controller.Settings;
        var updated = new AppSettings
        {
            StartWithWindows = StartupSwitch.IsOn,
            OpenDiscordAutomatically = current.OpenDiscordAutomatically,
            StartMinimized = current.StartMinimized,
            RestartDiscordIfAlreadyOpenAtStartup = current.RestartDiscordIfAlreadyOpenAtStartup,
            TorDestinations = [.. current.TorDestinations],
            DiscordAutoStartBackup = current.DiscordAutoStartBackup
        };
        try { await _controller.ApplySettingsAsync(updated); }
        catch (Exception ex)
        {
            _controller.State.LastError = ex.Message;
            _updatingStartup = true;
            StartupSwitch.IsOn = current.StartWithWindows;
            _updatingStartup = false;
            _appWindow.Hide();
            _showSettings();
        }
    }

    private async void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        _appWindow.Hide();
        await _exit();
    }

    private async Task RunAndHideAsync(Func<Task> action)
    {
        _appWindow.Hide();
        try { await action(); }
        catch (Exception ex)
        {
            _controller.State.LastError = ex.Message;
            _showSettings();
        }
    }

    private static string StatusTextFor(RouterStatus status) => status switch
    {
        RouterStatus.Stopped => "desligado",
        RouterStatus.Starting => "conectando",
        RouterStatus.Ready => "conectado",
        RouterStatus.Stopping => "desligando",
        _ => "erro"
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint windowHandle);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint windowHandle);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint windowHandle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint newLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint windowHandle, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint windowHandle, int attribute, ref int value, int valueSize);
}
