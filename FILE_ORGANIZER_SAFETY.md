# Downloaded recording organizer

This module organizes existing local recordings. It does not download media, create a second queue, or create another History store. `AppServices.Organizer` uses the canonical `HistoryService`, `DownloadQueueService`, settings and Batch URL parser. Both shells open the same native `OrganizerForm` from Other; Batch may pass the current canonical session.

## Review and admission

1. Select one or more TXT, CSV or JSON URL lists, or open an existing Batch session. Each list has an independently editable destination. Suggested subfolders use the list name after Windows filename validation.
2. Select a local source folder and whether to scan subfolders. The current application profile and configured temporary workspace are excluded; ancestors marked System or ReparsePoint are rejected. This is not a denylist of every Windows/Program Files directory. Drive roots, UNC shares, device paths, traversal, trailing-dot/space aliases and reserved Windows names are rejected.
3. Analyze without changing files. The detailed plan contains source URL, source file, destination, evidence/status, every associated file, lengths and SHA-256 values. The UI creates at most 40 result rows, with paging, status filtering and explicit selection. The first verified match opens in the inspector; no move checkbox is selected automatically.
4. Choose ambiguous candidates only from the candidates whose source identity was proved by the scan. A URL repeated in the same list is reported as a duplicate. A recording requested by different lists requires choosing one destination. Existing destinations are conflicts; no implicit replace, merge or duplicate-copy policy exists.
5. Confirm the selected recordings and their file/byte totals in `MessageBoxForm`. The service repeats validation and hashes every source before the first move. No move starts while an earlier incomplete journal exists, even if that journal is older than the 100 recent operations shown by the UI.

Limits: 100 lists, 10,000 URL entries, 100,000 scanned files, 4 MiB per inspected metadata JSON, 200 total files per selected recording (including media and JSON) and 64 MiB per operation manifest. These are safety bounds, not supported throughput claims. A larger operation must be split explicitly. The full planner performs directory/identity indexing once; it does not rescan all History entries for every URL. State guards cache active/LIVE path sets until canonical service change notifications invalidate them.

## Matching evidence

- History and completed queue records connect a normalized source identity to an existing media path inside the selected scan tree.
- Exact adjacent `<stem>.json` or `<stem>.info.json` metadata can establish identity when it contains a valid source URL and a nonempty media ID. YouTube URL variants use the canonical Batch provider identity. Different providers are not equated by a bare ID.
- Metadata must agree with the identity and the canonical media ID when one is available. Duplicate JSON properties, corrupt JSON, mismatched IDs/providers and unreadable JSON fail validation.
- Requested quality and time range must agree with canonical records. A sidecar-only match cannot prove a specifically requested quality or clipped time range. Unknown or incompatible variants are marked for attention.
- Titles, filename substrings, fuzzy similarity and a bare ID in a filename never authorize a move. This deliberately leaves some legacy files unmatched instead of moving a plausible but unproved recording.
- Multiple verified files require explicit candidate selection. When multiple media formats share the same basename, companion association remains blocked; the UI does not guess which variant owns them.
- Active, interrupted, partial and LIVE recordings are excluded. **Completed LIVE parts are also excluded** until relocation updates the canonical LIVE session and all of its part references together. Updating only History would be insufficient.

## Metadata and companions

JSON is required by default. The user may explicitly disable that requirement and analyze again; a source identity must still be proved. Raw JSON bytes are never regenerated or rewritten.

Adjacent verified JSON moves with its recording. If no adjacent JSON exists and one canonical media ID is known, a verified central `metadata/<id>.json` is copied to the recording's destination as `<stem>.json`; the central original remains present during move and undo. Invalid or mismatched adjacent JSON blocks relocation instead of falling back to the central copy. This avoids breaking other consumers of the shared metadata store.

The relocated canonical references are History `FinalPath` and completed queue `FinalFile`. Raw JSON bytes, including any embedded historical filepath strings, are intentionally unchanged. A job's `OutputDirectory` remains its download destination policy; it is not rewritten as if the original download had targeted the organized location.

Recognized adjacent companions are subtitles (`srt`, `vtt`, `ass`, `lrc`), images (`jpg`, `jpeg`, `png`, `webp`), descriptions and chapters. They must share the exact, unique media basename, with an optional recognized language suffix for subtitles. Prefix matches such as `film-other.srt` are not companions. Every member is independently hashed, verified and journaled. Files not associated by these rules stay where they are.

## Durable execution and failure recovery

Each operation has an immutable JSON manifest under the isolated application's `Organizer` directory, a compact `.state.json` operation header and an operation-owned `.files` directory containing atomic per-file checkpoints. The manifest is written once. A file checkpoint contains its destination identity, transfer state, whether transfer began and whether undo finished. This avoids rewriting a 10,000-entry manifest after every file, which would produce quadratic disk traffic.

