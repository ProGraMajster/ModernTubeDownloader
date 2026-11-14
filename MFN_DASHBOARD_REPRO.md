# MFN presentation limitations observed by the dashboard

Framework pin: `8d8a7679779608b4edbee7dc687c13e3b1a611d9`.
Framework source is unchanged. Small application presentation subclasses now use
public painting APIs and the inherited canonical semantic providers; no copied
renderer, private reflection or separate accessibility tree is used.

## Independent source audit, 2026-10-17

The pinned checkout above and the current upstream/main checkout
`307f08be46708d83222c93aac1912f81cf77d79a` contain byte-identical files for the
following relevant paths (SHA-256, computed independently in both checkouts):

| Path under ModernFormsNext | SHA-256 |
| --- | --- |
| `Layout/TextImageLayoutEngine.cs` | `C75D13BDF59DCBA7862E90BCC0BE505734FD5858C174A26F9EFD0D8E36A2EBC1` |
| `Extensions/SkiaExtensions.cs` | `F4328AA718C8229260597AEBBF144B3072FEFCE609432C853DA62A2325DC2A39` |
| `Renderers/LabelRenderer.cs` | `7B01EB55AA13E8ADA99D05B195DA3450008746D577D59FE674EAE29F5EAFFD3A` |
| `Extensions/SkiaTextExtensions.cs` | `1FB8D42EAA02BABE2D3AA440BD8189E737A62D0D33530F289A1F493EC63F4D00` |

GitHub issue #272 is OPEN with no fix or follow-up comment recorded at this check
(last update 2026-10-16T15:58:49Z). Issues #204 and #205 are also OPEN. An upstream
branch existing elsewhere or a later framework commit is not qualified by this
audit. Neither framework checkout was modified or built during the audit.

The source confirms two distinct measurement risks: single-line layout truncates
fractional measured width, and measurement uses `control.Style` whereas painting
uses `control.CurrentStyle`. A hover/focus font override can therefore invalidate
the measured rectangle. The same engine is used by Button, CheckBox and
RadioButton renderers; the application Label mitigation does not fix those
framework renderers globally.

`Control.Font` rounds its requested size to integer logical units; public
`Canvas.DrawText(..., control, ...)` applies `LogicalToDeviceUnits` once. The
application's Modern typography multiplier is a design choice, not a DPI fix.
OS DPI must still be qualified on actual native windows separately from app text
scale. `DashboardLabel` is intended for text-only labels with app-owned padding;
its custom paint path does not implement Label image/text composition.

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

## Fitting button captions and native child composition

The same tight measurement path also affects text-only `Button` captions. Selected
Modern actions now use `DashboardButton`, which registers a renderer for that
application-owned subclass through public `RenderManager.SetRenderer<T>`.
`DashboardButtonRenderer` derives from `ButtonRenderer`; Classic presentation and
buttons with a directly assigned bitmap delegate to the original renderer. The
Modern text-only path uses the actual padded field with public `Canvas.DrawText`.
Action width comes from public `TextMeasurer.MeasureText` using the current
style's font, with width rounded upward and space reserved for padding and an
optional icon. That measurement remains in logical units; painting applies DPI.

The subclass keeps inherited `Button.OnPaint`, input, focus, enabled checks and
the canonical accessible Invoke action. This matters because `Control.OnPaint`
owns child composition: replacing `Button.OnPaint` with only a text draw would
silently omit child icons. A renderer override preserves that path and the
native image/text margin contract without private API access or temporary text
mutation.

`DashboardVisualTests` includes raster cases for fitting PL/Japanese action
captions at app text 100/150/200, actual overflow, child visibility, native
Classic/image parity and canonical Invoke/disabled behavior. These are targeted
regression cases, not a replacement for native screenshots or physical-DPI
qualification; execution results belong to the current validation report.
The workaround is scoped to application-owned controls. Native framework Button,
CheckBox and RadioButton rendering elsewhere remains subject to the upstream
issue.

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

## Hidden sections still enlarge the default scroll canvas

At the pinned revision, `ScrollableControl.CalculateCanvasSize` iterates all
non-internal children without testing visibility. A hidden child Panel at
`Top=100, Height=900` still gives a 1000-unit extent inside a 500-unit scroll
viewport. This explains the Inspector's scrollbar despite its visible content
fitting: its hidden options editor retained a tall rectangle. The app gives the
inactive editor/fields section empty bounds after layout and restores bounds
when the section is opened. This uses public bounds only and does not alter MFN.
The minimal recipe is extracted from source; current native evidence is recorded
in `MODERN_DASHBOARD_ASTRA_VISUAL_QA.md`, separately from physical DPI testing.

The LIVE presentation regression initially compared raw
`VerticalScrollProperties.Maximum` before and after the roundtrip. That was a
test-fixture mistake: `ScrollableControl.Recalculate` updates the range only
while the bar is visible and retains its previous maximum after hiding it.
The observed 78-to-580 difference at app text 200% did not represent available
scroll movement; every original child had identical bounds. The regression now
compares the public `AccessibilityObject.ScrollInfo` (both axes and viewport)
and exact original child bounds. It covers both fitting and scrolling Classic
viewports at app text 100/200. No application or framework change was needed
for this stale-field assertion, and it is not an additional application defect.

## PictureBox Zoom ignores the padded client origin

Read-only inspection of the pinned `PictureBoxRenderer.cs` shows that `Zoom`
uses `PaddedClientRectangle.Width/Height` to calculate the image size and center,
but omits the rectangle's `X/Y` when placing the bitmap. `CenterImage` has the
same origin omission. A public-API recipe is a 92 × 52 `PictureBox`, a square
bitmap, `Padding = new Padding(20)` and `SizeMode = PictureBoxSizeMode.Zoom`.
The content field is `(20, 20, 52, 12)`: a 12 × 12 image should start at `(40, 20)`,
whereas the current Zoom arithmetic produces `(20, 0)`.

The pinned source and the independently inspected MFN checkout at
`307f08be46708d83222c93aac1912f81cf77d79a` have the same SHA-256 for that file:
`3F1B369A81C6861028CF6B7BD54F022EBB9423860B6E67C57DE59E8BCA6C6134`.

This source analysis matches the small icon at the top of missing History
thumbnails in the application's native gate capture. A separate standalone
upstream native reproduction was **NOT EXECUTED**. The application mitigation
uses a centered 24-unit native vector in the existing Modern History placeholder;
the Classic `PictureBox` recipe remains unchanged. No framework source was edited.

## Existing composite typography issues

MFN issues [#204](https://github.com/ProGraMajster/ModernFormsNext/issues/204) and
[#205](https://github.com/ProGraMajster/ModernFormsNext/issues/205) were still open
when checked on 2026-10-16. Tab headers and NumericUpDown's embedded editor require
their upstream fixes. No reflection, implicit-child font injection or DPI change
is used by the dashboard.
