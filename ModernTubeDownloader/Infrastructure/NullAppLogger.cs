namespace ModernTubeDownloader.Infrastructure;

public sealed class NullAppLogger : IAppLogger
{
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
}
