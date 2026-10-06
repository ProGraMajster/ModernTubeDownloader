using System.Globalization;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Settings;

public enum FileConflictBehavior
{
    Rename,
    Overwrite,
    Fail
}

public enum AppThemeMode
{
    System,
    Light,
    Dark
}

public sealed class AppSettings
{
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;
    public string Language { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pl", StringComparison.OrdinalIgnoreCase)
        ? "pl"
        : "en";

    public string TemporaryDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ModernTubeDownloader", "Temp");

    public string FinalOutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "YouTube");

    public string DefaultQualityPresetId { get; set; } = "best";
    public PreferredVideoContainer PreferredVideoContainer { get; set; } = PreferredVideoContainer.Auto;
    public bool BrowserIntegrationEnabled { get; set; } = true;
    public bool EnableWebRemote { get; set; }
    public bool WebRemoteRequireAuthentication { get; set; } = true;
    public int WebRemotePort { get; set; } = 18765;
    // Explicit address selection prevents accidentally binding VPN, public or wildcard interfaces.
    public string WebRemoteBindAddress { get; set; } = "127.0.0.1";
    public bool KeepTemporaryFilesAfterSuccessfulMerge { get; set; }
    public int MaxSimultaneousDownloads { get; set; } = 1;
    public bool RetryFailedDownloads { get; set; } = true;
    public int AdditionalRetryAttempts { get; set; } = 2;
    public bool ResumeInterruptedDownloads { get; set; } = true;
    public bool PreservePartialDownloadsOnFailure { get; set; } = true;
    public bool AutoResumeAfterRestart { get; set; }
    public int MinimumLiveCheckSeconds { get; set; } = 30;
    public int MaximumLiveCheckSeconds { get; set; } = 60;
    public int MaxSimultaneousLiveRecordings { get; set; } = 1;
    public bool PreservePartialLiveOnFailure { get; set; } = true;
    public bool ResumeLiveOnFailure { get; set; } = true;
    public bool AutoResumeLiveAfterRestart { get; set; }
    public bool PreservePartialLiveOnCancel { get; set; }
    public bool EnableInterDownloadDelay { get; set; }
    public int MinimumInterDownloadDelaySeconds { get; set; } = 3;
    public int MaximumInterDownloadDelaySeconds { get; set; } = 20;
    public bool QueuePaused { get; set; }
    public bool ShowQueuePositionNumbers { get; set; } = true;
    public bool UseCustomYtDlp { get; set; }
    public string? CustomYtDlpPath { get; set; }
    public bool UseCustomFfmpeg { get; set; }
    public string? CustomFfmpegPath { get; set; }
    public string? CustomFfprobePath { get; set; }
    public bool UseCustomDeno { get; set; }
    public string? CustomDenoPath { get; set; }

    // Legacy JSON properties remain readable so existing users keep their paths.
    public string? YtDlpPath
    {
        get => CustomYtDlpPath;
        set
        {
            CustomYtDlpPath = value;
            if (!string.IsNullOrWhiteSpace(value)) UseCustomYtDlp = true;
        }
    }

    public string? FfmpegPath
    {
        get => CustomFfmpegPath;
        set
        {
            CustomFfmpegPath = value;
            if (!string.IsNullOrWhiteSpace(value)) UseCustomFfmpeg = true;
        }
    }
    public string FilenameTemplate { get; set; } = "%(title).160s [%(id)s].%(ext)s";
    public bool OverwriteExistingFiles { get; set; }
    public FileConflictBehavior ConflictBehavior { get; set; } = FileConflictBehavior.Rename;
    public bool StoreMetadataJson { get; set; } = true;
    public bool SetFileCreationTimeFromMediaPublishDate { get; set; } = true;
    public bool SaveMetadataJsonSidecar { get; set; } = true;
    public bool DownloadThumbnail { get; set; } = true;
    public string CustomYtDlpArguments { get; set; } = string.Empty;
    public SubtitleOptions SubtitleDefaults { get; set; } = new();
    public bool UseCookieFile { get; set; }
    public string? CookieFilePath { get; set; }
    public SponsorBlockOptions SponsorBlockDefaults { get; set; } = new();
}
