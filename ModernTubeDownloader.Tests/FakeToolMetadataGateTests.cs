using System.Diagnostics;
using System.Text.Json;

namespace ModernTubeDownloader.Tests;

public sealed class FakeToolMetadataGateTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MetadataGateRequiresExplicitReleaseAndCanBeTerminatedWhileHeld(bool cancel)
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-fake-metadata-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "request-prefix.txt"), "https://example.test/watch/");
        var start = new ProcessStartInfo(CoreTests.FakeToolPath())
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--dump-single-json"); start.ArgumentList.Add("https://example.test/watch/gated");
        // A per-process environment avoids changing another concurrent test's tools.
        start.Environment["MTD_QA_FAKE_METADATA_GATE"] = root;
        start.Environment.Remove("MTD_FAKE_METADATA_DELAY_MS");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            var ready = Path.Combine(root, process.Id + ".ready.json");
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(ready) && !process.HasExited && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
            Assert.True(File.Exists(ready), "The synthetic metadata process did not reach its explicit gate.");
            using var witness = JsonDocument.Parse(await File.ReadAllTextAsync(ready));
            Assert.Equal(process.Id, witness.RootElement.GetProperty("processId").GetInt32());
            Assert.Equal(process.StartTime.ToUniversalTime().Ticks, witness.RootElement.GetProperty("startUtcTicks").GetInt64());
            Assert.Equal("https://example.test/watch/gated", witness.RootElement.GetProperty("sourceUrl").GetString());
            Assert.False(process.HasExited); Assert.False(stdout.IsCompleted);
            var release = Path.Combine(root, "release");
            if (cancel) process.Kill(entireProcessTree: true);
            else await File.WriteAllTextAsync(release, "capture assertions complete");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var output = await stdout;
            if (cancel)
            {
                Assert.False(File.Exists(release)); Assert.Equal("", output);
            }
            else
            {
                Assert.Equal(0, process.ExitCode); Assert.Equal("", await stderr);
                using var metadata = JsonDocument.Parse(output);
                Assert.Equal("sample123", metadata.RootElement.GetProperty("id").GetString());
                Assert.NotEqual(0, metadata.RootElement.GetProperty("formats").GetArrayLength());
            }
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await stdout; await stderr;
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ScopedMetadataGateDoesNotHoldUnrelatedLiveProbeOrPublishItsReadyMarker()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-fake-metadata-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "request-prefix.txt"), "https://example.test/watch/visual");
        try
        {
            var result = await RunMetadataAsync(root, "https://example.test/live/active?id=visual");
            Assert.Equal(0, result.ExitCode); Assert.Equal("", result.Error);
            using var metadata = JsonDocument.Parse(result.Output);
            Assert.Equal("is_live", metadata.RootElement.GetProperty("live_status").GetString());
            Assert.NotEmpty(metadata.RootElement.GetProperty("formats").EnumerateArray());
            Assert.Empty(Directory.EnumerateFiles(root, "*.ready.json"));
            Assert.False(File.Exists(Path.Combine(root, "release")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("invalid")]
    [InlineData("oversized")]
    public async Task ExplicitMetadataGateFailsClosedWithoutValidBoundedRequestScope(string scope)
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-fake-metadata-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (scope != "missing")
            await File.WriteAllTextAsync(Path.Combine(root, "request-prefix.txt"), scope switch
            {
                "empty" => " \r\n",
                "invalid" => "not-an-absolute-http-url",
                _ => "https://example.test/" + new string('x', 4096)
            });
        try
        {
            // Even a nonmatching LIVE request must not silently accept a broken
            // explicit gate configuration and let the harness capture another task.
            var result = await RunMetadataAsync(root, "https://example.test/live/active");
            Assert.Equal(75, result.ExitCode); Assert.Equal("", result.Output);
            Assert.Contains("Missing or invalid owned QA metadata request prefix", result.Error);
            Assert.Empty(Directory.EnumerateFiles(root, "*.ready.json"));
            Assert.False(File.Exists(Path.Combine(root, "release")));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunMetadataAsync(string gate, string url)
    {
        var start = new ProcessStartInfo(CoreTests.FakeToolPath())
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--dump-single-json"); start.ArgumentList.Add(url);
        start.Environment["MTD_QA_FAKE_METADATA_GATE"] = gate;
        start.Environment.Remove("MTD_FAKE_METADATA_DELAY_MS");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            return (process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await stdout; await stderr;
        }
    }
}
