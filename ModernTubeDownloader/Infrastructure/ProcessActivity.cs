using System.Collections.Concurrent;
using System.Diagnostics;

namespace ModernTubeDownloader.Infrastructure;

/// <summary>Sanitized, process-local presentation of children started by the canonical runner.</summary>
internal static class ProcessActivity
{
    private static readonly ConcurrentDictionary<int, (string Name, long Start)> active = new();
    internal sealed record Activity(string Name, TimeSpan Elapsed);
    public static IDisposable Track(int id, string executable)
    {
        var name = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
        // Custom executable paths and arguments never enter the UI telemetry.
        name = name switch { "yt-dlp" => "yt-dlp", "ffmpeg" => "FFmpeg", "ffprobe" => "ffprobe", "deno" => "Deno", _ => "Tool" };
        active[id] = (name, Stopwatch.GetTimestamp()); return new Lease(id);
    }
    public static IReadOnlyList<Activity> Snapshot() => active.Values.Select(v => new Activity(v.Name, Stopwatch.GetElapsedTime(v.Start))).ToArray();
    private sealed class Lease(int id) : IDisposable { public void Dispose() => active.TryRemove(id, out _); }
}
