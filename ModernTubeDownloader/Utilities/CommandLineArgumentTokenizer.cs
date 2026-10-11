using System.Text;
using System.Text.Json;

namespace ModernTubeDownloader.Utilities;

public static class CommandLineArgumentTokenizer
{
    private static readonly HashSet<string> ForbiddenOptions = new(StringComparer.Ordinal)
    {
        "-o", "--output", "-P", "--paths", "-f", "--format", "--progress-template",
        "--dump-json", "--dump-single-json", "--print", "-O", "--print-to-file",
        "--exec", "--exec-before-download", "--config-locations", "--load-info-json",
        "--ffmpeg-location", "--no-simulate", "--simulate",
        "--no-playlist", "--yes-playlist", "--flat-playlist", "--no-flat-playlist",
        "--ignore-errors", "--no-ignore-errors",
        "--cookies", "--no-cookies", "--cookies-from-browser", "--no-cookies-from-browser",
        // Auth owns credentials and diagnostics. Optparse aliases/abbreviations must not bypass it.
        "--alias", "--preset-alias", "-t", "--netrc-cmd", "--netrc", "-n", "--netrc-location",
        "--username", "-u", "--password", "-p", "--twofactor", "-2", "--video-password",
        "--ap-username", "--ap-password", "--client-certificate", "--client-certificate-key", "--client-certificate-password",
        "--print-traffic", "--dump-pages", "--write-pages", "--verbose", "-v",
        "--downloader", "--external-downloader", "--downloader-args", "--external-downloader-args",
        "--postprocessor-args", "--ppa", "--plugin-dirs", "--use-postprocessor",
        "--remote-components", "--enable-file-urls", "--mark-watched",
        "--write-subs", "--no-write-subs", "--write-auto-subs", "--no-write-auto-subs",
        "--sub-langs", "--sub-format", "--convert-subs", "--embed-subs", "--no-embed-subs",
        "--sponsorblock-mark", "--sponsorblock-remove", "--no-sponsorblock",
        "--sponsorblock-api", "--sponsorblock-chapter-title", "--embed-chapters",
        "--js-runtimes", "--no-js-runtimes", "--extractor-args", "--batch-file", "-a",
        // Upstream info-json writers bypass our recursive credential redaction.
        "--write-info-json", "--embed-info-json", "--no-clean-info-json",
        "--update", "--update-to", "-U"
    };

    private sealed record OptionGroup(string[] Options, int Arity);
    private sealed record OptionCatalog(string UpstreamCommit, string SourceSha256, OptionGroup[] Groups);
    private static readonly OptionGroup[] Groups = LoadCatalog();
    private static readonly Dictionary<string, OptionGroup> Options = Groups.SelectMany(g => g.Options.Select(o => (o, g)))
        .ToDictionary(p => p.o, p => p.g, StringComparer.Ordinal);
    private static OptionGroup[] LoadCatalog()
    {
        using var stream = typeof(CommandLineArgumentTokenizer).Assembly.GetManifestResourceStream("ModernTubeDownloader.Resources.YtDlpOptions.json")
            ?? throw new InvalidOperationException("Missing reviewed yt-dlp option catalog.");
        return JsonSerializer.Deserialize<OptionCatalog>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Groups;
    }
    private static ArgumentException Reject(string reason) => new CustomArgumentException(reason);
    private static OptionGroup Resolve(string option)
    {
        if (Options.TryGetValue(option, out var exact)) return exact;
        var matches = Options.Where(p => p.Key.StartsWith(option, StringComparison.Ordinal)).Select(p => p.Value).Distinct().ToArray();
        return matches.Length switch { 1 => matches[0], 0 => throw Reject("unknown-option"), _ => throw Reject("ambiguous-option") };
    }
    private static void Validate(OptionGroup group, string? value)
    {
        // Resolve aliases to their shared optparse option identity, never echo an attached credential.
        var canonical = group.Options.FirstOrDefault(o => o.StartsWith("--", StringComparison.Ordinal)) ?? group.Options[0];
        if (group.Options.Any(ForbiddenOptions.Contains)) throw Reject("managed:" + canonical);
        if (group.Options.Contains("--add-headers", StringComparer.Ordinal) || group.Options.Contains("--add-header", StringComparer.Ordinal))
        {
            var name = (value ?? "").Split(':', 2)[0].Trim();
            if (name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) || name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)) throw Reject("credential-header");
        }
    }

    public static IReadOnlyList<string> ParseSafeYtDlpArguments(string? commandLine)
    {
        var tokens = Tokenize(commandLine);
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token == "--") throw Reject("end-of-options"); // Would turn MTD's own flags into extra URL arguments.
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var parts = token.Split('=', 2);
                var group = Resolve(parts[0]);
                Validate(group, parts.Length == 2 ? parts[1] : index + 1 < tokens.Count ? tokens[index + 1] : null);
                if (parts.Length == 2 && group.Arity == 0) throw Reject("unexpected-value");
                var remaining = group.Arity - (parts.Length == 2 ? 1 : 0);
                if (index + remaining >= tokens.Count) throw Reject("missing-value");
                index += remaining;
            }
            else if (token.StartsWith('-') && token.Length > 1)
            {
                for (var character = 1; character < token.Length; character++)
                {
                    if (!Options.TryGetValue("-" + token[character], out var group)) throw Reject("unknown-option");
                    var attached = character + 1 < token.Length ? token[(character + 1)..] : null;
                    Validate(group, attached ?? (index + 1 < tokens.Count ? tokens[index + 1] : null));
                    if (group.Arity == 0) continue;
                    var remaining = group.Arity - (attached is null ? 0 : 1);
                    if (index + remaining >= tokens.Count) throw Reject("missing-value");
                    index += remaining;
                    break; // Optparse consumes the rest of a short cluster as this option's value.
                }
            }
            else throw Reject("positional-argument");
        }

        return tokens;
    }

    internal static IReadOnlyList<string> Tokenize(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return [];

        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var tokenStarted = false;
        char quote = '\0';

        for (var index = 0; index < commandLine.Length; index++)
        {
            var character = commandLine[index];
            if ((character is '"' or '\'') && (!inQuotes || character == quote))
            {
                if (inQuotes)
                {
                    inQuotes = false;
                    quote = '\0';
                }
                else
                {
                    inQuotes = true;
                    tokenStarted = true;
                    quote = character;
                }
                continue;
            }

            if (character == '\\' && index + 1 < commandLine.Length && commandLine[index + 1] == quote)
            {
                current.Append(quote);
                index++;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                AddCurrent(result, current, tokenStarted);
                tokenStarted = false;
                continue;
            }

            current.Append(character);
            tokenStarted = true;
        }

        if (inQuotes)
            throw new ArgumentException("Custom yt-dlp arguments contain an unclosed quote.");

        AddCurrent(result, current, tokenStarted);
        return result;
    }

    private static void AddCurrent(List<string> result, StringBuilder current, bool started)
    {
        if (current.Length == 0 && !started)
            return;
        result.Add(current.ToString());
        current.Clear();
    }
}

public sealed class CustomArgumentException(string reason) : ArgumentException("Custom yt-dlp arguments rejected: " + reason)
{
    public string Reason { get; } = reason;
}
