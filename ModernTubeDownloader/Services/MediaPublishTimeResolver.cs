using System.Globalization;
using System.Text.Json;

namespace ModernTubeDownloader.Services;

internal enum MediaPublishTimePrecision
{
    ExactInstant,
    CalendarDate
}

internal readonly record struct MediaPublishTime(
    DateTimeOffset Instant,
    MediaPublishTimePrecision Precision,
    string SourceField);

internal static class MediaPublishTimeResolver
{
    private static readonly string[] LiveStatuses = ["is_live", "was_live", "post_live", "is_upcoming"];

    public static bool TryResolve(string? rawMetadataJson, out MediaPublishTime publishTime)
    {
        publishTime = default;
        if (string.IsNullOrWhiteSpace(rawMetadataJson))
            return false;

        try
        {
            using var document = JsonDocument.Parse(rawMetadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var metadata = document.RootElement;
            var liveMedia = ReadBoolean(metadata, "is_live") == true ||
                            ReadBoolean(metadata, "was_live") == true ||
                            ReadString(metadata, "live_status") is { } status &&
                            LiveStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);

            return liveMedia
                ? TryUnixTimestamp(metadata, "release_timestamp", out publishTime) ||
                  TryCalendarDate(metadata, "release_date", out publishTime) ||
                  TryUnixTimestamp(metadata, "timestamp", out publishTime) ||
                  TryCalendarDate(metadata, "upload_date", out publishTime)
                : TryUnixTimestamp(metadata, "timestamp", out publishTime) ||
                  TryCalendarDate(metadata, "upload_date", out publishTime) ||
                  TryUnixTimestamp(metadata, "release_timestamp", out publishTime) ||
                  TryCalendarDate(metadata, "release_date", out publishTime);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryUnixTimestamp(JsonElement metadata, string propertyName, out MediaPublishTime publishTime)
    {
        publishTime = default;
        if (!metadata.TryGetProperty(propertyName, out var value))
            return false;

        long seconds;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out seconds))
        {
            // Already read.
        }
        else if (value.ValueKind == JsonValueKind.String &&
                 long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds))
        {
            // String-valued numeric metadata is accepted for compatibility.
        }
        else
        {
            return false;
        }

        try
        {
            publishTime = new MediaPublishTime(
                DateTimeOffset.FromUnixTimeSeconds(seconds),
                MediaPublishTimePrecision.ExactInstant,
                propertyName);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryCalendarDate(JsonElement metadata, string propertyName, out MediaPublishTime publishTime)
    {
        publishTime = default;
        var value = ReadString(metadata, propertyName);
        if (!DateTime.TryParseExact(
                value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
            return false;

        publishTime = new MediaPublishTime(
            new DateTimeOffset(DateTime.SpecifyKind(parsed.Date, DateTimeKind.Unspecified), TimeSpan.Zero),
            MediaPublishTimePrecision.CalendarDate,
            propertyName);
        return true;
    }

    private static string? ReadString(JsonElement metadata, string propertyName)
        => metadata.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBoolean(JsonElement metadata, string propertyName)
        => metadata.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
