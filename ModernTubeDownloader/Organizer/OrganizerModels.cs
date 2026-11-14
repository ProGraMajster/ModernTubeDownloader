using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Organizer;

public enum OrganizerMatchStatus { Found, Missing, Ambiguous, Conflict, AlreadyOrganized, Attention, Invalid, Duplicate }
public enum OrganizerJournalState { Prepared, Moving, UpdatingReferences, Completed, RecoveryRequired, Undoing, Undone }
public enum OrganizerFileState { Pending, Published, SourceRemoved }

public sealed record OrganizerSourceList(string Name, string DestinationDirectory, string? SourceFile = null,
    IReadOnlyList<BatchImportEntry>? Entries = null);

public sealed class OrganizerFile
{
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTime LastWriteUtc { get; set; }
    public DateTime CreationUtc { get; set; }
    public bool CopyOnly { get; set; }
    public OrganizerFileState State { get; set; }
    public bool TransferStarted { get; set; }
    public bool UndoCompleted { get; set; }
}

public sealed class OrganizerMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ListName { get; set; } = "";
    public string Url { get; set; } = "";
    public string Identity { get; set; } = "";
    public MediaTimeRange RequestedRange { get; set; } = MediaTimeRange.Full;
    public string RequestedQuality { get; set; } = "best";
    public string? MediaPath { get; set; }
    public string DestinationDirectory { get; set; } = "";
    public OrganizerMatchStatus Status { get; set; }
    public string ReasonKey { get; set; } = "";
    public string[] Candidates { get; set; } = [];
    public List<OrganizerFile> Files { get; set; } = [];
    public long Bytes => Files.Sum(file => file.Length);
}

public sealed class OrganizerPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ScanRoot { get; set; } = "";
    public bool Recursive { get; set; }
    public bool RequireJson { get; set; } = true;
    public List<OrganizerMatch> Matches { get; set; } = [];
    public int ScannedFiles { get; set; }
    public int SkippedFiles { get; set; }
}

public sealed class OrganizerJournal
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public OrganizerJournalState State { get; set; } = OrganizerJournalState.Prepared;
    public string ScanRoot { get; set; } = "";
    public List<OrganizerMatch> Matches { get; set; } = [];
    public string? ErrorCode { get; set; }
    public bool UndoRequested { get; set; }
    public int EntryCount => Matches.Count;
    public int CompletedFiles => Matches.SelectMany(match => match.Files).Count(file => file.State == OrganizerFileState.SourceRemoved);
}

public sealed record OrganizerJournalSummary(Guid Id, DateTimeOffset CreatedAt, OrganizerJournalState State, int EntryCount, string? ErrorCode);

public sealed record OrganizerProgress(string Phase, int CompletedFiles, int TotalFiles, long CompletedBytes, long TotalBytes, string? CurrentName = null);

/// <summary>Test seam at durable boundaries; never used to suppress an I/O error.</summary>
public interface IOrganizerFaultObserver
{
    void Checkpoint(string step, int completedFiles);
    bool ForceVerifiedCopy => false;
}
