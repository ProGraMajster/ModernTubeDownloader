using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Tests;

public sealed class CoreTests
{
    [Fact]
    public void FormatSelector_ChoosesSeparateCompatibleStreamsFor1080p()
    {
        var metadata = TestMetadata();
        var selection = FormatSelector.Select(metadata, QualityPreset.Find("1080"));

        Assert.Equal("137", selection.VideoFormatId);
        Assert.Equal("140", selection.AudioFormatId);
        Assert.True(selection.RequiresMerge);
        Assert.Equal("mp4", FfmpegService.ChooseContainer(selection));
    }

    [Fact]
    public void FormatSelector_UsesCombinedStreamWhenItIsBestWithin720p()
    {
        var selection = FormatSelector.Select(TestMetadata(), QualityPreset.Find("720"));
        Assert.Equal("22", selection.VideoFormatId);
        Assert.Null(selection.AudioFormatId);
        Assert.False(selection.RequiresMerge);
    }

    [Fact]
    public void ProgressParser_ReadsStableTemplateOutput()
    {
        Assert.True(YtDlpProgressParser.TryParse("MTD_PROGRESS|downloading|524288|1048576|NA|262144|2| 50.0%", out var value));
        Assert.Equal(50, value.Percent);
        Assert.Equal(524288, value.DownloadedBytes);
        Assert.Equal(1048576, value.TotalBytes);
        Assert.Equal(262144, value.SpeedBytesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(2), value.Eta);
    }

    [Theory]
    [InlineData("--output stolen.mp4")]
    [InlineData("-f 22")]
    [InlineData("--exec calc.exe")]
    [InlineData("https://unexpected.example/video")]
    public void CustomArguments_RejectApplicationOwnedOrAdditionalUrl(string value) =>
        Assert.Throws<ArgumentException>(() => CommandLineArgumentTokenizer.ParseSafeYtDlpArguments(value));

    [Fact]
    public void CustomArguments_PreserveQuotedValuesWithoutUsingAShell()
    {
        var result = CommandLineArgumentTokenizer.ParseSafeYtDlpArguments("--user-agent \"Modern Tube Downloader\" --retries 3");
        Assert.Equal(["--user-agent", "Modern Tube Downloader", "--retries", "3"], result);
    }

    [Fact]
    public async Task ProcessRunner_CancellationTerminatesChildProcess()
    {
        var runner = new AsyncProcessRunner(new NullAppLogger());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            new ProcessRunRequest(FakeToolPath(), ["--sleep"]), cancellation.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("file:///c:/secret.txt")]
    public void MetadataUrlValidation_RejectsInvalidOrNonHttpInput(string value)
    {
        Assert.False(YtDlpMetadataService.TryValidateSourceUrl(value, out _, out var error));
        Assert.NotEmpty(error);
    }

    private static VideoMetadata TestMetadata() => new()
    {
        Id = "sample123",
        Title = "Sample",
        WebpageUrl = "https://example.test/watch/sample123",
        Formats =
        [
            new MediaFormat { Id = "137", Extension = "mp4", Height = 1080, Fps = 30, VideoCodec = "avc1", AudioCodec = "none", VideoBitrateKbps = 4500 },
            new MediaFormat { Id = "22", Extension = "mp4", Height = 720, Fps = 30, VideoCodec = "avc1", AudioCodec = "mp4a", TotalBitrateKbps = 1800 },
            new MediaFormat { Id = "140", Extension = "m4a", VideoCodec = "none", AudioCodec = "mp4a", AudioBitrateKbps = 129 }
        ]
    };

    internal static string FakeToolPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "ModernTubeDownloader.FakeTool.exe" : "ModernTubeDownloader.FakeTool");
        Assert.True(File.Exists(path), $"Fake tool was not copied to {path}");
        return path;
    }
}
