# Batch import (1.2.1)

Open **Other → Batch import**, choose a TXT/CSV/JSON file, and import. Importing does
not download or start analysis. Analyze with 1–8 metadata workers (default 1), pause,
cancel, retry selected rows, then explicitly add ready rows to the existing VOD queue.
The analysis pool is independent of download and LIVE concurrency. Active/upcoming
LIVE and playlists require an interactive decision in their dedicated view, not an
unattended batch recording. Existing authentication and source checks are reused.

- TXT: one URL per line; blank lines and `#` comments ignored.
- CSV: quoted RFC-style fields, required `url`, optional `quality,container,start,end`.
- JSON: root array of URL strings or objects with these same fields, a single `url`
  object, or an object containing exactly one `urls` or `items` array; no `$type`,
  executable properties, external references, or unknown fields.
- Quality: best, 2160, 1440, 1080, 720, 480, 360, audio (also `1080p`, `4k`).
- Container: Auto/Mp4/Mkv/WebM. Times: seconds or `hh:mm:ss`; end must follow start.

Limits: 250,000 rows, 256 MB input, 16 KiB textual lines/JSON strings, 64 KiB JSON
objects/quoted CSV records (including multiline records). A page renders at most 20 rows; 50,000 imports do not create 50,000 controls
or tasks. Search and filters operate on data before paging. Selection survives paging.

JSON envelopes are streamed, not deserialized as a whole. Depth is limited to 8.
`Supported formats` in the Batch window shows copyable examples:

```json
{"url":"https://example.com/video"}
```

```json
{"urls":["https://example.com/one","https://example.com/two"]}
```

```json
{"items":[{"url":"https://example.com/video","quality":"720"}]}
```

Unsupported roots/malformed JSON show a localized format explanation; technical parser
details remain in the diagnostic log. Invalid individual rows remain visible row errors.
With no selection, Analyze/Add applies to eligible rows matching the current filter
and search across all pages. With selection, it applies only to the intersection with
that filtered set; hidden selected rows are not acted on accidentally. Apply/remove
requires explicit selection and only changes matching preparation rows, never canonical active jobs.

Sessions live under `%LOCALAPPDATA%\ModernTubeDownloader\Batch\<session-id>`:
immutable imported rows, an append-only update journal, separate metadata files and a
versioned manifest. Only the filename is retained, not the source-file path. Recent
sessions appear in Other. Restart loads paused preparation; no unattended analysis.
Deleting the original input does not lose the session. Closing Batch leaves its
service-owned analysis running; application shutdown cancels and persists it.

Exports are user-requested CSV/JSON. CSV quotes fields and neutralizes spreadsheet
formula prefixes. Exports exclude raw metadata, headers, cookies and signed media URLs;
generic source URLs lose their query/fragment. Result exports include status, title,
source and a localized error key; they are reports, not the strict import schema.
Duplicate import URLs use normalized identity (watch/shorts/live/youtu.be for the same
YouTube ID); final queue handoff also enforces canonical exact option identity.

Tests distinguish generated 10/10k/50k inputs and fake metadata from real network/UI
evidence. Consult `CANDIDATE_1_2_IMPLEMENTATION_REPORT.md` for executed checks.

## Large batches and estimates — 2026-10-18 follow-up

Import, handoff, metadata reads, edits and export run outside the UI context. Long import
and handoff operations report coalesced counters and can be cancelled. Closing the window
cancels its own import/handoff, not the service-owned analysis. Already admitted jobs remain
linked; unprocessed rows return to Ready. No new scheduler or authentication path is added.
Download concurrency defaults to **one** in existing settings; an explicitly saved user
preference is retained. New analysis sessions now also default to one worker.

Queue handoff remains in chunks of 100, but each chunk uses one journal append and
coalesced canonical checkpoints. Status reconciliation uses one copied ID lookup instead
of repeatedly searching the entire queue. Missing/removed/interrupted jobs no longer leave
rows permanently in the Queued filter. Downloads displays at most **50 queue cards** per
page, reuses cards, coalesces collection updates, and can reveal a job on another page.
All jobs remain in the canonical queue; paging does not cap the batch or change order.

