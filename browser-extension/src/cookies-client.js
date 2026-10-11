/* Content/popup never receive cookie records. Only a sanitized ACK crosses this boundary. */
globalThis.MTDCookieClient = (() => {
  let profiles = [];
  const update = async () => {
    profiles = (await MTDContext.call(() => chrome.storage.local.get({cookieProfiles:[]}), {cookieProfiles:[]})).cookieProfiles;
  };
  const ready = update().catch(() => {});
  MTDContext.subscribe(chrome.storage.onChanged, (changes, area) => {
    if(area === 'local' && changes.cookieProfiles) profiles = changes.cookieProfiles.newValue || [];
  });
  function prepareClick(event, win, target, url, finished = () => {}, valid = () => true) {
    const domain = new URL(url).hostname;
    if(!profiles.some(p => p.enabled && p.beforeUse && p.scopes.some(s => domain === s || domain.endsWith('.'+s)))) return false;
    event.preventDefault();
    void (async () => {
      let ack;
      try { ack = await MTDContext.call(() => chrome.runtime.sendMessage({type:'cookie-before-use',url}), {ok:false}); }
      catch { ack={ok:false}; } // Generic sync failure still offers an explicit public-material fallback.
      if(!MTDContext.check() || !valid()) return;
      // Never silently suppress public handoff, nor pretend an old snapshot has been refreshed.
      if(!ack?.ok && !win.confirm(MTDI18n.t('cookieContinue'))) return;
      if(!MTDContext.check() || !valid()) return;
      win.location.href = target;
      finished();
    })().catch(() => {}); // Context retirement is handled by MTDContext, not raw Chrome exceptions.
    return true;
  }
  return {prepareClick,ready};
})();
