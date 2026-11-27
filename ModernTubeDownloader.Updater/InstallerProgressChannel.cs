using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Updates;

/// <summary>The transaction owns no window and never waits for a progress frame to be consumed.</summary>
internal sealed class InstallerProgressChannel(Guid operation, string language, Action<DiagnosticOutcome> diagnostic)
{
    private const string Prefix = "ModernTubeDownloader.Update.";
    private readonly Channel<InstallerProgressFrame> frames = Channel.CreateBounded<InstallerProgressFrame>(
        new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private Task? pump;
    private long sequence;

    internal void Start() => pump = Task.Run(PublishAsync);

    internal void Report(InstallerPhase phase, int done = 0, int total = 0) =>
        frames.Writer.TryWrite(new(operation, Interlocked.Increment(ref sequence), phase, done, total));

    internal async Task FinishAsync(InstallerPhase phase)
    {
        Report(phase);
        frames.Writer.TryComplete();
        if (pump is not null)
            await Task.WhenAny(pump, Task.Delay(TimeSpan.FromMilliseconds(350))).ConfigureAwait(false);
    }

    private async Task PublishAsync()
    {
        try
        {
            var nonce = Guid.NewGuid();
            using var listener = new NamedPipeServerStream(Prefix + nonce.ToString("N"), PipeDirection.Out,
                1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var childInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            foreach (var argument in new[] { "--progress", nonce.ToString("N"), operation.ToString("N"),
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture), InstallerProgressWindow.NormalizeLanguage(language) })
                childInfo.ArgumentList.Add(argument);
            using var child = Process.Start(childInfo) ?? throw new IOException("The installer presentation could not start.");
            using var connect = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await listener.WaitForConnectionAsync(connect.Token).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(listener.SafePipeHandle, out var pid) || pid != child.Id)
                throw new IOException("The installer presentation client is not the owned observer.");
            diagnostic(DiagnosticOutcome.Completed);
            var data = new byte[InstallerProgressFrame.Size];
            await foreach (var frame in frames.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                frame.Write(data);
                using var write = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                await listener.WriteAsync(data, write.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // The optional observer cannot affect the worker or its durable result.
            try { diagnostic(DiagnosticOutcome.Unavailable); }
            catch (Exception) { }
        }
    }

    internal static async Task ReceiveAsync(Guid nonce, Guid operation, int workerId, Action<InstallerProgressFrame> publish,
        CancellationToken token)
    {
        using var client = new NamedPipeClientStream(".", Prefix + nonce.ToString("N"), PipeDirection.In,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync(5000, token).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(client.SafePipeHandle, out var pid) || pid != workerId)
                throw new IOException("Unexpected installer presentation server.");
            var data = new byte[InstallerProgressFrame.Size];
            long sequence = 0;
            while (!token.IsCancellationRequested)
            {
                await client.ReadExactlyAsync(data, token).ConfigureAwait(false);
                var frame = InstallerProgressFrame.Read(data, operation, sequence);
                sequence = frame.Sequence;
                publish(frame);
                if (frame.Terminal) return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or TimeoutException)
        {
            publish(new(operation, long.MaxValue, InstallerPhase.Disconnected));
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out int processId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out int processId);
}
