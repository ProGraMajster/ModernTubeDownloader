using System.Diagnostics;
using System.Security.Cryptography;
using ModernTubeDownloader.Organizer;
using Xunit.Abstractions;
using Fixture = ModernTubeDownloader.Tests.OrganizerTests.Fixture;

namespace ModernTubeDownloader.Tests;

public sealed class OrganizerPathRegressionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(@"C:\Downloads", @"C:\Downloads")]
    [InlineData(@"D:\YouTube", @"D:\YouTube")]
    [InlineData(@"E:\Music", @"E:\Music")]
    [InlineData(@"D:\Moje pliki\YouTube", @"D:\Moje pliki\YouTube")]
    [InlineData(@"D:\Pobrane\Muzyka polska", @"D:\Pobrane\Muzyka polska")]
    [InlineData(@"D:\Zażółć gęślą jaźń", @"D:\Zażółć gęślą jaźń")]
    [InlineData(@"D:\Downloads\", @"D:\Downloads")]
    [InlineData("D:/Downloads/Videos", @"D:\Downloads\Videos")]
    [InlineData(@"D:\Music\\2026.11 (Live)-日本語", @"D:\Music\2026.11 (Live)-日本語")]
    [InlineData("C:\\Cafe\u0301", "C:\\Cafe\u0301")]
    [InlineData("D:/", @"D:\")]
    public void ValidLocalPathsPreserveWindowsDirectoryNames(string input, string expected) =>
        Assert.Equal(expected, OrganizerPathSafety.LocalPath(input));

    [Theory]
    [InlineData(@"relative\folder", "PathType")]
    [InlineData(@"C:Downloads", "PathType")]
    [InlineData(@"\Downloads", "PathType")]
    [InlineData(@"\\server\share\folder", "PathType")]
    [InlineData("//server/share/folder", "PathType")]
    [InlineData(@"\\?\C:\Downloads", "PathType")]
    [InlineData(@"\\.\C:\Downloads", "PathType")]
    [InlineData(@"C:\Downloads\..\private", "PathTraversal")]
    [InlineData(@"C:\Downloads\.\videos", "PathTraversal")]
    [InlineData(@"C:\Downloads\COM¹.txt", "FolderName")]
    [InlineData(@"C:\Downloads\NUL .txt", "FolderName")]
    [InlineData(@"C:\Downloads\folder ", "FolderName")]
    [InlineData(@"C:\Downloads\folder:stream", "FolderName")]
    public void UnsupportedPathsAreRejectedBeforeFilesystemLookup(string input, string error) =>
        Assert.Equal("Organizer.Error." + error, Assert.Throws<IOException>(() => OrganizerPathSafety.LocalPath(input)).Message);

    [Fact]
    public async Task ExistingAndNewFoldersKeepExactUnicodeThroughMoveAndUndo()
    {
        await using var f = new Fixture();
        var recording = await f.SeedAsync(fileName: "Clip (version 2)-日本語.mp4");
        var parent = Directory.CreateDirectory(Path.Combine(f.Root, "Muzyka .polska (test) Cafe\u0301")).FullName;
        var longName = new string('z', 120);
        var destination = Path.Combine(parent, longName, longName, "Z\u0307o\u0301łc\u0301");
        var suggested = DownloadOrganizerService.SuggestDestination(parent, "Muzyka .");
        Assert.Equal(Path.Combine(parent, "Muzyka"), suggested);
        Assert.Equal(Path.Combine(parent, new string('a', 99)),
            DownloadOrganizerService.SuggestDestination(parent, new string('a', 99) + "🎵 playlist"));

        var sourcePath = Path.Combine(f.Root, "Wyjs\u0301cie");
        Directory.Move(f.Source, sourcePath);
        var movedSource = Path.Combine(sourcePath, Path.GetFileName(recording.FinalPath));
        // Update canonical references to the folder spelling used by the filesystem.
        await f.History.RelocatePathsAsync(new Dictionary<string, string> { [recording.FinalPath] = movedSource });
        await f.Queue.RelocateCompletedPathsAsync(new Dictionary<string, string> { [recording.FinalPath] = movedSource });
        var service = f.Service();
        var input = sourcePath.Replace('\\', '/') + "/";
        var plan = await service.BuildPlanAsync([new("Muzyka .txt", destination, Entries: [new() { Url = recording.SourceUrl }])], input, true);
        Assert.Equal(sourcePath, plan.ScanRoot);
        var match = Assert.Single(plan.Matches);
        Assert.Equal(OrganizerMatchStatus.Found, match.Status);
        Assert.False(Directory.Exists(destination));
        Assert.True(destination.Length > 260);
        var operation = await service.MoveSelectedAsync(plan, [match.Id]);
        Assert.All(match.Files, file => { Assert.False(File.Exists(file.Source)); Assert.Equal(file.Sha256, Hash(file.Destination)); });
        Assert.Equal(recording with { FinalPath = match.Files[0].Destination }, Assert.Single(f.History.Snapshot()));
        Assert.Equal(match.Files[0].Destination, Assert.Single(f.Queue.Snapshot()).FinalFile);
        Assert.False(Directory.Exists(parent.Normalize()));
        await service.UndoAsync(operation.Id);
        Assert.All(match.Files, file => Assert.Equal(file.Sha256, Hash(file.Source)));
        Assert.Equal(movedSource, Assert.Single(f.History.Snapshot()).FinalPath);
    }

    [Fact]
    public async Task FailingFieldAndListAreIdentifiedBeforeAnyMove()
    {
        await using var f = new Fixture();
        var service = f.Service();
        var input = new OrganizerSourceList("Archived playlist.txt", f.Destination, Entries: [new() { Url = Fixture.Url() }]);

        var missing = await Assert.ThrowsAsync<OrganizerPathException>(() =>
            service.BuildPlanAsync([input], Path.Combine(f.Root, "missing"), true));
        Assert.Equal("Organizer.Source", missing.ContextKey);
        Assert.Equal("Organizer.Error.FolderMissing", missing.Message);

        var occupied = Path.Combine(f.Root, "occupied");
        await File.WriteAllTextAsync(occupied, "keep this file");
        var invalid = await Assert.ThrowsAsync<OrganizerPathException>(() =>
            service.BuildPlanAsync([input with { DestinationDirectory = Path.Combine(occupied, "child") }], f.Source, true));
        Assert.Equal("Organizer.Destination", invalid.ContextKey);
        Assert.Equal(input.Name, invalid.ListName);
        Assert.Equal("Organizer.Error.NotDirectory", invalid.Message);

        var suggestion = Assert.Throws<OrganizerPathException>(() => DownloadOrganizerService.SuggestDestination(f.Root, "occupied"));
        Assert.Equal("Organizer.Error.ListFolder", suggestion.ContextKey);
        Assert.Equal("Organizer.Error.NotDirectory", suggestion.Message);
        Assert.Equal("keep this file", await File.ReadAllTextAsync(occupied));
        Assert.Empty(await service.JournalsAsync());

        var absentDrive = Enumerable.Range('A', 26).Select(letter => $"{(char)letter}:\\")
            .FirstOrDefault(drive => new DriveInfo(drive).DriveType == DriveType.NoRootDirectory);
        Assert.NotNull(absentDrive);
        var unavailable = await Assert.ThrowsAsync<OrganizerPathException>(() =>
            service.BuildPlanAsync([input with { DestinationDirectory = Path.Combine(absentDrive!, "Organized") }], f.Source, false));
        Assert.Equal("Organizer.Error.DriveUnavailable", unavailable.Message);
        Assert.IsAssignableFrom<IOException>(unavailable.InnerException?.InnerException);
        Assert.Equal("Organizer.Error.Permission", DownloadOrganizerService.SafeError(new UnauthorizedAccessException()));
    }

    [Fact]
    public async Task DriveRootAllowsTopLevelPlanningInAnIsolatedMapping()
    {
        await using var f = new Fixture();
        var recording = await f.SeedAsync(queue: false);
        var parent = Directory.CreateDirectory(Path.Combine(f.Root, "mapped disk"));
        File.Copy(recording.FinalPath, Path.Combine(parent.FullName, "video.mp4"));
        File.Copy(Path.ChangeExtension(recording.FinalPath, ".json"), Path.Combine(parent.FullName, "video.json"));
        var nested = parent.CreateSubdirectory("subfolder");
        File.Copy(recording.FinalPath, Path.Combine(nested.FullName, "video.mp4"));
        File.Copy(Path.ChangeExtension(recording.FinalPath, ".json"), Path.Combine(nested.FullName, "video.json"));

        var root = Enumerable.Range('R', 9).Reverse().Select(letter => $"{(char)letter}:\\")
            .First(drive => new DriveInfo(drive).DriveType == DriveType.NoRootDirectory);
        var letter = root[..2];
        await SubstAsync(letter, parent.FullName);
        try
        {
            var dest = DownloadOrganizerService.SuggestDestination(root, "Organized");
            Assert.Equal(root + "Organized", dest);
            Assert.True(OrganizerPathSafety.IsWithin(root + "video.mp4", root));
            Assert.False(OrganizerPathSafety.IsWithin(root, root));
            Assert.False(OrganizerPathSafety.IsWithin(root + "Organized Else\\video.mp4", dest));

            var list = new OrganizerSourceList("List", dest, Entries: [new() { Url = recording.SourceUrl }]);
            var service = f.Service();
            var recursive = await Assert.ThrowsAsync<OrganizerPathException>(() => service.BuildPlanAsync([list], root, true));
            Assert.Equal("Organizer.Error.RootRecursive", recursive.Message);
            var rootDestination = await Assert.ThrowsAsync<OrganizerPathException>(() =>
                service.BuildPlanAsync([list with { DestinationDirectory = root }], root, false));
            Assert.Equal("Organizer.Error.Root", rootDestination.Message);

            var plan = await service.BuildPlanAsync([list], root, false);
            var match = Assert.Single(plan.Matches);
            Assert.Equal(OrganizerMatchStatus.Found, match.Status);
            Assert.Equal(root + "video.mp4", match.MediaPath);
            var operation = await service.MoveSelectedAsync(plan, [match.Id]);
            Assert.All(match.Files, file => Assert.Equal(file.Sha256, Hash(file.Destination)));
            await service.UndoAsync(operation.Id);
            Assert.True(File.Exists(Path.Combine(nested.FullName, "video.mp4")));
        }
        finally { await SubstAsync(letter, "/D"); }
    }

    [Fact]
    public async Task VolumeRemovedAfterScanFailsTheWholePreview()
    {
        await using var f = new Fixture();
        var recording = await f.SeedAsync(queue: false);
        var root = Enumerable.Range('R', 9).Reverse().Select(letter => $"{(char)letter}:\\")
            .First(drive => new DriveInfo(drive).DriveType == DriveType.NoRootDirectory);
        var letter = root[..2];
        await SubstAsync(letter, f.Source);
        var removed = false;
        try
        {
            var service = f.Service();
            service.ProgressChanged += (_, progress) =>
            {
                if (progress.Phase != "Organizer.Phase.Matching" || removed) return;
                SubstAsync(letter, "/D").GetAwaiter().GetResult();
                removed = true;
            };
            var list = new OrganizerSourceList("Fixture", f.Destination, Entries: [new() { Url = recording.SourceUrl }]);
            var failure = await Assert.ThrowsAsync<OrganizerPathException>(() => service.BuildPlanAsync([list], root, false));
            Assert.True(removed);
            Assert.Equal("Organizer.Source", failure.ContextKey);
            Assert.Equal("Organizer.Error.DriveUnavailable", failure.Message);
            Assert.True(File.Exists(recording.FinalPath));
            Assert.False(Directory.Exists(f.Destination));

            await SubstAsync(letter, f.Source);
            try
            {
                var retry = await service.BuildPlanAsync([list with { RetryDestinationSuggestion = true, DestinationDirectory = "" }], root, false);
                Assert.Equal(Path.Combine(root, "Fixture"), Assert.Single(retry.ResolvedDestinations));
                Assert.Equal(OrganizerMatchStatus.Found, Assert.Single(retry.Matches).Status);
            }
            finally { await SubstAsync(letter, "/D"); }
        }
        finally { if (!removed) await SubstAsync(letter, "/D"); }
    }

    [Fact]
    public async Task PhysicalCrossVolumeMoveAndUndoWhenSecondFixtureVolumeIsConfigured()
    {
        var configured = Environment.GetEnvironmentVariable("MTD_TEST_ORGANIZER_SECOND_VOLUME");
        if (string.IsNullOrEmpty(configured))
        {
            output.WriteLine("Physical copy fixture requires MTD_TEST_ORGANIZER_SECOND_VOLUME.");
            return;
        }
        await using var f = new Fixture();
        Assert.False(OrganizerPathSafety.Comparer.Equals(Path.GetPathRoot(f.Source), Path.GetPathRoot(configured)));
        Assert.True(Directory.Exists(configured));
        var destination = Path.Combine(configured, "mtd-organizer-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var video = await f.SeedAsync(1);
            var audio = await f.SeedAsync(2, fileName: "same title.mp3");
            await File.WriteAllBytesAsync(Path.ChangeExtension(audio.FinalPath, ".jpg"), [1, 2, 3]);
            // Same basename in another playlist stays outside the selected recordings.
            var otherDirectory = Directory.CreateDirectory(Path.Combine(f.Source, "another playlist")).FullName;
            await File.WriteAllBytesAsync(Path.Combine(otherDirectory, "same title.mp4"), [4, 5, 6]);
            await File.WriteAllTextAsync(Path.Combine(otherDirectory, "same title.json"), Fixture.Metadata(3));

            var service = f.Service();
            var plan = await service.BuildPlanAsync([
                new("Playlist videos", Path.Combine(destination, "Videos"), Entries: [new() { Url = video.SourceUrl }, new() { Url = Fixture.Url(99) }]),
                new("Playlist audio", Path.Combine(destination, "Audio"), Entries: [new() { Url = audio.SourceUrl }])
            ], f.Source, true);
            Assert.Equal(1, plan.Matches.Count(match => match.Status == OrganizerMatchStatus.Missing));
            var selected = plan.Matches.Where(match => match.Status == OrganizerMatchStatus.Found).ToArray();
            Assert.Equal(2, selected.Length);
            var operation = await service.MoveSelectedAsync(plan, selected.Select(match => match.Id).ToArray());
            var files = selected.SelectMany(match => match.Files).ToArray();
            Assert.Equal(5, files.Length);
            Assert.All(files, file => { Assert.False(File.Exists(file.Source)); Assert.Equal(file.Sha256, Hash(file.Destination)); });
            await service.UndoAsync(operation.Id);
            Assert.All(files, file => { Assert.Equal(file.Sha256, Hash(file.Source)); Assert.False(File.Exists(file.Destination)); });
            Assert.Equal(audio, f.History.Snapshot().Single(item => item.QueueItemId == audio.QueueItemId));
            output.WriteLine($"Verified {files.Length} files across {Path.GetPathRoot(f.Source)} and {Path.GetPathRoot(destination)}.");
        }
        finally { if (Directory.Exists(destination)) Directory.Delete(destination, true); }
    }

    private static async Task SubstAsync(string letter, string path)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "subst.exe"))
            { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { letter, path } };
        using var proc = Process.Start(start)!;
        await proc.WaitForExitAsync();
        Assert.Equal(0, proc.ExitCode);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
