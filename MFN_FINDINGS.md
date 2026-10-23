# Framework findings and bounded application adaptations

20 October 2026. Pinned MFN: `8d8a7679779608b4edbee7dc687c13e3b1a611d9`. Its checkout remains clean. Modern and Classic use the same runtime and canonical accessibility providers.

## File drops: missing public contract at the pin

Source inspection: `ModernFormsNext/Control.States.cs` has the `AllowDrop` contract commented out; `ModernFormsNext.WindowKit.Backend.Windows/Avalonia.Win32/WindowImpl.cs` has the OLE registration block commented out. Merely exposing unmanaged declarations is not an operational public MFN drag/drop API.

MTD uses an intentionally small Windows shell-file adapter in `Infrastructure/WindowsFileDrop.cs`: attach `SetWindowSubclass` to its own top-level HWND on the UI thread; enable `DragAcceptFiles` only on Modern Batch/Other; decode a single Unicode path with a 32767-character limit; always `DragFinish`; detach on disposal/WM_NCDESTROY; forward unrelated messages through `DefSubclassProc`. The path enters the existing bounded importer. There is no message-filter/UIPI relaxation, framework patch, copied OLE subsystem or parallel semantic tree.

Minimal application repro/test: `WindowsFileDropTests` creates its own native HWND, sends an owned HDROP payload containing a Unicode filename, checks the received path and enabled/disabled/multiple-file behavior, then detaches. This is same-process native-message evidence. A physical Explorer drag was attempted with a guard that verifies both physical pointer endpoints belong to the QA app/Explorer. BeamNG.drive covered the source endpoint; the guard aborted before input. The user chose to continue other tests, so **Explorer drag/drop is NOT QUALIFIED**.

Upstream proposal: define file-drop capabilities and a bounded immutable path payload at the native backend boundary; expose enter/over/drop/leave on the canonical Control input route, with disposal, thread affinity, clipping and platform capability tests. The application adapter can then be removed. No new upstream issue or PR was submitted during this task.

API contracts: [WM_DROPFILES](https://learn.microsoft.com/en-us/windows/win32/shell/wm-dropfiles), [DragAcceptFiles](https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-dragacceptfiles), [DragQueryFileW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-dragqueryfilew), [SetWindowSubclass](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass), [DefSubclassProc](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-defsubclassproc).

## Existing text/caption findings

[MFN_DASHBOARD_REPRO.md](MFN_DASHBOARD_REPRO.md) preserves prior native evidence, extracted reproduction recipes and the historical 17 October audit. [MFN #272](https://github.com/ProGraMajster/ModernFormsNext/issues/272) tracks premature text ellipsis. Its issue status is not revalidated by this document. The small DashboardLabel/DashboardButton paint adaptations retain inherited semantic providers and restore Classic behavior. This round does not claim an upstream fix.

The Batch metric failure found in this round was an **application height-allocation bug**, not a new MFN defect: the numeral had less vertical space than its actual scaled font required. Measured layout and raster tests repair it.

## Windows save dialog evidence boundary

The thumbnail tool exposes real UIA Invoke actions. Native Windows save dialogs appeared, but their UIA descendant queries were not reliably available on this host. The export helper therefore scopes real child HWNDs to the owned process/dialog and performs filename input and native IDOK submission there. Both actual encoded outputs and their history records are verified. This proves the HWND export flow, not full common-dialog UIA accessibility. No cause was attributed to MFN without an isolated framework reproduction. Manual screen-reader and full physical keyboard acceptance remain separate qualification.
