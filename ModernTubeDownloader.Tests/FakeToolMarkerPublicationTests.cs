using System.Diagnostics;
using System.Globalization;

namespace ModernTubeDownloader.Tests;

public sealed class FakeToolMarkerPublicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartedMarkerBecomesVisibleOnlyAfterItsWriterCloses(bool terminateBeforePublication)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-marker-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, ".test-started.json");
        var staging = marker + ".tmp";
        var start = new ProcessStartInfo(CoreTests.FakeToolPath())
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("--qa-publish-started-marker");
        start.ArgumentList.Add(directory);
        start.Environment["MTD_QA_FAKE_MARKER_PUBLICATION"] = "1";
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var bound = TimeSpan.FromSeconds(5);
        try
        {
            Assert.Equal("STAGING_READY", await process.StandardOutput.ReadLineAsync().WaitAsync(bound));
            Assert.False(process.HasExited);
            Assert.True(File.Exists(staging));
            Assert.False(File.Exists(marker));
            Assert.Empty(Directory.EnumerateFiles(directory, ".test-started.json"));
            // The event is raised while the child actually holds the writer.
            // No sleep or polling is used to manufacture the race window.
            Assert.Throws<IOException>(() =>
            {
                using var conflicting = new FileStream(staging, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            });

            if (terminateBeforePublication)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(bound);
                Assert.False(File.Exists(marker));
                Assert.True(File.Exists(staging));
            }
            else
            {
                await process.StandardInput.WriteLineAsync("publish");
                await process.StandardInput.FlushAsync();
                Assert.Equal("PUBLISHED", await process.StandardOutput.ReadLineAsync().WaitAsync(bound));
                Assert.False(process.HasExited);
                Assert.False(File.Exists(staging));
                Assert.Equal(process.Id.ToString(CultureInfo.InvariantCulture), await File.ReadAllTextAsync(marker));
                using (var exclusive = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Assert.Equal(process.Id.ToString(CultureInfo.InvariantCulture).Length, exclusive.Length);
                await process.StandardInput.WriteLineAsync("exit");
                await process.StandardInput.FlushAsync();
                await process.WaitForExitAsync().WaitAsync(bound);
                Assert.Equal(0, process.ExitCode);
                Assert.Equal("", await error);
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(bound);
            }
            await error;
            // Retain the isolated files as evidence, especially the unpublished
            // staging file after a killed child. User profiles are never used.
        }
    }
}
