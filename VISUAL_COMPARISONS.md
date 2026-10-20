# Native visual comparisons

20 October 2026. The reference PNGs are the owner's six supplied target designs. Their DPI is unknown. Native originals below are owned application-window captures, with no image replacement, retouching or stretching. Each side-by-side sheet fits the source images proportionally into 768x590 cells; differing aspect ratios remain visible. TextScale is independent of HWND DPI.

## Six populated views

Run `56-final-populated.log`: PL/Dark/100%, actual window 1536x1024, HWND DPI 96. Isolated temporary profile, canonical stores, controlled FakeTool only. The five queue records have mixed persisted statuses; LIVE's first session is actually executing the FakeTool. Thumbnail images are original geometric QA fixtures, not photographic media from production. Batch parsing/metadata analysis and thumbnail exports execute their real application paths. These fixtures do not establish real Internet/source or media-playback qualification.

| View | Original native PNG | Reference/current sheet | Visual result |
| --- | --- | --- | --- |
| Downloads: five records, selection | [native](artifacts/dashboard-visual-first/56-final-populated/dashboard-downloads.png) | [comparison](artifacts/dashboard-visual-first/56-final-populated/comparison-downloads.png) | PARTIAL: closest composition; different actual status mix and inspector editing model |
| LIVE: recording and waiting sessions | [native](artifacts/dashboard-visual-first/56-final-populated/dashboard-live.png) | [comparison](artifacts/dashboard-visual-first/56-final-populated/comparison-live.png) | PARTIAL: all four rows/widgets fit; connector-specific fields intentionally absent |
| History: ten records | [native](artifacts/dashboard-visual-first/56-final-populated/dashboard-history.png) | [comparison](artifacts/dashboard-visual-first/56-final-populated/comparison-history.png) | PARTIAL: actual optional time/bytes, denser measured columns; reference has more visible rows/features |
| Batch: ten valid, duplicate, error | [native](artifacts/dashboard-visual-first/56-final-populated/dashboard-batch.png) | [comparison](artifacts/dashboard-visual-first/56-final-populated/comparison-batch.png) | PARTIAL: visible counts and mixed rows; less table density and fewer columns |
| Other: actual completed exports | [native](artifacts/dashboard-visual-first/56-final-populated/dashboard-other.png) | [comparison](artifacts/dashboard-visual-first/56-final-populated/comparison-other.png) | PARTIAL: working module and activity; reference has a broader tool collection |
| Settings: eleven categories | [native](artifacts/dashboard-visual-first/56-final-populated/dashboard-settings.png) | [comparison](artifacts/dashboard-visual-first/56-final-populated/comparison-settings.png) | PARTIAL: measured three-column overview; some controls remain in full category editors |

All six originals and all six comparison sheets above were opened and visually inspected. The request's additional 28-item/toast/custom-range user images were not supplied. [Visual gaps](VISUAL_GAP_AND_FIXES.md) separates appearance, layout, missing modules, fixture differences and unknown DPI. Owner visual acceptance remains pending.

## Theme, text scale and physical DPI

The following native sets completed all routes, one Classic/Modern switch and restart:

| Configuration | Representative originals | Review |
| --- | --- | --- |
| PL / Light / 100%, 1536x1024, DPI96 | [Downloads](artifacts/dashboard-visual-first/33-pl-light-100/dashboard-downloads.png), [Batch](artifacts/dashboard-visual-first/33-pl-light-100/dashboard-batch.png), [Settings](artifacts/dashboard-visual-first/33-pl-light-100/dashboard-settings.png) | Opened; readable text, visible semantic states/counts, complete compact overview |
| PL / Dark / 150%, 1536x1024, DPI96 | [Downloads](artifacts/dashboard-visual-first/34-pl-dark-150/dashboard-downloads.png), [Batch](artifacts/dashboard-visual-first/34-pl-dark-150/dashboard-batch.png), [Settings](artifacts/dashboard-visual-first/34-pl-dark-150/dashboard-settings.png) | Opened; icon sidebar, inspector drawer, stacked content and vertical scrolling rather than shrinking text |
| EN / Dark / 200%, 1920x1080, DPI96 | [Downloads](artifacts/dashboard-visual-first/35-en-dark-200/dashboard-downloads.png), [Batch](artifacts/dashboard-visual-first/35-en-dark-200/dashboard-batch.png) | Opened; large text remains legible, fewer rows visible, lower controls require scrolling |
| PL / Dark / TextScale100, physical Windows225%, 5120x2880, DPI216 | [Downloads](artifacts/dashboard-visual-first/36-pl-dark-5k225/dashboard-downloads.png), [Batch](artifacts/dashboard-visual-first/36-pl-dark-5k225/dashboard-batch.png), [Settings](artifacts/dashboard-visual-first/36-pl-dark-5k225/dashboard-settings.png) | Opened; full native extent, no black off-monitor capture region; wider logical viewport exposes more table rows |

Additional queue states: [empty queue](artifacts/dashboard-visual-first/38-empty/dashboard-downloads.png), [100-entry queue](artifacts/dashboard-visual-first/39-switch20-many/dashboard-downloads.png), [second page](artifacts/dashboard-visual-first/39-switch20-many/queue-page2.png). The last set completed 20 Classic/Modern switches with canonical queue identity preserved.

The separate layout run 52 verifies and captures [inspector custom range](artifacts/dashboard-visual-first/52-layout/inspector-options-custom.png), [the same options after switching to Light](artifacts/dashboard-visual-first/52-layout/inspector-theme-changed.png), and [URL analysis with custom range](artifacts/dashboard-visual-first/52-layout/analysis-custom.png). These originals were opened: range text is readable in both themes; controls fit horizontally; the inspector uses vertical scrolling where needed. Apply persists into the canonical queue and the theme returns to Dark.

Run 53 captures [controlled active transfer](artifacts/dashboard-visual-first/53-transfer/controlled-active-transfer.png) through the real queue/executor/progress route. It uses FakeTool in an isolated profile, not public Internet media. The subsequent transfer-completion assertion passed.

Additional native states are recorded in [VALIDATION_FINAL.md](VALIDATION_FINAL.md). Local PNG/log evidence is intentionally outside the source package under ignored `artifacts`; the Markdown reports are versioned. Preserve this worktree's evidence directory for reviewing these links.
