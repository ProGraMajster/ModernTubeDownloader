using System.Diagnostics;
#if DEBUG
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using ModernFormsNext;
using Path = System.IO.Path;
#endif

namespace ModernTubeDownloader.Views;

internal sealed partial class BatchImportView
{
    // Opt-in QA only. Calls are removed from Release, including argument evaluation.
    [Conditional("DEBUG")]
    private void InitializeBatchTrace()
    {
#if DEBUG
        batchTrace = BatchTraceSink.TryCreate(services.Paths.RootDirectory);
        if (batchTrace is null) return;
        TraceControl(this, "view"); TraceControl(body, "body"); TraceControl(rows, "rows");
        TraceControl(workflowMode, "workflowMode"); TraceControl(sessionChoice, "sessionChoice");
        TraceControl(filter, "filter"); TraceControl(quality, "quality"); TraceControl(container, "container");
        batchTrace.Record("initialize", BatchTraceState());
#endif
    }

    [Conditional("DEBUG")]
    private void TraceBatchEvent(string name)
    {
#if DEBUG
        if (batchTrace is { } trace) { trace.Count(name); trace.Record(name, BatchTraceState()); }
#endif
    }

    [Conditional("DEBUG")]
    private void TraceBatchRow(ModernFormsNext.Control row, bool created)
    {
#if DEBUG
        if (batchTrace is not { } trace) return;
        var name = created ? "row.create" : "row.dispose";
        trace.Count(name);
        trace.Record(name, new { identity = TraceIdentity(row), children = row.Controls.Count, rows = rows.Controls.Count });
        if (created)
        {
            // Owned row events do not root retired rows from the view's subscription list.
            row.SizeChanged += (_, _) =>
            {
                if (batchTrace is not { } current) return;
                current.Count("SizeChanged"); current.Count("row.SizeChanged");
                current.Record("row.SizeChanged", new { identity = TraceIdentity(row), row.Width, row.Height });
            };
        }
#endif
    }

    [Conditional("DEBUG")]
    private void StopBatchTrace()
    {
#if DEBUG
        if (batchTrace is not { } trace) return;
        foreach (var unsubscribe in batchTraceSubscriptions) unsubscribe();
        batchTraceSubscriptions.Clear();
        trace.Complete(BatchTraceState());
        batchTrace = null;
#endif
    }

#if DEBUG
    private BatchTraceSink? batchTrace;
    private readonly List<Action> batchTraceSubscriptions = [];
    private readonly ConditionalWeakTable<Control, BatchTraceIdentity> batchTraceIdentities = new();
    private int nextBatchTraceIdentity;
    private readonly Stack<string> batchTraceScopes = new();
    private sealed record BatchTraceIdentity(int Value);
    private int TraceIdentity(Control? control) => control is null ? 0 :
        batchTraceIdentities.GetValue(control, _ => new(++nextBatchTraceIdentity)).Value;

    private object TraceControlState(Control control) => new
    {
        identity = TraceIdentity(control), parent = TraceIdentity(control.Parent),
        enabled = control.Enabled, visible = control.Visible, focused = control.Focused,
        bounds = new { control.Left, control.Top, control.Width, control.Height },
        children = control.Controls.Count,
        selectedIndex = control is ComboBox combo ? combo.SelectedIndex : (int?)null,
        itemCount = control is ComboBox choices ? choices.Items.Count : (int?)null,
        droppedDown = control is ComboBox dropdown ? dropdown.DroppedDown : (bool?)null
    };

    private object BatchTraceState() => new
    {
        busy, disposed, dashboard, page, hasSession = session is not null,
        // Counts only: no titles, list names, URLs, free text, paths, or metadata.
        imported = session?.ImportedCount ?? 0, selectedCount = selection.Count,
        sessionCount = visibleSessions.Count, isOperating = services.Batch.IsOperating,
        rows = rows.Controls.Count, scope = batchTraceScopes.TryPeek(out var scope) ? scope : null,
        view = TraceControlState(this), body = TraceControlState(body), rowHost = TraceControlState(rows),
        workflowMode = TraceControlState(workflowMode), sessionChoice = TraceControlState(sessionChoice),
        filter = TraceControlState(filter), quality = TraceControlState(quality), container = TraceControlState(container)
    };

