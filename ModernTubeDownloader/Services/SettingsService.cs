using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Authentication;
using System.Text.Json;

namespace ModernTubeDownloader.Services;

public sealed class SettingsService(AppPaths paths, IAppLogger logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public AppSettings Current { get; private set; } = new();
    public event EventHandler? Changed;
    public bool UpdatePolicyNeedsConsent { get; private set; }
    public bool LoadFailed { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // File.Exists also returns false for access errors. Only an actual missing
            // path permits fresh defaults; unreadable user settings remain protected.
            string? json;
            try { json = await File.ReadAllTextAsync(paths.SettingsFile, cancellationToken).ConfigureAwait(false); }
            catch (FileNotFoundException) { json = null; }
            catch (DirectoryNotFoundException) { json = null; }
            Current = json is null ? new AppSettings() : JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options) ?? throw new InvalidDataException("Empty settings object.");
            LoadFailed = false;
            UpdatePolicyNeedsConsent = false;
            Current.Updates ??= new();
            if (json is not null)
            {
                using var document = JsonDocument.Parse(json);
                var updates = document.RootElement.EnumerateObject().FirstOrDefault(p => p.Name.Equals("updates", StringComparison.OrdinalIgnoreCase)).Value;
                var fields = updates.ValueKind == JsonValueKind.Object ? updates.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, JsonElement>();
                if (!fields.ContainsKey("mode")) Current.Updates.Mode = UpdateInstallMode.NotifyOnly;
                UpdatePolicyNeedsConsent = Current.Updates.PolicyVersion < 1 && Current.Updates.AutomaticChecks && Current.Updates.Mode == UpdateInstallMode.NotifyOnly;
            }
            Normalize(Current);
            if (json is not null) UpdatePolicyNeedsConsent = Current.Updates.PolicyVersion < 1 && Current.Updates.AutomaticChecks && Current.Updates.Mode == UpdateInstallMode.NotifyOnly;
            LogSanitizer.RegisterCookieFilePath(Current.CookieFilePath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.Error("Could not read settings; defaults will be used.", ex);
            Current = new AppSettings();
            Current.Updates.AutomaticChecks = false;
            Current.Updates.Mode = UpdateInstallMode.NotifyOnly;
            LoadFailed = true;
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        // Never replace unreadable user data with defaults during routine shutdown.
        if (LoadFailed) throw new InvalidDataException("Existing settings could not be read; the original file is preserved.");
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
        if (Current.Updates.PolicyVersion >= 1) UpdatePolicyNeedsConsent = false;
    }

    internal void PreserveLegacyUpdatePolicyWithoutSettings()
    {
        Current.Updates.Mode = UpdateInstallMode.NotifyOnly;
        UpdatePolicyNeedsConsent = true;
    }

    private static void Normalize(AppSettings settings)
    {
        settings.Startup ??= new();
        settings.Updates ??= new();
        if (!Enum.IsDefined(settings.Updates.Frequency)) settings.Updates.Frequency = default;
        if (!Enum.IsDefined(settings.Updates.Mode)) settings.Updates.Mode = default;
        if (!Enum.IsDefined(settings.Startup.MinimizeBehavior)) settings.Startup.MinimizeBehavior = default;
        if (!Enum.IsDefined(settings.Startup.CloseBehavior)) settings.Startup.CloseBehavior = default;
        settings.Notifications ??= new();
        if (!Enum.IsDefined(settings.Notifications.Sound)) settings.Notifications.Sound = NotificationSound.Default;
        if (!Enum.IsDefined(settings.Notifications.Presentation)) settings.Notifications.Presentation = NotificationPresentation.Normal;
        var defaults = new AppSettings();
        if (settings.CookieSource is { } source && !Enum.IsDefined(source)) settings.CookieSource = AuthenticationSource.None;
        if (settings.CookieSource is not null) settings.UseCookieFile = settings.CookieSource == AuthenticationSource.ManualFile;
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
        settings.Language = ModernTubeDownloader.Localization.LocaleRegistry.Normalize(settings.Language);
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
