using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Services;

public sealed class SettingsService(AppPaths paths, IAppLogger logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public AppSettings Current { get; private set; } = new();
    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Current = await AtomicJsonFile.ReadAsync<AppSettings>(paths.SettingsFile, cancellationToken).ConfigureAwait(false) ?? new AppSettings();
            Normalize(Current);
            LogSanitizer.RegisterCookieFilePath(Current.CookieFilePath);
        }
        catch (Exception ex)
        {
            logger.Error("Could not read settings; defaults will be used.", ex);
            Current = new AppSettings();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Normalize(Current);
        LogSanitizer.RegisterCookieFilePath(Current.CookieFilePath);
        if (Current.UseCookieFile)
            Current.CookieFilePath = CookieFileService.Validate(Current.CookieFilePath);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicJsonFile.WriteAsync(paths.SettingsFile, Current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void Normalize(AppSettings settings)
    {
        var defaults = new AppSettings();
        if (!Enum.IsDefined(settings.ThemeMode))
            settings.ThemeMode = AppThemeMode.System;
        if (!Enum.IsDefined(settings.ConflictBehavior))
            settings.ConflictBehavior = FileConflictBehavior.Rename;
        if (!Enum.IsDefined(settings.PreferredVideoContainer))
            settings.PreferredVideoContainer = Models.PreferredVideoContainer.Auto;
        settings.MaxSimultaneousDownloads = Math.Clamp(settings.MaxSimultaneousDownloads, 1, 3);
        settings.MaxSimultaneousLiveRecordings = Math.Clamp(settings.MaxSimultaneousLiveRecordings, 1, 3);
        settings.WebRemotePort = Math.Clamp(settings.WebRemotePort, 1024, 65535);
        settings.WebRemoteBindAddress = string.IsNullOrWhiteSpace(settings.WebRemoteBindAddress)
            ? "127.0.0.1" : settings.WebRemoteBindAddress.Trim();
        settings.AdditionalRetryAttempts = Math.Clamp(settings.AdditionalRetryAttempts, 0, 5);
        settings.MinimumLiveCheckSeconds = Math.Clamp(settings.MinimumLiveCheckSeconds, 30, 3600);
        settings.MaximumLiveCheckSeconds = Math.Clamp(settings.MaximumLiveCheckSeconds,
            settings.MinimumLiveCheckSeconds, 3600);
        settings.MinimumInterDownloadDelaySeconds = Math.Clamp(settings.MinimumInterDownloadDelaySeconds, 0, 300);
        settings.MaximumInterDownloadDelaySeconds = Math.Clamp(settings.MaximumInterDownloadDelaySeconds,
            settings.MinimumInterDownloadDelaySeconds, 300);
        settings.Language = string.Equals(settings.Language, "pl", StringComparison.OrdinalIgnoreCase) ? "pl" : "en";
        settings.TemporaryDirectory = string.IsNullOrWhiteSpace(settings.TemporaryDirectory)
            ? defaults.TemporaryDirectory
            : settings.TemporaryDirectory.Trim();
        settings.FinalOutputDirectory = string.IsNullOrWhiteSpace(settings.FinalOutputDirectory)
            ? defaults.FinalOutputDirectory
            : settings.FinalOutputDirectory.Trim();
        settings.DefaultQualityPresetId = Models.QualityPreset.Find(settings.DefaultQualityPresetId ?? defaults.DefaultQualityPresetId).Id;
        settings.FilenameTemplate = string.IsNullOrWhiteSpace(settings.FilenameTemplate)
            ? defaults.FilenameTemplate
            : settings.FilenameTemplate.Trim();
        settings.CustomYtDlpArguments ??= string.Empty;
        settings.SubtitleDefaults ??= new();
        settings.SubtitleDefaults.Languages ??= [];
        settings.SponsorBlockDefaults ??= new();
        settings.SponsorBlockDefaults.Categories ??= ["sponsor"];
        settings.CookieFilePath = settings.CookieFilePath?.Trim();
        try { settings.SubtitleDefaults.Validate(); }
        catch (ArgumentException) { settings.SubtitleDefaults = new(); }
        try { settings.SponsorBlockDefaults.ToYtDlpCategories(); }
        catch (ArgumentException) { settings.SponsorBlockDefaults = new(); }
        if (settings.SubtitleDefaults.Enabled && settings.SponsorBlockDefaults.Mode == Models.SponsorBlockMode.Remove)
            settings.SponsorBlockDefaults.Mode = Models.SponsorBlockMode.Off;
    }
}
