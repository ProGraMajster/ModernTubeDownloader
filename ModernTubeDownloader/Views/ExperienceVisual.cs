using ModernFormsNext;
using ModernTubeDownloader.Shell;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

/// <summary>Shared native dialog finishes; caption buttons, dragging and accessibility remain MFN-owned.</summary>
internal static class ExperienceVisual
{
    internal static void ApplyWindow(Form form)
    {
        var bar = form.TitleBar;
        var caption = new CaptionLabel { Text = form.Text, AutoEllipsis = true };
        AppUi.BindForeground(caption); AppTypography.Bind(caption, 10.5f, FontStyle.Bold);
        bar.Controls.Add(caption); AppTypography.SetDashboardPresentation(bar, true);
        AppUi.BindGradient(bar, DownloadsVisual.Background, DownloadsVisual.BackgroundEnd);
        bar.Image = AppBranding.CaptionIcon(true);
        // Form.Text still supplies the native window/accessibility name. MFN's default
        // caption painter uses accent-white text, so its decorative text is replaced
        // with the same mouse-transparent label used by the main dashboard.
        void SyncTitle(object? sender, EventArgs e)
        {
            if (bar.Text.Length == 0) return;
            caption.Text = form.Text; bar.Text = "";
        }
        void Layout()
        {
            bar.Height = AppTypography.TextHeight(38);
            var left = bar.Height + 8;
            caption.SetBounds(left, 0, Math.Max(1, bar.Width - left - 154), bar.Height);
        }
        void ThemeChanged(object? sender, EventArgs e) => form.Style.Border.Color = AppUi.GetColor(DownloadsVisual.Border);
        bar.TextChanged += SyncTitle; bar.SizeChanged += (_, _) => Layout();
        ThemeManager.Current.ThemeChanged += ThemeChanged;
        bar.Disposed += (_, _) => ThemeManager.Current.ThemeChanged -= ThemeChanged;
        AppTypography.RegisterLayout(bar, Layout); SyncTitle(null, EventArgs.Empty); Layout(); ThemeChanged(null, EventArgs.Empty);
    }

    internal static void Surface(Control control) => AppUi.BindGradient(control, DownloadsVisual.Background, DownloadsVisual.BackgroundEnd);

    internal static void Action(Button button, bool primary = false)
    {
        DownloadsVisual.Button(button, primary);
        button.TextBrush = primary ? new ModernFormsNext.Drawing.SolidColorBrush(SkiaSharp.SKColors.White) : null;
    }

    internal static Label Paragraph(string text, bool strong = false)
    {
        var label = strong ? AppUi.Heading(text, 11) : AppUi.Muted(text);
        label.Multiline = true; return label;
    }
}

/// <summary>A finite, measured card in a native scroll viewport, with no independent scrolling or retained layout cache.</summary>
internal sealed class ExperienceCard : Panel
{
    internal Func<int, int>? ArrangeContent { get; set; }
    internal ExperienceCard() => DownloadsVisual.Card(this);
    internal int FitWidth(int width) => ArrangeContent?.Invoke(Math.Max(1, width)) ?? Height;
}
