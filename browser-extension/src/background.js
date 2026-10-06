importScripts('shared.js');

const pageMenuId = 'mtd-send-page';
const linkMenuId = 'mtd-send-link';
chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: pageMenuId,
    title: 'Pobierz w ModernTubeDownloader',
    contexts: ['page'],
    documentUrlPatterns: ['*://*.youtube.com/*', '*://youtube.com/*', '*://youtu.be/*']
  });
  chrome.contextMenus.create({
    id: linkMenuId,
    title: 'Pobierz link w ModernTubeDownloader',
    contexts: ['link'],
    targetUrlPatterns: ['*://*.youtube.com/*', '*://youtube.com/*', '*://youtu.be/*']
  });
});

async function launch(sourceUrl) {
  const { quality = 'best' } = await chrome.storage.local.get({ quality: 'best' });
  const media = MTDUrls.detect(sourceUrl);
  const target = MTDUrls.protocolUrl(sourceUrl, {
    quality: MTDUrls.qualities.has(quality) ? quality : 'best',
    mode: media?.kind === 'live' ? 'open' : 'queue'
  });
  if (!target) return { ok: false, reason: 'unsupported' };
  try {
    await chrome.tabs.create({ url: target, active: false });
    return { ok: true }; // Browser/OS acceptance cannot be acknowledged by a custom URI.
  } catch {
    return { ok: false, reason: 'protocol' };
  }
}

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === pageMenuId) void launch(info.pageUrl || tab?.url);
  else if (info.menuItemId === linkMenuId) void launch(info.linkUrl);
});
