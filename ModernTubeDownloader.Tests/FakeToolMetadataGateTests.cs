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
}
