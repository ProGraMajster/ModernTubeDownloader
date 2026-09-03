using System.Text.Json;
using ModernFormsNext;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Views;

namespace ModernTubeDownloader.Tests;

public sealed class VideoDetailsFormTests
{
    [Fact]
    public void ConstructingDetailsWithFullMetadataDoesNotThrow()
    {
        var metadata = JsonSerializer.Deserialize<VideoMetadata>(
            """
            {
              "id": "full",
              "webpage_url": "https://example.test/watch/full",
              "title": "Full metadata",
              "description": "Description",
              "uploader": "Uploader",
              "channel": "Channel",
              "channel_id": "channel-id",
              "duration": 125,
              "duration_string": "02:05",
              "upload_date": "20260830",
              "release_date": "20260831",
              "view_count": 1234,
              "like_count": 120,
              "comment_count": 12,
              "thumbnail": "https://example.test/thumb.jpg",
              "thumbnails": [{ "url": "https://example.test/thumb.jpg", "width": 1280, "height": 720 }],
              "tags": ["test"],
              "categories": ["Testing"],
              "language": "en",
              "availability": "public",
              "age_limit": 0,
              "live_status": "not_live",
              "chapters": [{ "title": "Start", "start_time": 0, "end_time": 125 }],
              "subtitles": { "en": [] },
              "automatic_captions": { "pl": [] },
              "extractor": "test",
              "extractor_key": "Test",
              "formats": [{ "format_id": "137", "ext": "mp4", "height": 1080, "vcodec": "avc1", "acodec": "none" }]
            }
            """,
            JsonOptions())!;

        using var form = new VideoDetailsForm(metadata, new LocalizationService("en"));
    }

    [Fact]
    public void ConstructingDetailsWithPartiallyNullJsonMetadataDoesNotThrow()
    {
        var metadata = JsonSerializer.Deserialize<VideoMetadata>(
            """
            {
              "id": "partial",
              "title": "Partial metadata",
              "description": null,
              "channel": null,
              "uploader": null,
              "tags": null,
              "categories": null,
              "chapters": null,
              "subtitles": null,
              "automatic_captions": null,
              "formats": null,
              "thumbnails": null,
              "upload_date": null,
              "release_date": null,
              "view_count": null,
              "like_count": null,
              "comment_count": null,
              "duration": null,
              "language": null,
              "live_status": null
            }
            """,
            JsonOptions())!;

        using var form = new VideoDetailsForm(metadata, new LocalizationService("pl"));
        Assert.Empty(metadata.Chapters);
        Assert.Empty(metadata.Subtitles);
        Assert.Empty(metadata.AutomaticCaptions);
        Assert.Empty(metadata.Formats);
    }

    [Fact]
    public void ConstructingDetailsWithoutChaptersDoesNotThrow()
    {
        var metadata = MinimalMetadata();
        metadata.Chapters = null!;
        using var form = new VideoDetailsForm(metadata, new LocalizationService("pl"));
    }

    [Fact]
    public void ConstructingDetailsWithoutSubtitlesDoesNotThrow()
    {
        var metadata = MinimalMetadata();
        metadata.Subtitles = null!;
        metadata.AutomaticCaptions = null!;
        using var form = new VideoDetailsForm(metadata, new LocalizationService("pl"));
    }

    [Fact]
    public void ConstructingDetailsWithoutDescriptionDoesNotThrow()
    {
        var metadata = MinimalMetadata();
        metadata.Description = null;
        using var form = new VideoDetailsForm(metadata, new LocalizationService("pl"));
    }

    [Fact]
    public void ConstructingDetailsWithoutThumbnailDoesNotThrow()
    {
        var metadata = MinimalMetadata();
        metadata.ThumbnailUrl = null;
        metadata.Thumbnails = null!;
        using var form = new VideoDetailsForm(metadata, new LocalizationService("pl"));
    }

    [Fact]
    public void ConstructingDetailsWithEmptyFormatsDoesNotThrow()
    {
        var metadata = MinimalMetadata();
        metadata.Formats = [];
        using var form = new VideoDetailsForm(metadata, new LocalizationService("pl"));
    }

    [Fact]
    public void DetailsDoesNotRenderTheEntireRawYtDlpDocument()
    {
        var metadata = MinimalMetadata();
        metadata.RawJson = $$"""
            { "id": "minimal", "large_payload": "{{new string('x', 700_000)}}" }
            """;

        using var form = new VideoDetailsForm(metadata, new LocalizationService("en"));
        var tabs = Assert.Single(form.Controls.OfType<TabControl>());
        var metadataPage = tabs.TabPages[1];
        var content = Assert.Single(metadataPage.Controls.OfType<TextBox>());

        Assert.True(content.Text.Length < 10_000, $"Metadata tab unexpectedly contains {content.Text.Length:N0} characters.");
        Assert.Contains("Format count", content.Text, StringComparison.Ordinal);
    }

    private static VideoMetadata MinimalMetadata()
        => new()
        {
            Id = "minimal",
            Title = "Minimal metadata",
            WebpageUrl = "https://example.test/watch/minimal",
            RawJson = "{}"
        };

    private static JsonSerializerOptions JsonOptions()
        => new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
}
