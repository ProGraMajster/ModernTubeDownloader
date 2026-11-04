# Modern Dashboard: implemented modules and next stages

Working evidence, 20 October 2026. This document does not mark the Draft PR ready or constitute visual acceptance. All QA media and histories described below belong to temporary `mtd-automation-smoke-*` profiles.

## Implemented

### Thumbnail preview and export

The Other page opens a real native tool. A supported source URL is analyzed by the existing metadata service/process runner. The canonical thumbnail cache supplies an independently owned preview. PNG and JPEG exports use Skia encoding, a Windows save dialog, unique temporary files and an atomic non-overwriting commit. The tool reports cancellation, unsupported input, missing images, download/decoding failures, existing destinations and write errors. A history-write failure after image commit reports success with a history warning, rather than inviting a duplicate export.

Limits: 12 MiB encoded data, 8192 pixels per dimension, 16 megapixels decoded, 30 recent export records. An explicit preview works when automatic queue thumbnails are disabled. The tool owns and disposes its bitmap; it does not create another downloader, queue, process runner or settings store. It is available in Classic as well as Modern.

Evidence so far: service tests cover real PNG/JPEG codec and size, safe filenames, non-overwrite, missing directories, invalid URL/extension, cancellation of an in-flight request, excessive encoded and decoded dimensions, history reload and failure after successful commit. Native runs `24-populated.log` and `25-populated.log` completed both exports through the actual tool UIA actions and Windows HWND controls, decoded 960x540 output, and verified persistent records. The system dialog's UIA descendants were not qualified; its HWND interaction is a separate evidence lane. The complete runs subsequently stopped at the Explorer gesture, so these are module passes, not whole-run passes.

Runs `29-populated.log`, `31-populated.log` and the final `56-final-populated.log` subsequently completed the entire native suite, including both exports and restart. Export history reads are bounded to 1 MiB of actual bytes and allow atomic replacement; concurrent overview refreshes discard stale results. Full build validation passes 864/864 Debug and Release tests and 92/92 JS tests.

The tool downloads/previews/saves existing thumbnails. It does not generate video frames, edit pictures, or promise a source-specific thumbnail API.

### File drop into Batch Import

The main window accepts one Unicode file on Modern Batch and Other through a small Windows `WM_DROPFILES` adapter, then calls the same bounded TXT/CSV/JSON importer as Browse. Multi-file drops produce a validation error. Classic and unrelated routes do not register an active drop target. No OLE framework implementation or second importer was copied into MTD.

The native owned-HWND test covers Unicode paths, rejection, enable/disable and detach. Populated native importer captures preserve duplicate and invalid rows. A physical Explorer gesture remains pending: another foreground application covers its source point on this host; the helper stops before injecting input when either endpoint is not owned. This is not a qualified Explorer PASS.

### Optional output facts in History

History entries can persist actual final-file bytes and ffprobe duration. VOD records the final post-processed file; segmented LIVE records the current part's duration rather than the aggregate. Existing JSON without these fields loads with unknown values. The UI uses an em dash for missing facts and does not rescan or invent facts for older history. Compatibility and persistence tests cover old/new entries and unavailable files.

### Presentation connected to existing services

Source shortcuts, shared queue actions, inspectors, LIVE actions, Batch parsing/analysis/selection summary, Other quick actions and the Settings overview use existing routes and state. All eleven Settings categories remain available. Read-only overview cards open the full existing editors. Queue output-format labels use the executor's selected format or completed output extension; audio-only jobs do not falsely claim a video container.

## Planned, not implemented

| Order | Module | Contract and dependencies | Required evidence before enabling UI |
| --- | --- | --- | --- |
| 1 | Media conversion | Reuse canonical process runner and FFmpeg/ffprobe; explicit input/output codecs, overwrite policy, owned temporary outputs, cancellation and disk-space checks | Real sample conversion, probe output, cancellation/cleanup, invalid streams, I/O failure, Classic/Modern UI |
| 2 | Media joining | Explicit compatible-stream checks; distinguish concat/remux from re-encode; preserve sources | Matching/mismatching streams, audio/video sync, partial failure, cancellation, output validation |
| 3 | Subtitle tools | First inspect/extract and convert defined SRT/VTT inputs; define timestamp and encoding semantics before any editor | Unicode, malformed cues, timing boundaries, cancellation, non-destructive output and native UI |
| 4 | Video-frame thumbnails | Bounded FFmpeg extraction at a validated time from an existing file | Rotation/aspect ratio, out-of-range time, unsupported media, output limits |
| 5 | LIVE source connectors | Per-provider public capability/API, authorization and privacy requirements; account for unavailable fields | Actual provider data and failure/rate-limit cases before chat/viewer/like counters appear |
| 6 | History notes/tags and multiselect | Separate user-authored fields and selection contract, backwards-compatible storage | Migration, persistence, keyboard/UIA selection and batch-action semantics |

These stages are not clickable placeholders in production and are not included in the current readiness claim. No release, version increment, merge or framework change is authorized by this implementation.
