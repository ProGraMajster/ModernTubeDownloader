# Tray and Windows startup

The tray is owned by `TrayApplicationController`, using ModernFormsNext's Windows
`NotifyIcon` / `NotifyIconContextMenu`. It is independent of system notifications.
Menu counters refresh at most once a second. Queue pause/resume uses the canonical
queue; LIVE remains independently scheduled. Hiding never stops these services.

Settings → Startup and system tray offers normal/minimize-to-tray, Exit/Hide/Ask,
first-hide notice and optional startup with Windows. Existing Exit/normal-minimize
defaults are preserved. Exiting with pending or active queue/LIVE or Web Remote
offers Cancel, Hide and Exit anyway. Disabling or failing tray restores the window.

Startup registration changes only `ProGraMajster.ModernTubeDownloader` in
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, on an explicit Apply action.
No services, scheduled tasks, HKLM or elevation. The quoted current executable
receives `--autostart`, optionally `--start-hidden`; a stale portable path is
reported and repaired explicitly. Foreign commands are not replaced/deleted.

Hidden startup initializes the normal services and UI dispatcher without first
showing/activating the main window. Manual relaunch, URL and notification activation
use the existing single-instance pipe to restore the same application. Autostart
of an already running instance does not activate it. If tray is unavailable, the
window stays accessible. No second lifetime or IPC tree exists.

Native QA: `scripts/Test-Automation.ps1 -TraySmoke -Language pl -Theme Dark`
and EN/Light. This uses temporary settings and never writes the user's HKCU Run
entry. Debug QA protocol registration is also suppressed. Actual Windows login
startup and shell-menu gestures still need manual confirmation.
