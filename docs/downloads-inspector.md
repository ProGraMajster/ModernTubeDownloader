# Modern Downloads inspector

Details and Options share the same controls and uncommitted draft. Details exposes format, quality and the existing subtitles/advanced dialog. Options additionally exposes the full/custom range and advanced action. Switching tabs, changing theme, or refreshing the selected job preserves that draft; choosing a different job reloads its saved options.

Only a Queued job with available format metadata is editable. Apply uses the existing range and compatibility validation, format selector, and `DownloadQueueService.Update(..., persist: true)`. The queue status is checked again inside the update lock, so a job that starts before Apply cannot be changed by a stale editor. No new queue, persistence or undo mechanism is introduced.

Audio is derived from the selected format metadata; it is not a separate track selector. Folder is the final file's directory or the global output directory, with the existing folder action. Both values remain read-only. Active, interrupted, failed and completed jobs expose read-only options with an explanation. An existing completed file can be opened through the existing shell service. There is no per-item folder override, per-item transfer pause, or post-completion conversion contract here.

The title and source support Ctrl+C and F1. The native read-only folder text box supports text selection/copy and F1 for its full path. F1 explicitly opens the same managed MFN tooltip; this does not qualify automatic hover timing. No extra accessibility tree is used.

The pinned Windows MFN raw input path discards control characters in `WM_CHAR`, while its adapter handles Tab in `RaiseKeyPress`. The queue inspector therefore passes unhandled Tab/Shift+Tab from KeyDown to the existing root `SelectNextControl` method, respecting enabled/visible/tab-stop controls and marking the event handled. This is scoped to the Modern queue inspector, including its range editor. An upstream fix should unify the raw Windows and surface Tab paths and test a single focus move per key, reverse traversal, skipped controls, and text editors that accept tabs. The pinned framework sources are unchanged.

Downloads labels explicitly bound to Segoe UI Semibold use the existing Unicode painter and measurer with their weight-600 typeface. Passing MFN's inferred Bold flag would promote that typeface to weight 700. Other label recipes and Classic retain their existing paths.
