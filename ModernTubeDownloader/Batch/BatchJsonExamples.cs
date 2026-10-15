namespace ModernTubeDownloader.Batch;

/// <summary>Small copyable schema examples shared by the Batch help and regression tests.</summary>
public static class BatchJsonExamples
{
    public static IReadOnlyList<string> Documents { get; } =
    [
        """["https://example.com/video"]""",
        """[{"url":"https://example.com/video","quality":"720"}]""",
        """{"url":"https://example.com/video"}""",
        """{"urls":["https://example.com/one","https://example.com/two"]}""",
        """{"items":[{"url":"https://example.com/one"},{"url":"https://example.com/two"}]}"""
    ];
    public static string Text => string.Join("\n\n", Documents);
}
