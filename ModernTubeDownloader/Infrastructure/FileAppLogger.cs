using System.Collections.Concurrent;
using System.Text;

namespace ModernTubeDownloader.Infrastructure;

public sealed class FileAppLogger : IAppLogger, IDisposable
{
    private readonly BlockingCollection<string> entries = [];
    private readonly Task writerTask;
    private readonly string logFile;
    private bool disposed;

    public FileAppLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        logFile = Path.Combine(logDirectory, $"ModernTubeDownloader-{DateTimeOffset.Now:yyyyMMdd}.log");
        writerTask = Task.Run(WriteLoop);
    }

    public void Info(string message) => Write("INF", message);
    public void Warning(string message) => Write("WRN", message);
    public void Error(string message, Exception? exception = null) =>
        Write("ERR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        if (!disposed)
            entries.Add($"{DateTimeOffset.Now:O} [{level}] {message}");
    }

    private void WriteLoop()
    {
        try
        {
            using var writer = new StreamWriter(logFile, append: true, new UTF8Encoding(false)) { AutoFlush = true };
            foreach (var entry in entries.GetConsumingEnumerable())
                writer.WriteLine(entry);
        }
        catch
        {
            // A logging failure must never terminate the application.
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        entries.CompleteAdding();
        try { writerTask.GetAwaiter().GetResult(); } catch { }
        entries.Dispose();
    }
}
