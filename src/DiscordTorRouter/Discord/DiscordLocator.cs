namespace DiscordTorRouter.Discord;

public sealed class DiscordLocator
{
    public string? FindUpdateExecutable()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
        var update = Path.Combine(root, "Update.exe");
        return File.Exists(update) ? update : null;
    }

    public string? FindCurrentExecutable()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateDirectories(root, "app-*", SearchOption.TopDirectoryOnly)
            .Select(path => new { Path = path, Version = ParseVersion(Path.GetFileName(path)[4..]) })
            .OrderByDescending(x => x.Version)
            .Select(x => Path.Combine(x.Path, "Discord.exe"))
            .FirstOrDefault(File.Exists);
    }

    private static Version ParseVersion(string value) => Version.TryParse(value, out var version) ? version : new Version(0, 0);
}
