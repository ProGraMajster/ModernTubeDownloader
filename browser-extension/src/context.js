/* One lifecycle owner per extension realm. Reload invalidates old content scripts,
   but does not unload their DOM, observers or promises. Never retry that realm. */
globalThis.MTDContext = (() => {
  let alive = true;
  const disposers = new Set();
  const api = typeof chrome === 'undefined' ? null : chrome;
  const identity = api?.runtime?.id;
  function invalidate() {
    if (!alive) return;
    alive = false;
    for (const dispose of [...disposers]) {
      try { dispose(); } catch (error) { console.warn('MTD cleanup failed:', error?.name || 'Error'); }
    }
    disposers.clear();
  }
  function check() {
    if (alive && identity && api?.runtime?.id !== identity) invalidate();
    return alive;
  }
  function handle(error) {
    if (/extension context invalidated/i.test(String(error?.message || error))) { invalidate(); return true; }
    return false;
  }
  function sync(operation, fallback) {
    if (!check()) return fallback;
    try { return operation(); } catch (error) { if (handle(error)) return fallback; throw error; }
  }
  async function call(operation, fallback) {
    if (!check()) return fallback;
    try { const result = await operation(); return check() ? result : fallback; }
    catch (error) { if (handle(error)) return fallback; throw error; }
  }
  function own(dispose) { if (alive) disposers.add(dispose); else dispose(); return () => disposers.delete(dispose); }
  function listen(target, event, handler, options) {
    const guarded = (...args) => { if (check()) return handler(...args); args[0]?.preventDefault?.(); };
    target?.addEventListener?.(event, guarded, options);
    own(() => target?.removeEventListener?.(event, guarded, options));
  }
  function subscribe(event, handler) {
    const guarded = (...args) => { if (check()) return handler(...args); };
    sync(() => event?.addListener(guarded));
    own(() => {
      // Removing a Chrome listener in an invalid realm would itself be an invalid API call.
      // Its guarded callback is inert until Chrome unloads it. On pagehide the live API is safe.
      if (!identity || api?.runtime?.id === identity)
        try { event?.removeListener?.(guarded); } catch (error) { if (!handle(error)) console.warn('MTD listener cleanup failed:',error?.name||'Error'); }
    });
  }
  if (typeof window !== 'undefined') window.addEventListener('pagehide', invalidate, {once:true});
  return {check,call,sync,handle,invalidate,own,listen,subscribe};
})();
