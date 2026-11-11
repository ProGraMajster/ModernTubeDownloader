using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Services;

internal enum SetupProgress { NotStarted, InProgress, Completed, Skipped }
internal enum StartupExperience { None, Welcome, UpgradeChoice, WhatsNew }

internal sealed class SetupDraft
{
    public string Language { get; set; } = "en";
    public AppInterfaceMode Interface { get; set; }
    public string Output { get; set; } = "";
    public string Temporary { get; set; } = "";
    public string Filename { get; set; } = "";
    public string Quality { get; set; } = "best";
    public PreferredVideoContainer Container { get; set; }
    public FileConflictBehavior Conflict { get; set; }
    public bool AutomaticUpdates { get; set; } = true;
    public UpdateCheckFrequency Frequency { get; set; }
    public UpdateInstallMode UpdateMode { get; set; } = UpdateInstallMode.InstallOnExit;
    public bool Notifications { get; set; } = true;
    public bool Tray { get; set; } = true;
    public int Step { get; set; }

    internal static SetupDraft From(AppServices services, bool recommendModern) => new()
    {
        Language = services.Settings.Current.Language,
        Interface = recommendModern ? AppInterfaceMode.ModernDashboard : services.Appearance.Profile.InterfaceMode,
        Output = services.Settings.Current.FinalOutputDirectory, Temporary = services.Settings.Current.TemporaryDirectory,
        Filename = services.Settings.Current.FilenameTemplate, Quality = services.Settings.Current.DefaultQualityPresetId,
        Container = services.Settings.Current.PreferredVideoContainer, Conflict = services.Settings.Current.ConflictBehavior,
        AutomaticUpdates = services.Settings.Current.Updates.AutomaticChecks,
        Frequency = services.Settings.Current.Updates.Frequency,
        UpdateMode = services.Settings.UpdatePolicyNeedsConsent ? UpdateInstallMode.InstallOnExit : services.Settings.Current.Updates.Mode,
        Notifications = services.Settings.Current.Notifications.Enabled, Tray = services.Settings.Current.Startup.ShowTrayIcon
    };
}

internal sealed class UserExperienceState
{
    public int SchemaVersion { get; set; } = 1;
    public bool ExistingProfile { get; set; }
    public SetupProgress Setup { get; set; }
    public SetupDraft? Draft { get; set; }
    public string? InterfaceChoiceFamily { get; set; }
    public bool InterfaceChoiceSkipped { get; set; }
    public string? LastObservedVersion { get; set; }
    public string? LastWhatsNewAcknowledgedVersion { get; set; }
    public string? DeferredWhatsNewVersion { get; set; }
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalState { get; set; }
}

/// <summary>Small atomic journey ledger. It never reads or rewrites queue/history/cookie contents.</summary>
internal sealed class UserExperienceService
{
    private readonly string file;
    private readonly IAppLogger logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool damaged;
    private bool unsupportedSchema;
    private bool storageUnavailable;
    public UserExperienceState State { get; private set; } = new();
    public string Version { get; }
    public bool Available { get; private set; } = true;
    public bool SettingsExisted { get; }
    public bool IsCandidate => Version.Contains('-');
    public const string ChoiceFamily = "1.3";

    private UserExperienceService(AppPaths paths, IAppLogger logger, string version)
    { file = Path.Combine(paths.RootDirectory, "experience.json"); this.logger = logger; Version = version.Split('+')[0]; SettingsExisted = File.Exists(paths.SettingsFile); }

