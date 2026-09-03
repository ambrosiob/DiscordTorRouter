namespace DiscordTorRouter.Settings;

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool OpenDiscordAutomatically { get; set; } = true;
    public bool StartMinimized { get; set; } = true;
    public bool RestartDiscordIfAlreadyOpenAtStartup { get; set; }
    public List<RouteDestination> TorDestinations { get; set; } = [RouteDestination.Default];
    public DiscordAutoStartBackup? DiscordAutoStartBackup { get; set; }
}

public sealed class DiscordAutoStartBackup
{
    public bool WasEnabled { get; set; }
    public string? ValueName { get; set; }
    public string? OriginalValue { get; set; }
}
