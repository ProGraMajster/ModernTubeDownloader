using System.Diagnostics;
using System.Text;

namespace ModernTubeDownloader.Tests;

public sealed class AutomationCliEncodingTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task OwnedQaStartupHookPreservesUnicodePipesWithoutChangingParentConsole(bool optIn)
    {
        const string value = "仮ファイル | Zażółć gęślą jaźń | العربية | हिन्दी | 中文 | 한국어 | 😀\nsecond line";
        var parentInputEncoding = Console.InputEncoding.CodePage;
        var parentOutputEncoding = Console.OutputEncoding.CodePage;
        var utf8 = new UTF8Encoding(false, true);
        var assembly = Path.ChangeExtension(CoreTests.FakeToolPath(), ".dll");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = utf8, StandardOutputEncoding = utf8, StandardErrorEncoding = utf8
        };
        start.ArgumentList.Add(assembly); start.ArgumentList.Add("--qa-automation-utf8-echo");
        start.Environment["DOTNET_STARTUP_HOOKS"] = assembly;
        if (optIn) start.Environment["MTD_QA_AUTOMATION_UTF8"] = "1";
        else start.Environment.Remove("MTD_QA_AUTOMATION_UTF8");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try
        {
            if (optIn) await process.StandardInput.WriteAsync(value);
            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (optIn)
            {
                Assert.Equal(0, process.ExitCode); Assert.Equal("", await error); Assert.Equal(value, await output);
            }
            else
            {
                Assert.Equal(76, process.ExitCode); Assert.Equal("", await output);
                Assert.Contains("Explicit QA UTF-8 opt-in required.", await error);
            }
            Assert.Equal(parentInputEncoding, Console.InputEncoding.CodePage);
            Assert.Equal(parentOutputEncoding, Console.OutputEncoding.CodePage);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await output; await error;
        }
    }
}
