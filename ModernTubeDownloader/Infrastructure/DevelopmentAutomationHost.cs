#if DEBUG
using ModernFormsNext;
using ModernFormsNext.Automation;
using ModernFormsNext.Automation.Windows;

namespace ModernTubeDownloader.Infrastructure;

// Development-only opt-in bridge. The framework owns semantic traversal and transport.
internal sealed class DevelopmentAutomationHost
{
    private AutomationSession? session;
    private WindowsAutomationServer? server;

    public void Start(Form mainWindow)
    {
        if (session is not null)
            throw new InvalidOperationException("Automation has already started.");

        var created = new AutomationSession();
        try
        {
            created.RegisterRoot(mainWindow);
            server = WindowsAutomationServer.Start(created, new WindowsAutomationOptions
            {
                ApplicationName = "ModernTubeDownloader"
            });
            session = created;
        }
        catch
        {
            created.Dispose();
            throw;
        }
    }

    public IDisposable? RegisterWindow(Form window) => session?.RegisterRoot(window);

    public async Task StopAsync()
    {
        try
        {
            if (server is not null)
                await server.StopAsync();
        }
        finally
        {
            if (session is not null)
                await session.StopAsync();
            server = null;
            session = null;
        }
    }
}
#endif
