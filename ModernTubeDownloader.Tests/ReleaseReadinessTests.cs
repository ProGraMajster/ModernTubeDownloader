using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Tests;

public sealed class ReleaseReadinessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ApplicationAssemblyExposesTheCentralReleaseIdentity()
    {
        var assembly = typeof(MainForm).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var repository = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "RepositoryUrl");
        var file = FileVersionInfo.GetVersionInfo(assembly.Location);

        // Both projects inherit the same central version props, including candidate suffixes.
        var expected = typeof(ReleaseReadinessTests).Assembly;
        Assert.Equal(expected.GetName().Version, assembly.GetName().Version);
        Assert.Equal(expected.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, informationalVersion?.InformationalVersion);
        Assert.Equal(FileVersionInfo.GetVersionInfo(expected.Location).FileVersion, file.FileVersion);
        Assert.Equal("ModernTubeDownloader", file.ProductName);
        Assert.Equal("ProGraMajster", file.CompanyName);
        Assert.Equal("https://github.com/ProGraMajster/ModernTubeDownloader", repository.Value);
    }

    [Fact]
    public void FatalFallbackLoggerWritesSafeCriticalEventToAppDataLogs()
    {
        var paths = AppPaths.Create(root);
        var exception = new InvalidOperationException("release-readiness-sentinel");

        FatalErrorReporter.Log(paths, logger: null, "Fatal test message.", exception);

        var log = Assert.Single(Directory.GetFiles(paths.LogDirectory, "MTD-events-v1-*.log"));
        var contents = File.ReadAllText(log);
        var record = JsonSerializer.Deserialize<DiagnosticRecord>(contents, DiagnosticRecord.Json)!;
        Assert.Equal(DiagnosticLevel.Fatal, record.LogLevel);
        Assert.Equal(DiagnosticError.InvalidOperation, record.ExceptionType);
        Assert.Equal(exception.HResult, record.HResult);
        Assert.DoesNotContain("Fatal test message.", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("release-readiness-sentinel", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void FatalCrashArtifactContainsStructuralDiagnosticsButRedactsPrivateData()
    {
        var paths = AppPaths.Create(root);
        UiCrashDiagnostics.SetActiveView("Settings");
        UiCrashDiagnostics.Record("settings.remote.save", "settings", 7);
        var exception = new InvalidOperationException(
            "Collection was modified at https://example.test/watch/private-id Authorization: Bearer secret-bearer token=secret-token file:///C:/private/video.mp4");

        FatalErrorReporter.Log(paths, logger: null, "Fatal UI exception.", exception);

        var artifact = Assert.Single(Directory.GetFiles(paths.LogDirectory, "MTD-events-v1-*.log"));
        var contents = File.ReadAllText(artifact);
        var record = JsonSerializer.Deserialize<DiagnosticRecord>(contents, DiagnosticRecord.Json)!;
        Assert.Equal(DiagnosticLevel.Fatal, record.LogLevel);
        Assert.Equal(DiagnosticError.InvalidOperation, record.ExceptionType);
        Assert.NotNull(record.Counts?.Context);
        Assert.True(record.Counts?.Items > 0);
        Assert.True(record.Counts?.Total > 0);
        Assert.DoesNotContain("private-id", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-bearer", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("/private/video.mp4", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationBrandingLoadsTheEmbeddedIconForModernFormsWindows()
    {
        using var form = new ModernFormsNext.Form();

        AppBranding.Apply(form);

        Assert.NotNull(form.Image);
        Assert.Same(form.Image, form.TitleBar.Image);
        Assert.Equal(24, form.Image.Width);
        Assert.Equal(24, form.Image.Height);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
