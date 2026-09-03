using System.Text;
using System.Text.Json.Nodes;

var arguments = args.ToList();
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
    Console.WriteLine("2026.08.30-test");
    return 0;
}

if (arguments.Contains("-version"))
{
    Console.WriteLine("ffmpeg version 8.0-test");
    return 0;
}

if (arguments.Contains("--dump-single-json"))
{
    if (int.TryParse(Environment.GetEnvironmentVariable("MTD_FAKE_METADATA_DELAY_MS"), out var metadataDelay) && metadataDelay > 0)
        await Task.Delay(Math.Min(metadataDelay, 10000));
    var metadataJson = """
        {"id":"sample123","webpage_url":"https://example.test/watch/sample123","original_url":"https://example.test/watch/sample123","title":"Integration Sample","description":"Synthetic metadata emitted only by the integration-test tool.","uploader":"Test Uploader","channel":"Test Channel","channel_id":"channel-test","duration":12.5,"duration_string":"00:12","upload_date":"20260830","view_count":1234,"like_count":100,"comment_count":8,"thumbnail":null,"tags":["test"],"categories":["Testing"],"language":"en","availability":"public","age_limit":0,"live_status":"not_live","extractor":"test","extractor_key":"Test","unknown_future_field":{"preserved":true},"chapters":[{"title":"Start","start_time":0,"end_time":12.5}],"subtitles":{"en":[{"ext":"vtt","url":"https://example.test/sub.vtt"}]},"automatic_captions":{},"formats":[{"format_id":"137","ext":"mp4","width":1920,"height":1080,"resolution":"1920x1080","fps":30,"vcodec":"avc1.640028","acodec":"none","vbr":4500,"protocol":"https","filesize":1000,"format_note":"1080p"},{"format_id":"22","ext":"mp4","width":1280,"height":720,"resolution":"1280x720","fps":30,"vcodec":"avc1.64001F","acodec":"mp4a.40.2","tbr":1800,"protocol":"https","filesize":800,"format_note":"720p"},{"format_id":"140","ext":"m4a","vcodec":"none","acodec":"mp4a.40.2","abr":129,"asr":44100,"audio_channels":2,"protocol":"https","filesize":200,"format_note":"medium"}]}
        """;
    if (Environment.GetEnvironmentVariable("MTD_FAKE_METADATA_TITLE") is { Length: > 0 } title)
    {
        var metadata = JsonNode.Parse(metadataJson)!.AsObject();
        metadata["title"] = title;
        metadataJson = metadata.ToJsonString();
    }
    Console.WriteLine(metadataJson);
    return 0;
}

if (arguments.Contains("-f"))
{
    var format = ValueAfter(arguments, "-f") ?? "unknown";
    var outputDirectory = ValueAfter(arguments, "-P") ?? Environment.CurrentDirectory;
    Directory.CreateDirectory(outputDirectory);
    var extension = format == "140" ? "m4a" : "mp4";
    var output = Path.Combine(outputDirectory, $"Integration Sample [sample123].{extension}");
    Console.WriteLine("MTD_PROGRESS|downloading|50|100|100|25|2| 50.0%");
    if (int.TryParse(Environment.GetEnvironmentVariable("MTD_FAKE_DOWNLOAD_DELAY_MS"), out var downloadDelay) && downloadDelay > 0)
        await Task.Delay(Math.Min(downloadDelay, 10000));
    await File.WriteAllBytesAsync(output, Encoding.UTF8.GetBytes($"fake-{format}-media"));
    Console.WriteLine("MTD_PROGRESS|finished|100|100|100|25|0|100.0%");
    Console.WriteLine($"MTD_FILE|{output}");
    return 0;
}

if (arguments.Contains("-progress"))
{
    var output = arguments[^1];
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    Console.WriteLine("out_time_us=6250000");
    await File.WriteAllBytesAsync(output, Encoding.UTF8.GetBytes("fake-remuxed-media"));
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