    private void TraceControl(Control control, string name)
    {
        void Observe(string eventName)
        {
            if (batchTrace is not { } trace) return;
            trace.Count(eventName); trace.Count(name + "." + eventName);
            // Method names alone capture the synchronous causal chain, never file paths or arguments.
            var callers = eventName is "DropDownClosed" or "EnabledChanged"
                ? new StackTrace(false).GetFrames().Skip(1).Take(14).Select(frame => frame.GetMethod())
                    .Select(method => method?.DeclaringType?.FullName + "." + method?.Name).ToArray()
                : null;
            trace.Record(name + "." + eventName, new
            {
                busy, dashboard, scope = batchTraceScopes.TryPeek(out var scope) ? scope : null,
                control = TraceControlState(control), callers
            });
        }
        void Hook(Action<EventHandler> add, Action<EventHandler> remove, string eventName)
        {
            EventHandler handler = (_, _) => Observe(eventName);
            add(handler); batchTraceSubscriptions.Add(() => remove(handler));
        }
        Hook(h => control.EnabledChanged += h, h => control.EnabledChanged -= h, "EnabledChanged");
        Hook(h => control.VisibleChanged += h, h => control.VisibleChanged -= h, "VisibleChanged");
        Hook(h => control.ParentChanged += h, h => control.ParentChanged -= h, "ParentChanged");
        Hook(h => control.SizeChanged += h, h => control.SizeChanged -= h, "SizeChanged");
        Hook(h => control.LocationChanged += h, h => control.LocationChanged -= h, "LocationChanged");
        Hook(h => control.GotFocus += h, h => control.GotFocus -= h, "GotFocus");
        Hook(h => control.LostFocus += h, h => control.LostFocus -= h, "LostFocus");
        if (control is ComboBox combo)
        {
            Hook(h => combo.DropDownOpened += h, h => combo.DropDownOpened -= h, "DropDownOpened");
            Hook(h => combo.DropDownClosed += h, h => combo.DropDownClosed -= h, "DropDownClosed");
            Hook(h => combo.SelectedIndexChanged += h, h => combo.SelectedIndexChanged -= h, "SelectedIndexChanged");
        }
    }

    private IDisposable? TraceBatchScope(string name)
    {
        if (batchTrace is not { } trace) return null;
        trace.Count(name); batchTraceScopes.Push(name);
        var started = Stopwatch.GetTimestamp();
        trace.Record(name + ".begin", BatchTraceState());
        return new BatchTraceScope(() =>
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            trace.Duration(name, elapsed);
            trace.Record(name + ".end", new { elapsedMs = elapsed, state = BatchTraceState() });
            batchTraceScopes.Pop();
        });
    }
    private sealed class BatchTraceScope(Action complete) : IDisposable { public void Dispose() => complete(); }

    private sealed class BatchTraceSink
    {
        private const int Limit = 4096;
        private readonly Channel<object> pending = Channel.CreateBounded<object>(new BoundedChannelOptions(Limit)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        private readonly Dictionary<string, long> counts = [];
        private readonly Dictionary<string, double> durations = [];
        private readonly long started = Stopwatch.GetTimestamp();
        private int records;
        private bool completed;
        private volatile bool writeFailed;

        private BatchTraceSink(FileStream stream)
        {
            // File IO/JSON serialization stay off the UI thread. Records contain detached primitives only.
            _ = Task.Run(async () =>
            {
                using var writer = new StreamWriter(stream) { AutoFlush = true };
                try
                {
                    await foreach (var item in pending.Reader.ReadAllAsync())
                        await writer.WriteLineAsync(JsonSerializer.Serialize(item));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
                { writeFailed = true; }
            });
        }

        public static BatchTraceSink? TryCreate(string profile)
        {
            var destination = Environment.GetEnvironmentVariable("MTD_QA_BATCH_TRACE");
            var configuredProfile = Environment.GetEnvironmentVariable("MODERNTUBEDOWNLOADER_DATA_ROOT");
            if (string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(configuredProfile) ||
                !Path.IsPathFullyQualified(destination) || !Path.IsPathFullyQualified(configuredProfile)) return null;
            try
            {
                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile));
                var requestedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredProfile));
                var output = Path.GetFullPath(destination);
                // Require explicit profile ownership, a direct child, no reparse ancestors, and a NEW file.
                if (!string.Equals(root, requestedRoot, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetDirectoryName(output), root, StringComparison.OrdinalIgnoreCase)) return null;
                for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
                    if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) return null;
                var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                    16384, FileOptions.Asynchronous | FileOptions.SequentialScan);
                return new BatchTraceSink(stream);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { return null; }
        }

        public void Count(string name) { counts.TryGetValue(name, out var count); counts[name] = count + 1; }
        public void Duration(string name, double elapsed) { durations.TryGetValue(name, out var total); durations[name] = total + elapsed; }
        public void Record(string name, object state)
        {
            if (completed || writeFailed || records >= Limit - 1) return;
            if (records == Limit - 2) Emit("trace.limit", new { maxRecords = Limit });
            else Emit(name, state);
        }
        private void Emit(string name, object state)
        {
            pending.Writer.TryWrite(new
            {
                sequence = ++records, utc = DateTimeOffset.UtcNow,
                elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                threadId = Environment.CurrentManagedThreadId, name, state,
                counts = new Dictionary<string, long>(counts), inclusiveDurationMs = new Dictionary<string, double>(durations)
            });
        }
        public void Complete(object state)
        {
            if (completed) return;
            // One reserved record retains final counters even if detailed event logging hit its limit.
            if (!writeFailed) Emit("dispose", state);
            completed = true; pending.Writer.TryComplete();
        }
    }
#endif
}
