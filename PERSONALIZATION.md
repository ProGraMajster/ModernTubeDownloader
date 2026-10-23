# Appearance and accessibility

## Optional Modern Dashboard (1.3.0 candidate)

Schema 2 adds an explicit Classic/Modern dashboard choice and a dashboard-specific
section. Schema 1 defaults to Classic. Interface previews and live selection are
in Settings → Personalization. Sidebar, inspector, widgets, density, thumbnail and
metadata choices are independent of Classic. Import/export/reset use the same
closed profile contract. A Modern Dashboard preset is available; `--classic-ui`
forces Classic for one session without rewriting the preference. See
[the dashboard guide](MODERN_DASHBOARD_UI.md) for layout, lifecycle and recovery.


The application stores presentation independently in `appearance.json` under the
application data directory. It contains no output paths, URLs, credentials or jobs.
The versioned schema rejects unsupported versions, unknown executable/reference
fields and out-of-range values. A corrupt file is preserved for diagnosis and uses
safe defaults; it is not silently overwritten during startup.

## Text

Settings → Personalization → Text supports 80–200%, including 80/90/100/110/125/150/
175/200 presets. Small = 90, Default = 100, Large = 125, Extra large = 150; numeric
input supports Custom. Reset text restores only 100%, not the rest of the profile.
Changes are live and persisted without restarting.

`AppTypography` owns BaseText/SmallText/Caption/Body/BodyStrong/Subtitle/Title/Hero
logical sizes and all interaction-state fonts. Each assignment is absolute base ×
AppTextScale, never multiplication of the current font. Row reflow expands logical
vertical space; images keep their independent sizes. No backend DPI, RenderScaling,
window scaling or render transforms are changed. Physical Windows DPI testing must
be reported separately from token calculations or native semantic smoke.

## Profiles and live preview

Settings → Personalization provides Global, Navigation, Downloads, LIVE, History,
Other, Batch, Settings and Details sections. A synthetic card/button/text/progress
preview follows semantic resources live. Valid HEX edits and control changes are
coalesced at 80 ms; there is no whole-application rebuild loop.

- Presets: Default, Compact, Large (125%), Minimal.
- Global: System/Light/Dark/Custom, seven semantic color overrides, corner radius,
  density/spacing, independent icon size, animations and intensity.
- Colors: HEX or native ColorDialog, per-token reset; low surface/text contrast
  displays a warning, not a forced override. Accent text chooses contrasting black/white.
- Navigation: three widths and icon/text or icons-only; semantic names remain.
- Downloads: thumbnails, row/analysis density, author/source/transfer facts,
  full/compact/hidden progress and always/hover actions with keyboard focus support.
- LIVE: density, thumbnails/facts, compact waiting and active emphasis. Critical
  state/actions are not hidden by cosmetic options.
- History: compact list, thumbnails, date/source/path/quality/container.
- Other: tool-card density. Batch: paged-row density, visible fields, URL width,
  optional thumbnail, status details and progress presentation.
- Settings: compact cards/section spacing. Details: tab spacing, metadata padding,
  optional thumbnail and initially collapsed technical fields (explicit reveal).

Export/import uses a user-selected JSON file, <=64 KiB and depth 10. Schema 1 has
safe defaults for missing sections (including the earlier typography-only shape).
Unknown fields, invalid enums, duplicate property names and invalid colors/ranges
are rejected. No scripts, arbitrary types, external asset/file references or paths.
Per-section/global reset uses confirmation; no download/auth settings are reset.
Only one active profile is managed now; import/export prepares future named profiles.

## Known composite-control limitation

On pinned MFN `d36015711f63945a49025675acb9aca3df60101d`, TabStripRenderer and
TabStripItem measure/render with Theme.FontSize rather than the TabControl's Font.
NumericUpDown's implicit TextBox has its own default style and does not propagate
the owning numeric control's font. The native 200% captures therefore show unchanged
Details tab labels and numeric-editor digits despite a scaled parent style. No
reflection, copied framework controls or DPI scaling workaround is used.

Minimal repro: create a TabControl with two pages and a NumericUpDown; set their
Font and all interaction-state TextFont to a 21pt font. Body controls render at
21pt; tabs/digits retain their default size. TabControl also has no public strip
height/font property. This is a framework typography-contract follow-up, not a
Details crash. Full global-text accessibility qualification remains PARTIAL.
Tracked upstream: [MFN #204](https://github.com/ProGraMajster/ModernFormsNext/issues/204)
and [MFN #205](https://github.com/ProGraMajster/ModernFormsNext/issues/205).
Owner classification for 1.2.0: **KNOWN LIMITATION / POST-1.2 FOLLOW-UP / NOT RELEASE BLOCKER**.
Polski: niektóre etykiety kart i edytory liczb nie skalują się w pełni wraz z tekstem
aplikacji. Po naprawie obu zgłoszeń zaktualizujemy pin MFN i rozważymy wydanie 1.2.1.
Physical Windows 100/125/150% DPI and exhaustive long-label clipping are not claimed.
See the implementation report for exact native evidence.
