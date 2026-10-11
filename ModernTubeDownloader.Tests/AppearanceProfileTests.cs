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

[Collection("ModernFormsNext TestHost")]
public sealed class AppearanceProfileTests
{
    [Theory]
    [InlineData("Default")] [InlineData("Compact")] [InlineData("Large")] [InlineData("Minimal")]
    public void PresetsRoundTripWithClosedSchema(string name)
    {
        var profile = AppearanceProfile.Preset(name); var copy = profile.Copy();
        Assert.Equal(JsonSerializer.Serialize(profile), JsonSerializer.Serialize(copy));
        Assert.Equal(1, copy.SchemaVersion); Assert.NotSame(profile.Global, copy.Global);
    }
    [Theory]
    [InlineData("{\"global\":{\"theme\":100}}")]
    [InlineData("{\"global\":{\"colors\":{\"Accent\":\"file:///secret\"}}}")]
    [InlineData("{\"global\":{\"colors\":{\"Accent\":null}}}")]
    [InlineData("{\"global\":{\"colors\":{\"Script\":\"#FFFFFF\"}}}")]
    [InlineData("{\"global\":{\"spacingPercent\":1000}}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":2}")]
    [InlineData("{\"details\":{\"externalFile\":\"C:/private\"}}")]
    public void UnsafeProfilesAreRejected(string json) => Assert.ThrowsAny<Exception>(() => AppearanceProfileStore.Validate(json));
    [Fact]
    public void OldTypographyOnlyProfileMigratesThroughSafeDefaultsAndSectionResetIsIsolated()
    {
        var value = AppearanceProfileStore.Validate("{\"schemaVersion\":1,\"typography\":{\"appTextScale\":150}}");
        value.Downloads.ShowAuthor = false; value.Global.Colors["Accent"] = "#FFFFFF";
        value.ResetSection("Downloads"); Assert.True(value.Downloads.ShowAuthor); Assert.Equal(150, value.Typography.AppTextScale); Assert.Single(value.Global.Colors);
        Assert.Throws<ArgumentException>(() => value.ResetSection("SchemaVersion"));
        Assert.Equal(100, AppearanceProfile.Preset("Default").Typography.AppTextScale);
    }
    [Fact]
    public async Task ProfilePersistsAllSectionsAndThemeIsRestoredWithoutChangingDownloadSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-appearance-" + Guid.NewGuid().ToString("N")); var paths = AppPaths.Create(root); paths.EnsureCreated();
        try
        {
            var settings = new SettingsService(paths, new NullAppLogger()); Task saved;
            var originalOutput = settings.Current.FinalOutputDirectory;
            using (var host = ModernFormsTestHost.Create())
            using (var service = new AppAppearanceService(settings, new NullAppLogger(), Path.Combine(root, "appearance.json")))
            {
                service.Initialize(); using var card = new Panel(); AppUi.Card(card);
                var profile = AppearanceProfile.Preset("Minimal"); profile.Global.Theme = AppearanceTheme.Dark; profile.Global.Colors["Accent"] = "#123456";
                profile.Typography.AppTextScale = 125; profile.BatchImport.ShowUrl = false;
                saved = service.ApplyProfileAsync(profile); Assert.Equal(4, card.Style.Border.Radius); Assert.Equal(125, AppTypography.TextScale);
                Assert.Equal(originalOutput, settings.Current.FinalOutputDirectory);
                AppTypography.SetScale(100);
            }
            // No UI controls / test dispatcher survive the asynchronous disk-write continuation.
            await saved;
            using var restored = new AppAppearanceService(settings, new NullAppLogger(), Path.Combine(root, "appearance.json"));
            Assert.Equal(AppearanceTheme.Dark, restored.Profile.Global.Theme); Assert.Equal(AppThemeMode.Dark, restored.Mode); Assert.False(restored.Profile.BatchImport.ShowUrl);
        }
        finally { AppearancePresentation.Current = new(); Directory.Delete(root, true); }
    }
    [Fact]
    public void QueueAppearanceActuallyControlsThumbnailProgressAndMetadataWithoutChangingItem()
    {
        using var host = ModernFormsTestHost.Create(); var root = Path.Combine(Path.GetTempPath(), "mtd-card-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root); paths.EnsureCreated(); using var cache = new ThumbnailCacheService(paths, new NullAppLogger(), () => false);
            AppearancePresentation.Current = new() { Downloads = new() { ThumbnailSize = AppearanceSize.None, Progress = ProgressPresentation.Hidden, ShowSpeed = false, ShowEta = false, ShowAuthor = false, ShowSource = false } };
            using var card = new QueueItemCard(cache, new LocalizationService("en")) { Width = 1000 };
            var item = new DownloadQueueItem { Title = "Unicode Ł 日本語", Channel = "Hidden author", SourceUrl = "https://example.com/video", SpeedBytesPerSecond = 1000, Status = DownloadStatus.Queued };
            card.UpdateItem(item, false); Assert.False(card.Controls.OfType<ProgressBar>().Single().Visible); Assert.False(card.Controls.OfType<PictureBox>().First().Visible);
            Assert.DoesNotContain(card.Controls.OfType<Label>(), c => c.Text.Contains("Hidden author")); Assert.Equal("Hidden author", item.Channel);
        }
        finally { AppearancePresentation.Current = new(); AppTypography.SetScale(100); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void ContrastWarningCalculationIsDeterministic()
    { Assert.Equal(1, AppAppearanceService.ColorContrast(System.Drawing.Color.White, System.Drawing.Color.White)); Assert.InRange(AppAppearanceService.ColorContrast(System.Drawing.Color.White, System.Drawing.Color.Black), 20.9, 21.1); }
    [Fact]
    public void CompactHistoryReducesDensityWithoutSwitchingToTheNarrowWindowLayout()
    {
        using var host = ModernFormsTestHost.Create();
        var root = Path.Combine(Path.GetTempPath(), "mtd-history-appearance-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root); paths.EnsureCreated(); using var cache = new ThumbnailCacheService(paths, new NullAppLogger(), () => false);
            AppearancePresentation.Current = new(); using var card = new HistoryEntryCard(new(Guid.NewGuid(), "Sample", "https://example.test/video", "video", "Channel", "360", Path.Combine(root, "absent.mp4"), DateTimeOffset.UtcNow, "Completed"), new("en"), cache) { Width = 1000 };
            var normalHeight = card.Height; AppearancePresentation.Current.History.CompactList = true; AppTypography.SetScale(100);
            Assert.True(card.Height < normalHeight);
            var buttons = card.Controls.OfType<Button>().ToArray(); Assert.Equal(2, buttons.Length); Assert.True(buttons.All(button => button.Left > 500));
        }
        finally { AppearancePresentation.Current = new(); AppTypography.SetScale(100); Directory.Delete(root, true); }
    }
    [Fact]
    public void ConstructingServicesDoesNotRelayoutAnExistingUi()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-appearance-owner-" + Guid.NewGuid().ToString("N")); var paths = AppPaths.Create(root); paths.EnsureCreated(); var calls = 0;
        try { using var lease = AppTypography.RegisterLayout(() => calls++); using var service = new AppAppearanceService(new SettingsService(paths, new NullAppLogger()), new NullAppLogger()); Assert.Equal(0, calls); }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task ThemeAndTextChangesPublishOnTheCallingUiThreadBeforePersistence(bool theme)
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-profile-dispatch-" + Guid.NewGuid().ToString("N")); var paths = AppPaths.Create(root); paths.EnsureCreated();
        Task write;
        try
        {
            using (var host = ModernFormsTestHost.Create())
            using (var service = new AppAppearanceService(new(paths, new NullAppLogger()), new NullAppLogger(), Path.Combine(root, "appearance.json")))
            {
                service.Initialize(); var owner = Environment.CurrentManagedThreadId; var calls = 0;
                service.ModeChanged += (_, _) => { Assert.Equal(owner, Environment.CurrentManagedThreadId); calls++; };
                write = theme ? service.SetModeAsync(AppThemeMode.Light) : service.SetTextScaleAsync(150);
                Assert.Equal(1, calls); AppTypography.SetScale(100);
            }
            await write;
        }
        finally { AppearancePresentation.Current = new(); Directory.Delete(root, true); }
    }
}
