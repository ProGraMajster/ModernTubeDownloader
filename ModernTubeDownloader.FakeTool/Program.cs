using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

var arguments = args.ToList();
if (arguments.Contains("--list-extractors"))
{
    Console.WriteLine("youtube\nyoutube:tab\nyoutube:playlist\ntwitch:stream\ntwitch:vod\nvimeo\nTikTok\ngeneric\nBrokenSite (CURRENTLY BROKEN)");
    return 0;
}
if (arguments.Contains("--extractor-descriptions"))
{
    Console.WriteLine("youtube: [youtube] YouTube\nyoutube:tab: YouTube public playlists\ntwitch:stream: [twitch]\nvimeo: [vimeo]\ngeneric: Generic downloader that works on some sites");
    return 0;
}
if (arguments.Contains("--spawn-child-with-inherited-output"))
{
    await Task.Delay(200); // Let the caller assign this process to its Windows job.
    var start = new ProcessStartInfo(Environment.ProcessPath!)
    {
        UseShellExecute = false,
        CreateNoWindow = true
    };
    start.ArgumentList.Add("--sleep-short");
    using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start fake descendant.");
    Console.WriteLine($"CHILD_PID|{child.Id}");
    return 0;
}

if (arguments.Contains("--sleep-short"))
{
    await Task.Delay(TimeSpan.FromSeconds(5));
    return 0;
}

if ((arguments.Contains("--version") || arguments.Contains("-version"))
    && int.TryParse(Environment.GetEnvironmentVariable("MTD_FAKE_VALIDATION_DELAY_MS"), out var validationDelay)
    && validationDelay > 0)
{
    await Task.Delay(Math.Min(validationDelay, 10000));
}

if (arguments.Contains("--sleep"))
{
    await Task.Delay(TimeSpan.FromSeconds(30));
    return 0;
}

if (arguments.Contains("--version"))
{
    Console.WriteLine(Path.GetFileName(Environment.ProcessPath ?? string.Empty).StartsWith("deno", StringComparison.OrdinalIgnoreCase)
        ? "deno 2.3.0"
        : "2026.08.30-test");
    return 0;
}

if (arguments.Contains("-version"))
{
    Console.WriteLine("ffmpeg version 8.0-test");
    return 0;
}

