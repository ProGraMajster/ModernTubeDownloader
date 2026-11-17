using System.Text.Json;

namespace ModernTubeDownloader.Models;

/// <summary>Preserves unrecognized persisted fields without interpreting future behavior.</summary>
internal static class JsonExtensionFields
{
    internal static Dictionary<string, JsonElement>? Copy(Dictionary<string, JsonElement>? fields) =>
        fields?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
}
