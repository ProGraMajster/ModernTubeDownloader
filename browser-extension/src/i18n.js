/* One preference and the existing Chrome catalogs serve every extension surface.
   Chrome getMessage cannot select a locale; overrides load those same packaged catalogs. */
globalThis.MTDI18n = (() => {
  const defaults = Object.freeze({extensionLanguage:'auto', quality:'best',
    showInjectedButtons:true, contextMenusEnabled:true, defaultAction:'analyze'});
  const listeners = new Set(), catalogs = new Map();
  let preference = 'auto', catalog = null, revision = 0;
  const normalize = value => ['auto','pl','en'].includes(value) ? value : 'auto';
  const detectedLanguage = MTDContext.sync(() => chrome.i18n.getUILanguage(), 'en').toLowerCase().startsWith('pl') ? 'pl' : 'en';
  const snapshot = new Map();
  MTDContext.own(() => { revision++; listeners.clear(); });
  function t(key, substitutions) {
    const entry = catalog?.[key];
    if (!entry) {
      const cacheKey = JSON.stringify([key, substitutions]);
      if (!snapshot.has(cacheKey) && MTDContext.check())
        snapshot.set(cacheKey, MTDContext.sync(() => chrome.i18n.getMessage(key, substitutions), key) || key);
      return snapshot.get(cacheKey) || key;
    }
    const args = Array.isArray(substitutions) ? substitutions : substitutions === undefined ? [] : [substitutions];
    return entry.message.replace(/\$([a-z_]+)\$/gi, (_, name) => entry.placeholders?.[name.toLowerCase()]?.content || '')
      .replace(/\$(\d+)/g, (_, number) => args[Number(number)-1] ?? '').replace(/\$\$/g,'$');
  }
  async function load(value) {
    const current = ++revision, next = normalize(value);
    let messages = null;
    const locale = next === 'auto' ? detectedLanguage : next;
    if (typeof fetch === 'function' && MTDContext.check()) {
      if (!catalogs.has(locale)) {
        const url = MTDContext.sync(() => chrome.runtime.getURL(`_locales/${locale}/messages.json`));
        if (!url) return;
        const response = await fetch(url);
        if (!response.ok) throw new Error('Locale catalog unavailable');
        catalogs.set(locale, await response.json());
      }
      messages = catalogs.get(locale);
    }
    if (current !== revision || !MTDContext.check()) return;
    preference = next; catalog = messages;
    for (const listener of listeners) listener();
  }
  const ready = chrome.storage?.local ? MTDContext.call(() => chrome.storage.local.get({extensionLanguage:'auto'}), {extensionLanguage:'auto'})
    .then(value => load(value.extensionLanguage)).catch(error => {
      if (!/^extension context invalidated\.?$/i.test(String(error?.message || error)))
        console.warn('MTD language unavailable:', error?.name || 'Error');
    }) : Promise.resolve();
  MTDContext.subscribe(chrome.storage?.onChanged, (changes, area) => {
    if (area === 'local' && changes.extensionLanguage)
      void load(changes.extensionLanguage.newValue).catch(error => {
        if (!/^extension context invalidated\.?$/i.test(String(error?.message || error)))
          console.warn('MTD language update failed:', error?.name || 'Error');
      });
  });
  return {defaults,ready,t,get preference(){return preference},
    get language(){return preference === 'auto' ? detectedLanguage : preference},
    subscribe(listener){listeners.add(listener);return () => listeners.delete(listener)},
    async select(value){await MTDContext.call(() => chrome.storage.local.set({extensionLanguage:normalize(value)}));if(MTDContext.check())await load(value)},
    apply(doc){
      if(doc.documentElement)doc.documentElement.lang=this.language;
      for(const element of doc.querySelectorAll?.('[data-i18n]')||[])element.textContent=t(element.dataset.i18n);
      for(const element of doc.querySelectorAll?.('[data-i18n-aria]')||[])element.setAttribute('aria-label',t(element.dataset.i18nAria));
    }
  };
})();
