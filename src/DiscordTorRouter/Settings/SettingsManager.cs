using System.Text.Json;
using DiscordTorRouter.Infrastructure;

namespace DiscordTorRouter.Settings;

public sealed class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppPaths.BaseDirectory);
        if (!File.Exists(AppPaths.SettingsFile)) return new AppSettings();
        try
        {
            await using var stream = File.OpenRead(AppPaths.SettingsFile);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken) ?? new AppSettings();
            settings.TorDestinations = (settings.TorDestinations ?? [])
                .Where(x => x is not null && x.Enabled && IsValid(x))
                .DistinctBy(x => (x.Host.ToLowerInvariant(), x.Port))
                .ToList();
            if (settings.TorDestinations.Count == 0) settings.TorDestinations.Add(RouteDestination.Default);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            AppLog.Error("Could not load settings; defaults will be used", ex);
            return new AppSettings();
        }
    }

    private static bool IsValid(RouteDestination destination)
    {
        try { DestinationParser.Parse(destination.ToString()); return true; }
        catch (FormatException) { return false; }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppPaths.BaseDirectory);
        var temporary = AppPaths.SettingsFile + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        File.Move(temporary, AppPaths.SettingsFile, true);
    }
}
