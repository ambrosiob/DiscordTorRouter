using System.ComponentModel;
using System.Runtime.CompilerServices;
using DiscordTorRouter.Tor;

namespace DiscordTorRouter.Application;

public enum RouterStatus { Stopped, Starting, Ready, Stopping, Error }

public sealed class ApplicationState : INotifyPropertyChanged
{
    private readonly SynchronizationContext? _notificationContext = SynchronizationContext.Current;
    private bool _isEnabled;
    private TorStatus _torStatus = TorStatus.Stopped;
    private string _torStatusText = "Parado";
    private RouterStatus _routerStatus = RouterStatus.Stopped;
    private string _routerStatusText = "Parado";
    private string _discordStatus = "Não detectado";
    private string _destinationStatus = "Desprotegido";
    private string? _lastError;
    private IReadOnlyList<string> _resolvedDestinations = [];

    public bool IsEnabled { get => _isEnabled; internal set => Set(ref _isEnabled, value); }
    public TorStatus TorStatus { get => _torStatus; internal set => Set(ref _torStatus, value); }
    public string TorStatusText { get => _torStatusText; internal set => Set(ref _torStatusText, value); }
    public RouterStatus RouterStatus { get => _routerStatus; internal set => Set(ref _routerStatus, value); }
    public string RouterStatusText { get => _routerStatusText; internal set => Set(ref _routerStatusText, value); }
    public string DiscordStatus { get => _discordStatus; internal set => Set(ref _discordStatus, value); }
    public string DestinationStatus { get => _destinationStatus; internal set => Set(ref _destinationStatus, value); }
    public string? LastError { get => _lastError; internal set => Set(ref _lastError, value); }
    public IReadOnlyList<string> ResolvedDestinations { get => _resolvedDestinations; internal set => Set(ref _resolvedDestinations, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        var handlers = PropertyChanged;
        if (handlers is null) return;

        if (_notificationContext is null || ReferenceEquals(SynchronizationContext.Current, _notificationContext))
        {
            handlers(this, new PropertyChangedEventArgs(property));
            return;
        }

        _notificationContext.Post(static state =>
        {
            var notification = ((ApplicationState Sender, PropertyChangedEventHandler Handlers, string? Property))state!;
            notification.Handlers(notification.Sender, new PropertyChangedEventArgs(notification.Property));
        }, (this, handlers, property));
    }
}
