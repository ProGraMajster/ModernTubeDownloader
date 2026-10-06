using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Infrastructure;

public sealed record ExternalMediaRequest(
    string SourceUrl,
    bool AutoAnalyze,
    bool AutoQueue = false,
    string? RequestedQualityPresetId = null,
    string Source = "browser-extension",
    Guid? RequestId = null,
    bool AutoQueuePlaylist = false,
    PreferredVideoContainer RequestedContainer = PreferredVideoContainer.Auto,
    bool OpenInApp = false)
{
    public const string Scheme = "moderntubedownloader";
    public const int MaximumProtocolLength = 8192;
    public const int MaximumSourceLength = 4096;

    public static bool TryParseProtocol(string? input, out ExternalMediaRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(input) || input.Length > MaximumProtocolLength || input.Any(char.IsControl) ||
            HasMalformedEscape(input) ||
            !Uri.TryCreate(input, UriKind.Absolute, out var protocol) ||
            !protocol.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase) ||
            !protocol.Host.Equals("open", StringComparison.OrdinalIgnoreCase) ||
            protocol.AbsolutePath != "/" || protocol.Fragment.Length != 0 || protocol.UserInfo.Length != 0 ||
            !protocol.IsDefaultPort)
            return false;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in protocol.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2) return false;
            string key;
            string value;
            try
            {
                key = Uri.UnescapeDataString(parts[0]);
                value = Uri.UnescapeDataString(parts[1]);
            }
            catch (UriFormatException) { return false; }

            if (!values.TryAdd(key, value) || key is not ("v" or "url" or "analyze" or "queue" or "playlist" or "quality" or "container" or "open" or "source" or "id"))
                return false;
        }

        if (values.TryGetValue("v", out var version) && version != "1") return false;
        if (!values.TryGetValue("url", out var url) || !TryValidateSource(url, out var normalized)) return false;
        if (!TryFlag(values, "analyze", true, out var analyze) ||
            !TryFlag(values, "queue", false, out var queue) ||
            !TryFlag(values, "playlist", false, out var playlist) ||
            !TryFlag(values, "open", !queue, out var openInApp)) return false;
        var isPlaylistUrl = Services.YtDlpMetadataService.IsPlaylistUrl(normalized);
        if ((queue && !analyze) || (playlist && !isPlaylistUrl) || (queue && isPlaylistUrl && !playlist))
            return false;
        if (queue == openInApp) return false;
        var quality = values.GetValueOrDefault("quality") ?? "best";
        if (!QualityPreset.All.Any(preset => preset.Id == quality)) return false;
        if (!PreferredVideoContainerExtensions.TryParseProtocol(values.GetValueOrDefault("container"), out var container))
            return false;
        var source = values.GetValueOrDefault("source") ?? "browser-extension";
        if (source is not ("browser-extension" or "external")) return false;
        Guid? id = null;
        if (values.TryGetValue("id", out var rawId))
        {
            if (!Guid.TryParseExact(rawId, "D", out var parsed)) return false;
            id = parsed;
        }

        request = new ExternalMediaRequest(normalized, analyze, queue, quality, source, id, playlist, container, openInApp);
        return true;
    }

    private static bool TryFlag(Dictionary<string, string> values, string key, bool fallback, out bool result)
    {
        result = fallback;
        if (!values.TryGetValue(key, out var value)) return true;
        if (value is not ("0" or "1")) return false;
        result = value == "1";
        return true;
    }

    public static bool TryValidateSource(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input) || input.Length > MaximumSourceLength || input.Any(char.IsControl) ||
            HasMalformedEscape(input) ||
            !Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 ||
            !uri.IsDefaultPort || uri.Fragment.Length != 0)
            return false;

        var host = uri.IdnHost.ToLowerInvariant();
        var youtube = host is "youtube.com" or "www.youtube.com" or "m.youtube.com";
        var shortLink = host == "youtu.be";
        if (!youtube && !shortLink) return false;

        var path = uri.AbsolutePath;
        if (shortLink)
        {
            if (!ValidId(path.Trim('/'))) return false;
        }
        else if (path == "/watch")
        {
            if (!ValidId(GetQueryValue(uri.Query, "v"))) return false;
        }
        else if (path.StartsWith("/shorts/", StringComparison.Ordinal))
        {
            if (!ValidId(path[8..].Trim('/'))) return false;
        }
        else if (path.StartsWith("/live/", StringComparison.Ordinal))
        {
            if (!ValidId(path[6..].Trim('/'))) return false;
        }
        else if (path == "/playlist")
        {
            if (!ValidId(GetQueryValue(uri.Query, "list"))) return false;
        }
        else return false;

        normalized = uri.AbsoluteUri;
        if (normalized.Length > MaximumSourceLength)
        {
            normalized = string.Empty;
            return false;
        }
        return true;
    }

    private static string? GetQueryValue(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == name)
                return Uri.UnescapeDataString(parts[1]);
        }
        return null;
    }

    private static bool ValidId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 128 &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool HasMalformedEscape(string input)
    {
        for (var index = 0; index < input.Length; index++)
        {
            if (input[index] != '%') continue;
            if (index + 2 >= input.Length || !Uri.IsHexDigit(input[index + 1]) || !Uri.IsHexDigit(input[index + 2]))
                return true;
            index += 2;
        }
        return false;
    }
}
