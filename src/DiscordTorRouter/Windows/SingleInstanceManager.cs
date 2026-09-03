namespace DiscordTorRouter.Windows;

public sealed class SingleInstanceManager : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;
    private int _disposed;
    public bool IsPrimary { get; }
    public event EventHandler? Activated;

    public SingleInstanceManager(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, $"Local\\{name}.Mutex", out var created);
        IsPrimary = created;
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\{name}.Activate");
    }

    public void StartListening()
    {
        if (!IsPrimary || _listener is not null) return;
        _listener = Task.Run(() =>
        {
            var handles = new WaitHandle[] { _activationEvent, _stop.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0) Activated?.Invoke(this, EventArgs.Empty);
        });
    }

    public void SignalPrimary() => _activationEvent.Set();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        try { _listener?.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        if (IsPrimary) try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
        _activationEvent.Dispose();
        _stop.Dispose();
    }
}
