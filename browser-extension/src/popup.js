globalThis.MTDPopup = (() => {
  function stateForUrl(url) {
    return MTDUrls.detect(url) ? 'ready' : 'unsupported';
  }

  async function initialize(doc = document, api = chrome) {
    const page = doc.getElementById('page');
    const launch = doc.getElementById('launch');
    const open = doc.getElementById('open');
    const status = doc.getElementById('status');
    const quality = doc.getElementById('quality');
    const stored = await api.storage.local.get({ quality: 'best' });
    quality.value = MTDUrls.qualities.has(stored.quality) ? stored.quality : 'best';
    quality.addEventListener('change', () => {
      void api.storage.local.set({ quality: quality.value });
      if (media) {
        launch.href = media.kind === 'live' ? '#' : MTDUrls.protocolUrl(media.url, { quality: quality.value, mode: 'queue' });
        open.href = MTDUrls.protocolUrl(media.url, { quality: quality.value, mode: 'open' });
      }
    });
    const [tab] = await api.tabs.query({ active: true, currentWindow: true });
    const media = MTDUrls.detect(tab?.url || '');
    page.textContent = media ? (tab.title || media.url) : 'Nieobsługiwana strona';
    page.title = media?.url || '';
    launch.href = media && media.kind !== 'live' ? MTDUrls.protocolUrl(media.url, { quality: quality.value, mode: 'queue' }) : '#';
    open.href = media ? MTDUrls.protocolUrl(media.url, { quality: quality.value, mode: 'open' }) : '#';
    launch.setAttribute('aria-disabled', media && media.kind !== 'live' ? 'false' : 'true');
    launch.hidden = media?.kind === 'live';
    open.setAttribute('aria-disabled', media ? 'false' : 'true');
    launch.textContent = media?.kind === 'playlist' ? 'Dodaj playlistę do kolejki' : 'Dodaj do kolejki';
    status.textContent = media?.kind === 'live'
      ? 'Otwórz w aplikacji, aby rozpoznać transmisję i wybrać nagrywanie lub pobranie zapisu.'
      : media ? 'Gotowe do przekazania aplikacji' : 'Otwórz film, Short lub playlistę YouTube.';
    launch.addEventListener('click', event => {
      if (!media || media.kind === 'live') { event.preventDefault(); return; }
      launch.href = MTDUrls.protocolUrl(media.url, { quality: quality.value, mode: 'queue' });
      status.textContent = 'Podjęto próbę otwarcia aplikacji. Potwierdź ją w przeglądarce, jeśli pojawi się pytanie.';
    });
    open.addEventListener('click', event => {
      if (!media) { event.preventDefault(); return; }
      open.href = MTDUrls.protocolUrl(media.url, { quality: quality.value, mode: 'open' });
      status.textContent = 'Aplikacja przeanalizuje materiał i pokaże dokładne opcje.';
    });
  }

  if (typeof document !== 'undefined' && typeof chrome !== 'undefined')
    void initialize();
  return { stateForUrl, initialize };
})();
