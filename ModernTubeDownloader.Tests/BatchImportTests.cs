using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Xunit.Abstractions;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class BatchImportTests(ITestOutputHelper output)
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mtd-batch-test-" + Guid.NewGuid().ToString("N"));
        public AppPaths Paths { get; }
        public BatchSessionStore Store { get; }
        public SettingsService Settings { get; }
        public DownloadQueueService Queue { get; }
        public Fixture()
        {
            Paths = AppPaths.Create(Root); Paths.EnsureCreated(); Store = new(Path.Combine(Root, "Batch"));
            Settings = new(Paths, new NullAppLogger()); Queue = new(new QueuePersistenceService(Paths, new NullAppLogger()), new NullAppLogger(), Settings); Queue.SetPaused(true);
        }
        public BatchImportService Service(Func<string, CancellationToken, Task<SourceCheckResult>>? check = null)
            => new(Store, check ?? ((url, _) => Task.FromResult(new SourceCheckResult(url, Metadata(url), null, null))), Queue, Settings, new NullAppLogger());
        public async Task<string> File(string extension, string text)
        { var file = Path.Combine(Root, Guid.NewGuid().ToString("N") + extension); await System.IO.File.WriteAllTextAsync(file, text, Encoding.UTF8); return file; }
        public void Dispose() => Directory.Delete(Root, true);
    }
    private static VideoMetadata Metadata(string url) => new()
    {
        Id = new Uri(url).AbsolutePath.Trim('/'), WebpageUrl = url, Title = "Żółć 世界", DurationSeconds = 100,
        Formats = [new() { Id = "18", Extension = "mp4", Height = 360, VideoCodec = "h264", AudioCodec = "aac" }]
    };
    [Theory]
    [InlineData(10)] [InlineData(10000)] [InlineData(50000)]
    public async Task TxtImportIsDurableAndPagesRemainBounded(int count)
    {
        using var f = new Fixture(); using var service = f.Service();
        var file = await f.File(".txt", "# comment\n\n" + string.Join('\n', Enumerable.Range(0, count).Select(i => "https://example.test/video/" + i)));
        var timer = Stopwatch.StartNew(); var session = await service.ImportAsync(file); var importMilliseconds = timer.Elapsed.TotalMilliseconds; System.IO.File.Delete(file);
        Assert.Equal(count, session.ImportedCount); Assert.False(service.IsAnalyzing); Assert.Empty(f.Queue.Snapshot());
        var page = service.Page(session, BatchFilter.All, "", 90000); Assert.InRange(page.Entries.Count, 1, 20);
        timer.Restart(); var reopened = await f.Store.LoadAsync(session.Id); var loadMilliseconds = timer.Elapsed.TotalMilliseconds; Assert.Equal(count, reopened.Snapshot().Count); Assert.True(reopened.Paused);
        timer.Restart(); var filtered = service.Page(session, BatchFilter.All, "999", 0); var filterMilliseconds = timer.Elapsed.TotalMilliseconds;
        if (count == 50000) { Assert.True(filtered.Total > 0); output.WriteLine($"Indicative single-run 50k TXT: import={importMilliseconds:F1}ms load={loadMilliseconds:F1}ms filter/page={filterMilliseconds:F1}ms; rows/controls per page <=20."); }
    }
    [Fact]
    public async Task CsvParsesQuotesCommasUnicodeAndRanges()
    {
        using var f = new Fixture(); using var service = f.Service();
        var session = await service.ImportAsync(await f.File(".csv", "url,quality,container,start,end\n\"https://example.test/Żółć?q=a,b\",360p,mp4,2,10\nhttps://example.test/two,bogus,mp4,,\n"));
        var rows = session.Snapshot(); Assert.Equal(2, rows.Count); Assert.Equal("360", rows[0].Quality); Assert.Equal(TimeSpan.FromSeconds(2), rows[0].Range.Start);
        Assert.Equal(BatchEntryStatus.Failed, rows[1].Status);
    }
    [Theory]
    [InlineData("title,url\na,b")] [InlineData("url,url\na,b")] [InlineData("quality\nbest")]
    public async Task InvalidCsvHeadersAreRejected(string content)
    { using var f = new Fixture(); using var service = f.Service(); await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(f.File(".csv", content).Result)); }
    [Fact]
    public async Task JsonStreamsStringsObjectsAndRejectsExecutableSchema()
    {
        using var f = new Fixture(); using var service = f.Service();
        var session = await service.ImportAsync(await f.File(".json", """["https://example.test/a",{"url":"https://example.test/b","quality":"720","start":2,"end":10},{"url":"https://example.test/c","$type":"Injected"},{"url":123}]"""));
        Assert.Equal(4, session.ImportedCount); Assert.Equal(2, service.Page(session, BatchFilter.Errors, "", 0).Total);
    }
    [Theory]
    [InlineData("[\"https://example.test/a\"]")]
    [InlineData("[{\"url\":\"https://example.test/a\"}]")]
    [InlineData("{\"url\":\"https://example.test/a\"}")]
    [InlineData("{\"urls\":[\"https://example.test/a\"]}")]
    [InlineData("{\"items\":[{\"url\":\"https://example.test/a\"}]}")]
    public async Task JsonRootShapesUseTheSameBoundedPreparationRows(string json)
    {
        using var f = new Fixture(); using var service = f.Service();
        var session = await service.ImportAsync(await f.File(".json", json));
        Assert.Equal(1, session.ImportedCount);
        Assert.Equal(0, service.Page(session, BatchFilter.Errors, "", 0).Total);
        Assert.Empty(f.Queue.Snapshot());
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"title\":\"not an import\"}")]
    [InlineData("{\"url\":123}")]
    [InlineData("{\"urls\":\"wrong\"}")]
    [InlineData("{\"items\":[],\"urls\":[]}")]
    [InlineData("{\"urls\":[],\"$type\":\"Injected\"}")]
    [InlineData("\"https://example.test/not-array\"")]
    [InlineData("[{\"url\":")]
    [InlineData("[] true")]
    public async Task UnsupportedOrMalformedJsonHasLocalizedSafeMessageAndTechnicalInnerException(string json)
    {
        using var f = new Fixture(); using var service = f.Service();
        var error = await Assert.ThrowsAsync<BatchJsonFormatException>(() => service.ImportAsync(f.File(".json", json).Result));
        Assert.Equal("Batch.Error.JsonFormat", error.Message);
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
        foreach (var language in new[] { "en", "pl" })
        {
            var text = new ModernTubeDownloader.Localization.LocalizationService(language)[error.Message];
            Assert.DoesNotContain("List`1", text); Assert.DoesNotContain("not-array", text);
            Assert.DoesNotContain("[Batch.", text);
        }
    }
    [Fact]
    public async Task EnvelopeStreamsFiftyThousandRowsAndMixedRowErrorsWithoutRelaxingBounds()
    {
        using var f = new Fixture(); using var service = f.Service();
        var json = "{\"urls\":" + JsonSerializer.Serialize(Enumerable.Range(0, 50000).Select(i => "https://example.test/" + i)) + "}";
        var session = await service.ImportAsync(await f.File(".json", json));
        Assert.Equal(50000, session.ImportedCount);
        var mixed = await service.ImportAsync(await f.File(".json", """{"items":[{"url":"https://example.test/ok"},{"title":"missing"},123,{"url":true}]}"""));
        Assert.Equal(4, mixed.ImportedCount); Assert.Equal(3, service.Page(mixed, BatchFilter.Errors, "", 0).Total);
    }
    [Fact]
    public async Task ExamplesAreExecutableSchemasAndUtf8BomRemainsSupported()
    {
        using var f = new Fixture(); using var service = f.Service();
        foreach (var example in BatchJsonExamples.Documents)
            Assert.True((await service.ImportAsync(await f.File(".json", example))).ImportedCount > 0);
        // The fixture's Encoding.UTF8 already writes the BOM; do not add a second one.
        var bom = await f.File(".json", "{\"url\":\"https://example.test/bom\"}");
        Assert.Equal(1, (await service.ImportAsync(bom)).ImportedCount);
    }
    [Fact]
    public async Task DeepJsonEnvelopeIsRejectedWithoutDynamicDeserialization()
    {
        using var f = new Fixture(); using var service = f.Service();
        await Assert.ThrowsAsync<BatchJsonFormatException>(() => service.ImportAsync(f.File(".json", "{\"items\":[" + new string('[', 10) + "0" + new string(']', 10) + "]}").Result));
    }
    [Theory]
    [InlineData(10000)] [InlineData(50000)]
    public async Task LargeJsonDoesNotCreateUiOrAnalysisTasks(int count)
    {
        using var f = new Fixture(); using var service = f.Service();
        var session = await service.ImportAsync(await f.File(".json", JsonSerializer.Serialize(Enumerable.Range(0, count).Select(i => "https://example.test/" + i))));
        Assert.Equal(count, session.ImportedCount); Assert.False(service.IsAnalyzing); Assert.Equal(20, service.Page(session, BatchFilter.All, "", 0).Entries.Count);
    }
    [Theory]
    [InlineData(".txt", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 17000)]
    [InlineData(".json", "[\"", 17000)]
    public async Task OversizeRecordIsRejectedBeforeUnboundedDeserialization(string extension, string prefix, int count)
    { using var f = new Fixture(); using var service = f.Service(); await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(f.File(extension, prefix + new string('a', count) + "\"]").Result)); }
    [Fact]
    public async Task MultilineCsvRecordLimitIsBoundedBeforeTheCsvParserBuffersIt()
    {
        using var f = new Fixture(); using var service = f.Service();
        var file = await f.File(".csv", "url\n\"https://example.test/" + string.Concat(Enumerable.Repeat(new string('a', 1000) + "\n", 70)) + "\"\n");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(file));
    }
    [Fact]
    public async Task QueueHandoffUsesTheValidatedInputWhenMetadataOmitsWebpageUrl()
    {
        using var f = new Fixture(); using var service = f.Service((url, _) => { var metadata = Metadata(url); metadata.WebpageUrl = ""; return Task.FromResult(new SourceCheckResult(url, metadata, null, null)); });
        var session = await service.ImportAsync(await f.File(".txt", "https://example.test/one"));
        await service.StartAsync(session); Assert.Equal(1, await service.AddReadyAsync(session));
        Assert.Equal("https://example.test/one", f.Queue.Snapshot().Single().SourceUrl);
    }
    [Fact]
    public async Task AnalysisWorkersAreIndependentBoundedAndQueueHandoffIsExplicit()
    {
        using var f = new Fixture(); var current = 0; var maximum = 0;
        using var service = f.Service(async (url, token) => { var active = Interlocked.Increment(ref current); int observed; do { observed = maximum; } while (active > observed && Interlocked.CompareExchange(ref maximum, active, observed) != observed); await Task.Delay(20, token); Interlocked.Decrement(ref current); return new(url, Metadata(url), null, null); });
        var session = await service.ImportAsync(await f.File(".txt", string.Join('\n', Enumerable.Range(0, 30).Select(i => "https://example.test/" + i))), workers: 3);
        await service.StartAsync(session); Assert.InRange(maximum, 2, 3); Assert.Empty(f.Queue.Snapshot()); Assert.Equal(30, service.Page(session, BatchFilter.Ready, "", 0).Total);
        var selected = session.Snapshot().Take(3).Select(row => row.Id).ToHashSet(); Assert.Equal(3, await service.AddReadyAsync(session, selected)); Assert.Equal(0, await service.AddReadyAsync(session, selected));
        Assert.Equal(3, f.Queue.Snapshot().Count); await f.Queue.SaveAsync();
        var reopened = await f.Store.LoadAsync(session.Id); Assert.Equal(3, reopened.Snapshot().Count(row => row.QueueItemId is not null));
    }
    [Fact]
    public async Task PauseCancelResumeAndSessionRecoveryPreserveReadyRows()
    {
        using var f = new Fixture(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = f.Service(async (url, token) => { started.TrySetResult(); await Task.Delay(80, token); return new(url, Metadata(url), null, null); });
        var session = await service.ImportAsync(await f.File(".txt", string.Join('\n', Enumerable.Range(0, 15).Select(i => "https://example.test/" + i))), workers: 1);
        var operation = service.StartAsync(session); await started.Task; await service.SetPausedAsync(session, true); await service.SetPausedAsync(session, true);
        await Task.Delay(160); Assert.InRange(session.Snapshot().Count(row => row.Status == BatchEntryStatus.Ready), 0, 1);
        await service.CancelAsync(); await operation; Assert.True(session.Paused);
        await service.StartAsync(session); Assert.Equal(15, service.Page(session, BatchFilter.Ready, "", 0).Total);
    }
    [Fact]
    public async Task DedupUsesNormalizedSourceAndCanonicalQueueOptionsNotTitle()
    {
        using var f = new Fixture(); using var service = f.Service();
        var session = await service.ImportAsync(await f.File(".txt", "https://youtube.com/watch?v=same\nhttps://youtu.be/same\nhttps://youtube.com/live/same\nhttps://example.test/a\nhttps://example.test/b"));
        Assert.Equal(2, service.Page(session, BatchFilter.Duplicates, "", 0).Total);
        await service.StartAsync(session); Assert.Equal(3, service.Page(session, BatchFilter.Ready, "", 0).Total);
    }
    [Fact]
    public async Task MultiselectEditsRemovalFiltersAndExportsDoNotLeakQuerySecrets()
    {
        using var f = new Fixture(); using var service = f.Service(); var session = await service.ImportAsync(await f.File(".txt", "https://example.test/a?token=secret\nhttps://example.test/b"));
        var selected = session.Snapshot().Take(1).Select(row => row.Id).ToHashSet(); await service.EditAsync(session, selected, "360", PreferredVideoContainer.Mp4, new(MediaRangeMode.Custom, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)));
        await service.StartAsync(session); var export = Path.Combine(f.Root, "export.json"); await service.ExportAsync(session, export);
        Assert.DoesNotContain("secret", await System.IO.File.ReadAllTextAsync(export)); Assert.Equal(1, service.Page(session, BatchFilter.All, "/a", 0).Total);
        await service.EditAsync(session, selected, remove: true); Assert.Equal(1, service.Page(session, BatchFilter.All, "", 0).Total);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task AuthAndInteractiveLiveNeverEnterOrdinaryQueue(bool auth)
    {
        using var f = new Fixture(); using var service = f.Service((url, _) => Task.FromResult(new SourceCheckResult(url, Metadata(url), null, null, AuthenticationMayBeRequired: auth, OpenLive: !auth)));
        var session = await service.ImportAsync(await f.File(".txt", "https://example.test/live")); await service.StartAsync(session);
        Assert.Equal(auth ? BatchEntryStatus.AuthRequired : BatchEntryStatus.Unsupported, session.Snapshot().Single().Status); Assert.Equal(0, await service.AddReadyAsync(session));
    }
}
