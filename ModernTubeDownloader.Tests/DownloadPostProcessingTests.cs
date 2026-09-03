using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Tests;

public sealed class DownloadPostProcessingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.PostProcessingTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void NewSettings_EnablePublishDateAndMetadataSidecarByDefault()
    {
        var settings = new AppSettings();

        Assert.True(settings.SetFileCreationTimeFromMediaPublishDate);
        Assert.True(settings.SaveMetadataJsonSidecar);
    }

    [Fact]
    public async Task SettingsPersistBothPostProcessingOptions()
    {
        var paths = AppPaths.Create(Path.Combine(root, "settings"));
        paths.EnsureCreated();
        var service = new SettingsService(paths, new NullAppLogger());
        await service.LoadAsync();
        service.Current.SetFileCreationTimeFromMediaPublishDate = false;
        service.Current.SaveMetadataJsonSidecar = false;
        await service.SaveAsync();

        var reloaded = new SettingsService(paths, new NullAppLogger());
        await reloaded.LoadAsync();

        Assert.False(reloaded.Current.SetFileCreationTimeFromMediaPublishDate);
        Assert.False(reloaded.Current.SaveMetadataJsonSidecar);
    }

    [Fact]
    public async Task ExactTimestampUpdatesCreationTimeAsUtcInstant()
    {
        var finalPath = CreateVideo("timestamp.mp4");
        const long timestamp = 1_710_675_896;
        var settings = Settings(timestampEnabled: true, sidecarEnabled: false);

        await DownloadPostProcessor.ProcessAsync(
            finalPath,
            Item($$"""{"timestamp":{{timestamp}}}"""),
            settings,
            new NullAppLogger());

        var expected = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
        Assert.InRange((File.GetCreationTimeUtc(finalPath) - expected).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DisabledTimestampOptionLeavesCreationTimeUnchanged()
    {
        var finalPath = CreateVideo("disabled-date.mp4");
        var original = File.GetCreationTimeUtc(finalPath);

        await DownloadPostProcessor.ProcessAsync(
            finalPath,
            Item("{\"timestamp\":1710675896}"),
            Settings(timestampEnabled: false, sidecarEnabled: false),
            new NullAppLogger());

        Assert.Equal(original, File.GetCreationTimeUtc(finalPath));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"timestamp\":\"invalid\",\"upload_date\":\"20241399\"}")]
    [InlineData("not-json")]
    public async Task MissingOrInvalidPublishDateDoesNotFailOrChangeTheFile(string rawJson)
    {
        var finalPath = CreateVideo("invalid-date.mp4");
        var original = File.GetCreationTimeUtc(finalPath);

        var result = await DownloadPostProcessor.ProcessAsync(
            finalPath,
            Item(rawJson),
            Settings(timestampEnabled: true, sidecarEnabled: false),
            new NullAppLogger());

        Assert.Empty(result.Warnings);
        Assert.Equal(original, File.GetCreationTimeUtc(finalPath));
        Assert.True(File.Exists(finalPath));
    }

    [Fact]
    public void ResolverPrefersUploadTimestampForOrdinaryMediaAndReleaseTimestampForLiveMedia()
    {
        Assert.True(MediaPublishTimeResolver.TryResolve(
            "{\"timestamp\":1710000000,\"release_timestamp\":1720000000,\"live_status\":\"not_live\"}",
            out var ordinary));
        Assert.Equal("timestamp", ordinary.SourceField);

        Assert.True(MediaPublishTimeResolver.TryResolve(
            "{\"timestamp\":1710000000,\"release_timestamp\":1720000000,\"was_live\":true}",
            out var live));
        Assert.Equal("release_timestamp", live.SourceField);
    }

    [Theory]
    [InlineData("movie.mp4", "movie.json")]
    [InlineData("movie (1).mp4", "movie (1).json")]
    public async Task SidecarUsesTheFinalVideoBasenameAndPreservesRawMetadata(string videoName, string expectedJsonName)
    {
        var finalPath = CreateVideo(videoName);
        const string rawJson = "{\"id\":\"sample\",\"unknown_future_field\":{\"preserved\":true}}";

        var result = await DownloadPostProcessor.ProcessAsync(
            finalPath,
            Item(rawJson),
            Settings(timestampEnabled: false, sidecarEnabled: true),
            new NullAppLogger());

        var expectedPath = Path.Combine(root, expectedJsonName);
        Assert.Equal(expectedPath, result.MetadataSidecarPath);
        Assert.Equal(rawJson, await File.ReadAllTextAsync(expectedPath));
    }

    [Fact]
    public async Task DisabledSidecarOptionDoesNotCreateJson()
    {
        var finalPath = CreateVideo("no-sidecar.mp4");

        var result = await DownloadPostProcessor.ProcessAsync(
            finalPath,
            Item("{\"id\":\"sample\"}"),
            Settings(timestampEnabled: false, sidecarEnabled: false),
            new NullAppLogger());

        Assert.Null(result.MetadataSidecarPath);
        Assert.False(File.Exists(Path.ChangeExtension(finalPath, ".json")));
    }

    [Fact]
    public async Task SidecarWriteFailureLeavesCompletedVideoIntactAndReturnsWarning()
    {
        var finalPath = CreateVideo("write-failure.mp4");
        Directory.CreateDirectory(Path.ChangeExtension(finalPath, ".json"));

        var result = await DownloadPostProcessor.ProcessAsync(
            finalPath,
            Item("{\"id\":\"sample\"}"),
            Settings(timestampEnabled: false, sidecarEnabled: true),
            new NullAppLogger());

        Assert.Null(result.MetadataSidecarPath);
        Assert.Contains("metadata sidecar could not be saved", result.Warnings);
        Assert.True(File.Exists(finalPath));
        Assert.NotEqual(0, new FileInfo(finalPath).Length);
    }

    private string CreateVideo(string name)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, name);
        File.WriteAllText(path, "video");
        return path;
    }

    private static DownloadQueueItem Item(string rawJson)
        => new() { Id = Guid.NewGuid(), RawMetadataJson = rawJson };

    private static AppSettings Settings(bool timestampEnabled, bool sidecarEnabled)
        => new()
        {
            SetFileCreationTimeFromMediaPublishDate = timestampEnabled,
            SaveMetadataJsonSidecar = sidecarEnabled
        };

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
