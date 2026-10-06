namespace ModernTubeDownloader.Models;

public enum SourceVerificationStatus { YtDlpSupported, VerifiedByMtd, Limited }
public enum SourceCapability { Analysis, VodDownload, PlaylistAnalysis, ReplayDownload, LiveCapture, LiveRecovery, Subtitles }
public enum SourceMediaKind { Video, Playlist, ActiveLive, Upcoming, Replay, Processing }

public sealed record SourceVerification(SourceCapability Capability, string Evidence, string Scope);
public sealed record SupportedSource(string ExtractorKey, string DisplayName, string Description,
    string Family, bool IsGeneric, bool IsBroken, bool IsPopular,
    SourceVerificationStatus VerificationStatus, IReadOnlyList<SourceVerification> Verification);
public sealed record SupportedSourcesCatalog(string Version, bool Managed, string Fingerprint,
    IReadOnlyList<SupportedSource> Sources);
public sealed record SourceIdentity(string DisplayName, string RuntimeName, string ExtractorKey, bool IsGeneric);
public sealed record SourceFacts(SourceIdentity Identity, SourceMediaKind Kind, string? Availability,
    string? LiveStatus, int Formats, int Subtitles, int AutomaticSubtitles, int Chapters, bool PlaylistContext);
public sealed record SourceCheckResult(string SourceUrl, VideoMetadata? Metadata, PlaylistMetadata? Playlist,
    SourceFacts? Facts, string? MessageKey = null, bool AuthenticationMayBeRequired = false,
    bool OpenLive = false);
