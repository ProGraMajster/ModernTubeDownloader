using System.Text.Json.Serialization;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Batch;

public enum BatchEntryStatus { Pending, Validating, Analyzing, Ready, Duplicate, Unsupported, AuthRequired, AddedToQueue, Downloading, Completed, Failed, Skipped, Cancelled }
public enum BatchFilter { All, Ready, Errors, Duplicates, Unsupported, AuthRequired, Queued, Completed }
public sealed class BatchImportEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Ordinal { get; set; }
    public string Url { get; set; } = "";
    public string Identity { get; set; } = "";
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string Quality { get; set; } = "best";
    public PreferredVideoContainer Container { get; set; }
    public MediaTimeRange Range { get; set; } = MediaTimeRange.Full;
    public BatchEntryStatus Status { get; set; }
    public string? ErrorKey { get; set; }
    public Guid? QueueItemId { get; set; }
    public bool HasMetadata { get; set; }
    public long? EstimatedSizeBytes { get; set; }
    public double? DurationSeconds { get; set; }
    public bool Removed { get; set; }
    public long Revision { get; set; }
    public BatchImportEntry Copy() => (BatchImportEntry)MemberwiseClone();
}
public sealed class BatchImportSession
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string DefaultQuality { get; set; } = "best";
    public PreferredVideoContainer DefaultContainer { get; set; }
    public int AnalysisWorkers { get; set; } = 1;
    public bool Paused { get; set; } = true;
    public int ImportedCount { get; set; }
    [JsonIgnore] internal object Sync { get; } = new();
    [JsonIgnore] internal List<BatchImportEntry> Entries { get; } = [];
    [JsonIgnore] internal Dictionary<Guid, BatchImportEntry> ById { get; } = [];
    public IReadOnlyList<BatchImportEntry> Snapshot() { lock (Sync) return Entries.Where(entry => !entry.Removed).Select(entry => entry.Copy()).ToArray(); }
}
public sealed record BatchPage(IReadOnlyList<BatchImportEntry> Entries, int Total, int Page, int Pages);
public sealed record BatchRawEntry(string Url, string? Quality = null, string? Container = null, string? Start = null, string? End = null, string? ErrorKey = null);
public sealed record BatchOperationProgress(string Phase, int Completed, int? Total = null);
