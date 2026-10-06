using System.Text.Json;
using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Utilities;
using ModernTubeDownloader.Views;
using SkiaSharp;

namespace ModernTubeDownloader.Tests;

public sealed class PlaylistTests
{
    private const string PlaylistUrl = "https://www.youtube.com/playlist?list=PLexample";

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PLexample", true)]
    [InlineData("https://soundcloud.com/example/sets/album", true)]
    [InlineData("https://www.youtube.com/watch?v=video123&list=PLexample", false)]
    [InlineData("https://www.youtube.com/shorts/video123", false)]
    [InlineData("https://www.youtube.com/watch?v=video123", false)]
    [InlineData("not a url", false)]
    public void PlaylistUrlClassification_KeepsVideoUrlsOnTheSingleVideoPath(string url, bool expected) =>
        Assert.Equal(expected, YtDlpMetadataService.IsPlaylistUrl(url));

    [Fact]
    public void PlaylistArguments_UseFlatMetadataAndKeepUrlAfterTerminator()
    {
        var arguments = YtDlpMetadataService.BuildPlaylistArguments(PlaylistUrl, "--retries 3");

        Assert.Contains("--flat-playlist", arguments);
        Assert.Contains("--yes-playlist", arguments);
        Assert.Contains("--ignore-errors", arguments);
        Assert.Contains("--dump-single-json", arguments);
        Assert.DoesNotContain("--no-playlist", arguments);
        Assert.Equal("--", arguments[^2]);
        Assert.Equal(PlaylistUrl, arguments[^1]);
        Assert.Throws<ArgumentException>(() => CommandLineArgumentTokenizer.ParseSafeYtDlpArguments("--no-playlist"));
        Assert.Throws<ArgumentException>(() => CommandLineArgumentTokenizer.ParseSafeYtDlpArguments("--flat-playlist"));
    }

    [Fact]
    public void FlatPlaylist_ParsesPartialEntriesAndYouTubeIdsWithoutDeepFormats()
    {
        const string json = """
            {"_type":"playlist","id":"PL123","title":"Samples","channel":"Creator","entries":[
              {"id":"video1","title":"One","url":"video1","ie_key":"Youtube","playlist_index":2},
              null,
              {"id":"video2","title":"[Private video]","url":"video2","ie_key":"Youtube","availability":"private"},
              {"id":"video3","title":"Three","webpage_url":"https://example.test/watch/video3","playlist_index":1},
              {"id":"video4","title":"No URL"}
            ]}
            """;

        var playlist = PlaylistMetadata.Parse(json, PlaylistUrl);

        Assert.Equal(5, playlist.EntryCount);
        Assert.Equal("https://www.youtube.com/watch?v=video1", playlist.Entries[0].SourceUrl);
        Assert.Equal(2, playlist.Entries[0].PlaylistIndex);
        Assert.False(playlist.Entries[1].IsAvailable);
        Assert.False(playlist.Entries[2].IsAvailable);
        Assert.True(playlist.Entries[3].IsAvailable);
        Assert.False(playlist.Entries[4].IsAvailable);
        Assert.Equal(PlaylistUrl, playlist.SourceUrl);
    }

    [Fact]
    public void EmptyPlaylist_HasNoSelectionAndNoQueueItems()
    {
        var playlist = PlaylistMetadata.Parse("""{"_type":"playlist","id":"empty","entries":[]}""", PlaylistUrl);
        var selected = new PlaylistSelection(playlist);

        Assert.Equal(0, playlist.EntryCount);
        Assert.Equal(0, selected.SelectedCount);
        Assert.Empty(selected.CreateQueueItems(QualityPreset.Find("best")));
    }

    [Fact]
    public void PlaylistSelection_SkipsUnavailableAndPreservesPlaylistOrderAndQuality()
    {
        var playlist = PlaylistMetadata.Parse("""
            {"_type":"playlist","id":"PL123","title":"Samples","entries":[
              {"id":"late","title":"Late","webpage_url":"https://example.test/watch/late","playlist_index":20},
              {"id":"private","title":"Private","webpage_url":"https://example.test/watch/private","availability":"private"},
              {"id":"early","title":"Early","webpage_url":"https://example.test/watch/early","playlist_index":1}
            ]}
            """, PlaylistUrl);
        var selection = new PlaylistSelection(playlist);

        Assert.Equal(2, selection.SelectedCount);
        selection.Clear();
        Assert.Equal(0, selection.SelectedCount);
        selection.Set(1, true);
        Assert.Equal(0, selection.SelectedCount);
        selection.SelectAll();
        var items = selection.CreateQueueItems(QualityPreset.Find("1080"));

        Assert.Equal(["early", "late"], items.Select(item => item.VideoId));
        Assert.All(items, item =>
        {
            Assert.Equal("1080", item.QualityPresetId);
            Assert.Equal("PL123", item.PlaylistId);
            Assert.Equal("Samples", item.PlaylistTitle);
            Assert.Null(item.SelectedFormats);
        });
    }

