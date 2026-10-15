using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.WebRemote;

namespace ModernTubeDownloader.Services;

public sealed class AppServices : IDisposable
{
    private bool disposed;
    private readonly HttpClient httpClient;

    private AppServices(
        AppPaths paths,
        FileAppLogger logger,
        SettingsService settings,
        LocalizationService localization,
        AppAppearanceService appearance,
        ToolManager tools,
        YtDlpMetadataService metadata,
        DownloadQueueService queue,
        QueueProcessor processor,
        HistoryService history,
        ThumbnailCacheService thumbnails,
        HttpClient httpClient)
    {
        Paths = paths;
        Logger = logger;
        Settings = settings;
        Localization = localization;
        Appearance = appearance;
        Tools = tools;
        Metadata = metadata;
        Queue = queue;
        Processor = processor;
        History = history;
        Thumbnails = thumbnails;
        this.httpClient = httpClient;
    }

    public AppPaths Paths { get; }
    public IAppLogger Logger { get; }
    public SettingsService Settings { get; }
    public LocalizationService Localization { get; }
    public AppAppearanceService Appearance { get; }
    public ToolManager Tools { get; }
    public YtDlpMetadataService Metadata { get; }
    public YtDlpAuthenticationProvider Authentication { get; private set; } = null!;
    public DownloadQueueService Queue { get; }
    public QueueProcessor Processor { get; }
    public HistoryService History { get; }
    public ThumbnailCacheService Thumbnails { get; }
    public SystemNotificationCoordinator? Notifications { get; private set; }
    public LiveSessionService Live { get; private set; } = null!;
    public LiveRecordingScheduler LiveScheduler { get; private set; } = null!;
    public WebRemoteHost WebRemote { get; private set; } = null!;
    public SupportedSourcesService Sources { get; private set; } = null!;
    public SourceCheckService SourceCheck { get; private set; } = null!;
    public ApplicationUpdateService Updates { get; private set; } = null!;
    public ModernTubeDownloader.Batch.BatchImportService Batch { get; private set; } = null!;

    public static AppServices Create(
        AppPaths paths,
        HttpClient? httpClient = null,
        IToolReleaseSource? releaseSource = null,
        IToolBinaryValidator? toolValidator = null,
        ModernFormsNext.Notifications.ISystemNotificationService? notifications = null)
    {
        paths.EnsureCreated();
        var logger = new FileAppLogger(paths.LogDirectory);
        var settings = new SettingsService(paths, logger);
        settings.LoadAsync().GetAwaiter().GetResult();
        var localization = new LocalizationService(settings.Current.Language);
        var appearance = new AppAppearanceService(settings, logger, Path.Combine(paths.RootDirectory, "appearance.json"));
        var processRunner = new AsyncProcessRunner(logger);
        httpClient ??= new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ModernTubeDownloader/1.0 (+https://github.com/ProGraMajster/ModernTubeDownloader)");
        releaseSource ??= new GitHubToolReleaseSource(httpClient);
        toolValidator ??= new ProcessToolBinaryValidator(processRunner);
        var packageInstaller = new ToolPackageInstaller(paths, new ToolDownloadService(httpClient), toolValidator);
        var tools = new ToolManager(paths, settings, releaseSource, packageInstaller, toolValidator, logger);
        ModernTubeDownloader.Authentication.CookieJarLease.CleanupAbandoned(Path.Combine(paths.AuthenticationDirectory, "tmp"));
        var authentication = new YtDlpAuthenticationProvider(paths, settings);
        var ytDlpRunner = new YtDlpProcessRunner(processRunner, tools, logger, authentication);
        var metadata = new YtDlpMetadataService(ytDlpRunner, settings, logger);
        var history = new HistoryService(paths, logger);
        history.LoadAsync().GetAwaiter().GetResult();
        var queueStore = new QueuePersistenceService(paths, logger);
        var liveStore = new LiveSessionPersistenceService(paths, logger);
        liveStore.MigrateLegacyQueueAsync(settings).GetAwaiter().GetResult();
        var live = new LiveSessionService(liveStore, settings, logger);
        live.LoadAsync().GetAwaiter().GetResult();
        var storageSafety = new QueueStorageSafety(directories: () =>
            [paths.RootDirectory, paths.AuthenticationDirectory, settings.Current.TemporaryDirectory, settings.Current.FinalOutputDirectory]);
        var queue = new DownloadQueueService(queueStore, logger, settings, storageSafety);
        queue.LoadAsync().GetAwaiter().GetResult();
        var ffmpeg = new FfmpegService(processRunner, tools, logger);
        var downloader = new DownloadJobExecutor(paths, settings, ytDlpRunner, metadata, ffmpeg, history, logger, storageSafety);
        var processor = new QueueProcessor(queue, downloader, settings, logger);
        var liveScheduler = new LiveRecordingScheduler(live,
            new LiveRecordingExecutor(settings, ytDlpRunner, metadata, ffmpeg, history, logger, storageSafety),
            new YtDlpLiveAvailabilityProbe(metadata), settings, logger);
        var thumbnails = new ThumbnailCacheService(paths, logger, () => settings.Current.DownloadThumbnail);
        var services = new AppServices(paths, logger, settings, localization, appearance, tools, metadata, queue, processor, history, thumbnails, httpClient);
        services.Live = live;
        services.Updates = new ApplicationUpdateService(paths, settings, httpClient, logger);
        services.Authentication = authentication;
        services.LiveScheduler = liveScheduler;
        if (notifications is not null)
        {
            services.Notifications = new(notifications, () => settings.Current.Notifications, localization, logger,
                thumbnails.GetCachedNotificationPath);
            services.Notifications.Attach(queue, live);
        }
        processor.Start();
        liveScheduler.Start();
        tools.Start();
        services.Sources = new SupportedSourcesService(tools, processRunner, logger);
        services.SourceCheck = new SourceCheckService(metadata, logger);
        services.Batch = new(new ModernTubeDownloader.Batch.BatchSessionStore(Path.Combine(paths.RootDirectory, "Batch")),
            services.SourceCheck.CheckAsync, queue, settings, logger);
        services.Updates.UpdatePrepared += (_, version) => services.Notifications?.UpdateReady(version);
        services.Batch.AnalysisCompleted += (_, counts) => services.Notifications?.BatchCompleted(counts.Analyzed, counts.Total);
        services.WebRemote = new WebRemoteHost(services);
        services.WebRemote.ReconcileAsync().GetAwaiter().GetResult();
        return services;
    }

    public async Task ShutdownAsync()
    {
        await Updates.StopAsync().ConfigureAwait(false);
        await Batch.StopAsync().ConfigureAwait(false);
        if (Notifications is not null) await Notifications.DisposeAsync().ConfigureAwait(false);
        await WebRemote.StopAsync().ConfigureAwait(false);
        await Task.WhenAll(Processor.StopAsync(), LiveScheduler.StopAsync()).ConfigureAwait(false);
        await Tools.StopAsync().ConfigureAwait(false);
        await Queue.SaveAsync().ConfigureAwait(false);
        await Settings.SaveAsync().ConfigureAwait(false);
        await History.SaveAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        WebRemote.Dispose();
        Updates.Dispose();
        Batch.Dispose();
        Thumbnails.Dispose();
        Processor.Dispose();
        LiveScheduler.Dispose();
        Sources.Dispose();
        Appearance.Dispose();
        httpClient.Dispose();
        if (Logger is IDisposable disposableLogger)
            disposableLogger.Dispose();
    }
}
