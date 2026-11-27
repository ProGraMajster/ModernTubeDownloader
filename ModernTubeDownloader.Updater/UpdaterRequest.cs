namespace ModernTubeDownloader.Updates;

public sealed record UpdaterRequest(string Installation, string Archive, string Sha256, int ParentProcessId, long ParentStartUtcTicks, bool Restart,
    string? StartupCommitToken = null, Guid? OperationId = null, string? ProgressLanguage = null)
{
    public const string RestartEnvironmentKey = "MODERNTUBEDOWNLOADER_UPDATE_RESTART";
}
