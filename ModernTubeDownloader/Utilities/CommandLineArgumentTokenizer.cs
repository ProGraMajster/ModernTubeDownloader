using System.Text;

namespace ModernTubeDownloader.Utilities;

public static class CommandLineArgumentTokenizer
{
    private static readonly HashSet<string> ForbiddenOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "-o", "--output", "-P", "--paths", "-f", "--format", "--progress-template",
        "--dump-json", "--dump-single-json", "--print", "-O", "--print-to-file",
        "--exec", "--exec-before-download", "--config-locations", "--load-info-json",
        "--ffmpeg-location", "--no-simulate", "--simulate"
    };

    public static IReadOnlyList<string> ParseSafeYtDlpArguments(string? commandLine)
    {
        var tokens = Tokenize(commandLine);
        foreach (var token in tokens)
        {
            var option = token.Split('=', 2)[0];
            if (ForbiddenOptions.Contains(option))
                throw new ArgumentException($"Custom yt-dlp option '{option}' is managed by the application and cannot be overridden.");
            if (Uri.TryCreate(token, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                throw new ArgumentException("Custom yt-dlp arguments cannot contain an additional URL.");
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
                AddCurrent(result, current);
                continue;
            }

            current.Append(character);
        }

        if (inQuotes)
            throw new ArgumentException("Custom yt-dlp arguments contain an unclosed quote.");

        AddCurrent(result, current);
        return result;
    }

    private static void AddCurrent(List<string> result, StringBuilder current)
    {
        if (current.Length == 0)
            return;
        result.Add(current.ToString());
        current.Clear();
    }
}