    [Fact]
    public async Task BatchQueueAdd_SkipsActiveAndInBatchDuplicatesAndPersistsPlaylistContext()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ModernTubeDownloader.PlaylistTests", Guid.NewGuid().ToString("N"));
        var paths = AppPaths.Create(root);
        paths.EnsureCreated();
        var persistence = new QueuePersistenceService(paths, new NullAppLogger());
        var queue = new DownloadQueueService(persistence, new NullAppLogger());
        var changes = 0;
        queue.Changed += (_, _) => changes++;
        var first = new DownloadQueueItem { VideoId = "one", QualityPresetId = "1080", PlaylistId = "PL123", PlaylistIndex = 1 };
        var second = new DownloadQueueItem { VideoId = "two", QualityPresetId = "1080", PlaylistId = "PL123", PlaylistIndex = 2 };

        var result = queue.AddRangeSkippingDuplicates([first, first, second]);

        Assert.Equal(2, result.Added);
        Assert.Equal(1, result.DuplicatesSkipped);
        Assert.Equal(1, changes);
        Assert.Equal(["one", "two"], queue.Snapshot().Select(item => item.VideoId));
        await queue.SaveAsync();
        var restored = new DownloadQueueService(persistence, new NullAppLogger());
        await restored.LoadAsync();
        Assert.Equal([1, 2], restored.Snapshot().Select(item => item.PlaylistIndex));
        Assert.All(restored.Snapshot(), item => Assert.Equal("PL123", item.PlaylistId));
    }

    [Fact]
    public void TwoHundredEntries_DoNotRequireDeepPerEntryMetadata()
    {
        var entries = Enumerable.Range(1, 250).Select(index => new
        {
            id = $"v{index:000}", title = $"Video {index:000}",
            webpage_url = $"https://example.test/watch/v{index:000}", playlist_index = index
        });
        var json = JsonSerializer.Serialize(new { _type = "playlist", id = "PL250", entries });

        var playlist = PlaylistMetadata.Parse(json, PlaylistUrl);
        var selected = new PlaylistSelection(playlist).CreateQueueItems(QualityPreset.Find("best"));

        Assert.Equal(250, playlist.EntryCount);
        Assert.Equal(250, selected.Count);
        Assert.Equal("v001", selected[0].VideoId);
        Assert.Equal("v250", selected[^1].VideoId);
    }
}

