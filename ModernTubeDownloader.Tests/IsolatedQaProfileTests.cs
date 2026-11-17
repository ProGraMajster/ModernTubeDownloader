using System.Diagnostics;
using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Tests;

public sealed class IsolatedQaProfileTests
{
    private const string Temp = @"C:\MtdQaFixture\Temp";
    private const string Profile = Temp + @"\mtd-automation-smoke-0123456789abcdef0123456789abcdef";

    [Fact]
    public void ExplicitFlagAndDirectTempChildInspectEveryAncestor()
    {
        var inspected = new List<string>();
        Assert.True(IsolatedQaProfile.IsEligible(Profile + "\\", "1", Temp + "\\", path =>
        { inspected.Add(path); return FileAttributes.Directory; }));
        Assert.Equal(Profile, inspected[0]);
        Assert.Equal(Temp, inspected[1]);
        Assert.Equal(@"C:\", inspected[^1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    public void NormalApplicationNeverSuppressesRegistration(string? requested)
    {
        Assert.False(IsolatedQaProfile.IsEligible(Profile, requested, Temp, _ => throw new InvalidOperationException("Must not inspect paths without explicit opt-in.")));
    }

    [Theory]
    [InlineData(Temp + @"\unrelated-profile")]
    [InlineData(Temp + @"\mtd-automation-smoke-")]
    [InlineData(Temp + @"\nested\mtd-automation-smoke-profile")]
    [InlineData(Temp + @"-outside\mtd-automation-smoke-profile")]
    [InlineData(@"C:\Other\mtd-automation-smoke-profile")]
    [InlineData(@"relative\mtd-automation-smoke-profile")]
    public void PrefixLookalikesAndOutsideOrNestedProfilesAreRejected(string profile)
    {
        Assert.False(IsolatedQaProfile.IsEligible(profile, "1", Temp, _ => throw new InvalidOperationException("Lexically ineligible paths must not be inspected.")));
    }

    [Theory]
    [InlineData(Profile)]
    [InlineData(Temp)]
    [InlineData(@"C:\MtdQaFixture")]
    public void ReparsePointAtProfileTempOrAncestorIsRejected(string reparsePath)
    {
        Assert.False(IsolatedQaProfile.IsEligible(Profile, "1", Temp,
            path => FileAttributes.Directory | (path == reparsePath ? FileAttributes.ReparsePoint : 0)));
    }

    [Fact]
    public void MissingUnreadableAndNonDirectoryPathsAreRejected()
    {
        Assert.False(IsolatedQaProfile.IsEligible(Profile, "1", Temp, _ => throw new DirectoryNotFoundException()));
        Assert.False(IsolatedQaProfile.IsEligible(Profile, "1", Temp, _ => throw new UnauthorizedAccessException()));
        Assert.False(IsolatedQaProfile.IsEligible(Profile, "1", Temp, _ => FileAttributes.Normal));
    }

    [Fact]
    public async Task RealOwnedTempDirectoryIsAcceptedButItsJunctionIsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-automation-smoke-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(Path.GetTempPath(), "mtd-automation-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.True(IsolatedQaProfile.IsEligible(root, "1", Path.GetTempPath(), File.GetAttributes));
            var start = new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/d /c mklink /J \"{link}\" \"{root}\"",
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await stderr + await stdout);
            Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
            Assert.False(IsolatedQaProfile.IsEligible(link, "1", Path.GetTempPath(), File.GetAttributes));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            // Only these two newly allocated fixture paths; no recursive delete
            // and never follow the junction into its target.
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root);
        }
    }
}
