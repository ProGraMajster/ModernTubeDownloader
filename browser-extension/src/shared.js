/* Shared by MV3 contexts and Node tests; no remote code or page-provided HTML. */
globalThis.MTDUrls = (() => {
  const allowedHosts = new Set(['youtube.com', 'www.youtube.com', 'm.youtube.com', 'youtu.be']);
  const validId = value => typeof value === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(value);
  const malformedEscape = value => /%(?![0-9A-Fa-f]{2})/.test(value);

  function detectYouTube(input) {
    if (typeof input !== 'string' || input.length > 4096 || /[\u0000-\u001f\u007f]/.test(input) || malformedEscape(input)) return null;
    let url;
    try { url = new URL(input); } catch { return null; }
    if (!['https:', 'http:'].includes(url.protocol) || !allowedHosts.has(url.hostname.toLowerCase()) ||
        url.username || url.password || url.port || url.hash) return null;
    let kind;
    if (url.hostname.toLowerCase() === 'youtu.be') {
      if (!validId(url.pathname.slice(1))) return null;
      kind = 'video';
    } else if (url.pathname === '/watch') {
      if (!validId(url.searchParams.get('v'))) return null;
      kind = 'video';
    } else if (/^\/shorts\/[A-Za-z0-9_-]+\/?$/.test(url.pathname)) {
      if (!validId(url.pathname.split('/')[2])) return null;
      kind = 'short';
    } else if (/^\/live\/[A-Za-z0-9_-]+\/?$/.test(url.pathname)) {
      if (!validId(url.pathname.split('/')[2])) return null;
      kind = 'live';
    } else if (url.pathname === '/playlist') {
      if (!validId(url.searchParams.get('list'))) return null;
      kind = 'playlist';
    } else return null;
    return url.href.length <= 4096 ? { url: url.href, kind } : null;
  }

  const qualities = new Set(['best', '2160', '1440', '1080', '720', '480', '360', 'audio']);
  const containers = new Set(['auto', 'mp4', 'mkv', 'webm']);

  function detect(input) {
    const youtube = detectYouTube(input);
    if (youtube) return youtube;
    if (typeof input !== 'string' || input.length > 4096 || /[\u0000-\u001f\u007f]/.test(input) || malformedEscape(input)) return null;
    try {
      const url = new URL(input);
      return ['http:', 'https:'].includes(url.protocol) && !url.username && !url.password && url.hostname && url.href.length <= 4096
        ? {url:url.href,kind:'generic',source:url.hostname} : null;
    } catch { return null; }
  }

  function protocolUrl(input, options = {}) {
    const item = detect(input);
    const mode = options.mode || 'analyze';
    const container = options.container || 'auto';
    if (!item || !qualities.has(options.quality || 'best') || !containers.has(container) || !['queue', 'open', 'analyze'].includes(mode)) return null;
    const id = options.requestId || globalThis.crypto?.randomUUID?.();
    const query = new URLSearchParams({
      v: '2', url: item.url, quality: item.kind === 'generic' ? 'best' : options.quality || 'best', container:item.kind === 'generic' ? 'auto' : container,
      action: mode === 'queue' ? 'quick-queue' : mode === 'open' ? 'open' : 'analyze',
      playlist: item.kind === 'playlist' && mode === 'queue' ? '1' : '0', source: 'browser-extension'
    });
    if (id) query.set('id', id);
    const target = `moderntubedownloader://open?${query}`;
    return target.length <= 8192 ? target : null;
  }

  return { detect, detectYouTube, protocolUrl, qualities, containers };
})();