    internal static async Task<UserExperienceService> OpenAsync(AppPaths paths, IAppLogger logger, string version)
    {
        var service = new UserExperienceService(paths, logger, version);
        try
        {
            if (TryGetAttributes(service.file) is { } attributes)
            {
                if ((attributes & FileAttributes.Directory) != 0) throw new IOException("The experience marker is occupied by a directory.");
                var json = await File.ReadAllTextAsync(service.file);
                service.State = JsonSerializer.Deserialize<UserExperienceState>(json, JsonDefaults.Options) ?? throw new InvalidDataException("Empty experience state.");
                service.unsupportedSchema = service.State.SchemaVersion > 1;
                using var document = JsonDocument.Parse(json);
                var fields = document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!fields.Contains("schemaVersion") || !fields.Contains("existingProfile") || !fields.Contains("setup"))
                    throw new InvalidDataException("Incomplete experience state; profile provenance cannot be inferred from defaults.");
                if (service.State.SchemaVersion != 1 || !Enum.IsDefined(service.State.Setup))
                    throw new InvalidDataException("Unsupported experience state.");
                if (service.State.Draft is { } draft)
                {
                    draft.Step = Math.Clamp(draft.Step, 0, 5);
                    if (!Enum.IsDefined(draft.Interface) || !Enum.IsDefined(draft.UpdateMode) || !Enum.IsDefined(draft.Container) ||
                        !Enum.IsDefined(draft.Conflict) || !Enum.IsDefined(draft.Frequency)) service.State.Draft = null;
                    else
                    {
                        draft.Language = string.Equals(draft.Language, "pl", StringComparison.OrdinalIgnoreCase) ? "pl" : "en";
                        draft.Quality = QualityPreset.Find(draft.Quality).Id;
                        draft.Output ??= ""; draft.Temporary ??= ""; draft.Filename ??= "";
                    }
                }
            }
            else
            {
                // Probe before services/tool setup can save their first settings or empty stores.
                // Unlike File.Exists, metadata access does not disguise an ACL failure as absence.
                var known = new[] { paths.SettingsFile, paths.QueueFile, paths.HistoryFile, paths.LiveSessionsFile, Path.Combine(paths.RootDirectory, "appearance.json") };
                service.State.ExistingProfile = known.Any(path => TryGetAttributes(path) is not null) || HasFiles(paths.AuthenticationDirectory) || HasFiles(Path.Combine(paths.RootDirectory, "Batch"));
                service.State.LastObservedVersion = service.State.ExistingProfile ? null : service.Version;
                await service.SaveAsync();
            }
        }
        catch (Exception error)
        {
            logger.Error("Experience state could not be initialized; automatic prompts are suppressed and existing profile evidence is retained.", error);
            service.Available = false;
            service.damaged = error is JsonException or InvalidDataException;
            service.storageUnavailable = !service.damaged;
            service.State = new() { ExistingProfile = true };
        }
        return service;
        static bool HasFiles(string directory) => TryGetAttributes(directory) is { } attributes &&
            ((attributes & FileAttributes.Directory) == 0 || Directory.EnumerateFileSystemEntries(directory).Any());

