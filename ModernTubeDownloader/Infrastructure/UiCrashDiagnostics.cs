using System.Text;

namespace ModernTubeDownloader.Infrastructure;

/// <summary>Small, privacy-safe history of structural UI work for fatal crash reports.</summary>
internal static class UiCrashDiagnostics
{
    private const int Capacity = 64;
    private static readonly object Gate = new();
    private static readonly Queue<string> Recent = new(Capacity);
    private static readonly Dictionary<string, int> ContainerCounts = new(StringComparer.Ordinal);
    private static int uiThreadId;
    private static long treeVersion;
    private static string activeView = "not-shown";
    private static Func<bool, string>? stateProvider;

    public static void RegisterUiThread() => Volatile.Write(ref uiThreadId, Environment.CurrentManagedThreadId);

    public static void RegisterStateProvider(Func<bool, string> provider)
    {
        lock (Gate) stateProvider = provider;
    }

    public static void SetActiveView(string view)
    {
        lock (Gate) activeView = view;
        Record("view.changed");
    }

    public static void Record(string operation, string? container = null, int? childCount = null)
    {
        // Callers pass fixed operation/container identifiers, never titles, URLs or control text.
        lock (Gate)
        {
            if (container is not null && childCount is { } count)
            {
                ContainerCounts[container] = count;
                treeVersion++;
            }
            if (Recent.Count == Capacity) Recent.Dequeue();
            Recent.Enqueue($"{DateTimeOffset.Now:O} thread={Environment.CurrentManagedThreadId} {operation}" +
                (container is null ? string.Empty : $" container={container} children={childCount?.ToString() ?? "n/a"}"));
        }
    }

    [System.Diagnostics.Conditional("DEBUG")]
    public static void VerifyUiThread(string operation)
    {
        var expected = Volatile.Read(ref uiThreadId);
        if (expected != 0 && expected != Environment.CurrentManagedThreadId)
        {
            Record("ui-thread-violation." + operation);
            throw new InvalidOperationException($"UI tree mutation '{operation}' on thread {Environment.CurrentManagedThreadId}; UI thread is {expected}.");
        }
    }

    public static string Snapshot()
    {
        Func<bool, string>? provider;
        var builder = new StringBuilder();
        var expected = Volatile.Read(ref uiThreadId);
        var onUiThread = expected != 0 && expected == Environment.CurrentManagedThreadId;
        lock (Gate)
        {
            builder.AppendLine($"UI thread id: {expected}; current thread id: {Environment.CurrentManagedThreadId}");
            builder.AppendLine($"Active view: {activeView}; app tree mutation sequence: {treeVersion}");
            foreach (var count in ContainerCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                builder.AppendLine($"Container {count.Key}: {count.Value} children");
            builder.AppendLine("Recent structural events (oldest first):");
            foreach (var entry in Recent) builder.AppendLine(entry);
            provider = stateProvider;
        }
        if (provider is not null)
        {
            try { builder.AppendLine(provider(onUiThread)); }
            catch (Exception error) { builder.AppendLine($"UI state unavailable: {error.GetType().Name}"); }
        }
        return builder.ToString();
    }
}