[Collection("ModernFormsNext TestHost")]
public sealed class PlaylistPreviewHostTests
{
    [Theory]
    [InlineData(AppThemeMode.Light, "en")]
    [InlineData(AppThemeMode.Dark, "pl")]
    public async Task PlaylistRows_UseSemanticStatesAndKeepUnavailableSeparate(AppThemeMode themeMode, string language)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ModernTubeDownloader.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root);
            paths.EnsureCreated();
            var settings = new SettingsService(paths, new NullAppLogger());
            await settings.LoadAsync();
            settings.Current.ThemeMode = themeMode;
            using var host = ModernFormsTestHost.Create();
            using var appearance = new AppAppearanceService(settings, new NullAppLogger());
            appearance.Initialize();
            using var preview = new PlaylistPreviewControl(new LocalizationService(language));
            host.Show(preview, 900, 380);
            preview.ShowPlaylist(new PlaylistMetadata("PL4", "Playlist", null, "https://example.test/playlist/PL4", null,
            [
                new PlaylistEntry("v1", "A long playlist title with Unicode Żółw 🐢", "https://example.test/watch/v1", 1, 88, null, null, null, null),
                new PlaylistEntry("v2", "[Private video]", string.Empty, 2, null, null, null, "private", null),
                new PlaylistEntry("v3", "Long duration", "https://example.test/watch/v3", 3, 90061, null, null, null, null),
                new PlaylistEntry("v4", "Oversized duration", "https://example.test/watch/v4", 4, 1e20, null, null, null, null)
            ]), "best");
            host.ProcessPendingWork();

            var list = Assert.Single(preview.Controls.OfType<FlowLayoutPanel>());
            var rows = list.Controls.OfType<PlaylistEntryRow>().ToArray();
            Assert.Equal(4, rows.Length);
            Assert.Equal(AppUi.GetColor(AppThemeTokens.SurfaceSecondary), rows[0].Style.GetBackgroundColor());
            Assert.Equal(AppUi.GetColor(AppThemeTokens.SurfaceHover), rows[0].StyleHover.GetBackgroundColor());
            Assert.NotEqual(rows[0].Style.GetBackgroundColor(), rows[0].StyleHover.GetBackgroundColor());
            Assert.Equal(rows[0].Style.GetBackgroundColor(), rows[1].Style.GetBackgroundColor());
            Assert.True(rows[0].Selector.Checked);
            Assert.False(rows[1].Selector.Checked);
            Assert.True(rows[0].Selector.Enabled);
            Assert.False(rows[1].Selector.Enabled);
            Assert.True(rows[0].Selector.TabStop);
            Assert.Equal(SKColors.Transparent, rows[0].Selector.Style.GetBackgroundColor());
            Assert.Equal(SKColors.Transparent, rows[0].Selector.StyleFocused.GetBackgroundColor());
            var availableTitle = Assert.Single(rows[0].Controls.OfType<Label>(), control => control.AccessibleAutomationId == "PlaylistEntryTitle1");
            var availableDetail = Assert.Single(rows[0].Controls.OfType<Label>(), control => control.AccessibleAutomationId == "PlaylistEntryDetail1");
            var unavailableDetail = Assert.Single(rows[1].Controls.OfType<Label>(), control => control.AccessibleAutomationId == "PlaylistEntryDetail2");
            Assert.Contains("Żółw 🐢", availableTitle.Text);
            Assert.True(availableTitle.Right <= availableDetail.Left);
            Assert.Equal("00:01:28", availableDetail.Text);
            Assert.False(string.IsNullOrWhiteSpace(unavailableDetail.Text));
            Assert.Equal("25:01:01", Assert.Single(rows[2].Controls.OfType<Label>(), control => control.AccessibleAutomationId == "PlaylistEntryDetail3").Text);
            Assert.Equal("—", Assert.Single(rows[3].Controls.OfType<Label>(), control => control.AccessibleAutomationId == "PlaylistEntryDetail4").Text);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("en", "220 of 220 selected")]
    [InlineData("pl", "Wybrano 220 z 220")]
    public void TwoHundredEntries_RenderOnlyOnePageAndKeepStableAutomationIds(string language, string selectedText)
    {
        using var host = ModernFormsTestHost.Create();
        var entries = Enumerable.Range(1, 220)
            .Select(index => new PlaylistEntry($"v{index}", $"Video {index}", $"https://example.test/watch/v{index}", index, null, null, null, null, null))
            .ToArray();
        using var preview = new PlaylistPreviewControl(new LocalizationService(language));
        var window = host.Show(preview, 900, 380);
        preview.ShowPlaylist(new PlaylistMetadata("PL220", "Big playlist", null, "https://example.test/playlist/PL220", null, entries), "720");
        host.ProcessPendingWork();

        var list = Assert.Single(preview.Controls.OfType<FlowLayoutPanel>());
        Assert.Equal(40, list.Controls.OfType<PlaylistEntryRow>().Count());
        Assert.Equal("PlaylistEntryToggle1", Assert.IsType<PlaylistEntryRow>(list.Controls[0]).Selector.AccessibleAutomationId);
        Assert.Contains(preview.Controls.OfType<Label>(), label => label.AccessibleAutomationId == "PlaylistSelectedCount" && label.Text == selectedText);
        Assert.NotEmpty(window.CaptureTree().Children);

        var next = Assert.Single(preview.Controls.OfType<Button>(), button => button.AccessibleAutomationId == "PlaylistNextPageButton");
        Assert.True(list.VerticalScrollProperties.Maximum > 0);
        list.VerticalScrollProperties.Value = Math.Min(500, list.VerticalScrollProperties.Maximum);
        Assert.True(list.VerticalScrollProperties.Value > 0);
        next.PerformClick();
        host.ProcessPendingWork();
        Assert.Equal(40, list.Controls.OfType<PlaylistEntryRow>().Count());
        Assert.Equal("PlaylistEntryToggle41", Assert.IsType<PlaylistEntryRow>(list.Controls[0]).Selector.AccessibleAutomationId);
        Assert.Equal(0, list.VerticalScrollProperties.Value);
        Assert.InRange(list.VerticalScrollProperties.Value, list.VerticalScrollProperties.Minimum, list.VerticalScrollProperties.Maximum);
        preview.SetBounds(0, 0, 600, 300);
        host.ProcessPendingWork();
        Assert.InRange(list.VerticalScrollProperties.Value, list.VerticalScrollProperties.Minimum, list.VerticalScrollProperties.Maximum);
        list.VerticalScrollProperties.Value = Math.Min(400, list.VerticalScrollProperties.Maximum);
        var previous = Assert.Single(preview.Controls.OfType<Button>(), button => button.AccessibleAutomationId == "PlaylistPreviousPageButton");
        previous.PerformClick();
        host.ProcessPendingWork();
        Assert.Equal("PlaylistEntryToggle1", Assert.IsType<PlaylistEntryRow>(list.Controls[0]).Selector.AccessibleAutomationId);
        Assert.Equal(0, list.VerticalScrollProperties.Value);
    }
}
