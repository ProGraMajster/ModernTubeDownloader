using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Services;

public sealed class SettingsService(AppPaths paths, IAppLogger logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public AppSettings Current { get; private set; } = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Current = await AtomicJsonFile.ReadAsync<AppSettings>(paths.SettingsFile, cancellationToken).ConfigureAwait(false) ?? new AppSettings();
            Normalize(Current);
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
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicJsonFile.WriteAsync(paths.SettingsFile, Current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static void Normalize(AppSettings settings)
    {
        settings.MaxSimultaneousDownloads = Math.Clamp(settings.MaxSimultaneousDownloads, 1, 3);
        settings.Language = settings.Language.Equals("pl", StringComparison.OrdinalIgnoreCase) ? "pl" : "en";
        settings.DefaultQualityPresetId = Models.QualityPreset.Find(settings.DefaultQualityPresetId).Id;
        settings.FilenameTemplate = string.IsNullOrWhiteSpace(settings.FilenameTemplate)
            ? "%(title).160s [%(id)s].%(ext)s"
            : settings.FilenameTemplate.Trim();
    }
}
