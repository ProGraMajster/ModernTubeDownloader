using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ModernTubeDownloader.Infrastructure;

public sealed class FileAppLogger : IAppLogger, IDisposable
{
    internal const int MaximumLine = 8192;
    internal static readonly string ApplicationVersion = DiagnosticRecord.ExecutingVersion;
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly Channel<DiagnosticRecord> normal;
    private readonly Channel<DiagnosticRecord> priority;
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(1);
    private readonly string directory;
    private readonly DiagnosticLogLimits limits;
    private readonly TimeProvider clock;
    private readonly Func<string, Stream> open;
    private readonly Task writer;
    private readonly Guid session = Guid.NewGuid();
    private int minimum = (int)DiagnosticLevel.Info;
    private int stopped, lastError, segment;
    private long droppedNormal, droppedPriority;

    public FileAppLogger(string logDirectory) : this(logDirectory, new(), TimeProvider.System) { }

    internal FileAppLogger(string logDirectory, DiagnosticLogLimits limits, TimeProvider clock, Func<string, Stream>? open = null)
    {
        directory = logDirectory;
        this.limits = limits;
        this.clock = clock;
        this.open = open ?? (path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 32768));
        normal = Channel.CreateBounded<DiagnosticRecord>(limits.NormalCapacity);
        priority = Channel.CreateBounded<DiagnosticRecord>(limits.PriorityCapacity);
        writer = Task.Run(WriteLoopAsync);
    }

    internal int LastError => Volatile.Read(ref lastError);
    internal void SetDetail(bool advanced, bool trace) =>
        Volatile.Write(ref minimum, (int)(advanced ? trace ? DiagnosticLevel.Trace : DiagnosticLevel.Debug : DiagnosticLevel.Info));
    public bool IsEnabled(DiagnosticLevel level) => (int)level >= Volatile.Read(ref minimum);
    public void Event(DiagnosticEvent entry)
    {
        if (IsEnabled(entry.Level)) Publish(entry, null);
    }

    public void Info(string message, [CallerFilePath] string file = "", [CallerMemberName] string member = "") =>
        Legacy(DiagnosticLevel.Info, message, null, file, member);
    public void Warning(string message, [CallerFilePath] string file = "", [CallerMemberName] string member = "") =>
        Legacy(DiagnosticLevel.Warn, message, null, file, member);
    public void Error(string message, Exception? exception = null, [CallerFilePath] string file = "", [CallerMemberName] string member = "") =>
        Legacy(DiagnosticLevel.Error, message, exception, file, member);

    private void Legacy(DiagnosticLevel level, string message, Exception? error, string file, string member)
    {
        var (module, callSite) = DiagnosticCallSite.Classify(file, member);
        var status = DiagnosticCallSite.HttpStatus(message);
        var outcome = level == DiagnosticLevel.Error ? DiagnosticOutcome.Failed :
            level == DiagnosticLevel.Warn ? DiagnosticOutcome.Unavailable : DiagnosticOutcome.Progress;
        Publish(new(module, DiagnosticStage.None, outcome, level, Exception: error,
            Counts: status is { } code ? new(Code: code) : null), callSite);
    }

    private void Publish(DiagnosticEvent entry, string? callSite)
    {
        if (Volatile.Read(ref stopped) != 0) return;
        var value = DiagnosticRecord.Create(entry, session, ApplicationVersion, callSite);
        var important = entry.Level >= DiagnosticLevel.Warn;
        if (!(important ? priority : normal).Writer.TryWrite(value))
        {
            if (important) Interlocked.Increment(ref droppedPriority);
            else Interlocked.Increment(ref droppedNormal);
        }
        wake.Writer.TryWrite(true);
    }

    private async Task WriteLoopAsync()
    {
        Stream? stream = null;
        long bytes = 0;
        var opened = DateOnly.MinValue;
        DiagnosticRecord? pending = null;
        try
        {
            while (await wake.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (wake.Reader.TryRead(out _)) { }
                try
                {
                    do
                    {
                        for (var i = 0; i < 256; i++)
                        {
                            pending ??= Read();
                            if (pending is null) break;
                            Write(pending);
                            pending = null;
                        }
                        var low = Interlocked.Exchange(ref droppedNormal, 0);
                        var high = Interlocked.Exchange(ref droppedPriority, 0);
                        try
                        {
                            if (low != 0 || high != 0)
                                Write(DiagnosticRecord.Create(new(DiagnosticModule.Logging, DiagnosticStage.Run, DiagnosticOutcome.Skipped,
                                    DiagnosticLevel.Warn, Counts: new(Dropped: low, Errors: high)), session, ApplicationVersion));
                            stream?.Flush();
                            Volatile.Write(ref lastError, 0);
                        }
                        catch
                        {
                            Interlocked.Add(ref droppedNormal, low);
                            Interlocked.Add(ref droppedPriority, high);
                            throw;
                        }
                    } while (Volatile.Read(ref stopped) != 0 && HasWork());
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    Volatile.Write(ref lastError, error.HResult);
                    Close(stream);
                    stream = null;
                    if (Volatile.Read(ref stopped) != 0) break;
                    await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                if (pending is not null || HasWork()) wake.Writer.TryWrite(true);
            }
        }
        finally { Close(stream); }

        DiagnosticRecord? Read() => priority.Reader.TryRead(out var value) || normal.Reader.TryRead(out value) ? value : null;
        bool HasWork() => priority.Reader.TryPeek(out _) || normal.Reader.TryPeek(out _);

        void Write(DiagnosticRecord value)
        {
            var data = Utf8.GetBytes(JsonSerializer.Serialize(value, DiagnosticRecord.Json) + "\n");
            var now = clock.GetUtcNow();
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            if (stream is null || opened != today || bytes + data.Length > limits.FileBytes)
            {
                Close(stream);
                stream = null;
                Directory.CreateDirectory(directory);
                DiagnosticLogFiles.Prune(directory, limits, now, reserve: limits.FileBytes);
                var file = Path.Combine(directory, $"MTD-events-v1-{today:yyyyMMdd}-{session:N}-{++segment:D6}.log");
                stream = open(file);
                opened = today;
                bytes = 0;
            }
            stream.Write(data);
            bytes += data.Length;
        }
    }

    internal static void Emergency(string directory, Exception error)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var id = Guid.NewGuid();
            var record = DiagnosticRecord.Create(new(DiagnosticModule.Application, DiagnosticStage.Run, DiagnosticOutcome.Failed,
                DiagnosticLevel.Fatal, Exception: error, Counts: UiCrashDiagnostics.CrashCounts()), id, ApplicationVersion);
            DiagnosticLogFiles.Prune(directory, new(), DateTimeOffset.UtcNow, 8192);
            var file = Path.Combine(directory, $"MTD-events-v1-{DateTime.UtcNow:yyyyMMdd}-{id:N}-000001.log");
            File.WriteAllText(file, JsonSerializer.Serialize(record, DiagnosticRecord.Json) + "\n", Utf8);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    private static void Close(Stream? stream)
    {
        try { stream?.Dispose(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        priority.Writer.TryComplete();
        normal.Writer.TryComplete();
        wake.Writer.TryWrite(true);
        wake.Writer.TryComplete();
        try { writer.Wait(TimeSpan.FromSeconds(3)); } catch (AggregateException) { }
    }
}
