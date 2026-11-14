using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace ModernTubeDownloader.Infrastructure;

// Opt-in, bounded numeric diagnostics. Never record URLs, titles, paths or credentials.
internal static class PerformanceDiagnostics
{
    private static readonly ConcurrentDictionary<string, double> counters = new();
    private static readonly object sync = new();
    private static StreamWriter? writer;
    private static System.Threading.Timer? sampler;
    private static ModernFormsNext.Timer? heartbeat;
    private static long uiTick;
    private static int records;
    private static WeakReference<ModernFormsNext.Form>? window;
    private static long lastRenderAudit;
    internal static void AttachWindow(ModernFormsNext.Form form)
    {
        if (Enabled && Environment.GetEnvironmentVariable("MTD_RENDER_AUDIT") == "1") window = new(form);
    }
    internal static bool Enabled => writer is not null;
    internal static Func<long>? BitmapBytes;
    internal static Action? SampleSources;
    internal static void Start(AppPaths paths)
    {
        if (Environment.GetEnvironmentVariable("MTD_PERFORMANCE_DIAGNOSTICS") != "1") return;
        try
        {
            paths.EnsureCreated();
            writer = new StreamWriter(Path.Combine(paths.RootDirectory, "performance-" + Environment.ProcessId + ".jsonl"), append: false) { AutoFlush = true };
            Emit("process.entry");
            sampler = new System.Threading.Timer(_ => Sample(), null, 0, 1000);
        }
        catch (IOException) { writer = null; }
        catch (UnauthorizedAccessException) { writer = null; }
    }
    internal static void UiReady()
    {
        if (!Enabled) return;
        Emit("ui.input-ready");
        uiTick = Stopwatch.GetTimestamp();
        heartbeat = new ModernFormsNext.Timer { Interval = 100 };
        heartbeat.Tick += (_, _) =>
        {
            var previous = Interlocked.Exchange(ref uiTick, Stopwatch.GetTimestamp());
            var gap = Stopwatch.GetElapsedTime(previous).TotalMilliseconds;
            counters.AddOrUpdate("uiHeartbeatMaxGapMs", gap, (_, prior) => Math.Max(gap, prior));
            if (window is not null && Stopwatch.GetElapsedTime(lastRenderAudit).TotalSeconds >= 1)
            {
                lastRenderAudit = Stopwatch.GetTimestamp();
                if (window.TryGetTarget(out var form)) AuditRenderBuffers(form);
            }
        };
        heartbeat.Start();
    }
    internal static void Set(string name, double value) { if (Enabled) counters[name] = value; }
    internal static void Increment(string name) { if (Enabled) counters.AddOrUpdate(name, 1, (_, value) => value + 1); }
    internal static IDisposable? Measure(string name) => Enabled ? new Measurement(name) : null;
    private sealed class Measurement(string name) : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp();
        public void Dispose() => Emit(name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
    internal static void Emit(string name, double? elapsedMs = null)
    {
        if (!Enabled) return;
        lock (sync)
        {
            if (writer is null || ++records > 20000) return;
            try { writer.WriteLine(JsonSerializer.Serialize(new { utc = DateTime.UtcNow, pid = Environment.ProcessId, thread = Environment.CurrentManagedThreadId, name, elapsedMs, counters = counters.ToArray() })); }
            catch (IOException) { /* Diagnostics never change application behavior. */ }
        }
    }
    private static void Sample()
    {
        if (!Enabled) return;
        try
        {
            using var process = Process.GetCurrentProcess();
            var gc = GC.GetGCMemoryInfo();
            Set("workingSet", process.WorkingSet64); Set("privateBytes", process.PrivateMemorySize64);
            Set("managedBytes", GC.GetTotalMemory(false)); Set("gcHeapBytes", gc.HeapSizeBytes);
            Set("lohBytes", gc.GenerationInfo.Length > 3 ? gc.GenerationInfo[3].SizeAfterBytes : 0);
            Set("allocatedTotalBytes", GC.GetTotalAllocatedBytes()); Set("handles", process.HandleCount);
            Set("gc0", GC.CollectionCount(0)); Set("gc1", GC.CollectionCount(1)); Set("gc2", GC.CollectionCount(2));
            Set("nativeBitmapBytes", BitmapBytes?.Invoke() ?? 0);
            SampleSources?.Invoke();
            if (Interlocked.Read(ref uiTick) is var tick && tick != 0) Set("uiHeartbeatAgeMs", Stopwatch.GetElapsedTime(tick).TotalMilliseconds);
            Emit("sample");
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException) { }
    }
    internal static void Stop()
    {
        heartbeat?.Dispose(); sampler?.Dispose();
        Sample();
        Emit("process.stop");
        lock (sync) { writer?.Dispose(); writer = null; }
        BitmapBytes = null; SampleSources = null; window = null;
    }

    // Read-only audit of the centrally pinned renderer, enabled separately from normal
    // diagnostics. No field is written, no native pointer is borrowed, and no UI content
    // is logged. The public profiler counts surfaces but does not expose their live bytes.
    private static void AuditRenderBuffers(ModernFormsNext.Form form)
    {
        var bufferField = typeof(ModernFormsNext.Control).GetField("back_buffer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (bufferField is null) { Set("render.auditAvailable", 0); return; }
        var started = Stopwatch.GetTimestamp();
        var seen = new HashSet<ModernFormsNext.Control>();
        var buffers = new HashSet<SkiaSharp.SKBitmap>();
        var images = new HashSet<SkiaSharp.SKBitmap>();
        long bytes = 0, hiddenBytes = 0;
        void Visit(ModernFormsNext.Control control)
        {
            if (seen.Count >= 10000 || !seen.Add(control)) return;
            if (control is ModernFormsNext.PictureBox { Image: SkiaSharp.SKBitmap image }) images.Add(image);
            if (bufferField.GetValue(control) is SkiaSharp.SKBitmap bitmap && buffers.Add(bitmap))
            {
                var size = (long)bitmap.RowBytes * bitmap.Height;
                bytes += size;
                if (!control.Visible) hiddenBytes += size;
            }
            foreach (var child in control.Controls) Visit(child);
        }
        foreach (var control in form.Controls) Visit(control);
        Visit(form.TitleBar);
        Set("render.auditAvailable", 1); Set("render.controls", seen.Count);
        Set("render.buffers", buffers.Count); Set("render.bufferBytes", bytes); Set("render.hiddenBufferBytes", hiddenBytes);
        Set("render.imageBitmaps", images.Count);
        Set("render.auditMs", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}
