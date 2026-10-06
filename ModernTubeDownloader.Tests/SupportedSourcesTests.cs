using System.Diagnostics;
using System.Text.Json;
using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed class SupportedSourcesTests
{
    [Fact]
    public void CatalogUsesExactRuntimeNames_LongestDescriptionPrefix_BrokenDuplicatesUnicode()
    {
        var list = SupportedSourcesService.Parse(["twitch:stream", "twitch:vod", "twitch:stream", "世界:動画", "bad (CURRENTLY BROKEN)", "generic"],
            ["twitch:stream: Live description", "twitch:vod: VOD", "世界:動画: Żółć 日本語", "generic: Some embeds"]);
        Assert.Equal(5, list.Count);
        Assert.Equal("Live description", list.Single(item => item.ExtractorKey == "twitch:stream").Description);
        Assert.Equal("Żółć 日本語", list.Single(item => item.ExtractorKey == "世界:動画").Description);
        Assert.True(list.Single(item => item.ExtractorKey == "bad").IsBroken);
        Assert.Equal(SourceVerificationStatus.Limited, list.Single(item => item.IsGeneric).VerificationStatus);
        Assert.Equal(2, list.Count(item => item.Family == "Twitch"));
        Assert.Equal(SourceVerificationStatus.YtDlpSupported, list.Single(item => item.ExtractorKey == "twitch:vod").VerificationStatus);
    }
    [Theory]
    [InlineData("YoutubeTab", "YouTube", "youtube:tab", false)]
    [InlineData("TwitchStream", "Twitch", "twitch:stream", false)]
    [InlineData("Generic", "Generic", "Generic", true)]
    [InlineData("新規サービス", "新規サービス", "新規サービス", false)]
    public void IdentityDoesNotGuessCapabilities(string key, string family, string runtime, bool generic)
    {
        var identity = ExtractorIdentityService.Identify(null, key);
        Assert.Equal(family, identity.DisplayName, ignoreCase: true);
        Assert.Equal(runtime, identity.RuntimeName, ignoreCase: true);
        Assert.Equal(key, identity.ExtractorKey);
        Assert.Equal(generic, identity.IsGeneric);
    }
    [Fact]
    public void SearchUsesDisplayNameKeyDescriptionAndExactMatchesBeforePopularity()
    {
        var list = SupportedSourcesService.Parse(["twitch:stream", "twitch:vod", "Other", "youtube"], ["Other: twitch description"]);
        Assert.Equal(3, SupportedSourcesService.Search(list, "TwItCh").Count);
        Assert.Equal("Other", SupportedSourcesService.Search(list, "Other")[0].ExtractorKey);
        Assert.Equal("youtube", SupportedSourcesService.Search(list, "YouTube")[0].ExtractorKey);
        Assert.Empty(SupportedSourcesService.Search(list, "not present"));
    }
    [Fact]
    public void RegistryIsPerFeatureAndDoesNotVerifySiblingExtractors()
    {
        Assert.Contains(VerifiedSourcesRegistry.Get("twitch:stream"), item => item.Capability == SourceCapability.LiveCapture);
        Assert.Empty(VerifiedSourcesRegistry.Get("twitch:vod"));
        Assert.Empty(VerifiedSourcesRegistry.Get("vimeo"));
        Assert.DoesNotContain(VerifiedSourcesRegistry.Get("Youtube"), item => item.Capability == SourceCapability.LiveCapture);
        Assert.All(VerifiedSourcesRegistry.Get("youtube"), item => Assert.False(string.IsNullOrWhiteSpace(item.Evidence)));
        foreach (var language in new[] { "pl", "en" })
            Assert.DoesNotContain("[Sources.", SourcePresentation.VerificationText("Youtube", new LocalizationService(language)));
    }
    [Fact]
    public void ActualVimeoLoggedInErrorIsAuthentication_NotUnknown()
    {
        var failure = DownloadFailureClassifier.Classify(new InvalidOperationException(
            "ERROR: [vimeo] 1084537: The web client only works when logged-in. Use --cookies to provide account credentials."));
        Assert.Equal(DownloadFailureCategory.Authentication, failure.Category);
        Assert.Equal("Error.Download.Authentication", failure.UserMessageKey);
    }
    [Fact]
    public void DesktopSourceValidationDoesNotBroadenExternalProtocolAllowlist()
    {
        foreach (var url in new[] { "https://www.twitch.tv/videos/123", "https://vimeo.com/1084537", "https://archive.org/details/sample" })
        {
            Assert.True(YtDlpMetadataService.TryValidateSourceUrl(url, out _, out _));
            Assert.False(ExternalMediaRequest.TryValidateSource(url, out _));
        }
        Assert.False(YtDlpMetadataService.TryValidateSourceUrl("file:///C:/private", out _, out _));
    }
    [Fact]
    public void ActualMissingCodecFactsAreExplained_WithoutGuessingStreams()
    {
        var metadata = new VideoMetadata { Extractor = "archive.org", ExtractorKey = "ArchiveOrg",
            Formats = [new MediaFormat { Id = "0", Extension = "mp4", Height = 240 }] };
        Assert.False(metadata.Formats[0].HasVideo); Assert.False(metadata.Formats[0].HasAudio);
        foreach (var language in new[] { "pl", "en" })
        {
            var text = new LocalizationService(language);
            Assert.Contains(text["Sources.CodecsUnknown"], SourcePresentation.Details(metadata, text));
        }
        Assert.Empty(VerifiedSourcesRegistry.Get(metadata.Extractor));
    }
    [Fact]
    public async Task CacheInvalidatesByEventAndFingerprint_NoOldDataAfterFailedUpdate()
    {
        var runtime = new FakeCatalogRuntime();
        using var service = new SupportedSourcesService(runtime);
        var first = await service.GetAsync();
        Assert.Same(first, await service.GetAsync());
        Assert.Equal(2, runtime.Calls);
        runtime.Fingerprint = "same-version/new-hash";
        var updated = await service.GetAsync();
        Assert.NotSame(first, updated);
        Assert.Equal(4, runtime.Calls);
        runtime.Version = "new-version"; runtime.Fingerprint = "v2"; runtime.Notify();
        Assert.Equal("new-version", (await service.GetAsync()).Version);
        runtime.Fingerprint = "failed"; runtime.Fail = true; runtime.Notify();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetAsync());
        runtime.Fail = false;
        Assert.Equal("failed", (await service.GetAsync()).Fingerprint);
    }
    [Fact]
    public async Task ConcurrentCacheRequests_RunOnlyOneCatalogPair()
    {
        var runtime = new FakeCatalogRuntime(); using var service = new SupportedSourcesService(runtime);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.GetAsync()));
        Assert.Equal(2, runtime.Calls);
    }
    [Fact]
    public async Task UpdateActivatedOnLeaseReleaseReturnsOnlyTheNewCatalog()
    {
        var runtime = new FakeCatalogRuntime { UpdateOnRelease = true };
        using var service = new SupportedSourcesService(runtime);
        var result = await service.GetAsync();
        Assert.Equal("v2", result.Version);
        Assert.Equal("v2/hash", result.Fingerprint);
        Assert.Equal(4, runtime.Calls);
        Assert.Same(result, await service.GetAsync());
    }
    [Fact]
    public async Task MissingExecutableFailsWithoutInventedCatalog()
    {
        using var service = new SupportedSourcesService(new FakeCatalogRuntime { Missing = true });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync());
    }
    [Fact]
    public async Task CatalogCommandDeadlineIsAReportedFailure_NotEndlessLoadingOrStaleCache()
    {
        var runtime = new FakeCatalogRuntime { Timeout = true };
        using var service = new SupportedSourcesService(runtime);
        await Assert.ThrowsAsync<TimeoutException>(() => service.GetAsync());
        runtime.Timeout = false;
        Assert.Equal(5, (await service.GetAsync()).Sources.Count);
    }
    [Fact]
    public void LongListAndSearchRemainDataOnly_UnknownServicesAreNotInventedVerified()
    {
        var timer = Stopwatch.StartNew();
        var list = SupportedSourcesService.Parse(Enumerable.Range(0, 10000).Select(i => $"Site{i:D5}:video"), []);
        var result = SupportedSourcesService.Search(list, "Site09999");
        Assert.Single(result);
        Assert.All(list, item => Assert.Equal(SourceVerificationStatus.YtDlpSupported, item.VerificationStatus));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), $"Parsing/search took {timer.Elapsed}");
    }
    [Theory]
    [InlineData("not_live", SourceMediaKind.Video)]
    [InlineData("is_live", SourceMediaKind.ActiveLive)]
    [InlineData("is_upcoming", SourceMediaKind.Upcoming)]
    [InlineData("was_live", SourceMediaKind.Replay)]
    [InlineData("post_live", SourceMediaKind.Processing)]
    public void FactsUseMetadataOnly(string state, SourceMediaKind kind)
    {
        var facts = ExtractorIdentityService.Facts(new VideoMetadata { LiveStatus = state, ExtractorKey = "Unknown" });
        Assert.Equal(kind, facts.Kind); Assert.Equal(0, facts.Subtitles); Assert.Equal(0, facts.Formats);
        Assert.False(facts.PlaylistContext);
    }
    [Fact]
    public void SourceDetailsStripSecretsAndRemainNullSafeInBothLanguages()
    {
        var metadata = JsonSerializer.Deserialize<VideoMetadata>("""{"extractor_key":"Generic","original_url":"https://user:password@example.test/video?token=secret#fragment","formats":null,"subtitles":null,"automatic_captions":null,"chapters":null} """, JsonDefaults.Options)!;
        foreach (var language in new[] { "pl", "en" })
        {
            var body = SourcePresentation.Details(metadata, new LocalizationService(language));
            Assert.DoesNotContain("password", body); Assert.DoesNotContain("token=secret", body);
            Assert.DoesNotContain("#fragment", body); Assert.Contains("https://example.test/video?…", body);
            Assert.DoesNotContain("[Sources.", body);
        }
    }
    internal sealed class FakeCatalogRuntime : ISupportedSourcesRuntime
    {
        public event EventHandler? Changed;
        public string Fingerprint = "v1/hash";
        public string Version = "v1";
        public bool Missing, Fail, UpdateOnRelease, Timeout;
        public int Calls;
        public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
        public Task<ExtractorCatalogLease> AcquireAsync(CancellationToken token)
        {
            if (Missing) throw new InvalidOperationException("missing");
            return Task.FromResult(new ExtractorCatalogLease(Fingerprint, Version, false, (option, _) =>
            {
                Calls++;
                if (Timeout) throw new OperationCanceledException();
                if (Fail) throw new InvalidDataException("failed process");
                return Task.FromResult<IReadOnlyList<string>>(option == "--list-extractors"
                    ? ["youtube", "twitch:stream", "twitch:vod", "vimeo", "generic"] : ["youtube: YouTube", "vimeo: Vimeo"]);
            }, () =>
            {
                if (UpdateOnRelease)
                {
                    UpdateOnRelease = false; Fingerprint = "v2/hash"; Version = "v2"; Notify();
                }
                return ValueTask.CompletedTask;
            }));
        }
    }
}

