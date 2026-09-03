namespace DiscordTorRouter.Infrastructure;

public static class AppPaths
{
    public static string BaseDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscordTorRouter");
    public static string SettingsFile => Path.Combine(BaseDirectory, "settings.json");
    public static string LogDirectory => Path.Combine(BaseDirectory, "logs");
    public static string TorDataDirectory => Path.Combine(BaseDirectory, "tor-data");
    public static string ExecutableDirectory => AppContext.BaseDirectory;
    public static string TorExecutable => Path.Combine(ExecutableDirectory, "tor", "tor.exe");
    public static string TorConfig => Path.Combine(ExecutableDirectory, "tor", "torrc");
    public static string WinDivertDirectory => Path.Combine(ExecutableDirectory, "windivert");
}
