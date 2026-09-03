using System.Drawing;
using ModernFormsNext;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Tests;

public sealed class QueueScrollPlannerTests
{
    [Fact]
    public void OverflowingFlowLayoutPanelUsesFinalBoundsToPlanTheNewLastCardIntoViewWithoutHorizontalMovement()
    {
        using var queue = new FlowLayoutPanel
        {
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 0, 8, 0)
        };
        queue.SetBounds(0, 0, 700, 300);
        for (var index = 0; index < 5; index++)
            queue.Controls.Add(new Panel { Width = 650, Height = 150 });
        queue.PerformLayout();

        var target = queue.Controls[^1];
        var vertical = queue.VerticalScrollProperties;
        var horizontalBefore = queue.HorizontalScrollProperties.Value;
        var verticalBefore = vertical.Value;
        var plannedValue = QueueScrollPlanner.CalculateVerticalValue(
            queue.DisplayRectangle,
            target.Bounds,
            verticalBefore,
            vertical.Minimum,
            vertical.Maximum);
        var projectedTarget = target.Bounds;
        projectedTarget.Offset(0, -(plannedValue - verticalBefore));

        Assert.True(
            projectedTarget.Bottom <= queue.DisplayRectangle.Bottom,
            $"Projected target {projectedTarget} is below viewport {queue.DisplayRectangle}; value={plannedValue}, range={vertical.Minimum}..{vertical.Maximum}.");
        Assert.True(
            projectedTarget.Top >= queue.DisplayRectangle.Top,
            $"Projected target {projectedTarget} is above viewport {queue.DisplayRectangle}; value={plannedValue}, range={vertical.Minimum}..{vertical.Maximum}.");
        Assert.Equal(horizontalBefore, queue.HorizontalScrollProperties.Value);

        queue.SetBounds(0, 0, 600, 240);
        foreach (var card in queue.Controls)
            card.Width = 550;
        queue.PerformLayout();

        var resizedValue = QueueScrollPlanner.CalculateVerticalValue(
            queue.DisplayRectangle,
            target.Bounds,
            plannedValue,
            vertical.Minimum,
            vertical.Maximum);
        Assert.InRange(resizedValue, vertical.Minimum, vertical.Maximum);
        Assert.Equal(horizontalBefore, queue.HorizontalScrollProperties.Value);
    }

    [Fact]
    public void NewCardBelowOverflowingViewportIsScrolledFullyIntoView()
    {
        var value = QueueScrollPlanner.CalculateVerticalValue(
            new Rectangle(0, 0, 700, 300),
            new Rectangle(0, 460, 700, 150),
            currentValue: 100,
            minimum: 0,
            maximum: 500);

        Assert.Equal(410, value);
    }

    [Fact]
    public void ShortListAlreadyInViewDoesNotMove()
    {
        var value = QueueScrollPlanner.CalculateVerticalValue(
            new Rectangle(0, 0, 700, 500),
            new Rectangle(0, 160, 700, 150),
            currentValue: 0,
            minimum: 0,
            maximum: 0);

        Assert.Equal(0, value);
    }

    [Fact]
    public void HorizontalOverflowDoesNotChangeVerticalPosition()
    {
        var value = QueueScrollPlanner.CalculateVerticalValue(
            new Rectangle(0, 0, 500, 300),
            new Rectangle(-30, 80, 900, 120),
            currentValue: 75,
            minimum: 0,
            maximum: 400);

        Assert.Equal(75, value);
    }

    [Fact]
    public void RecalculationAfterResizeAlwaysClampsToValidScrollRange()
    {
        var value = QueueScrollPlanner.CalculateVerticalValue(
            new Rectangle(0, 0, 700, 240),
            new Rectangle(0, 620, 700, 150),
            currentValue: 350,
            minimum: 0,
            maximum: 420);

        Assert.InRange(value, 0, 420);
        Assert.Equal(420, value);
    }
}
