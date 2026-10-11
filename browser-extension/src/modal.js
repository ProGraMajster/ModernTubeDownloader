/* One confirmation flow for watch videos, Shorts and explicit playlists. */
globalThis.MTDModal = (() => {
  const qualityOptions = [
    ['best', 'qualityBest'], ['2160', '2160p / 4K'], ['1440', '1440p'],
    ['1080', '1080p'], ['720', '720p'], ['480', '480p'], ['360', '360p'],
    ['audio', 'qualityAudio']
  ];

  function firstText(doc, selectors) {
    for (const selector of selectors) {
      const text = doc.querySelector?.(selector)?.textContent?.trim();
      if (text) return text;
    }
    return null;
  }

  function playlistCount(doc) {
    const pattern = /\b\d[\d\s,.]*\s*(?:film(?:ów|y)?|wideo|videos?|pozycj(?:i|e))\b/i;
    for (const selector of [
      'yt-page-header-renderer #stats',
      'ytd-playlist-header-renderer #stats',
      'yt-page-header-renderer yt-content-metadata-view-model [role="text"]',
      'yt-page-header-renderer .yt-content-metadata-view-model-wiz__metadata-text'
    ]) {
      const elements = doc.querySelectorAll?.(selector) || [doc.querySelector?.(selector)].filter(Boolean);
      for (const element of elements) {
        const count = element.textContent?.trim().match(pattern)?.[0];
        if (count) return count;
      }
    }
    return null;
  }

  function presentationFor(doc, media) {
    const selectors = media.kind === 'playlist'
      ? ['yt-page-header-renderer h1', 'ytd-playlist-header-renderer h1', 'ytd-playlist-header-renderer #title']
      : media.kind === 'short'
        ? ['ytd-reel-video-renderer[is-active] #title', 'ytd-shorts yt-formatted-string#title', 'ytd-reel-player-header-renderer #title']
        : ['ytd-watch-metadata h1 yt-formatted-string', 'ytd-watch-metadata h1', 'ytd-video-primary-info-renderer h1'];
    const pageTitle = doc.title?.replace(/\s+-\s+YouTube\s*$/i, '').trim();
    const title = firstText(doc, selectors) || pageTitle || media.url;
    const count = media.kind === 'playlist' ? playlistCount(doc) : null;
    return { title, count };
  }

  function createController(doc, win, preferences = {}) {
    let backdrop = null;
    let previousFocus = null;
    let keyHandler = null;
    let updateLanguage = () => {};
    let closeTimer = null;

    function element(tag, className, text) {
      const node = doc.createElement(tag);
      node.className = className;
      if (text !== undefined) node.textContent = text;
      return node;
    }

    function close() {
      if (closeTimer !== null) win.clearTimeout?.(closeTimer);
      closeTimer = null;
      if (!backdrop) return;
      doc.removeEventListener?.('keydown', keyHandler, true);
      backdrop.remove();
      backdrop = null;
      if (previousFocus?.isConnected !== false) previousFocus?.focus?.();
      previousFocus = null;
      keyHandler = null;
      updateLanguage = () => {};
    }

    function open(media, opener) {
      if (!MTDContext.check() || !media || !doc.body) return false;
      close();
      previousFocus = opener || doc.activeElement;
      const details = presentationFor(doc, media);
      const isPlaylist = media.kind === 'playlist';
      const root = element('div', 'mtd-modal-backdrop');
      root.id = 'mtd-modal-backdrop';
      const dialog = element('section', 'mtd-modal-dialog');
      dialog.setAttribute('role', 'dialog');
      dialog.setAttribute('aria-modal', 'true');
      dialog.setAttribute('aria-labelledby', 'mtd-modal-heading');
      dialog.setAttribute('aria-describedby', 'mtd-modal-description');

      const header = element('div', 'mtd-modal-header');
      const heading = element('h2', 'mtd-modal-heading', isPlaylist ? MTDI18n.t('addPlaylist') : MTDI18n.t('addMedia'));
      heading.id = 'mtd-modal-heading';
      const closeButton = element('button', 'mtd-modal-close', '×');
      closeButton.type = 'button';
      closeButton.setAttribute('aria-label', MTDI18n.t('close'));
      closeButton.addEventListener('click', close);
      header.append(heading, closeButton);

      const kind = element('p', 'mtd-modal-kind', isPlaylist ? MTDI18n.t('playlist') : media.kind === 'short' ? MTDI18n.t('short') : media.kind === 'live' ? MTDI18n.t('live') : MTDI18n.t('video'));
      const title = element('p', 'mtd-modal-media-title', details.title);
      title.title = details.title;
      const description = element('p', 'mtd-modal-description', isPlaylist
        ? MTDI18n.t('playlistDescription')
        : MTDI18n.t('mediaDescription'));
      description.id = 'mtd-modal-description';
      const count = details.count ? element('p', 'mtd-modal-count', details.count) : null;
      const liveHint = media.kind === 'live' ? element('p', 'mtd-modal-description',
        MTDI18n.t('liveHint')) : null;

      const qualityLabel = element('label', 'mtd-modal-quality-label', isPlaylist ? MTDI18n.t('defaultQuality') : MTDI18n.t('quality'));
      qualityLabel.setAttribute('for', 'mtd-modal-quality');
      const quality = element('select', 'mtd-modal-quality');
      quality.id = 'mtd-modal-quality';
      for (const [value, label] of qualityOptions) {
        const option = element('option', '', value === 'best' || value === 'audio' ? MTDI18n.t(label) : label);
        option.value = value;
        quality.append(option);
      }
      const savedQuality = preferences.getQuality?.();
      quality.value = MTDUrls.qualities.has(savedQuality) ? savedQuality : 'best';
      const qualityHint = element('p', 'mtd-modal-quality-hint',
        MTDI18n.t('qualityHint'));

      const actions = element('div', 'mtd-modal-actions');
      const cancel = element('button', 'mtd-modal-cancel', MTDI18n.t('cancel'));
      cancel.type = 'button';
      cancel.addEventListener('click', close);
      const openInApp = element('a', 'mtd-modal-open', MTDI18n.t('openApp'));
      openInApp.setAttribute('role', 'button');
      const confirm = element('a', 'mtd-modal-confirm', isPlaylist ? MTDI18n.t('queuePlaylist') : MTDI18n.t('queue'));
      updateLanguage = () => {
        heading.textContent = MTDI18n.t(isPlaylist ? 'addPlaylist' : 'addMedia');
        closeButton.setAttribute('aria-label', MTDI18n.t('close'));
        kind.textContent = MTDI18n.t(isPlaylist ? 'playlist' : media.kind === 'short' ? 'short' : media.kind === 'live' ? 'live' : 'video');
        description.textContent = MTDI18n.t(isPlaylist ? 'playlistDescription' : 'mediaDescription');
        if (liveHint) liveHint.textContent = MTDI18n.t('liveHint');
        qualityLabel.textContent = MTDI18n.t(isPlaylist ? 'defaultQuality' : 'quality');
        quality.children[0].textContent = MTDI18n.t('qualityBest');
        quality.children[7].textContent = MTDI18n.t('qualityAudio');
        qualityHint.textContent = MTDI18n.t('qualityHint');
        cancel.textContent = MTDI18n.t('cancel'); openInApp.textContent = MTDI18n.t('openApp');
        confirm.textContent = MTDI18n.t(isPlaylist ? 'queuePlaylist' : 'queue');
      };
      confirm.setAttribute('role', 'button');
      const updateTargets = () => {
        confirm.href = MTDUrls.protocolUrl(media.url, { quality: quality.value, container: 'auto', mode: 'queue' }) || '#';
        openInApp.href = MTDUrls.protocolUrl(media.url, { quality: quality.value, container: 'auto', mode: 'analyze' }) || '#';
      };
      updateTargets();
      quality.addEventListener('change', () => {
        if (!MTDContext.check()) return;
        const value = MTDUrls.qualities.has(quality.value) ? quality.value : 'best';
        quality.value = value;
        preferences.setQuality?.(value);
        updateTargets();
      });
      const launch = event => {
        if (!MTDContext.check()) { event.preventDefault(); close(); return; }
        // A SPA navigation must not turn an old modal into a request for another film.
        if (MTDUrls.detect(win.location.href)?.url !== media.url) {
          event.preventDefault();
          close();
          return;
        }
        preferences.setQuality?.(quality.value);
        updateTargets();
        if(globalThis.MTDCookieClient?.prepareClick(event,win,event.currentTarget.href,media.url,close,
          () => backdrop === root && MTDUrls.detect(win.location.href)?.url === media.url)) return;
        // Keep the trusted anchor click intact; the custom protocol has no acknowledgement.
        closeTimer = win.setTimeout(close, 0);
      };
      openInApp.addEventListener('click', launch);
      confirm.addEventListener('click', launch);
      actions.append(cancel, openInApp, confirm);
      dialog.append(header, kind, title, description);
      if (count) dialog.append(count);
      if (liveHint) dialog.append(liveHint);
      dialog.append(qualityLabel, quality, qualityHint, actions);
      root.append(dialog);
      root.addEventListener('click', event => { if (event.target === root) close(); });

      const focusable = [closeButton, quality, cancel, openInApp, confirm];
      keyHandler = event => {
        if (event.key === 'Escape') {
          event.preventDefault();
          close();
        } else if (event.key === ' ' && (doc.activeElement === confirm || doc.activeElement === openInApp)) {
          event.preventDefault();
          doc.activeElement.click();
        } else if (event.key === 'Tab') {
          const current = focusable.indexOf(doc.activeElement);
          if (event.shiftKey && current <= 0) {
            event.preventDefault();
            focusable.at(-1).focus();
          } else if (!event.shiftKey && (current === focusable.length - 1 || current < 0)) {
            event.preventDefault();
            closeButton.focus();
          }
        }
      };
      doc.addEventListener?.('keydown', keyHandler, true);
      doc.body.append(root);
      backdrop = root;
      closeButton.focus();
      return true;
    }

    MTDContext.own(close);
    return { open, close, updateLanguage: () => { if(MTDContext.check())updateLanguage(); }, isOpen: () => backdrop !== null };
  }

  return { createController, presentationFor };
})();