[Collection("ModernFormsNext TestHost")]
public sealed class SupportedSourcesUiTests
{
    [Theory]
    [InlineData("pl", AppThemeMode.Light)]
    [InlineData("en", AppThemeMode.Dark)]
    public async Task CatalogRendersOnlyOnePageWithSemanticIds(string language, AppThemeMode theme)
    {
        var paths = AppPaths.Create(Path.Combine(Path.GetTempPath(), "mtd-sources-ui", Guid.NewGuid().ToString("N")));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        settings.Current.ThemeMode = theme;
        using var appearance = new AppAppearanceService(settings, new NullAppLogger()); appearance.Initialize();
        using var catalog = new SupportedSourcesService(new SupportedSourcesTests.FakeCatalogRuntime());
        using var host = ModernFormsTestHost.Create();
        using var form = new SupportedSourcesForm(catalog, null!, new LocalizationService(language), loadOnShown: false);
        var sources = SupportedSourcesService.Parse(Enumerable.Range(0, 5000).Select(i => $"Platform{i:D4}"), []);
        form.PresentCatalog(new("test", false, "test", sources));
        using var window = host.Show(form, 980, 880);
        host.ProcessPendingWork();
        var list = Find(form, "SupportedSourcesList");
        Assert.Equal(12, list.Controls.Count);
        Assert.All(list.Controls.OfType<Panel>(), panel => Assert.Equal(panel.Width - 16, panel.Controls[0].Width));
        var input = Assert.IsType<TextBox>(Find(form, "SupportedSourcesSearch"));
        input.Text = "Platform4999"; host.ProcessPendingWork(); Assert.Single(list.Controls);
        Assert.False(Find(form, "SupportedSourcesOpenButton").Visible);
        Assert.Equal("SupportedSourcesWindow", form.AccessibilityObject.AutomationId);
    }
    private static Control Find(Form root, string id) => root.Controls.SelectMany(child => Flatten(child)).Single(child => child.AccessibleAutomationId == id);
    private static IEnumerable<Control> Flatten(Control root) => new[] { root }.Concat(root.Controls.SelectMany(Flatten));
}
