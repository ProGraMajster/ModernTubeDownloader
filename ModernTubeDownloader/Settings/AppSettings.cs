using System.Globalization;

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
    public bool KeepTemporaryFilesAfterSuccessfulMerge { get; set; }
    public int MaxSimultaneousDownloads { get; set; } = 1;
    public bool UseCustomYtDlp { get; set; }
    public string? CustomYtDlpPath { get; set; }
    public bool UseCustomFfmpeg { get; set; }
    public string? CustomFfmpegPath { get; set; }
    public string? CustomFfprobePath { get; set; }

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
}
