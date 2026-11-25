using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModernTubeDownloader.Infrastructure;

internal sealed record DiagnosticEnvironment(object Application, object System, object Storage, object Assemblies);

public enum DiagnosticLevel { Trace, Debug, Info, Warn, Error, Fatal }
public enum DiagnosticModule { Application, Organizer, Updater, Batch, Downloads, History, Queue, Live, Settings, Appearance, Localization, Notifications, WebRemote, Tools, Storage, Migration, Network, Framework, NativeHost, Performance, Logging }
public enum DiagnosticOutcome { Requested, Started, Progress, Completed, Failed, Cancelled, Denied, Unavailable, Skipped }
public enum DiagnosticStage { None, Startup, Shutdown, Snapshot, Import, Validate, Scan, Match, Plan, Dispatcher, Preview, Controls, Move, Undo, Recover, Initialize, Check, Download, Checksum, Extract, WaitForParent, Journal, Replace, Rollback, Commit, Restart, Result, Load, Save, Run, Export, Retention, Sample }

public sealed record DiagnosticEvent(DiagnosticModule Module, DiagnosticStage Stage, DiagnosticOutcome Outcome,
    DiagnosticLevel Level = DiagnosticLevel.Info, Guid? OperationId = null, Guid? ParentOperationId = null,
    long? DurationMs = null, Exception? Exception = null, DiagnosticCounters? Counts = null)
{
    internal DiagnosticEnvironment? Snapshot { get; init; }
}

public sealed record DiagnosticCounters(long? Items = null, long? Total = null, long? Files = null, long? Directories = null,
    long? Bytes = null, long? Errors = null, long? Matches = null, long? Missing = null, long? Duplicates = null,
    long? Conflicts = null, long? History = null, long? Queue = null, long? Active = null, long? Live = null,
    long? Code = null, long? Context = null, long? HttpStatus = null, long? ExitCode = null, long? Dropped = null, long? WorkingSet = null, long? PrivateMemory = null,
    long? ManagedHeap = null, long? Threads = null, double? CpuPercent = null, long? Gc0 = null,
    long? Gc1 = null, long? Gc2 = null, long? WaitMs = null, long? MediaFiles = null, long? MetadataFiles = null);

internal enum DiagnosticError { Other, IO, InvalidData, InvalidOperation, FileMissing, DirectoryMissing, DriveMissing, AccessDenied, Json, Timeout, Cancelled, Http, Win32, COM, BadImage, Aggregate, StorageFull, SharingViolation }

internal sealed record DiagnosticRecord(DateTimeOffset Timestamp, DiagnosticLevel LogLevel, ushort EventId, string EventName,
    string ApplicationVersion, Guid ApplicationSessionId, int ProcessId, int ThreadId, DiagnosticModule Module,
    Guid? OperationId, Guid? ParentOperationId, DiagnosticStage Stage, DiagnosticOutcome Outcome,
    long? DurationMs, DiagnosticError? ExceptionType, int? HResult, string? CancellationReason,
    DiagnosticCounters? Counts, string? CallSite = null, DiagnosticEnvironment? Snapshot = null)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static DiagnosticRecord Create(DiagnosticEvent entry, Guid session, string version, string? caller = null)
    {
        var error = entry.Exception?.GetBaseException();
        var code = error?.HResult;
        DiagnosticError? type = error switch
        {
            null => null, OperationCanceledException => DiagnosticError.Cancelled,
            UnauthorizedAccessException => DiagnosticError.AccessDenied, InvalidDataException => DiagnosticError.InvalidData,
            InvalidOperationException => DiagnosticError.InvalidOperation, FileNotFoundException => DiagnosticError.FileMissing,
            DirectoryNotFoundException => DiagnosticError.DirectoryMissing, DriveNotFoundException => DiagnosticError.DriveMissing,
            IOException when code is unchecked((int)0x80070070) or unchecked((int)0x80070027) => DiagnosticError.StorageFull,
            IOException when code is unchecked((int)0x80070020) or unchecked((int)0x80070021) => DiagnosticError.SharingViolation,
            IOException => DiagnosticError.IO, JsonException => DiagnosticError.Json, TimeoutException => DiagnosticError.Timeout,
            HttpRequestException => DiagnosticError.Http, System.ComponentModel.Win32Exception => DiagnosticError.Win32,
            System.Runtime.InteropServices.COMException => DiagnosticError.COM, BadImageFormatException => DiagnosticError.BadImage,
            AggregateException => DiagnosticError.Aggregate, _ => DiagnosticError.Other
        };
        return new(DateTimeOffset.UtcNow, entry.Level, Id(entry.Module, entry.Stage, entry.Outcome),
            Name(entry.Module, entry.Stage, entry.Outcome), version, session, Environment.ProcessId, Environment.CurrentManagedThreadId,
            entry.Module, entry.OperationId, entry.ParentOperationId, entry.Stage, entry.Outcome, entry.DurationMs, type, code,
            entry.Outcome == DiagnosticOutcome.Cancelled ? "CancellationRequested" : null, entry.Counts, caller, entry.Snapshot);
    }

    private static ushort Id(DiagnosticModule module, DiagnosticStage stage, DiagnosticOutcome outcome) =>
        checked((ushort)(1 + ((int)module * 64 + (int)stage) * 16 + (int)outcome));

    private static string Name(DiagnosticModule module, DiagnosticStage stage, DiagnosticOutcome outcome) => $"{module}.{stage}.{outcome}";

    internal static DiagnosticRecord? ReadForExport(string line)
    {
        try
        {
            var value = JsonSerializer.Deserialize<DiagnosticRecord>(line, Json);
            if (value is null || !Enum.IsDefined(value.LogLevel) || !Enum.IsDefined(value.Module) ||
                !Enum.IsDefined(value.Stage) || !Enum.IsDefined(value.Outcome) ||
                value.ExceptionType is { } exception && !Enum.IsDefined(exception) ||
                value.ApplicationSessionId == Guid.Empty || value.ApplicationVersion is not { Length: > 0 and <= 32 } version ||
                !version.All(c => char.IsAsciiDigit(c) || c == '.') || !Version.TryParse(version, out _)) return null;
            return value with
            {
                EventId = Id(value.Module, value.Stage, value.Outcome),
                EventName = Name(value.Module, value.Stage, value.Outcome),
                CancellationReason = value.Outcome == DiagnosticOutcome.Cancelled ? "CancellationRequested" : null,
                CallSite = null,
                Snapshot = null
            };
        }
        catch (JsonException) { return null; }
    }
}
