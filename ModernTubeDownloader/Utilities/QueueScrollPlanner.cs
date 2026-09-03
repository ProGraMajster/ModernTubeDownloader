using System.Drawing;

namespace ModernTubeDownloader.Utilities;

internal static class QueueScrollPlanner
{
    public static int CalculateVerticalValue(
        Rectangle viewport,
        Rectangle target,
        int currentValue,
        int minimum,
        int maximum)
    {
        if (maximum < minimum)
            return minimum;

        var targetValue = currentValue;
        if (target.Top < viewport.Top)
            targetValue += target.Top - viewport.Top;
        else if (target.Bottom > viewport.Bottom)
            targetValue += target.Bottom - viewport.Bottom;

        return Math.Clamp(targetValue, minimum, maximum);
    }
}
