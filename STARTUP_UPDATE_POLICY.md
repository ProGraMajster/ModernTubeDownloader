# Optional installation at startup

The development startup window runs above the existing worker-admission path.
On eligible foreground launches, **At every normal launch** checks once per process.
**At startup, at most once a day** keeps the existing default cadence. Daily and
weekly checks stay with the monitor when startup installation is off. Turn off
automatic checks for manual checks only. Download and installation consent stays
in Settings. An updater-initiated restart bypasses foreground preparation and
uses the monitor's retry interval.

The application checks for a pending or unreadable transaction before loading the
shared services. It preserves the journal and shows the full-package repair path.
The installer remains responsible for verifying and recovering the transaction.
The same window shows bootstrap and foreground update preparation. The updater
keeps its own native status window across process exit.

`UpdateSettings.InstallAtStartup` is an independent, opt-in Boolean. Its default is
false, including deserialization of older profiles. New profiles retain automatic
checks and InstallOnExit. Saving a policy preserves the existing version/provenance,
Skip/RemindAfter and conservative legacy migration contracts. Turning automatic
checks off also disables startup installation, without erasing the saved preference.
Both the setup wizard and Settings expose the preference and explain the distinction
from installation on exit. Settings shows the last startup preparation result.

## Admission sequence

The primary instance verifies the installation before opening the startup window.
Settings, History and the stores needed for recovery and work admission load off
the UI thread. The UI thread constructs the main form after those stores are
ready. History cards are created when the user opens History.

Shared services are created with worker admission deferred.
Tools may initialize, but queue processing, LIVE scheduling and the remote mutation
endpoint start only through `AppServices.StartWorkAsync`. This is also the normal
startup path when the new preference is off; no second runtime is created.

Only a manual foreground startup is eligible. Autostart, hidden/tray startup, an
initial external URI, Classic recovery mode, updater-driven restart and the debug
automation process are ineligible. New external activations or notification actions cancel preparation.
Any restored nonterminal queue/LIVE item (including a paused queue or interrupted
recording), unfinished Batch session or unresolved organizer operation defers it.
The scan of persisted Batch/journal state must be conservative, not restricted to
the recent-items UI. No organizer recovery is performed automatically at startup.

The foreground preparation has an eight-second budget and a Continue button. It
initializes the existing serialized updater, re-verifies an existing staged package
or checks/downloads a stable release through the same service. Work and policy are
checked again before the normal shutdown path can launch the updater. Network,
validation and deadline failures continue ordinary startup; they do not install an
unverified package. Normal configured monitoring can subsequently run independently.

## Installation and restart

Preparation never launches an installer. A successful foreground admission starts
the existing external updater while application workers are still deferred. The
updater waits for the exact parent PID/start-time to exit. Failed launch leaves the
ordinary runtime available. A final synchronous policy/work check, durable per-request
commit marker and `BeginShutdown` hand off to graceful shutdown and state saves.
The marker is required only for startup requests, before any extraction or install;
a parent crash before commitment cannot authorize installation. Manual and on-exit
requests retain their previous contract. The updater retains the official
repository/tag checks, HTTPS, SHA-256 integrity checks, archive containment, PID plus
start-time ownership, transaction journal and rollback. SHA-256 is not a separate
publisher signature. The updater runs with the user's current privileges.

If admission fails after launch, terminating the owned updater and removing its commit
marker are independent cleanup attempts. If both fail, the pending request/process
identity is retained and normal shutdown and worker admission remain blocked until
that owned process is verified gone. A MessageBoxForm explains this state; the
application does not clear evidence or proceed as if abort succeeded.

Before launching a startup installation, the existing update state records the
attempted version, timestamp, result and pending request. A pending installer, failed
installation or previously attempted startup version prevents an automatic repeat.
Manual intervention remains available. Successful installation changes the running
version, so that release is no longer offered. Skip, Later and explicit suppression
continue to prevent installation. Corrupt updater state is retained and blocks it.

During a visible install, the updater starts another instance of its own executable
in presentation mode. A one-way current-user pipe carries a versioned status frame
with an operation ID, sequence, stage and optional file counts. Both endpoints
check the peer's process ID. Closing the window dismisses it and the worker
continues independently. Without a language in the request, the worker runs
headless. Download tools can continue preparing while the user works in History,
Settings and other independent views.

## Verification boundaries

Use only synthetic package bytes and isolated installation/profile roots for tests.
Never exercise automatic installation against the owner's real installation. The
targeted suite covers policy, staging reuse, pending work, timeout, durable suppression
and no-launch preparation; real installer/rollback tests reuse the existing updater.
Physical cold-start/restart qualification and the complete release matrix remain
separate from these development checks.
