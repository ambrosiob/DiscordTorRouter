using System.Collections.Concurrent;
using DiscordTorRouter.Application;

namespace DiscordTorRouter.Tests;

public sealed class ApplicationStateThreadingTests
{
    [Fact]
    public async Task PropertyChanged_FromWorkerThread_IsPostedToCapturedContext()
    {
        var previous = SynchronizationContext.Current;
        var context = new RecordingSynchronizationContext();
        ApplicationState state;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            state = new ApplicationState();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var notifications = 0;
        state.PropertyChanged += (_, args) =>
        {
            Assert.Equal(nameof(ApplicationState.TorStatusText), args.PropertyName);
            notifications++;
        };

        await Task.Run(() => state.TorStatusText = "Conectando (50%)");

        Assert.Equal(0, notifications);
        Assert.Equal(1, context.PendingCount);
        context.RunAll();
        Assert.Equal(1, notifications);
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _pending = new();

        public int PendingCount => _pending.Count;

        public override void Post(SendOrPostCallback d, object? state) => _pending.Enqueue((d, state));

        public void RunAll()
        {
            while (_pending.TryDequeue(out var work)) work.Callback(work.State);
        }
    }
}
