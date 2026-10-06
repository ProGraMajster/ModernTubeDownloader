using System.IO.Pipes;
using System.Text;

namespace ModernTubeDownloader.Infrastructure;

/// <summary>Production, per-user URL hand-off. Kept separate from the Debug automation bridge.</summary>
public sealed class ExternalLinkInstance : IDisposable
{
    private readonly Semaphore instanceGate;
    private readonly string pipeName;
    private readonly CancellationTokenSource stop = new();
    private Task? listener;
    private bool ownsInstance;

    public ExternalLinkInstance(string instanceName = "ModernTubeDownloader")
    {
        if (string.IsNullOrWhiteSpace(instanceName) || instanceName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Invalid instance name.", nameof(instanceName));
        pipeName = $"ProGraMajster.{instanceName}.ExternalLink";
        instanceGate = new Semaphore(1, 1, $"Local\\ProGraMajster.{instanceName}.SingleInstance");
        ownsInstance = instanceGate.WaitOne(0);
    }

    public bool IsPrimary => ownsInstance;

    public void Start(Func<ExternalMediaRequest, Task> onRequest, IAppLogger? logger = null)
    {
        if (!ownsInstance || listener is not null) throw new InvalidOperationException("Not an unstarted primary instance.");
        listener = ListenAsync(onRequest, logger);
    }

    private async Task ListenAsync(Func<ExternalMediaRequest, Task> onRequest, IAppLogger? logger)
    {
        while (!stop.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await server.WaitForConnectionAsync(stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var lengthBytes = new byte[4];
                await server.ReadExactlyAsync(lengthBytes, timeout.Token);
                var length = BitConverter.ToInt32(lengthBytes);
                if (length is <= 0 or > ExternalMediaRequest.MaximumProtocolLength) continue;
                var data = new byte[length];
                await server.ReadExactlyAsync(data, timeout.Token);
                var payload = new UTF8Encoding(false, true).GetString(data);
                if (!ExternalMediaRequest.TryParseProtocol(payload, out var request) || request is null) continue;
                await onRequest(request);
                await server.WriteAsync(new byte[] { 1 }, timeout.Token);
                await server.FlushAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                // A malformed or disconnected client must not terminate the listener.
                logger?.Warning($"External link IPC client was rejected: {error.GetType().Name}.");
            }
        }
    }

    public async Task<bool> ForwardAsync(string protocolUrl, CancellationToken cancellationToken = default)
    {
        if (ownsInstance || !ExternalMediaRequest.TryParseProtocol(protocolUrl, out _)) return false;
        var bytes = Encoding.UTF8.GetBytes(protocolUrl);
        if (bytes.Length > ExternalMediaRequest.MaximumProtocolLength) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(350, timeout.Token);
                await client.WriteAsync(BitConverter.GetBytes(bytes.Length), timeout.Token);
                await client.WriteAsync(bytes, timeout.Token);
                await client.FlushAsync(timeout.Token);
                var response = new byte[1];
                await client.ReadExactlyAsync(response, timeout.Token);
                return response[0] == 1;
            }
            catch (TimeoutException) when (attempt < 11) { await Task.Delay(100, timeout.Token); }
            catch (IOException) when (attempt < 11) { await Task.Delay(100, timeout.Token); }
            catch (Exception) { return false; }
        }
        return false;
    }

    public void Dispose()
    {
        stop.Cancel();
        if (ownsInstance) { instanceGate.Release(); ownsInstance = false; }
        instanceGate.Dispose();
        stop.Dispose();
    }
}
