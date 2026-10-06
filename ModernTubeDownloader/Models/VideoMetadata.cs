using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModernTubeDownloader.Models;

public sealed class VideoMetadata
{
    private List<ThumbnailInfo>? thumbnails = [];
    private List<string>? tags = [];
    private List<string>? categories = [];
    private List<ChapterInfo>? chapters = [];
    private Dictionary<string, JsonElement>? subtitles = [];
    private Dictionary<string, JsonElement>? automaticCaptions = [];
    private List<MediaFormat>? formats = [];
    private Dictionary<string, JsonElement>? additionalData = [];

    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("_type")] public string? MediaType { get; set; }
    [JsonPropertyName("url")] public string? MediaUrl { get; set; }
    [JsonPropertyName("webpage_url")] public string WebpageUrl { get; set; } = string.Empty;
    [JsonPropertyName("original_url")] public string? OriginalUrl { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("fulltitle")] public string? FullTitle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("uploader")] public string? Uploader { get; set; }
    [JsonPropertyName("uploader_id")] public string? UploaderId { get; set; }
    [JsonPropertyName("uploader_url")] public string? UploaderUrl { get; set; }
    [JsonPropertyName("channel")] public string? Channel { get; set; }
    [JsonPropertyName("channel_id")] public string? ChannelId { get; set; }
    [JsonPropertyName("channel_url")] public string? ChannelUrl { get; set; }
    [JsonPropertyName("duration")] public double? DurationSeconds { get; set; }
    [JsonPropertyName("duration_string")] public string? DurationText { get; set; }
    [JsonPropertyName("upload_date")] public string? UploadDate { get; set; }
    [JsonPropertyName("release_date")] public string? ReleaseDate { get; set; }
    [JsonPropertyName("timestamp")] public long? Timestamp { get; set; }
    [JsonPropertyName("view_count")] public long? ViewCount { get; set; }
    [JsonPropertyName("like_count")] public long? LikeCount { get; set; }
    [JsonPropertyName("comment_count")] public long? CommentCount { get; set; }
    [JsonPropertyName("thumbnail")] public string? ThumbnailUrl { get; set; }
    [JsonPropertyName("thumbnails")]
    public List<ThumbnailInfo> Thumbnails
    {
        get => thumbnails ??= [];
        set => thumbnails = value ?? [];
    }

    [JsonPropertyName("tags")]
    public List<string> Tags
    {
        get => tags ??= [];
        set => tags = value ?? [];
    }

    [JsonPropertyName("categories")]
    public List<string> Categories
    {
        get => categories ??= [];
        set => categories = value ?? [];
    }
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("availability")] public string? Availability { get; set; }
    [JsonPropertyName("age_limit")] public int? AgeLimit { get; set; }
    [JsonPropertyName("live_status")] public string? LiveStatus { get; set; }
    [JsonPropertyName("is_live")] public bool? IsLive { get; set; }
    [JsonPropertyName("was_live")] public bool? WasLive { get; set; }
    [JsonPropertyName("release_timestamp")] public long? ReleaseTimestamp { get; set; }
    [JsonPropertyName("chapters")]
    public List<ChapterInfo> Chapters
    {
        get => chapters ??= [];
        set => chapters = value ?? [];
    }

    [JsonPropertyName("subtitles")]
    public Dictionary<string, JsonElement> Subtitles
    {
        get => subtitles ??= [];
        set => subtitles = value ?? [];
    }

    [JsonPropertyName("automatic_captions")]
    public Dictionary<string, JsonElement> AutomaticCaptions
    {
        get => automaticCaptions ??= [];
        set => automaticCaptions = value ?? [];
    }
    [JsonPropertyName("extractor")] public string? Extractor { get; set; }
    [JsonPropertyName("extractor_key")] public string? ExtractorKey { get; set; }
    [JsonPropertyName("formats")]
    public List<MediaFormat> Formats
    {
        get => formats ??= [];
        set => formats = value ?? [];
    }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData
    {
        get => additionalData ??= [];
        set => additionalData = value ?? [];
    }
    [JsonIgnore] public string RawJson { get; set; } = string.Empty;

    public string DisplayChannel
        => !string.IsNullOrWhiteSpace(Channel)
            ? Channel
            : !string.IsNullOrWhiteSpace(Uploader)
                ? Uploader
                : "Unknown channel";
}

public sealed class ThumbnailInfo
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("preference")] public int? Preference { get; set; }
}

public sealed class ChapterInfo
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("start_time")] public double? StartTime { get; set; }
    [JsonPropertyName("end_time")] public double? EndTime { get; set; }
}

public sealed class MediaFormat
{
    private Dictionary<string, JsonElement>? additionalData = [];

    [JsonPropertyName("format_id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("format")] public string? DisplayName { get; set; }
    [JsonPropertyName("ext")] public string Extension { get; set; } = string.Empty;
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("resolution")] public string? Resolution { get; set; }
    [JsonPropertyName("fps")] public double? Fps { get; set; }
    [JsonPropertyName("vcodec")] public string? VideoCodec { get; set; }
    [JsonPropertyName("acodec")] public string? AudioCodec { get; set; }
    [JsonPropertyName("tbr")] public double? TotalBitrateKbps { get; set; }
    [JsonPropertyName("vbr")] public double? VideoBitrateKbps { get; set; }
    [JsonPropertyName("abr")] public double? AudioBitrateKbps { get; set; }
    [JsonPropertyName("asr")] public double? AudioSampleRate { get; set; }
    [JsonPropertyName("audio_channels")] public int? AudioChannels { get; set; }
    [JsonPropertyName("dynamic_range")] public string? DynamicRange { get; set; }
    [JsonPropertyName("protocol")] public string? Protocol { get; set; }
    [JsonPropertyName("filesize")] public long? FileSize { get; set; }
    [JsonPropertyName("filesize_approx")] public long? ApproximateFileSize { get; set; }
    [JsonPropertyName("quality")] public double? Quality { get; set; }
    [JsonPropertyName("format_note")] public string? Note { get; set; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData
    {
        get => additionalData ??= [];
        set => additionalData = value ?? [];
    }

    [JsonIgnore] public bool HasVideo => !string.IsNullOrWhiteSpace(VideoCodec) && !VideoCodec.Equals("none", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool HasAudio => !string.IsNullOrWhiteSpace(AudioCodec) && !AudioCodec.Equals("none", StringComparison.OrdinalIgnoreCase);
}