The upper Batch bar shows preparation/import/handoff. The lower bar and Downloads summary
show processed queued jobs, with successful and failed/cancelled counts kept separate.
Failed jobs count as processed, not successful. Percentages from separate video/audio
streams are not combined into a fabricated monotonic media percentage.

Size is an advisory estimate from the selected streams' sizes, or bitrate and duration;
custom ranges scale it. Unknown sizes are counted explicitly, not treated as zero. This is
not a guarantee of final mux size or peak temporary/free-space requirements. The transfer
ETA is available only with known remaining sizes and a current positive transfer rate;
paused/idle/unknown cases show `—`. It is conservative and excludes merge, retries,
admission pacing and pauses. Existing disk-space preflight and runtime safety remain authoritative.

### Additional 1.2.0 log evidence

The owner identifies the supplied 14/15/18 October logs as version 1.2.0; these lack the
new version diagnostics. File dates are logger-start dates, not necessarily the last event.
No raw logs, source URLs, titles, private paths or account data are added to the repository.

| Log | Lines | Download starts / completions | Download failures | Notification COM failures |
| --- | ---: | ---: | ---: | ---: |
| 14 October (continues into 15 October) | 55,154 | 785 / 770 | 13 | 1,574 |
| 15 October (continues into 16 October) | 10,524 | 673 / 0 | 673 | 674 |
| 18 October | 46 | 0 / 0 | 0 | 2 |

These are event counts, not distinct incidents or a complete job history. The first file
also has 92 source-metadata failures and records 1,456 admissions in 18 chunks (13 of 100).
Its download failures comprise 12 HTTP 403 and one HTTP 500. No parser/import exception or
main-process crash is established in these files; long gaps are not a UI profiler.

The second file has **672 DirectoryNotFoundException failures** after a configured
destination becomes unavailable, plus one earlier external-process failure. The exact
reason the folder/drive disappeared is unknown. The shared storage latch now also handles
unavailable directories/drives, pausing new admissions instead of failing the remaining batch.
The 2,250 notification exceptions are the known Classic WinRT `0x80070490` path; the pinned
MFN notification resilience fix already handles provider fallback separately. This is not
proof of OS banner visibility or a newly introduced defect in the candidate.

Code inspection found whole-queue card reconstruction per chunk, quadratic Batch status
lookups and filter/action scope mismatch. Regressions use synthetic 1,300-row handoff,
50k imports, cancellation/checkpoint failures, missing-drive admission protection and
headless MFN control trees in PL/Dark and EN/Light. These do not replace native/manual QA
or a real 1,300-video download. Native verification is deferred per the owner's request,
pending confirmation that their separate MTD session has finished; it is not stopped,
mutated or used for tests.

Pre-release-follow-up source validation: restore and Debug/Release solution builds pass with zero
warnings/errors; **770/770 application tests in each configuration**, zero failures/skips.
Extension/Web Remote JavaScript: **92/92**. MFN remains the clean, unchanged pin
`8d8a7679779608b4edbee7dc687c13e3b1a611d9`; no framework edits or release/version actions.
Results: ignored `artifacts/validation/batch-20261018/batch-verified-{Debug,Release}.trx`.
Indicative synthetic measurements (single local run, not a throughput guarantee): 1,300-row
handoff plus refresh 533.7 ms Debug / 299.0 ms Release, with zero UI-context posts;
50k TXT import 996.3 / 412.3 ms, filter/page 8.0 / 3.9 ms. Synthetic metadata is small;
real metadata size, storage and network throughput still affect end-to-end time.

## 1.2.1 release qualification

The final release pass retains that clean MFN pin. An isolated native production Release
UIA run in EN/Light passed small TXT and JSON imports, localized persistent invalid-JSON
feedback, 1,300-row import, paging/search/filter, 60-row cross-page selection/analysis/handoff,
preparation cancellation, saved-session reopen, queue pages and work-aware graceful exit.
This uses synthetic tool metadata, not real downloads. Chunk-boundary handoff cancellation
and zero UI-context posts remain deterministic test evidence. Final exact-source suite,
CI and independently downloaded official-package results are recorded by the release workflow
and local `FINAL_RELEASE_1_2_1.md`; historical deferred/manual statuses above are not new PASS claims.
