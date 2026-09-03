using System.Globalization;
using System.Net;

namespace DiscordTorRouter.Settings;

public static class DestinationParser
{
    public static IReadOnlyList<RouteDestination> ParseLines(string text)
    {
        var result = new List<RouteDestination>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.StartsWith('#')) continue;
            var (host, port) = Parse(raw);
            var key = $"{host}:{port}";
            if (seen.Add(key)) result.Add(new RouteDestination(host, port));
        }
        if (result.Count == 0) throw new FormatException("Informe pelo menos um endereço no formato host:porta.");
        return result;
    }

    public static (string Host, ushort Port) Parse(string value)
    {
        value = value.Trim();
        string host;
        string portText;
        if (value.StartsWith('['))
        {
            var end = value.IndexOf(']');
            if (end < 2 || end + 2 >= value.Length || value[end + 1] != ':') throw new FormatException($"Endereço inválido: {value}");
            host = value[1..end];
            portText = value[(end + 2)..];
        }
        else
        {
            var separator = value.LastIndexOf(':');
            if (separator <= 0 || separator == value.Length - 1) throw new FormatException($"Use host:porta: {value}");
            host = value[..separator].Trim().TrimEnd('.');
            portText = value[(separator + 1)..];
            if (host.Contains(':') && !IPAddress.TryParse(host, out _)) throw new FormatException($"IPv6 deve usar [endereço]:porta: {value}");
        }
        if (host.Length is < 1 or > 253) throw new FormatException($"Host inválido: {host}");
        if (!ushort.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port == 0)
            throw new FormatException($"Porta inválida: {portText}");
        if (!IPAddress.TryParse(host, out _) && host.Split('.').Any(label => label.Length is < 1 or > 63 || label.Any(c => !(char.IsLetterOrDigit(c) || c == '-'))))
            throw new FormatException($"Host inválido: {host}");
        return (host.ToLowerInvariant(), port);
    }
}
