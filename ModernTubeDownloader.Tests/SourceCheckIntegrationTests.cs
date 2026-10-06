using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class SourceCheckIntegrationTests
{
    [Theory]
    [InlineData("watch/sample", SourceMediaKind.Video, false, false)]
    [InlineData("playlist/sample", SourceMediaKind.Playlist, false, false)]
    [InlineData("live/active", SourceMediaKind.ActiveLive, true, false)]
    [InlineData("live/upcoming", SourceMediaKind.Upcoming, true, false)]
    [InlineData("live/replay", SourceMediaKind.Replay, false, false)]
    [InlineData("source/generic", SourceMediaKind.Video, false, false)]
    [InlineData("source/private", SourceMediaKind.Video, false, true)]
    public async Task MetadataCheckNeverQueuesOrStartsLive(string route, SourceMediaKind expected, bool live, bool auth)
    {
        var paths = await PrepareAsync();
        using var app = AppServices.Create(paths);
        try
        {
            var result = await app.SourceCheck.CheckAsync("https://example.test/" + route);
            Assert.NotNull(result.Metadata); Assert.Equal(expected, result.Facts!.Kind);
            Assert.Equal(live, result.OpenLive); Assert.Equal(auth, result.AuthenticationMayBeRequired);
            if (route.Contains("generic")) Assert.True(result.Facts.Identity.IsGeneric);
            if (expected == SourceMediaKind.Playlist) Assert.NotNull(result.Playlist);
            Assert.Empty(app.Queue.Snapshot()); Assert.Empty(app.Live.Snapshot());
        }
        finally { await app.ShutdownAsync(); app.Dispose(); Directory.Delete(paths.RootDirectory, true); }
    }
    [Theory]
    [InlineData("https://example.test/source/unsupported", "Sources.Unsupported", false)]
    [InlineData("https://example.test/source/auth", "Error.Download.Authentication", true)]
    [InlineData("plain text", "Downloads.InvalidUrl", false)]
    [InlineData("file:///C:/private", "Downloads.InvalidUrl", false)]
    public async Task CheckFailureReturnsLocalizedCategoryWithoutStackOrSideEffects(string url, string key, bool auth)
    {
        var paths = await PrepareAsync(); using var app = AppServices.Create(paths);
        try
        {
            var result = await app.SourceCheck.CheckAsync(url);
            Assert.Null(result.Metadata); Assert.Equal(key, result.MessageKey); Assert.Equal(auth, result.AuthenticationMayBeRequired);
            Assert.Empty(app.Queue.Snapshot()); Assert.Empty(app.Live.Snapshot());
        }
        finally { await app.ShutdownAsync(); app.Dispose(); Directory.Delete(paths.RootDirectory, true); }
    }
    [Fact]
    public async Task RealProcessCatalogUsesActiveCustomExecutable_AndRefreshesWithoutRestart()
    {
        var paths = await PrepareAsync(); using var app = AppServices.Create(paths);
        try
        {
            var first = await app.Sources.GetAsync();
            Assert.False(first.Managed); Assert.Equal("2026.08.30-test", first.Version); Assert.Equal(9, first.Sources.Count);
            Assert.Same(first, await app.Sources.GetAsync());
            var alternate = Path.Combine(paths.RootDirectory, "alternate"); Directory.CreateDirectory(alternate);
            foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool*"))
                File.Copy(source, Path.Combine(alternate, Path.GetFileName(source)));
            app.Settings.Current.CustomYtDlpPath = Path.Combine(alternate, "ModernTubeDownloader.FakeTool.exe");
            await app.Tools.ReconfigureAsync();
            var second = await app.Sources.GetAsync(); Assert.NotEqual(first.Fingerprint, second.Fingerprint);
            Assert.Equal(9, second.Sources.Count); Assert.Empty(app.Queue.Snapshot()); Assert.Empty(app.Live.Snapshot());
        }
        finally { await app.ShutdownAsync(); app.Dispose(); Directory.Delete(paths.RootDirectory, true); }
    }
    private static async Task<AppPaths> PrepareAsync()
    {
        var paths = AppPaths.Create(Path.Combine(Path.GetTempPath(), "mtd-source-check", Guid.NewGuid().ToString("N")));
        paths.EnsureCreated(); var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        settings.Current.UseCustomYtDlp = settings.Current.UseCustomFfmpeg = settings.Current.UseCustomDeno = true;
        settings.Current.CustomYtDlpPath = settings.Current.CustomFfmpegPath = settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
        var directory = Path.Combine(paths.RootDirectory, "deno"); Directory.CreateDirectory(directory);
        foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool*"))
            File.Copy(source, Path.Combine(directory, Path.GetFileName(source)));
        var deno = Path.Combine(directory, "deno.exe"); File.Copy(CoreTests.FakeToolPath(), deno);
        settings.Current.CustomDenoPath = deno; settings.Current.DownloadThumbnail = false;
        settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        await settings.SaveAsync(); return paths;
    }
}
