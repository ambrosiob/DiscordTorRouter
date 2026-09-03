using System.Diagnostics;
using DiscordTorRouter.Infrastructure;

namespace DiscordTorRouter.Discord;

public sealed class DiscordProcessManager(DiscordLocator locator)
{
    private static IReadOnlyList<Process> GetProcesses() => Process.GetProcessesByName("Discord");
    public bool IsRunning
    {
        get
        {
            var processes = GetProcesses();
            try { return processes.Any(process => !process.HasExited); }
            finally { DisposeAll(processes); }
        }
    }

    public void Start()
    {
        if (IsRunning) return;
        StartDiscordProcess();
    }

    private void StartDiscordProcess()
    {
        var update = locator.FindUpdateExecutable();
        var executable = locator.FindCurrentExecutable();
        if (update is not null)
            Process.Start(new ProcessStartInfo(update, "--processStart Discord.exe") { UseShellExecute = true });
        else if (executable is not null)
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        else throw new FileNotFoundException("Discord não foi encontrado em %LocalAppData%\\Discord.");
        AppLog.Info("Starting Discord");
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
        await Task.Delay(300, cancellationToken);
        if (IsRunning) throw new InvalidOperationException("Não foi possível encerrar completamente o Discord para reabri-lo.");
        StartDiscordProcess();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var processes = GetProcesses();
        if (processes.Count == 0) return;
        AppLog.Info($"Closing Discord; found {processes.Count} process(es)");
        foreach (var process in processes)
        {
            try { process.CloseMainWindow(); } catch (InvalidOperationException) { }
        }

        var gracefulDeadline = DateTime.UtcNow.AddSeconds(5);
        while (processes.Any(IsStillRunning) && DateTime.UtcNow < gracefulDeadline)
            await Task.Delay(150, cancellationToken);
        DisposeAll(processes);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            processes = GetProcesses();
            if (processes.Count == 0) break;
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
            }
            DisposeAll(processes);
            await Task.Delay(250, cancellationToken);
        }

        if (IsRunning) throw new InvalidOperationException("Não foi possível encerrar completamente o Discord.");
        AppLog.Info("Discord closed");
    }

    private static bool IsStillRunning(Process process)
    {
        try { return !process.HasExited; }
        catch (InvalidOperationException) { return false; }
    }

    private static void DisposeAll(IEnumerable<Process> processes)
    {
        foreach (var process in processes) process.Dispose();
    }
}
