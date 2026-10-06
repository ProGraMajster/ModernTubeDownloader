using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public static class VerifiedSourcesRegistry
{
    // Evidence is scoped per extractor and feature. An analysis-only success never promotes download support.
    public static IReadOnlyList<SourceVerification> Get(string runtimeName) =>
        ExtractorIdentityService.RuntimeName(runtimeName).ToLowerInvariant() switch
        {
            "youtube" =>
            [
                new(SourceCapability.Analysis, "VALIDATION_REPORT.md; RELEASE_CHECKLIST.md (2026-10-03)", "Sources.Scope.YouTubeAnalysis"),
                new(SourceCapability.VodDownload, "VALIDATION_REPORT.md (2026-10-03)", "Sources.Scope.YouTubeVod"),
                new(SourceCapability.ReplayDownload, "RELEASE_CHECKLIST.md (2026-09-27)", "Sources.Scope.YouTubeReplay"),
                new(SourceCapability.Subtitles, "RELEASE_CHECKLIST.md (Sintel MP4/MKV/WebM)", "Sources.Scope.YouTubeSubtitles")
            ],
            "twitch:stream" =>
            [
                new(SourceCapability.Analysis, "VALIDATION_REPORT.md (2026-10-03)", "Sources.Scope.TwitchAnalysis"),
                new(SourceCapability.LiveCapture, "VALIDATION_REPORT.md (2026-10-03)", "Sources.Scope.TwitchCapture"),
                new(SourceCapability.LiveRecovery, "VALIDATION_REPORT.md (2026-10-03)", "Sources.Scope.TwitchRecovery")
            ],
            _ => []
        };
}
