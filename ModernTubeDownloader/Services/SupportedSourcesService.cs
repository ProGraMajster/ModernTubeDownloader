using System.Security.Cryptography;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

internal interface ISupportedSourcesRuntime
{
    event EventHandler? Changed;
    Task<ExtractorCatalogLease> AcquireAsync(CancellationToken token);
}
internal sealed class ExtractorCatalogLease(string fingerprint, string version, bool managed,
    Func<string, CancellationToken, Task<IReadOnlyList<string>>> run, Func<ValueTask> release) : IAsyncDisposable
{
    public string Fingerprint { get; } = fingerprint;
    public string Version { get; } = version;
    public bool Managed { get; } = managed;
    public Task<IReadOnlyList<string>> RunAsync(string option, CancellationToken token) => run(option, token);
    public ValueTask DisposeAsync() => release();
}

internal sealed class ToolExtractorCatalogRuntime(ToolManager tools, AsyncProcessRunner runner) : ISupportedSourcesRuntime
{
    private string observedIdentity = Identity(tools.YtDlp);
    public event EventHandler? Changed;
    public void Observe(object? sender, EventArgs e)
    {
        var identity = Identity(tools.YtDlp);
        if (Interlocked.Exchange(ref observedIdentity, identity) != identity) Changed?.Invoke(this, EventArgs.Empty);
    }
    private static string Identity(ToolInfo tool) => $"{tool.Installed}|{tool.ExecutablePath}|{tool.InstalledVersion}|{tool.ManagedByApplication}";
    public async Task<ExtractorCatalogLease> AcquireAsync(CancellationToken token)
    {
        var lease = await tools.AcquireAsync(ExternalToolKind.YtDlp, token).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(lease.ExecutablePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
            // Identity belongs to the acquired executable, not a later ToolManager snapshot.
            return new($"{lease.ExecutablePath}|{lease.Version}|{lease.Managed}|{hash}", lease.Version ?? "—",
                lease.Managed, async (option, cancellation) =>
                {
                    var result = await runner.RunAsync(new ProcessRunRequest(lease.ExecutablePath,
                        ["--ignore-config", "--encoding", "utf-8", option]), cancellation).ConfigureAwait(false);
                    if (!result.IsSuccess) throw new ExternalProcessException("yt-dlp extractor catalog failed.", result);
                    return result.StandardOutput;
                }, lease.DisposeAsync);
        }
        catch { await lease.DisposeAsync(); throw; }
    }
}

public sealed class SupportedSourcesService : IDisposable
{
    private readonly ISupportedSourcesRuntime runtime;
    private readonly Action? detach;
    private readonly IAppLogger? logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object sync = new();
    private SupportedSourcesCatalog? cache;
    private int generation;
    public event EventHandler? Changed;
    internal SupportedSourcesService(ISupportedSourcesRuntime runtime, Action? detach = null)
    { this.runtime = runtime; this.detach = detach; runtime.Changed += Invalidate; }
    public SupportedSourcesService(ToolManager tools, AsyncProcessRunner runner, IAppLogger? logger = null)
    {
        this.logger = logger;
        var adapter = new ToolExtractorCatalogRuntime(tools, runner);
        runtime = adapter;
        tools.Changed += adapter.Observe;
        detach = () => tools.Changed -= adapter.Observe;
        runtime.Changed += Invalidate;
    }
    private void Invalidate(object? sender, EventArgs e)
    {
        lock (sync) { cache = null; generation++; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task<SupportedSourcesCatalog> GetAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                int current;
                lock (sync) current = generation;
                SupportedSourcesCatalog result;
                await using (var lease = await runtime.AcquireAsync(token).ConfigureAwait(false))
                {
                    SupportedSourcesCatalog? existing;
                    lock (sync)
                    {
                        existing = cache?.Fingerprint == lease.Fingerprint ? cache : null;
                        if (existing is null) cache = null;
                    }
                    if (existing is not null) result = existing;
                    else
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(45));
                        var list = await lease.RunAsync("--list-extractors", timeout.Token).ConfigureAwait(false);
                        var descriptions = await lease.RunAsync("--extractor-descriptions", timeout.Token).ConfigureAwait(false);
                        result = new(lease.Version, lease.Managed, lease.Fingerprint, Parse(list, descriptions));
                        if (result.Sources.Count == 0) throw new InvalidDataException("yt-dlp returned an empty extractor list.");
                    }
                }
                // Releasing a lease can activate a pending update. Never return its old catalog.
                lock (sync)
                {
                    if (current != generation) continue;
                    cache = result;
                    return result;
                }
            }
            throw new InvalidOperationException("The active yt-dlp changed repeatedly while reading its catalog.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            logger?.Warning("The yt-dlp source catalog command timed out.");
            throw new TimeoutException("The yt-dlp source catalog command timed out.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger?.Error("Could not read the active yt-dlp source catalog.", ex); throw; }
        finally { gate.Release(); }
    }
    public static IReadOnlyList<SupportedSource> Parse(IEnumerable<string> list, IEnumerable<string> descriptions)
    {
        const string brokenMarker = " (CURRENTLY BROKEN)";
        var records = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in list)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var broken = line.EndsWith(brokenMarker, StringComparison.Ordinal);
            var name = broken ? line[..^brokenMarker.Length] : line;
            records[name] = records.GetValueOrDefault(name) || broken;
        }
        // Names themselves may contain colons. Match the longest exact known prefix.
        var names = records.Keys.OrderByDescending(name => name.Length).ToArray();
        var detail = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in descriptions)
        {
            var line = raw.Trim();
            var name = names.FirstOrDefault(key => line.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith(key + ": ", StringComparison.OrdinalIgnoreCase));
            if (name is not null && line.Length > name.Length + 2) detail[name] = line[(name.Length + 2)..].Trim();
        }
        return records.Select(pair =>
        {
            var identity = ExtractorIdentityService.Identify(pair.Key);
            var verification = VerifiedSourcesRegistry.Get(pair.Key);
            return new SupportedSource(pair.Key, identity.DisplayName, detail.GetValueOrDefault(pair.Key) ?? string.Empty,
                identity.DisplayName, identity.IsGeneric, pair.Value, ExtractorIdentityService.IsPopular(pair.Key),
                pair.Value || identity.IsGeneric ? SourceVerificationStatus.Limited
                    : verification.Count > 0 ? SourceVerificationStatus.VerifiedByMtd : SourceVerificationStatus.YtDlpSupported,
                verification);
        }).ToArray();
    }
    public static IReadOnlyList<SupportedSource> Search(IEnumerable<SupportedSource> sources, string? query)
    {
        var search = query?.Trim() ?? string.Empty;
        int Rank(SupportedSource source) => source.ExtractorKey.Equals(search, StringComparison.OrdinalIgnoreCase) ||
            source.DisplayName.Equals(search, StringComparison.OrdinalIgnoreCase) ? 0 :
            source.ExtractorKey.StartsWith(search, StringComparison.OrdinalIgnoreCase) ||
            source.DisplayName.StartsWith(search, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        return sources.Where(source => search.Length == 0 || new[] { source.DisplayName, source.ExtractorKey, source.Description }
                .Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(source => search.Length > 0 ? Rank(source) : source.IsGeneric ? 3 :
                source.VerificationStatus == SourceVerificationStatus.VerifiedByMtd ? 0 : source.IsPopular ? 1 : 2)
            .ThenBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.ExtractorKey, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public void Dispose() { runtime.Changed -= Invalidate; detach?.Invoke(); }
}
