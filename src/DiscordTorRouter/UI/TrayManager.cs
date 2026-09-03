using System.ComponentModel;
using DiscordTorRouter.Application;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DiscordTorRouter.UI;

public sealed class TrayManager : IDisposable
{
    private readonly AppController _controller;
    private readonly TaskbarIcon _notifyIcon;
    private readonly TrayMenuWindow _menuWindow;
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private readonly XamlUICommand _showWindowCommand = new();
    private readonly XamlUICommand _showMenuCommand = new();
    private int _disposed;

    public TrayManager(AppController controller, Action showSettings, Func<Task> exit)
    {
        _controller = controller;
        _menuWindow = new TrayMenuWindow(controller, showSettings, exit);
        _notifyIcon = new TaskbarIcon
        {
            Visibility = Visibility.Visible,
            ToolTipText = "Discord Tor Router",
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/link.ico")),
            NoLeftClickDelay = true
        };

        _showWindowCommand.ExecuteRequested += (_, _) => showSettings();
        _showMenuCommand.ExecuteRequested += (_, _) => _menuWindow.ShowAtCursor();
        _notifyIcon.LeftClickCommand = _showWindowCommand;
        _notifyIcon.RightClickCommand = _showMenuCommand;
        controller.State.PropertyChanged += StateOnPropertyChanged;
        Update();
        _notifyIcon.ForceCreate();
    }

    private void StateOnPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        _dispatcherQueue.TryEnqueue(Update);

    private void Update()
    {
        var state = _controller.State;
        _menuWindow.UpdateState();
        var status = state.RouterStatus switch
        {
            RouterStatus.Stopped => "desligado",
            RouterStatus.Starting => "conectando",
            RouterStatus.Ready => "conectado",
            RouterStatus.Stopping => "desligando",
            _ => "erro"
        };
        var destination = state.RouterStatus == RouterStatus.Ready ? "destinos protegidos" : "sem proteção";
        _notifyIcon.ToolTipText = $"Discord Tor Router — {status} — {destination}";
    }

    internal void ShowMenu() => _menuWindow.ShowAtCursor();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _controller.State.PropertyChanged -= StateOnPropertyChanged;
        _menuWindow.Dispose();
        _notifyIcon.Dispose();
    }
}
