using System.Globalization;

namespace ModernTubeDownloader.Models;

public enum MediaRangeMode { Full, Custom }

public sealed record MediaTimeRange(MediaRangeMode Mode = MediaRangeMode.Full, TimeSpan? Start = null, TimeSpan? End = null)
{
    public static MediaTimeRange Full { get; } = new();

    public string? Validate(double? durationSeconds, bool isActiveLive = false)
    {
        if (!Enum.IsDefined(Mode)) return "Range.Error.Invalid";
        if (Mode == MediaRangeMode.Full)
            return Start is null && End is null ? null : "Range.Error.Invalid";
        if (isActiveLive) return "Range.Error.ActiveLive";
        if (Start is null && End is null) return "Range.Error.Required";
        if (Start is { } start && (start < TimeSpan.Zero || start.TotalDays > 30)) return "Range.Error.Invalid";
        if (End is { } end && (end <= TimeSpan.Zero || end.TotalDays > 30)) return "Range.Error.Invalid";
        if (Start is { } from && End is { } to && to <= from) return "Range.Error.Order";
        if (durationSeconds is > 0 and < double.PositiveInfinity)
        {
            if (Start is { } startAt && startAt.TotalSeconds >= durationSeconds.Value) return "Range.Error.Duration";
            if (End is { } endAt && endAt.TotalSeconds > durationSeconds.Value + 0.001d) return "Range.Error.Duration";
        }
        return null;
    }

    public string ToYtDlpSection()
    {
        if (Mode != MediaRangeMode.Custom || Validate(null) is not null)
            throw new InvalidOperationException("A valid custom range is required.");
        return $"*{(Start?.TotalSeconds ?? 0).ToString("0.###", CultureInfo.InvariantCulture)}-{(End?.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) ?? "inf")}";
    }

    public string DisplayText => Mode == MediaRangeMode.Full ? string.Empty :
        $"{Format(Start) ?? "00:00:00"} → {Format(End) ?? "∞"}";

    public static bool TryParseTime(string? value, out TimeSpan? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!TimeSpan.TryParseExact(value.Trim(), [@"h\:mm\:ss", @"hh\:mm\:ss", @"d\.hh\:mm\:ss"],
                CultureInfo.InvariantCulture, out var parsed)) return false;
        result = parsed;
        return true;
    }

    private static string? Format(TimeSpan? value) => value is { } time
        ? $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}" : null;
}
