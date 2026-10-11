globalThis.MTDContent = (() => {
  const buttonId = 'mtd-download-button';

  async function withExtensionContext(operation, fallback) {
    return MTDContext.call(operation, fallback);
  }

  function createStorageBridge(storage) {
    return {
      load: () => withExtensionContext(async () => (await storage.local.get({ quality: 'best' })).quality, 'best'),
      save: value => withExtensionContext(() => storage.local.set({ quality: value }))
    };
  }

  function createController(doc, win, loadQuality = async () => 'best', persistQuality = async () => {}) {
    let pageObserver = null;
    let anchorObserver = null;
    let observedAnchor = null;
    let scheduled = false;
    let timer = null;
    let started = false;
    let quality = 'best';
    let enabled = true;
    const adapter = MTDSiteAdapters.YouTube(doc, win);
    const modal = MTDModal.createController(doc, win, {
      getQuality: () => quality,
      setQuality: value => {
        quality = MTDUrls.qualities.has(value) ? value : 'best';
        if (MTDContext.check()) void persistQuality(quality).catch(error => console.warn('MTD quality save failed:', error?.name || 'Error'));
      }
    });

    function disconnectAnchor() {
      anchorObserver?.disconnect();
      anchorObserver = null;
      observedAnchor = null;
    }

    function observeAnchor(anchor) {
      if (observedAnchor === anchor) return;
      disconnectAnchor();
      observedAnchor = anchor;
      anchorObserver = new MutationObserver(records => {
        const changed = records.some(record =>
          [...record.addedNodes, ...record.removedNodes].some(node => node.id !== buttonId));
        if (changed || !doc.getElementById(buttonId)) schedule();
      });
      anchorObserver.observe(anchor, { childList: true, subtree: true });
      if (anchor.parentElement)
        anchorObserver.observe(anchor.parentElement, { childList: true });
    }

    function createButton() {
      const button = doc.createElement('button');
      button.id = buttonId;
      button.type = 'button';
      button.title = MTDI18n.t('downloadTitle');
      button.setAttribute('aria-label', button.title);
      const icon = doc.createElement('img');
      icon.alt = '';
      icon.setAttribute('aria-hidden', 'true');
      icon.src = typeof chrome === 'undefined' ? '' : MTDContext.sync(() => chrome.runtime.getURL('assets/icon48.png'), '');
      button.append(icon);
      const caption = doc.createElement('span');
      caption.textContent = MTDI18n.t('download');
      caption.className = 'mtd-watch-caption';
      button.append(caption);
      button.addEventListener('click', () => {
        if (!MTDContext.check()) return;
        const media = adapter.getCurrentMedia();
        if (media) modal.open(media, button);
      });
      return button;
    }

    function reconcile() {
      scheduled = false;
      if (!started || !MTDContext.check()) return;
      const media = adapter.getCurrentMedia();
      let button = doc.getElementById(buttonId);
      if (!enabled || !media) {
        modal.close();
        button?.remove();
        disconnectAnchor();
        return;
      }
      const target = adapter.findInsertionPoint(media);
      if (!target) {
        modal.close();
        button?.remove();
        disconnectAnchor();
        return;
      }
      if (!button) button = createButton();
      if (button.dataset.mtdUrl && button.dataset.mtdUrl !== media.url)
        modal.close();
      button.dataset.mtdUrl = media.url;
      button.dataset.mtdPlacement = target.placement;
      const children = target.anchor.children;
      if (button.parentElement !== target.anchor || children?.[children.length - 1] !== button)
        target.anchor.append(button);
      observeAnchor(target.anchor);
    }

    function schedule() {
      if (!started || scheduled || !MTDContext.check()) return;
      scheduled = true;
      timer = win.setTimeout(reconcile, 50);
    }

    function start() {
      if (started || !MTDContext.check()) return;
      started = true;
      if (doc.body) {
        pageObserver = new MutationObserver(() => {
          if (!MTDContext.check()) return;
          if (adapter.getCurrentMedia() && !doc.getElementById(buttonId)) schedule();
        });
        pageObserver.observe(doc.body, { childList: true, subtree: true });
      }
      void loadQuality().then(value => { if (!started || !MTDContext.check()) return; quality = MTDUrls.qualities.has(value) ? value : 'best'; schedule(); })
        .catch(error => console.warn('MTD quality load failed:', error?.name || 'Error'));
      win.addEventListener('yt-navigate-finish', schedule);
      win.addEventListener('popstate', schedule);
      win.addEventListener('resize', schedule);
      schedule();
    }

    function stop() {
      started = false;
      if (timer !== null) win.clearTimeout?.(timer);
      timer = null; scheduled = false;
      modal.close();
      win.removeEventListener('yt-navigate-finish', schedule);
      win.removeEventListener('popstate', schedule);
      win.removeEventListener('resize', schedule);
      pageObserver?.disconnect();
      pageObserver = null;
      disconnectAnchor();
      doc.getElementById(buttonId)?.remove();
    }

    function setQuality(value) { quality = MTDUrls.qualities.has(value) ? value : 'best'; schedule(); }

    function setEnabled(value) { enabled = value !== false; reconcile(); }
    function updateLanguage() {
      if (!MTDContext.check()) return;
      const button = doc.getElementById(buttonId);
      if (button) {
        button.title = MTDI18n.t('downloadTitle');
        button.setAttribute('aria-label', button.title);
        button.children[1].textContent = MTDI18n.t('download');
      }
      modal.updateLanguage();
    }
    MTDContext.own(stop);
    return { start, stop, reconcile, setQuality, setEnabled, updateLanguage, modal };
  }

  if (typeof chrome !== 'undefined' && chrome.runtime?.sendMessage) {
    const storage = createStorageBridge(chrome.storage);
    const controller = createController(document, window, storage.load, storage.save);
    // No Chrome API polling: only read runtime.id to retire even an otherwise idle stale page.
    const watchdog = window.setInterval(() => MTDContext.check(), 1000);
    MTDContext.own(() => window.clearInterval(watchdog));
    MTDContext.own(MTDI18n.subscribe(controller.updateLanguage));
    MTDContext.subscribe(chrome.storage.onChanged, (changes, area) => {
      if (area !== 'local') return;
      if (changes.quality) controller.setQuality(changes.quality.newValue);
      if (changes.showInjectedButtons) controller.setEnabled(changes.showInjectedButtons.newValue);
    });
    void withExtensionContext(() => chrome.storage.local.get({showInjectedButtons:true}), {showInjectedButtons:true})
      .then(async value => { await MTDI18n.ready; await globalThis.MTDCookieClient?.ready; if (!MTDContext.check()) return; controller.setEnabled(value.showInjectedButtons); controller.start(); })
      .catch(error => console.warn('MTD content setup failed:', error?.name || 'Error'));
  }
  return { createController, createStorageBridge };
})();
