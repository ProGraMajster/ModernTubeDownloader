using System.Runtime.CompilerServices;

namespace ModernTubeDownloader.Infrastructure;

public interface IAppLogger
{
    void Info(string message, [CallerFilePath] string file = "", [CallerMemberName] string member = "");
    void Warning(string message, [CallerFilePath] string file = "", [CallerMemberName] string member = "");
    void Error(string message, Exception? exception = null, [CallerFilePath] string file = "", [CallerMemberName] string member = "");
    bool IsEnabled(DiagnosticLevel level) => false;
    void Event(DiagnosticEvent entry) { }
}
