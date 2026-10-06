using ModernFormsNext;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Tests;

public sealed class AppScrollBehaviorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoScrollContainersKeepAppWheelStepAfterLayoutAndResize(bool flowLayout)
    {
        using ScrollableControl container = flowLayout
            ? new AppScrollFlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true }
            : new AppScrollPanel { AutoScroll = true };
        container.SetBounds(0, 0, 320, 240);
        container.Controls.Add(new Panel { Width = 270, Height = 1000 });
        container.PerformLayout();

        Assert.True(container.VerticalScrollProperties.Maximum > 0);
        Assert.Equal(AppScrollBehavior.MouseWheelScrollStep, container.VerticalScrollProperties.SmallChange);

        container.SetBounds(0, 0, 260, 180);
        container.PerformLayout();
        Assert.Equal(AppScrollBehavior.MouseWheelScrollStep, container.VerticalScrollProperties.SmallChange);
    }
}
