globalThis.MTDContent = (() => {
  const buttonId = 'mtd-download-button';
  const label = 'Pobierz w ModernTubeDownloader';

  async function withExtensionContext(operation, fallback) {
    try {
      return await operation();
    } catch (error) {
      if (/^extension context invalidated\.?$/i.test(String(error?.message || error))) return fallback;
      throw error;
    }
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
    let started = false;
    let quality = 'best';
    const modal = MTDModal.createController(doc, win, {
      getQuality: () => quality,
      setQuality: value => {
        quality = MTDUrls.qualities.has(value) ? value : 'best';
        void persistQuality(quality);
      }
    });

    function visible(element) {
      if (element.closest?.('[hidden], [aria-hidden="true"]')) return false;
      const rect = element.getBoundingClientRect?.();
      return !rect || (rect.width > 0 && rect.height > 0);
    }

    function firstVisible(selectors) {
      for (const selector of selectors) {
        const matches = doc.querySelectorAll?.(selector) || [doc.querySelector(selector)].filter(Boolean);
        for (const element of matches) if (visible(element)) return element;
      }
      return null;
    }

    function playlistRowHasRoom(row, host) {
      const nativeItems = [...row.children].filter(child => child.id !== buttonId);
      const last = nativeItems[nativeItems.length - 1];
      const right = last?.getBoundingClientRect?.().right;
      const limit = host.parentElement?.getBoundingClientRect?.().right;
      return right != null && limit != null && limit - right >= 48;
    }

    function watchMenuHasRoom(menu) {
      const menuRect = menu.getBoundingClientRect?.();
      const nativeItems = [...menu.children]
        .filter(child => child.id !== buttonId)
        .map(child => child.getBoundingClientRect?.())
        .filter(rect => rect?.width > 0 && rect.height > 0);
      if (!menuRect || nativeItems.length === 0) return false;
      return menuRect.right - Math.max(...nativeItems.map(rect => rect.right)) >= 112;
    }

    function findAnchor(kind) {
      if (kind === 'video' || kind === 'live') {
        const actions = firstVisible([
          'ytd-watch-flexy ytd-watch-metadata #actions',
          'ytd-watch-metadata #actions'
        ]);
        if (actions && actions.querySelector?.('ytd-menu-renderer, #menu')) {
          const menu = actions.querySelector('ytd-menu-renderer');
          if (menu && visible(menu) && watchMenuHasRoom(menu))
            return { anchor: menu, placement: 'watch-menu' };
          return { anchor: actions, placement: 'watch' };
        }
        const fallback = firstVisible([
          'ytd-watch-metadata #actions-inner',
          'ytd-watch-metadata #menu',
          'ytd-video-primary-info-renderer #menu'
        ]);
        return fallback && { anchor: fallback, placement: 'stack' };
      }

      if (kind === 'short') {
        const actions = firstVisible([
          'ytd-shorts ytd-reel-video-renderer reel-action-bar-view-model',
          'ytd-reel-video-renderer ytd-reel-player-overlay-renderer reel-action-bar-view-model',
          'ytd-reel-video-renderer #actions',
          'ytd-reel-player-overlay-renderer #actions'
        ]);
        return actions && { anchor: actions, placement: 'shorts' };
      }

      if (kind === 'playlist') {
        const host = firstVisible([
          'yt-page-header-renderer yt-flexible-actions-view-model',
          'ytd-playlist-header-renderer yt-flexible-actions-view-model'
        ]);
        if (host) {
          const row = host.querySelector('.ytFlexibleActionsViewModelActionRow');
          if (row && playlistRowHasRoom(row, host))
            return { anchor: row, placement: 'playlist-inline' };
          return { anchor: host, placement: 'playlist-stack' };
        }
        const legacy = firstVisible([
          'ytd-playlist-header-renderer #buttons',
          'ytd-playlist-header-renderer ytd-menu-renderer'
        ]);
        return legacy && { anchor: legacy, placement: 'playlist-inline' };
      }
      return null;
    }

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
      button.title = label;
      button.setAttribute('aria-label', label);
      const icon = doc.createElement('img');
      icon.alt = '';
      icon.setAttribute('aria-hidden', 'true');
      icon.src = typeof chrome === 'undefined' ? '' : chrome.runtime.getURL('assets/icon48.png');
      button.append(icon);
      const caption = doc.createElement('span');
      caption.textContent = 'Pobierz';
      caption.className = 'mtd-watch-caption';
      button.append(caption);
      button.addEventListener('click', () => {
        const media = MTDUrls.detect(win.location.href);
        if (media) modal.open(media, button);
      });
      return button;
    }

    function reconcile() {
      scheduled = false;
      if (!started) return;
      const media = MTDUrls.detect(win.location.href);
      let button = doc.getElementById(buttonId);
      if (!media) {
        modal.close();
        button?.remove();
        disconnectAnchor();
        return;
      }
      const target = findAnchor(media.kind);
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
      if (!started || scheduled) return;
      scheduled = true;
      win.setTimeout(reconcile, 50);
    }

    function start() {
      if (started) return;
      started = true;
      if (doc.body) {
        pageObserver = new MutationObserver(() => {
          if (MTDUrls.detect(win.location.href) && !doc.getElementById(buttonId)) schedule();
        });
        pageObserver.observe(doc.body, { childList: true, subtree: true });
      }
      void loadQuality().then(value => { quality = MTDUrls.qualities.has(value) ? value : 'best'; schedule(); });
      win.addEventListener('yt-navigate-finish', schedule);
      win.addEventListener('popstate', schedule);
      win.addEventListener('resize', schedule);
      schedule();
    }

    function stop() {
      started = false;
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

    return { start, stop, reconcile, setQuality, modal };
  }

  if (typeof chrome !== 'undefined' && chrome.runtime?.sendMessage) {
    const storage = createStorageBridge(chrome.storage);
    const controller = createController(document, window, storage.load, storage.save);
    chrome.storage.onChanged.addListener((changes, area) => {
      if (area === 'local' && changes.quality)
        controller.setQuality(changes.quality.newValue);
    });
    controller.start();
  }
  return { createController, createStorageBridge };
})();
