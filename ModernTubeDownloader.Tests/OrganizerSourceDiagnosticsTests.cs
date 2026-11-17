using System.Security.Cryptography;
using System.Text.Json;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Organizer;
using Fixture = ModernTubeDownloader.Tests.OrganizerTests.Fixture;

namespace ModernTubeDownloader.Tests;

public sealed class OrganizerSourceDiagnosticsTests
{
    private static string Id(int value) => $"QaClip{value:D5}";
    private static string Url(int value) => "https://www.youtube.com/watch?v=" + Id(value);

    [Fact]
    public async Task UnrelatedMetadataPairsAreExplainedWithoutAuthorizingTheirMove()
    {
        await using var f = new Fixture();
        // Real sidecar shapes, synthetic IDs/content, no production profile or media.
        for (var i = 0; i < 59; i++)
        {
            var path = Path.Combine(f.Source, $"Same title [{Id(i)}].mp4");
            await File.WriteAllBytesAsync(path, BitConverter.GetBytes(i));
            await File.WriteAllTextAsync(Path.ChangeExtension(path, ".json"), JsonSerializer.Serialize(new
            {
                id = Id(i), webpage_url = Url(i), original_url = Url(i), extractor_key = "Youtube",
                title = "Same title", requested_downloads = new[] { new { filepath = path } },
                retained_unknown = new { language = "日本語", values = new[] { i, 7 } }
            }));
        }
        var before = Directory.GetFiles(f.Source).ToDictionary(path => path,
            path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        var list = Path.Combine(f.Root, "synthetic-source-list.txt");
        await File.WriteAllLinesAsync(list, new[] { Url(0) }.Concat(Enumerable.Range(100, 117).Select(Url))
            .Concat(Enumerable.Range(100, 4).Select(Url)));
        var service = f.Service();
        var plan = await service.BuildPlanAsync([new("synthetic-source-list.txt", f.Destination, list)], f.Source, true);

        Assert.Equal(122, plan.Matches.Count);
        Assert.Equal(118, plan.ScannedFiles);
        Assert.Equal(59, plan.ScannedMediaFiles);
        Assert.Equal(59, plan.IdentifiedMediaFiles);
        Assert.Equal(58, plan.UnmatchedIdentifiedMediaFiles);
        Assert.Equal(0, plan.UnidentifiedMediaFiles);
        Assert.Equal(117, plan.Matches.Count(match => match.Status == OrganizerMatchStatus.Missing));
        Assert.Equal(4, plan.Matches.Count(match => match.Status == OrganizerMatchStatus.Duplicate));
        var found = Assert.Single(plan.Matches, match => match.Status == OrganizerMatchStatus.Found);
        Assert.Equal(Id(0), found.VerifiedVideoId);
        Assert.Equal(2, found.Files.Count);
        Assert.All(plan.Matches.Where(match => match.Status == OrganizerMatchStatus.Missing), match =>
        {
            Assert.Empty(match.Candidates);
            Assert.Empty(match.Files);
        });
        Assert.False(Directory.Exists(f.Destination));
        await Assert.ThrowsAsync<IOException>(() => service.MoveSelectedAsync(plan, plan.Matches.Select(match => match.Id).ToArray()));
        Assert.Empty(await service.JournalsAsync());

        var journal = await service.MoveSelectedAsync(plan, [found.Id]);
        Assert.Equal(OrganizerJournalState.Completed, journal.State);
        foreach (var file in found.Files)
            Assert.Equal(before[file.Source], Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file.Destination))));
        await service.UndoAsync(journal.Id);
        Assert.All(before, file => Assert.Equal(file.Value, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.Key)))));
        Assert.Empty(f.History.Snapshot());
        Assert.Empty(f.Queue.Snapshot());
    }

    [Fact]
    public async Task ScanCountsFilesOnceAcrossCanonicalReferencesAndDuplicateLists()
    {
        await using var f = new Fixture();
        await f.SeedAsync(1); // The same file is indexed from History, queue and sidecar.
        await f.SeedAsync(2);
        await File.WriteAllBytesAsync(Path.Combine(f.Source, "unidentified.mp4"), [5, 6, 7]);
        await File.WriteAllTextAsync(Path.Combine(f.Source, "unidentified.json"), "{corrupt");
        var service = f.Service();
        var plan = await service.BuildPlanAsync([
            new("first", f.Destination, Entries: [new() { Url = Fixture.Url(1) }]),
            new("second", Path.Combine(f.Root, "second"), Entries: [new() { Url = Fixture.Url(1) }])
        ], f.Source, true);

        Assert.Equal(3, plan.ScannedMediaFiles);
        Assert.Equal(2, plan.IdentifiedMediaFiles);
        Assert.Equal(1, plan.UnmatchedIdentifiedMediaFiles);
        Assert.Equal(1, plan.UnidentifiedMediaFiles);
        Assert.All(plan.Matches, match => Assert.Equal(OrganizerMatchStatus.Conflict, match.Status));
        Assert.Empty(await service.JournalsAsync());
        Assert.False(Directory.Exists(f.Destination));
    }
}
