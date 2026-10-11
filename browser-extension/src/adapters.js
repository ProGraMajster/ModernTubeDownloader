/* Site adapters provide DOM anchors only. Desktop yt-dlp remains the extractor authority. */
globalThis.MTDSiteAdapters = {
  YouTube(doc, win) {
    const buttonId = 'mtd-download-button';
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


    return {name:'YouTube', canHandle:url=>!!MTDUrls.detectYouTube(url),
      getCurrentMedia:()=>MTDUrls.detectYouTube(win.location.href),
      getDisplayTitle:media=>MTDModal.presentationFor(doc,media).title,
      getPlaylistContext:media=>media.kind==='playlist',
      findInsertionPoint:media=>findAnchor(media.kind)};
  }
};
