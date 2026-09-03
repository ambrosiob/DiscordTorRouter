using System.Diagnostics;
using System.Security.Principal;
using System.Xml.Linq;
using DiscordTorRouter.Infrastructure;

namespace DiscordTorRouter.Windows;

public sealed class StartupTaskManager
{
    private const string TaskName = "DiscordTorRouter";
    private const string LegacyTaskName = "DiscordVirtualRouter";

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!enabled)
        {
            await RunSchtasksAsync(["/Delete", "/TN", TaskName, "/F"], ignoreMissing: true, cancellationToken);
            await RunSchtasksAsync(["/Delete", "/TN", LegacyTaskName, "/F"], ignoreMissing: true, cancellationToken);
            return;
        }

        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Caminho do executável não disponível.");
        var user = WindowsIdentity.GetCurrent().Name;
        var escapedExecutable = SecurityElementEscape(executable);
        var escapedUser = SecurityElementEscape(user);
        var xml = $"""
                  <?xml version="1.0" encoding="UTF-16"?>
                  <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                    <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{escapedUser}</UserId></LogonTrigger></Triggers>
                    <Principals><Principal id="Author"><UserId>{escapedUser}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
                    <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled></Settings>
                    <Actions Context="Author"><Exec><Command>{escapedExecutable}</Command><Arguments>--startup</Arguments><WorkingDirectory>{SecurityElementEscape(AppContext.BaseDirectory)}</WorkingDirectory></Exec></Actions>
                  </Task>
                  """;
        var temporary = Path.Combine(Path.GetTempPath(), $"DiscordTorRouter-{Guid.NewGuid():N}.xml");
        try
        {
            await File.WriteAllTextAsync(temporary, xml, System.Text.Encoding.Unicode, cancellationToken);
            await RunSchtasksAsync(["/Create", "/TN", TaskName, "/XML", temporary, "/F"], false, cancellationToken);
            await RunSchtasksAsync(["/Delete", "/TN", LegacyTaskName, "/F"], ignoreMissing: true, cancellationToken);
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } }
    }

    private static async Task RunSchtasksAsync(string[] arguments, bool ignoreMissing, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo("schtasks.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível executar schtasks.exe.");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0 && !ignoreMissing)
            throw new InvalidOperationException($"Task Scheduler: {(stdout + stderr).Trim()}");
    }

    private static string SecurityElementEscape(string value) => System.Security.SecurityElement.Escape(value) ?? value;
}
