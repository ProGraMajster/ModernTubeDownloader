# Application updates

`ApplicationUpdateService` checks **ProGraMajster/ModernTubeDownloader GitHub Releases**,
not master. Stable selects the API's latest public non-prerelease, verifies the exact
repository/tag and assets `ModernTubeDownloader-X.Y.Z-win-x64.zip` / `.zip.sha256`.
The ZIP's SHA-256 must match before acceptance and is checked again by the updater.
Transport is HTTPS. Checksums are integrity checks, not a separate publisher signature.

Settings → Application updates: automatic checks (startup, at most daily by default;
daily/weekly), Notify only / Auto-download / Install on exit. Current/latest version,
original release notes, Check/Download/Later/Skip and Install now are localized.
Later and skipped version are persisted; they do not permit silent installation.
Metadata checks have a 30-second deadline. Network/timeout failures are logged and
shown as Failed; they do not terminate the recurring monitor or block manual retry.
State and staging live in AppData/ModernTubeDownloader/Updates, never the tool store.

`ModernTubeDownloader.Updater` has no UI, framework, worker, authentication or registry
dependencies. The GUI copies its files to staging, then launches a request with the
current PID and start-time identity. The updater waits for actual parent exit,
verifies/extracts again, journals each file's backup before replacement and rolls back
on copying failure. One private `.mtd-update-backup` holds the prior managed files.
An interrupted incomplete journal is recovered before the next transaction.
Only validated package files are changed; unlisted installation files are preserved.
No AppData, queues, history, LIVE, cookies, registrations, logs, downloads, certificates
or appearance profiles are updated. Install now uses the normal work warning/shutdown.
Install-on-exit never fires on Hide-to-tray.

ZIP validation rejects traversal, absolute/ADS/device/invalid paths, symlinks,
case-insensitive duplicates, unknown root directories and user-data files. File count,
compressed/uncompressed sizes and depth are bounded. Existing reparse points are
rejected. Failed updates leave the old installation intact and record diagnostics.
The optional restart goes through the application's normal single-instance startup.

Validation must include the real updater process against an isolated synthetic
installation. Never use a user's existing installation as an update fixture.
The stable 1.2.0 build checks public releases only; beta/candidate channels are not
supported. Installed 1.2.0 must not offer 1.2.0 again. Portable folder moves require
explicit URI/Native Messaging/autostart registration repair as applicable; the updater
does not silently rewrite registry settings. Clean-PC restart and prolonged staging
retention qualification remain post-1.2 QA.

Sources: [GitHub Releases API](https://docs.github.com/en/rest/releases/releases)
and [.NET ZIP archive API](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchive).
