using System.Globalization;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Views;

internal static class QueuePositionPresenter
{
    internal static IReadOnlyDictionary<Guid, string> Build(
        IReadOnlyList<DownloadQueueItem> items,
        bool showNumbers)
    {
        var positions = new Dictionary<Guid, string>(showNumbers ? items.Count : 0);
        if (!showNumbers)
            return positions;

        for (var index = 0; index < items.Count; index++)
            positions.Add(items[index].Id, (index + 1).ToString(CultureInfo.InvariantCulture));

        return positions;
    }
}
