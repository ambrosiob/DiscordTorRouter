using Microsoft.Win32;
using DiscordTorRouter.Settings;

namespace DiscordTorRouter.Discord;

public sealed class DiscordStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public void DisableNativeAutoStart(AppSettings settings)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null) return;
        var candidate = key.GetValueNames().FirstOrDefault(name =>
            name.Contains("Discord", StringComparison.OrdinalIgnoreCase) ||
            (key.GetValue(name) as string)?.Contains("Discord.exe", StringComparison.OrdinalIgnoreCase) == true);
        if (candidate is null) return;
        if (settings.DiscordAutoStartBackup is null)
        {
            settings.DiscordAutoStartBackup = new DiscordAutoStartBackup
            {
                WasEnabled = true,
                ValueName = candidate,
                OriginalValue = key.GetValue(candidate) as string
            };
        }
        key.DeleteValue(candidate, throwOnMissingValue: false);
    }

    public void RestoreNativeAutoStart(AppSettings settings)
    {
        var backup = settings.DiscordAutoStartBackup;
        if (backup is not { WasEnabled: true, ValueName: not null, OriginalValue: not null }) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(backup.ValueName, backup.OriginalValue, RegistryValueKind.String);
        settings.DiscordAutoStartBackup = null;
    }
}
