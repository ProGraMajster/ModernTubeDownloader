namespace ModernTubeDownloader.Infrastructure;

public sealed class NullAppLogger : IAppLogger
{
    public void Info(string message, string file = "", string member = "") { }
    public void Warning(string message, string file = "", string member = "") { }
    public void Error(string message, Exception? exception = null, string file = "", string member = "") { }
}
