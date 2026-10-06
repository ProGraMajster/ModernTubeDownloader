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
    public DownloadQueueService Queue { get; }
    public QueueProcessor Processor { get; }
    public HistoryService History { get; }
    public ThumbnailCacheService Thumbnails { get; }
    public LiveSessionService Live { get; private set; } = null!;
    public LiveRecordingScheduler LiveScheduler { get; private set; } = null!;
    public WebRemoteHost WebRemote { get; private set; } = null!;
    public SupportedSourcesService Sources { get; private set; } = null!;
    public SourceCheckService SourceCheck { get; private set; } = null!;

    public static AppServices Create(
        AppPaths paths,
        HttpClient? httpClient = null,
        IToolReleaseSource? releaseSource = null,
        IToolBinaryValidator? toolValidator = null)
    {
        paths.EnsureCreated();
        var logger = new FileAppLogger(paths.LogDirectory);
        var settings = new SettingsService(paths, logger);
        settings.LoadAsync().GetAwaiter().GetResult();
        var localization = new LocalizationService(settings.Current.Language);
        var appearance = new AppAppearanceService(settings, logger);
        var processRunner = new AsyncProcessRunner(logger);
        httpClient ??= new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ModernTubeDownloader/1.0 (+https://github.com/ProGraMajster/ModernTubeDownloader)");
        releaseSource ??= new GitHubToolReleaseSource(httpClient);
        toolValidator ??= new ProcessToolBinaryValidator(processRunner);
        var packageInstaller = new ToolPackageInstaller(paths, new ToolDownloadService(httpClient), toolValidator);
        var tools = new ToolManager(paths, settings, releaseSource, packageInstaller, toolValidator, logger);
        var ytDlpRunner = new YtDlpProcessRunner(processRunner, tools, logger);
        var metadata = new YtDlpMetadataService(ytDlpRunner, settings, logger);
        var history = new HistoryService(paths, logger);
        history.LoadAsync().GetAwaiter().GetResult();
        var queueStore = new QueuePersistenceService(paths, logger);
        var liveStore = new LiveSessionPersistenceService(paths, logger);
        liveStore.MigrateLegacyQueueAsync(settings).GetAwaiter().GetResult();
        var live = new LiveSessionService(liveStore, settings, logger);
        live.LoadAsync().GetAwaiter().GetResult();
        var queue = new DownloadQueueService(queueStore, logger, settings);
        queue.LoadAsync().GetAwaiter().GetResult();
        var ffmpeg = new FfmpegService(processRunner, tools, logger);
        var downloader = new DownloadJobExecutor(paths, settings, ytDlpRunner, metadata, ffmpeg, history, logger);
        var processor = new QueueProcessor(queue, downloader, settings, logger);
        var liveScheduler = new LiveRecordingScheduler(live,
            new LiveRecordingExecutor(settings, ytDlpRunner, metadata, ffmpeg, history, logger),
            new YtDlpLiveAvailabilityProbe(metadata), settings, logger);
        var thumbnails = new ThumbnailCacheService(paths, logger, () => settings.Current.DownloadThumbnail);
        processor.Start();
        liveScheduler.Start();
        tools.Start();
        var services = new AppServices(paths, logger, settings, localization, appearance, tools, metadata, queue, processor, history, thumbnails, httpClient);
        services.Live = live;
        services.LiveScheduler = liveScheduler;
        services.Sources = new SupportedSourcesService(tools, processRunner, logger);
        services.SourceCheck = new SourceCheckService(metadata, logger);
        services.WebRemote = new WebRemoteHost(services);
        services.WebRemote.ReconcileAsync().GetAwaiter().GetResult();
        return services;
    }

    public async Task ShutdownAsync()
    {
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
