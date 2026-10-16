# MFN presentation limitations observed by the dashboard

Framework pin: `8d8a7679779608b4edbee7dc687c13e3b1a611d9`.
Framework source is unchanged. Small application presentation subclasses now use
public painting APIs and the inherited canonical semantic providers; no copied
renderer, private reflection or separate accessibility tree is used.

## Premature Label.AutoEllipsis

Tracked separately in [MFN #272](https://github.com/ProGraMajster/ModernFormsNext/issues/272).

Observed in the real Windows HWND at DPI 96 and application text scales 100–200%.
A queue title `Integration Sample` had semantic bounds 1506 × 48 logical units
and rendered as `Integration Sam…`. Short inspector author/URL labels also
ellipsized with unused horizontal space. The full accessible name and tooltip
are preserved. This is separate from legitimately long titles exceeding a row.

Minimal native repro using public MFN APIs:

```csharp
using ModernFormsNext;
using ModernFormsNext.WindowKit;

var form = new Form { Text = "Label ellipsis repro", Width = 1700, Height = 300 };
form.Controls.Add(new Label {
    Left = 24, Top = 24, Width = 1506, Height = 48,
    Text = "Integration Sample", AutoEllipsis = true,
    Font = new Font("Segoe UI", 27, FontStyle.Bold)
});
form.Controls.Add(new Label {
    Left = 24, Top = 100, Width = 1506, Height = 48,
    Text = "Integration Sample", AutoEllipsis = false,
    Font = new Font("Segoe UI", 27, FontStyle.Bold)
});
Application.Run((ICloseable)form);
```

Expected: both short titles are complete; an actually overflowing title gets an
ellipsis only in the first label. Native MTD observations are confirmed; this
standalone sample is the extracted reproduction recipe, not a separately executed
framework qualification run.

Suspected cause: `TextImageLayoutEngine.CalculateImageAndTextBounds` aligns a tight
`GetTextSize().ToSize()` rectangle for a single-line label. `SKSize.ToSize` casts
floating widths to int; TextMeasurer/DrawText then lays out again against that
rounded-down width with ellipsis enabled. A subpixel difference can trigger
premature ellipsis. Measurement also uses a fixed 1000 × 1000 maximum. The upstream
fix should retain correct single-line alignment and account for the available
field and fractional measurement. DashboardLabel now draws against the actual
available field using public Canvas.DrawText. Classic calls the original renderer.
Headless raster regression tests at text 100/150/200 verify fitting Unicode has
identical pixels with ellipsis enabled/disabled, while actual overflow differs.
Native before/after captures separately confirm the application result. This is
an app presentation mitigation; it does not claim an upstream fix.

## Per-item ProgressBar fill color

The pinned native ProgressBarRenderer uses global `Theme.AccentColor2` for its fill.
Control foreground/background styles do not select a per-instance fill color.
DashboardProgress retains the native range/value model and provider and uses
public painting for the real status fill. Completed/failed/cancelled rows hide
active progress. Classic delegates to the pinned renderer.

## Zero-width rounded border hairlines

The pinned DrawBorder rounded path still invokes a Skia stroke with Width=0.
Skia interprets that as a device hairline. A minimal public-API recipe is a Panel
with Style.Border.Radius=8, Width=0 and an opaque border color. The before capture
shows unwanted outlines on controls intended to be flat. DashboardVisual.Flat
sets the zero border color explicitly transparent in all interaction states;
actual card boundaries remain one unit wide. This recipe is extracted from the
diagnosis, not a separately executed upstream native qualification.

## Input padding precedence

ControlStyle.Padding takes precedence over Control.Padding through
PresentationPadding. Setting only Control.Padding leaves text under a leading
icon. The app sets padding on the public state styles and restores the original
recipes for Classic. This was app API usage, not a new framework defect.

## Existing composite typography issues

MFN issues [#204](https://github.com/ProGraMajster/ModernFormsNext/issues/204) and
[#205](https://github.com/ProGraMajster/ModernFormsNext/issues/205) were still open
when checked on 2026-10-16. Tab headers and NumericUpDown's embedded editor require
their upstream fixes. No reflection, implicit-child font injection or DPI change
is used by the dashboard.
