using ModernFormsNext;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

/// <summary>Read-only overview of saved preferences; editing opens the existing section.</summary>
internal sealed class SettingsOverviewCard : Panel
{
    private readonly Label heading = AppUi.Heading("", 12);
    private readonly AppVectorIcon icon;
    private readonly Button open = new DashboardButton();
    private readonly List<(Label Name, Label Value)> rows = [];
    public SettingsOverviewCard(string id, AppIconKind kind, Action navigate)
    {
        AccessibleAutomationId = "SettingsOverview" + id; icon = new(kind, 24, AppThemeTokens.Info);
        icon.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden;
        open.AccessibleAutomationId = "SettingsOverviewOpen" + id; open.Click += (_, _) => navigate();
        Controls.AddRange([heading, icon, open]); DashboardStyle.Card(this);
    }
    public void Present(int width, string title, string action, params (string Name, string Value)[] facts)
    {
        Width = width; var inner = Math.Max(1, width - 32);
        heading.Text = title; heading.Multiline = true;
        heading.SetBounds(52, 16, width - 68, DashboardVisual.LabelHeight(heading, width - 68, 2)); icon.SetBounds(16, 18, 24, 24);
        while (rows.Count < facts.Length)
        {
            var name = AppUi.Muted(); var value = AppUi.Muted(); AppUi.BindForeground(value);
            name.Multiline = value.Multiline = true; AppTypography.Bind(name, 9.5f); AppTypography.Bind(value, 9.5f);
            rows.Add((name, value)); Controls.AddRange([name, value]);
        }
        var y = heading.Bottom + 14;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; row.Name.Visible = row.Value.Visible = i < facts.Length;
            if (i >= facts.Length) { row.Name.SetBounds(0, 0, 0, 0); row.Value.SetBounds(0, 0, 0, 0); continue; }
            row.Name.Text = facts[i].Name; row.Value.Text = facts[i].Value;
            var valueWidth = Math.Min(inner / 2, Math.Max(70, DashboardVisual.TextWidth(row.Value, row.Value.Text) + 4));
            var nameWidth = Math.Max(1, inner - valueWidth - 12);
            var height = Math.Max(DashboardVisual.LabelHeight(row.Name, nameWidth, 3), DashboardVisual.LabelHeight(row.Value, valueWidth, 3));
            row.Name.SetBounds(16, y, nameWidth, height); row.Value.SetBounds(16 + nameWidth + 12, y, valueWidth, height); y += height + 10;
        }
        open.Text = action; DashboardVisual.Button(open); open.SetBounds(16, y + 2, inner, AppTypography.TextHeight(34)); Height = open.Bottom + 16;
    }
}
