using System.Text;

// The runtime requires this exact, unnamespaced type name. Test-Automation loads
// it only into its owned diagnostic CLI child, never into the application.
internal static class StartupHook
{
    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("MTD_QA_AUTOMATION_UTF8") != "1") return;
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        // Console.InputEncoding changes the shared Windows console code page.
        // Replace only this process's managed pipe readers/writers instead.
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), utf8, detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096, leaveOpen: true));
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8, leaveOpen: true) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8, leaveOpen: true) { AutoFlush = true });
    }
}