if (arguments.Contains("--dump-single-json"))
{
    var checkUrl = arguments.LastOrDefault() ?? string.Empty;
    if (checkUrl.Contains("/source/unsupported", StringComparison.OrdinalIgnoreCase) ||
        checkUrl.Contains("/source/auth", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(checkUrl.Contains("/auth") ? "ERROR: Login required; confirm your age" : "ERROR: Unsupported URL");
        return 1;
    }
    if (int.TryParse(Environment.GetEnvironmentVariable("MTD_FAKE_METADATA_DELAY_MS"), out var metadataDelay) && metadataDelay > 0)
        await Task.Delay(Math.Min(metadataDelay, 10000));
    var sourceUrl = ValueAfter(arguments, "--") ?? arguments.LastOrDefault() ?? string.Empty;
    if (sourceUrl.Contains("/playlist?", StringComparison.OrdinalIgnoreCase) ||
        sourceUrl.Contains("/playlist/", StringComparison.OrdinalIgnoreCase))
    {
        var count = int.TryParse(Environment.GetEnvironmentVariable("MTD_FAKE_PLAYLIST_COUNT"), out var configuredCount)
            ? Math.Clamp(configuredCount, 0, 500) : 12;
        var entries = new JsonArray();
        for (var index = 1; index <= count; index++)
        {
            var id = $"p{index:000}";
            entries.Add(new JsonObject
            {
                ["id"] = id,
                ["title"] = index == 3 ? "[Private video]" : $"Playlist sample {index:000}",
                ["webpage_url"] = $"https://example.test/watch?id={id}",
                ["playlist_index"] = index,
                ["duration"] = 40 + index,
                ["channel"] = "Test Channel",
                ["availability"] = index == 3 ? "private" : "public"
            });
        }
        Console.WriteLine(new JsonObject
        {
            ["_type"] = "playlist",
            ["id"] = "synthetic-playlist",
            ["title"] = "Integration playlist",
            ["channel"] = "Test Channel",
            ["webpage_url"] = sourceUrl,
            ["entries"] = entries
        }.ToJsonString());
        return 0;
    }
    var metadataJson = """
        {"id":"sample123","webpage_url":"https://example.test/watch/sample123","original_url":"https://example.test/watch/sample123","title":"Integration Sample","description":"Synthetic metadata emitted only by the integration-test tool.","uploader":"Test Uploader","channel":"Test Channel","channel_id":"channel-test","duration":12.5,"duration_string":"00:12","upload_date":"20260830","view_count":1234,"like_count":100,"comment_count":8,"thumbnail":null,"tags":["test"],"categories":["Testing"],"language":"en","availability":"public","age_limit":0,"live_status":"not_live","extractor":"test","extractor_key":"Test","unknown_future_field":{"preserved":true},"chapters":[{"title":"Start","start_time":0,"end_time":12.5}],"subtitles":{"en":[{"ext":"vtt","url":"https://example.test/sub.vtt"}]},"automatic_captions":{},"formats":[{"format_id":"137","ext":"mp4","width":1920,"height":1080,"resolution":"1920x1080","fps":30,"vcodec":"avc1.640028","acodec":"none","vbr":4500,"protocol":"https","filesize":1000,"format_note":"1080p"},{"format_id":"22","ext":"mp4","width":1280,"height":720,"resolution":"1280x720","fps":30,"vcodec":"avc1.64001F","acodec":"mp4a.40.2","tbr":1800,"protocol":"https","filesize":800,"format_note":"720p"},{"format_id":"140","ext":"m4a","vcodec":"none","acodec":"mp4a.40.2","abr":129,"asr":44100,"audio_channels":2,"protocol":"https","filesize":200,"format_note":"medium"}]}
        """;
    var title = Environment.GetEnvironmentVariable("MTD_FAKE_METADATA_TITLE");
    var sourceUri = Uri.TryCreate(sourceUrl, UriKind.Absolute, out var parsedUri) ? parsedUri : null;
    if (sourceUrl.Contains("/source/opaque", StringComparison.Ordinal))
    {
        var opaque = JsonNode.Parse(metadataJson)!.AsObject();
        opaque["webpage_url"] = sourceUrl;
        opaque["original_url"] = sourceUrl;
        opaque["formats"] = new JsonArray(new JsonObject
        {
            ["format_id"] = sourceUri!.AbsolutePath.Split('/').Last(), ["ext"] = "mp4",
            ["width"] = 1280, ["height"] = 720, ["protocol"] = "https",
            ["url"] = "https://media.example.test/direct.mp4"
        });
        Console.WriteLine(opaque.ToJsonString());
        return 0;
    }
    if (sourceUrl.Contains("/source/generic") || sourceUrl.Contains("/source/private"))
    {
        var sourceMetadata = JsonNode.Parse(metadataJson)!.AsObject();
        sourceMetadata["extractor"] = sourceUrl.Contains("/generic") ? "generic" : "test";
        sourceMetadata["extractor_key"] = sourceUrl.Contains("/generic") ? "Generic" : "Test";
        sourceMetadata["availability"] = sourceUrl.Contains("/private") ? "private" : "public";
        Console.WriteLine(sourceMetadata.ToJsonString());
        return 0;
    }
    var liveRoute = sourceUri?.AbsolutePath.StartsWith("/live/", StringComparison.OrdinalIgnoreCase) == true;
    if (!string.IsNullOrWhiteSpace(title) || liveRoute ||
        sourceUri?.Query.Contains("id=", StringComparison.OrdinalIgnoreCase) == true)
    {
        var metadata = JsonNode.Parse(metadataJson)!.AsObject();
        if (!string.IsNullOrWhiteSpace(title)) metadata["title"] = title;
        if (liveRoute)
        {
            var status = sourceUri!.AbsolutePath.EndsWith("/active", StringComparison.OrdinalIgnoreCase)
                ? "is_live" : sourceUri.AbsolutePath.EndsWith("/upcoming", StringComparison.OrdinalIgnoreCase)
                    ? "is_upcoming" : sourceUri.AbsolutePath.EndsWith("/processing", StringComparison.OrdinalIgnoreCase)
                        ? "post_live" : "was_live";
            metadata["id"] = sourceUri.AbsolutePath.Split('/').Last();
            metadata["webpage_url"] = sourceUrl;
            metadata["original_url"] = sourceUrl;
            metadata["live_status"] = status;
            metadata["is_live"] = status == "is_live";
            metadata["was_live"] = status == "was_live";
            metadata["release_date"] = "20260829";
            metadata["release_timestamp"] = 1787990400;
            metadata["extractor_key"] = "Youtube";
            metadata["extractor"] = "youtube";
        }
        if (sourceUri is not null)
        {
            var id = sourceUri.Query.Split('&').FirstOrDefault(value => value.TrimStart('?').StartsWith("id=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2).Last();
            if (!string.IsNullOrWhiteSpace(id))
            {
                metadata["id"] = id;
                metadata["title"] = $"Playlist sample {id}";
                metadata["webpage_url"] = sourceUrl;
                metadata["original_url"] = sourceUrl;
            }
        }
        metadataJson = metadata.ToJsonString();
    }
    Console.WriteLine(metadataJson);
    return 0;
}

if (arguments.Contains("--skip-download") &&
    (arguments.Contains("--write-subs") || arguments.Contains("--write-auto-subs")))
{
    var outputDirectory = ValueAfter(arguments, "-P") ?? Environment.CurrentDirectory;
    Directory.CreateDirectory(outputDirectory);
    await File.WriteAllTextAsync(Path.Combine(outputDirectory, "subtitle-arguments.json"),
        System.Text.Json.JsonSerializer.Serialize(arguments));
    var language = (ValueAfter(arguments, "--sub-langs") ?? "en").Split(',')[0];
    var format = ValueAfter(arguments, "--convert-subs") ?? "vtt";
    await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"subtitle.{language}.{format}"),
        format == "srt" ? "1\n00:00:00,000 --> 00:00:01,000\nTest\n" : "WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nTest\n");
    return 0;
}

if (arguments.Contains("-f"))
{
    var format = ValueAfter(arguments, "-f") ?? "unknown";
    var outputDirectory = ValueAfter(arguments, "-P") ?? Environment.CurrentDirectory;
    var sourceUrl = ValueAfter(arguments, "--") ?? string.Empty;
    if (sourceUrl.Contains("id=retry403", StringComparison.OrdinalIgnoreCase))
    {
        var itemDirectory = Path.GetDirectoryName(Path.GetDirectoryName(outputDirectory));
        if (!string.IsNullOrWhiteSpace(itemDirectory))
        {
            Directory.CreateDirectory(itemDirectory);
            var firstFailure = Path.Combine(itemDirectory, ".synthetic-403-complete");
            if (!File.Exists(firstFailure))
            {
                await File.WriteAllTextAsync(firstFailure, "403");
                Console.Error.WriteLine("ERROR: HTTP Error 403: Forbidden");
                return 1;
            }
        }
    }
    var videoId = "sample123";
    if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var videoUri))
    {
        var queryId = videoUri.Query.Split('&').FirstOrDefault(value => value.TrimStart('?').StartsWith("id=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2).Last();
        if (!string.IsNullOrWhiteSpace(queryId)) videoId = queryId;
    }
    Directory.CreateDirectory(outputDirectory);
    await File.WriteAllTextAsync(Path.Combine(outputDirectory, "media-arguments.json"),
        System.Text.Json.JsonSerializer.Serialize(arguments));
    if (sourceUrl.Contains("id=resume503", StringComparison.OrdinalIgnoreCase))
    {
        var partial = Path.Combine(outputDirectory, "Integration Sample [resume503].mp4.part");
        var marker = Path.Combine(outputDirectory, ".synthetic-resume-503");
        if (!File.Exists(marker))
        {
            await File.WriteAllBytesAsync(partial, Encoding.UTF8.GetBytes("first-fragments|"));
            await File.WriteAllTextAsync(marker, "failed-once");
            Console.Error.WriteLine("ERROR: HTTP Error 503: Service Unavailable");
            return 1;
        }
        if (!File.Exists(partial))
        {
            Console.Error.WriteLine("ERROR: Resume data was not preserved");
            return 2;
        }
        await File.AppendAllTextAsync(partial, "later-fragments");
        var resumed = Path.Combine(outputDirectory, "Integration Sample [resume503].mp4");
        File.Move(partial, resumed, overwrite: true);
        Console.WriteLine($"MTD_FILE|{resumed}");
        return 0;
    }
    if (sourceUrl.Contains("/live/active", StringComparison.OrdinalIgnoreCase))
    {
        var mode = Uri.TryCreate(sourceUrl, UriKind.Absolute, out var liveUri)
            ? liveUri.Query.TrimStart('?').Split('&').FirstOrDefault(value => value.StartsWith("behavior=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2).Last()
            : null;
        mode ??= Environment.GetEnvironmentVariable("MTD_FAKE_LIVE_BEHAVIOR") ?? "natural";
        var partial = Path.Combine(outputDirectory, "capture.mkv.part");
        await File.WriteAllBytesAsync(partial, Encoding.UTF8.GetBytes("synthetic-live-audio-video-fragments"));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, ".test-started.json"), Environment.ProcessId.ToString());
        Console.WriteLine("MTD_PROGRESS|downloading|0|32|0|4|0|LIVE");
        if (mode == "split-fail")
        {
            File.Delete(partial);
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "capture.f137.mp4.part"), "video-only");
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "capture.f140.m4a.part"), "audio-only");
            Console.Error.WriteLine("ERROR: HTTP Error 503: Service Unavailable");
            return 1;
        }
        if (mode == "wait")
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            return 0;
        }
        if (mode == "fail-once")
        {
            var marker = Path.Combine(Path.GetDirectoryName(outputDirectory)!, ".synthetic-live-failed-once");
            if (!File.Exists(marker))
            {
                await File.WriteAllTextAsync(marker, "1");
                Console.Error.WriteLine("ERROR: HTTP Error 503: Service Unavailable");
                return 1;
            }
        }
        if (mode == "fail-always")
        {
            Console.Error.WriteLine("ERROR: HTTP Error 503: Service Unavailable");
            return 1;
        }
        var liveOutput = Path.Combine(outputDirectory, "capture.mkv");
        File.Move(partial, liveOutput, overwrite: true);
        Console.WriteLine($"MTD_FILE|{liveOutput}");
        return 0;
    }
    var extension = format == "140" ? "m4a" : "mp4";
    if (sourceUrl.Contains("hold=1", StringComparison.Ordinal))
    {
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, ".test-started.json"), Environment.ProcessId.ToString());
        while (!File.Exists(Path.Combine(outputDirectory, ".test-finish.json")))
            await Task.Delay(20);
    }
    var output = Path.Combine(outputDirectory, $"Integration Sample [{videoId}].{extension}");
    Console.WriteLine("MTD_PROGRESS|downloading|50|100|100|25|2| 50.0%");
    if (int.TryParse(Environment.GetEnvironmentVariable("MTD_FAKE_DOWNLOAD_DELAY_MS"), out var downloadDelay) && downloadDelay > 0)
        await Task.Delay(Math.Min(downloadDelay, 10000));
    var requestedSection = ValueAfter(arguments, "--download-sections") ?? "full";
    await File.WriteAllBytesAsync(output, Encoding.UTF8.GetBytes($"fake-{format}-media|section={requestedSection}"));
    Console.WriteLine("MTD_PROGRESS|finished|100|100|100|25|0|100.0%");
    Console.WriteLine($"MTD_FILE|{output}");
    return 0;
}

