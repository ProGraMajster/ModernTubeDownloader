namespace ModernTubeDownloader.Views;

/// <summary>Bound native control count, not queue size or scheduler concurrency.</summary>
internal static class QueueCardPage
{
    internal const int Size = 50;
    internal static int Clamp(int page, int count) => Math.Clamp(page, 0, Math.Max(0, (count - 1) / Size));
    internal static int ForIndex(int index) => Math.Max(0, index / Size);
}
