# Batch import (1.2.0)

Open **Other → Batch import**, choose a TXT/CSV/JSON file, and import. Importing does
not download or start analysis. Analyze with 1–8 metadata workers (default 3), pause,
cancel, retry selected rows, then explicitly add ready rows to the existing VOD queue.
The analysis pool is independent of download and LIVE concurrency. Active/upcoming
LIVE and playlists require an interactive decision in their dedicated view, not an
unattended batch recording. Existing authentication and source checks are reused.

- TXT: one URL per line; blank lines and `#` comments ignored.
- CSV: quoted RFC-style fields, required `url`, optional `quality,container,start,end`.
- JSON: root array of URL strings or objects with these same fields; no `$type`,
  executable properties, external references, or unknown fields.
- Quality: best, 2160, 1440, 1080, 720, 480, 360, audio (also `1080p`, `4k`).
- Container: Auto/Mp4/Mkv/WebM. Times: seconds or `hh:mm:ss`; end must follow start.

Limits: 250,000 rows, 256 MB input, 16 KiB textual lines/JSON strings, 64 KiB JSON
objects/quoted CSV records (including multiline records). A page renders at most 20 rows; 50,000 imports do not create 50,000 controls
or tasks. Search and filters operate on data before paging. Selection survives paging.
With no selection, Analyze/Add applies to eligible rows; otherwise it applies to the
selection. Apply/remove only changes preparation rows, never canonical active jobs.

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