if (arguments.Contains("-show_entries") && ValueAfter(arguments, "-show_entries") == "format=duration,start_time,format_name:stream=codec_type")
{
    var file = arguments[^1];
    var contents = File.Exists(file) ? File.ReadAllText(file) : string.Empty;
    if (contents.Contains("fake-opaque-invalid-media", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Invalid data found when processing input");
        return 1;
    }
    var videoOnly = file.Contains(".f137.", StringComparison.Ordinal) || contents.Contains("fake-opaque-silent-media", StringComparison.Ordinal);
    var audioOnly = file.Contains(".f140.", StringComparison.Ordinal);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
        streams = videoOnly ? new[] { new { codec_type = "video" } } : audioOnly ? new[] { new { codec_type = "audio" } }
            : new[] { new { codec_type = "video" }, new { codec_type = "audio" } },
        format = new { duration = audioOnly || File.Exists(file) && File.ReadAllText(file).Contains("duration=6", StringComparison.Ordinal) ? "6.0" : "8.0", start_time = "0.0", format_name = "matroska" }
    }));
    return 0;
}

if (arguments.Contains("-show_entries") && ValueAfter(arguments, "-show_entries") == "format=duration")
{
    Console.WriteLine(File.Exists(arguments[^1]) && File.ReadAllText(arguments[^1]).Contains("duration=6", StringComparison.Ordinal) ? "6.0" : "8.0");
    return 0;
}

if (arguments.Contains("-progress"))
{
    var output = arguments[^1];
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    Console.WriteLine("out_time_us=6250000");
    await File.WriteAllBytesAsync(output, Encoding.UTF8.GetBytes("fake-remuxed-media"));
    if (ValueAfter(arguments, "-i") is { } input && File.Exists(input) &&
        File.ReadAllText(input).Contains("fake-opaque-remux-bad-media", StringComparison.Ordinal))
        await File.WriteAllTextAsync(output, "fake-opaque-invalid-media");
    if (ValueAfter(arguments, "-t") is { } cap)
        await File.AppendAllTextAsync(output, $"|duration={cap}");
    Console.WriteLine("progress=end");
    return 0;
}

Console.Error.WriteLine("ERROR: Unsupported fake-tool invocation");
return 2;

static string? ValueAfter(IReadOnlyList<string> values, string option)
{
    for (var index = 0; index < values.Count - 1; index++)
        if (values[index] == option)
            return values[index + 1];
    return null;
}