Journal writes use the existing atomic JSON writer with write-through and disk flush before publication. Recent-operation queries read compact state headers and retain at most 100 summaries, prioritizing unfinished operations so an older pending operation remains actionable. The headers record the immutable manifest's length and last-write timestamp; unexpected changes fail closed. A manifest with no state header is streamed for its summary (the initial-write crash window). Queries never retain 100 full manifests. The full pending-recovery guard checks all manifest IDs and fails closed on unreadable/corrupt state or orphan state files. Full manifests and per-file checkpoints are loaded only for an explicit recovery/undo. Length/time checking is an ordinary external-edit safeguard, not authentication against a hostile local process restoring timestamps.

On a single volume, the service uses a non-overwriting rename after source verification. Across volumes, it copies to an exclusive operation-owned staging file, checks the full destination SHA-256, publishes with a non-overwriting rename, and only then removes the verified source. The source is never deleted while it is the only verified copy. A forced-copy test seam exercises this exact copy path on a single physical volume.

An interrupted partial staging file is retained as evidence. Recovery verifies the original and restarts into a fresh exclusive staging name. A fully verified staging file can be published directly. Retained partial `.organizing` evidence is not removed automatically; it may be reviewed after the operation completes. This is an intentional cleanup limitation.

Transfer-start checkpoints precede file mutations, so a crash after rename/publication but before the next checkpoint can be recognized by verifying the destination. Every destination is reverified before any canonical references change. History and queue use their own existing atomic persistence, preserving record IDs and unrelated fields. These two stores cannot share one atomic filesystem transaction; a crash between them leaves an incomplete journal, and explicit recovery idempotently completes the second update. No second source of truth is introduced.

History now publishes new/relocated records to memory only after the atomic disk write succeeds. A failed History load is remembered; later saves and relocations refuse to overwrite the unreadable original with an empty list. Queue relocation persists a snapshot before publishing changed completed paths and leaves concurrent queue revisions dirty for their next ordinary checkpoint.

Cancellation and pause occur at file/copy-chunk boundaries. Cancellation after movement begins leaves a recoverable journal. Window close cancels and waits for the operation to leave its durable boundary. Application shutdown awaits `StopAsync`; startup can defer installation when `HasPendingRecoveryAsync` reports an unfinished operation. Recovery is always an explicit action, never a boot-time side effect.

## Undo

Undo preflights all destination hashes, central metadata originals and vacant original paths before its first reverse mutation. It reverses the verified file operations, restores History/queue paths, and records each completed reverse step. Changed files or occupied original paths stop undo without overwriting. Interrupted undo resumes as undo; it is not accidentally treated as a forward move. All original files are reverified before references are restored. Central metadata originals are retained.

## Boundary and qualification notes

Malformed or unreadable journal data fails closed and can block recent-journal refresh
and new moves. The UI recovers valid interrupted journals; it does not repair corrupted
journal bytes. Preserve those files for explicit investigation instead of deleting them.

Managed path checks run before planning, at preflight and immediately around mutation boundaries. Reparse points introduced after the dry run are rejected. The implementation does **not** claim a transactional filesystem sandbox against a hostile process replacing path components during the remaining check/use interval. Simultaneous external editing/deleting of these files is unsupported; detected content changes stop execution. Full handle-relative Windows identity locking and physical device-removal tests remain outside this iteration.

The targeted suite defines real isolated file fixtures for exact URL/provider matching; Unicode names; sidecar/central JSON byte preservation; subtitle/image association; duplicate and cross-list conflicts; quality/range ambiguity; missing/corrupt/mismatched/locked JSON; collisions introduced after planning; completed LIVE/active queue exclusion; History write/load failure; rename crash boundaries; failure before copy, during copy and after publication; interrupted/changed undo; junction redirection; cancellation/shutdown; old pending journals beyond the recent-100 window; and real 2,750/10,000-record plans with distinct tiny media and JSON files. A native test-host scenario exercises 10,000 URLs with 40 visible controls at 100/150/200% text scaling.

The injected copy failures exercise the production verified-copy path and durable recovery; they are **not** evidence of physical disk-full, unplugged drive or real cross-volume qualification. Planner output records elapsed time, managed-memory delta and file counts; it is not a before/after comparison with an earlier organizer, because no earlier organizer existed. Full application Debug/Release/Windows UIA/DPI matrices, physical 5K/225% DPI and release qualification remain deferred according to the current development strategy. Actual executed commands, original logs, failures, timings and screenshots belong in this iteration's `IMPLEMENTATION_REPORT.md`; this document does not declare unexecuted cases PASS.