        static FileAttributes? TryGetAttributes(string path)
        {
            try { return File.GetAttributes(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }
    }

    internal StartupExperience Next(bool classicRecovery, bool backgroundStart, bool externalActivation, AppInterfaceMode selected, bool policyConsent)
    {
        if (!Available || classicRecovery || backgroundStart || externalActivation) return StartupExperience.None;
        if (!State.ExistingProfile && State.Setup is SetupProgress.NotStarted or SetupProgress.InProgress) return StartupExperience.Welcome;
        if (State.ExistingProfile && State.InterfaceChoiceFamily != ChoiceFamily && (selected == AppInterfaceMode.Classic || policyConsent))
            return StartupExperience.UpgradeChoice;
        return State.DeferredWhatsNewVersion != Version && ReleaseNotesCatalog.Supports(Version) &&
            ReleaseNotesCatalog.ShouldShow(Version, State.LastWhatsNewAcknowledgedVersion, State.LastObservedVersion)
            ? StartupExperience.WhatsNew : StartupExperience.None;
    }

    public async Task BeginAsync(SetupDraft draft)
    {
        if (unsupportedSchema || storageUnavailable) throw new InvalidDataException("Experience.UnsupportedState");
        if (damaged)
        {
            // Explicit reopening may recover the marker; keep the exact original bytes.
            File.Copy(file, file + ".preserved-" + Guid.NewGuid().ToString("N"));
            damaged = false; Available = true;
        }
        State.Setup = SetupProgress.InProgress; State.Draft = draft; await SaveAsync();
    }
    public async Task SaveDraftAsync(SetupDraft draft) { State.Draft = draft; await SaveAsync(); }
    public async Task SkipAsync(bool upgrade)
    {
        if (upgrade) { State.InterfaceChoiceFamily = ChoiceFamily; State.InterfaceChoiceSkipped = true; }
        else State.Setup = SetupProgress.Skipped;
        State.Draft = null; State.DeferredWhatsNewVersion = Version; await SaveAsync();
    }
    public async Task CompleteAsync(bool upgrade)
    {
        if (upgrade) { State.InterfaceChoiceFamily = ChoiceFamily; State.InterfaceChoiceSkipped = false; }
        else { State.Setup = SetupProgress.Completed; State.InterfaceChoiceFamily = ChoiceFamily; State.InterfaceChoiceSkipped = false; }
        State.Draft = null; State.LastObservedVersion = Version; await SaveAsync();
    }
    public async Task AcknowledgeWhatsNewAsync()
    { State.LastWhatsNewAcknowledgedVersion = Version; State.LastObservedVersion = Version; await SaveAsync(); }

    private async Task SaveAsync()
    {
        if (!Available) throw new InvalidDataException("Experience.UnsupportedState");
        await gate.WaitAsync();
        try { await AtomicJsonFile.WriteRawJsonAsync(file, JsonSerializer.Serialize(State, JsonDefaults.Options)); }
        finally { gate.Release(); }
    }

    internal static void ValidateFolders(SetupDraft draft)
    {
        foreach (var path in new[] { draft.Output, draft.Temporary })
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || File.Exists(path))
                throw new InvalidDataException("Experience.InvalidFolder");
            var full = Path.GetFullPath(path);
            // Probe the nearest existing directory without creating the chosen folder.
            var ancestor = full;
            while (!Directory.Exists(ancestor)) ancestor = Path.GetDirectoryName(ancestor) ?? throw new InvalidDataException("Experience.InvalidFolder");
            var probe = Path.Combine(ancestor, ".mtd-write-probe-" + Guid.NewGuid().ToString("N"));
            try { using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new InvalidDataException("Experience.InvalidFolder", ex); }
        }
        if (string.IsNullOrWhiteSpace(draft.Filename) || draft.Filename.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
            throw new InvalidDataException("Experience.InvalidFilename");
    }
}

internal static class ReleaseNotesCatalog
{
    internal static bool ShouldShow(string current, string? acknowledged, string? observed)
    {
        if (acknowledged is not null) return CompareVersions(current, acknowledged) > 0;
        return observed is null || CompareVersions(current, observed) >= 0;
    }
    private static int CompareVersions(string left, string right)
    {
        var a = left.Split('+')[0].Split('-', 2); var b = right.Split('+')[0].Split('-', 2);
        if (!System.Version.TryParse(a[0], out var av) || !System.Version.TryParse(b[0], out var bv)) return 0;
        var core = av.CompareTo(bv); if (core != 0) return core;
        if (a.Length == 1 || b.Length == 1) return (b.Length == 1 ? 0 : 1) - (a.Length == 1 ? 0 : 1);
        var ai = a[1].Split('.'); var bi = b[1].Split('.');
        for (var i = 0; i < Math.Min(ai.Length, bi.Length); i++)
        {
            var an = long.TryParse(ai[i], out var al); var bn = long.TryParse(bi[i], out var bl);
            var part = an && bn ? al.CompareTo(bl) : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(ai[i], bi[i]);
            if (part != 0) return part;
        }
        return ai.Length.CompareTo(bi.Length);
    }
    internal static bool Supports(string version) => version.Split('+')[0].Split('-')[0] == "1.3.0";
    internal static readonly string[] Cards = ["Experience.NewsDashboard", "Experience.NewsCollections", "Experience.NewsSetup"];
    internal const string OfficialUrl = "https://github.com/ProGraMajster/ModernTubeDownloader/releases";
}
