using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class ToolManagerTests
{
    [Fact]
    public async Task MissingTools_AreDownloadedValidatedAndActivated()
    {
        await using var harness = await ToolHarness.CreateAsync();

        await harness.Manager.Initialization;

        Assert.True(harness.Manager.YtDlp.Installed, harness.Manager.YtDlp.ErrorMessage);
        Assert.True(harness.Manager.Ffmpeg.Installed, harness.Manager.Ffmpeg.ErrorMessage);
        Assert.True(harness.Manager.Deno.Installed, harness.Manager.Deno.ErrorMessage);
        Assert.Equal(ToolStatus.Ready, harness.Manager.YtDlp.Status);
        Assert.Equal(ToolStatus.Ready, harness.Manager.Ffmpeg.Status);
        Assert.Equal(ToolStatus.Ready, harness.Manager.Deno.Status);
        Assert.Equal(DownloadEngineStatus.Ready, harness.Manager.EngineStatus);
        Assert.True(File.Exists(harness.Manager.YtDlp.ExecutablePath));
        Assert.True(File.Exists(harness.Manager.Ffmpeg.ExecutablePath));
        Assert.True(File.Exists(harness.Manager.Ffmpeg.FfprobePath));
        Assert.True(File.Exists(harness.Manager.Deno.ExecutablePath));
        Assert.Equal(3, harness.Source.CallCount);
        Assert.True(File.Exists(harness.Paths.ToolStateFile));
    }

    [Fact]
    public async Task ExistingValidInstall_IsReusedWithoutDownload()
    {
        var root = ToolHarness.NewRoot();
        await using var first = await ToolHarness.CreateAsync(root);
        await first.Manager.Initialization;
        await first.Manager.StopAsync();
        var persisted = await File.ReadAllTextAsync(AppPaths.Create(root).ToolStateFile);
        Assert.Contains("lastToolUpdateCheck", persisted, StringComparison.OrdinalIgnoreCase);

        await using var second = await ToolHarness.CreateAsync(root);
        await second.Manager.Initialization;

        Assert.True(second.Manager.YtDlp.Installed, second.Manager.YtDlp.ErrorMessage);
        Assert.True(second.Manager.Ffmpeg.Installed, second.Manager.Ffmpeg.ErrorMessage);
        Assert.True(second.Manager.Deno.Installed, second.Manager.Deno.ErrorMessage);
        Assert.Equal(ToolStatus.Ready, second.Manager.YtDlp.Status);
        Assert.Equal(ToolStatus.Ready, second.Manager.Ffmpeg.Status);
        Assert.Equal(ToolStatus.Ready, second.Manager.Deno.Status);
        Assert.Equal(DownloadEngineStatus.Ready, second.Manager.EngineStatus);
        Assert.Equal(0, second.Source.CallCount);
        Assert.Equal(0, second.Handler.RequestCount);
    }

    [Fact]
    public async Task AvailableUpdate_IsDownloadedAndActivated()
    {
        await using var harness = await ToolHarness.CreateAsync();
        await harness.Manager.Initialization;
        var oldPath = harness.Manager.YtDlp.ExecutablePath;
        harness.SetYtDlpRelease("v2", "yt-v2");

        await harness.Manager.CheckForUpdatesAsync();

        Assert.NotEqual(oldPath, harness.Manager.YtDlp.ExecutablePath);
        Assert.Contains("yt-v2", harness.Manager.YtDlp.InstalledVersion);
        Assert.Equal(ToolStatus.Ready, harness.Manager.YtDlp.Status);
    }

    [Fact]
    public async Task InterruptedDownload_LeavesNoActivePartialInstall()
    {
        await using var harness = await ToolHarness.CreateAsync();
        harness.Handler.InterruptedUris.Add(harness.Source.YtDlp.DownloadUri);

        await harness.Manager.Initialization;

        Assert.False(harness.Manager.YtDlp.Installed);
        Assert.Equal(ToolStatus.Failed, harness.Manager.YtDlp.Status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(harness.Paths.ToolStagingDirectory));
    }

    [Fact]
    public async Task FailedDownload_HttpErrorIsReportedWithoutInstallingPayload()
    {
        await using var harness = await ToolHarness.CreateAsync();
        harness.Handler.Payloads.Remove(harness.Source.YtDlp.DownloadUri);

        await harness.Manager.Initialization;

        Assert.False(harness.Manager.YtDlp.Installed);
        Assert.Equal(ToolStatus.Failed, harness.Manager.YtDlp.Status);
        Assert.Contains("404", harness.Manager.YtDlp.ErrorMessage);
        Assert.Empty(Directory.EnumerateFileSystemEntries(harness.Paths.ToolStagingDirectory));
    }

    [Fact]
    public async Task Sha256Mismatch_IsRejectedBeforeValidation()
    {
        await using var harness = await ToolHarness.CreateAsync();
        harness.Source.YtDlp = harness.Source.YtDlp with { Sha256 = new string('0', 64) };

        await harness.Manager.Initialization;

        Assert.Equal(ToolStatus.Failed, harness.Manager.YtDlp.Status);
        Assert.Contains("SHA-256", harness.Manager.YtDlp.ErrorMessage);
        Assert.DoesNotContain(ExternalToolKind.YtDlp, harness.Validator.ValidatedKinds);
    }

    [Fact]
    public async Task BinaryValidationFailure_DoesNotActivatePackage()
    {
        await using var harness = await ToolHarness.CreateAsync();
        harness.SetYtDlpRelease("invalid", "invalid-binary");

        await harness.Manager.Initialization;

        Assert.False(harness.Manager.YtDlp.Installed);
        Assert.Equal(ToolStatus.Failed, harness.Manager.YtDlp.Status);
        Assert.Null(harness.Manager.YtDlp.ExecutablePath);
    }

    [Fact]
    public async Task FailedUpdate_KeepsPreviousWorkingVersion()
    {
        await using var harness = await ToolHarness.CreateAsync();
        await harness.Manager.Initialization;
        var oldPath = harness.Manager.YtDlp.ExecutablePath;
        var oldVersion = harness.Manager.YtDlp.InstalledVersion;
        harness.SetYtDlpRelease("invalid-v2", "invalid-update");

        await harness.Manager.CheckForUpdatesAsync();

        Assert.True(harness.Manager.YtDlp.Installed);
        Assert.Equal(oldPath, harness.Manager.YtDlp.ExecutablePath);
        Assert.Equal(oldVersion, harness.Manager.YtDlp.InstalledVersion);
        Assert.True(File.Exists(oldPath));
        Assert.Equal(ToolStatus.Failed, harness.Manager.YtDlp.Status);
        Assert.Contains("remains active", harness.Manager.YtDlp.ErrorMessage);
        Assert.Equal(DownloadEngineStatus.Degraded, harness.Manager.EngineStatus);
    }

    [Fact]
    public async Task MissingFfmpeg_AllowsAnalysisCapabilitiesButRejectsOnlyFfmpegLease()
    {
        await using var harness = await ToolHarness.CreateAsync();
        harness.SetFfmpegRelease("invalid-ffmpeg", "invalid-ffmpeg", "ffprobe");

        await harness.Manager.Initialization;

        Assert.True(harness.Manager.CanAnalyze);
        Assert.False(harness.Manager.CanMerge);
        Assert.Equal(DownloadEngineStatus.Degraded, harness.Manager.EngineStatus);
        await using var ytDlp = await harness.Manager.AcquireAsync(ExternalToolKind.YtDlp);
        await using var deno = await harness.Manager.AcquireAsync(ExternalToolKind.Deno);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Manager.AcquireAsync(ExternalToolKind.Ffmpeg));
    }

    [Fact]
    public async Task CustomOverrides_AreValidatedButNeverDownloadedOrUpdated()
    {
        var root = ToolHarness.NewRoot();
        var customDirectory = Path.Combine(root, "custom");
        Directory.CreateDirectory(customDirectory);
        var yt = Path.Combine(customDirectory, "yt-dlp.exe");
        var ffmpeg = Path.Combine(customDirectory, "ffmpeg.exe");
        var ffprobe = Path.Combine(customDirectory, "ffprobe.exe");
        var deno = Path.Combine(customDirectory, "deno.exe");
        await File.WriteAllTextAsync(yt, "custom-yt");
        await File.WriteAllTextAsync(ffmpeg, "custom-ffmpeg");
        await File.WriteAllTextAsync(ffprobe, "custom-ffprobe");
        await File.WriteAllTextAsync(deno, "deno 2.3.0");
        await using var harness = await ToolHarness.CreateAsync(root, settings =>
        {
            settings.UseCustomYtDlp = true;
            settings.CustomYtDlpPath = yt;
            settings.UseCustomFfmpeg = true;
            settings.CustomFfmpegPath = ffmpeg;
            settings.CustomFfprobePath = ffprobe;
            settings.UseCustomDeno = true;
            settings.CustomDenoPath = deno;
        });

        await harness.Manager.Initialization;
        await harness.Manager.CheckForUpdatesAsync();

        Assert.False(harness.Manager.YtDlp.ManagedByApplication);
        Assert.False(harness.Manager.Ffmpeg.ManagedByApplication);
        Assert.False(harness.Manager.Deno.ManagedByApplication);
        Assert.Equal(0, harness.Source.CallCount);
        Assert.Equal(0, harness.Handler.RequestCount);
        Assert.Equal("custom-yt", await File.ReadAllTextAsync(yt));
    }

    [Fact]
    public async Task UpdateWhileToolIsBusy_IsActivatedAfterLeaseEnds()
    {
        await using var harness = await ToolHarness.CreateAsync();
        await harness.Manager.Initialization;
        var oldPath = harness.Manager.YtDlp.ExecutablePath;
        await using var lease = await harness.Manager.AcquireAsync(ExternalToolKind.YtDlp);
        harness.SetYtDlpRelease("v2-busy", "yt-v2-busy");

        await harness.Manager.CheckForUpdatesAsync();

        Assert.Equal(ToolStatus.UpdatePending, harness.Manager.YtDlp.Status);
        Assert.Equal(oldPath, harness.Manager.YtDlp.ExecutablePath);
        await lease.DisposeAsync();
        await WaitUntilAsync(() => harness.Manager.YtDlp.Status == ToolStatus.Ready, TimeSpan.FromSeconds(5));
        Assert.NotEqual(oldPath, harness.Manager.YtDlp.ExecutablePath);
        Assert.Equal(oldPath, lease.ExecutablePath);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!predicate()) await Task.Delay(20, cancellation.Token);
    }

    private sealed class ToolHarness : IAsyncDisposable
    {
        private readonly HttpClient client;
        private readonly string root;

        private ToolHarness(string root, AppPaths paths, MemoryHandler handler, MutableReleaseSource source, FakeValidator validator, HttpClient client, ToolManager manager)
        {
            this.root = root;
            Paths = paths;
            Handler = handler;
            Source = source;
            Validator = validator;
            this.client = client;
            Manager = manager;
        }

        public AppPaths Paths { get; }
        public MemoryHandler Handler { get; }
        public MutableReleaseSource Source { get; }
        public FakeValidator Validator { get; }
        public ToolManager Manager { get; }

        public static string NewRoot() => Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.ToolManager.Tests", Guid.NewGuid().ToString("N"));

        public static async Task<ToolHarness> CreateAsync(string? root = null, Action<Settings.AppSettings>? configure = null)
        {
            root ??= NewRoot();
            var paths = AppPaths.Create(root);
            paths.EnsureCreated();
            var logger = new NullAppLogger();
            var settings = new SettingsService(paths, logger);
            await settings.LoadAsync();
            configure?.Invoke(settings.Current);
            await settings.SaveAsync();
            var handler = new MemoryHandler();
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var source = new MutableReleaseSource();
            var validator = new FakeValidator();
            var harness = new ToolHarness(root, paths, handler, source, validator, client,
                new ToolManager(paths, settings, source, new ToolPackageInstaller(paths, new ToolDownloadService(client), validator), validator, logger));
            harness.SetYtDlpRelease("v1", "yt-v1");
            harness.SetFfmpegRelease("ff-v1", "ffmpeg-v1", "ffprobe-v1");
            harness.SetDenoRelease("deno-v1", "deno 2.3.0");
            return harness;
        }

        public void SetYtDlpRelease(string identity, string content)
        {
            var uri = new Uri($"https://assets.test/yt-dlp-{identity}.exe");
            var bytes = Encoding.UTF8.GetBytes(content);
            Handler.Payloads[uri] = bytes;
            Source.YtDlp = new ToolReleaseInfo(ExternalToolKind.YtDlp, identity, identity, uri, "yt-dlp.exe", Sha(bytes), bytes.Length, DateTimeOffset.UtcNow, false);
        }

        public void SetFfmpegRelease(string identity, string ffmpeg, string ffprobe)
        {
            var uri = new Uri($"https://assets.test/ffmpeg-{identity}.zip");
            var bytes = CreateZip(ffmpeg, ffprobe);
            Handler.Payloads[uri] = bytes;
            Source.Ffmpeg = new ToolReleaseInfo(ExternalToolKind.Ffmpeg, identity, identity, uri, "ffmpeg.zip", Sha(bytes), bytes.Length, DateTimeOffset.UtcNow, true);
        }

        public void SetDenoRelease(string identity, string deno)
        {
            var uri = new Uri($"https://assets.test/deno-{identity}.zip");
            var bytes = CreateDenoZip(deno);
            Handler.Payloads[uri] = bytes;
            Source.Deno = new ToolReleaseInfo(ExternalToolKind.Deno, identity, identity, uri, "deno.zip", Sha(bytes), bytes.Length, DateTimeOffset.UtcNow, true);
        }

        public async ValueTask DisposeAsync()
        {
            await Manager.StopAsync();
            client.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        private static byte[] CreateZip(string ffmpeg, string ffprobe)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(archive, "ffmpeg-build/bin/ffmpeg.exe", ffmpeg);
                WriteEntry(archive, "ffmpeg-build/bin/ffprobe.exe", ffprobe);
            }
            return stream.ToArray();
        }

        private static byte[] CreateDenoZip(string deno)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                WriteEntry(archive, "deno.exe", deno);
            return stream.ToArray();
        }

        private static void WriteEntry(ZipArchive archive, string name, string value)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
            writer.Write(value);
        }

        private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed class MutableReleaseSource : IToolReleaseSource
    {
        public ToolReleaseInfo YtDlp { get; set; } = null!;
        public ToolReleaseInfo Ffmpeg { get; set; } = null!;
        public ToolReleaseInfo Deno { get; set; } = null!;
        public int CallCount { get; private set; }

        public Task<ToolReleaseInfo> GetLatestAsync(ExternalToolKind kind, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(kind switch
            {
                ExternalToolKind.YtDlp => YtDlp,
                ExternalToolKind.Ffmpeg => Ffmpeg,
                ExternalToolKind.Deno => Deno,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            });
        }
    }

    private sealed class FakeValidator : IToolBinaryValidator
    {
        public List<ExternalToolKind> ValidatedKinds { get; } = [];

        public async Task<string> ValidateAsync(ExternalToolKind kind, string executablePath, string? ffprobePath, CancellationToken cancellationToken = default)
        {
            ValidatedKinds.Add(kind);
            var value = await File.ReadAllTextAsync(executablePath, cancellationToken);
            if (value.Contains("invalid", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Synthetic binary validation failure.");
            if (kind == ExternalToolKind.Ffmpeg)
            {
                if (ffprobePath is null) throw new InvalidDataException("ffprobe missing");
                _ = await File.ReadAllTextAsync(ffprobePath, cancellationToken);
            }
            return value.TrimStart('\uFEFF');
        }
    }

    private sealed class MemoryHandler : HttpMessageHandler
    {
        public Dictionary<Uri, byte[]> Payloads { get; } = [];
        public HashSet<Uri> InterruptedUris { get; } = [];
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI missing.");
            if (!Payloads.TryGetValue(uri, out var payload)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            HttpContent content = InterruptedUris.Contains(uri)
                ? new StreamContent(new InterruptingStream(payload))
                : new ByteArrayContent(payload);
            content.Headers.ContentLength = payload.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class InterruptingStream(byte[] payload) : Stream
    {
        private readonly MemoryStream inner = new(payload);
        private bool firstRead = true;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => payload.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!firstRead) throw new IOException("Synthetic connection interruption after partial data.");
            firstRead = false;
            return inner.Read(buffer, offset, Math.Min(count, Math.Max(1, payload.Length / 2)));
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!firstRead) return ValueTask.FromException<int>(new IOException("Synthetic connection interruption after partial data."));
            firstRead = false;
            return inner.ReadAsync(buffer[..Math.Min(buffer.Length, Math.Max(1, payload.Length / 2))], cancellationToken);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
