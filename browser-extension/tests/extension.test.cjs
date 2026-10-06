const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.join(__dirname, '..', 'src');
const context = vm.createContext({ URL, URLSearchParams, console, chrome: { runtime: { getURL: file => `chrome-extension://test/${file}` } } });
for (const file of ['shared.js', 'modal.js', 'content.js', 'popup.js'])
  vm.runInContext(fs.readFileSync(path.join(root, file), 'utf8'), context, { filename: file });

function fakePage(url, title = 'A sample video - YouTube') {
  const selectors = new Map();
  const documentHandlers = new Map();
  const windowHandlers = new Map();
  const timers = [];
  const launches = [];
  let doc;
  function node(tag) {
    const handlers = new Map();
    return {
      tag, children: [], parentElement: null, dataset: {}, attributes: {}, textContent: '',
      get isConnected() { return this === doc.body || !!this.parentElement?.isConnected; },
      append(...children) {
        for (const child of children) {
          child.remove();
          child.parentElement = this;
          this.children.push(child);
        }
      },
      remove() {
        if (this.parentElement) this.parentElement.children = this.parentElement.children.filter(item => item !== this);
        this.parentElement = null;
      },
      setAttribute(name, value) { this.attributes[name] = value; },
      addEventListener(name, callback) { handlers.set(name, callback); },
      dispatch(name, event = {}) {
        const action = { target: this, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }, ...event };
        handlers.get(name)?.(action);
        if (name === 'click' && this.tag === 'a' && this.href && !action.defaultPrevented) launches.push(this.href);
        return action;
      },
      click() { return this.dispatch('click'); },
      focus() { doc.activeElement = this; },
      getBoundingClientRect() { return { width: 200, height: 40, right: 300 }; },
      querySelector() { return null; },
      closest() { return null; }
    };
  }
  doc = {
    title, activeElement: null, body: node('body'),
    createElement: node,
    querySelector(selector) { return selectors.get(selector)?.[0] || null; },
    querySelectorAll(selector) { return selectors.get(selector) || []; },
    getElementById(id) {
      function find(element) {
        if (element.id === id) return element;
        for (const child of element.children) {
          const match = find(child);
          if (match) return match;
        }
        return null;
      }
      return find(doc.body);
    },
    addEventListener(name, callback) { documentHandlers.set(name, callback); },
    removeEventListener(name) { documentHandlers.delete(name); },
    key(event) {
      const action = { defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }, ...event };
      documentHandlers.get('keydown')?.(action);
      return action;
    }
  };
  const win = {
    location: { href: url },
    setTimeout(callback, delay) { if (delay === 0) timers.push(callback); else callback(); },
    addEventListener(name, callback) { windowHandlers.set(name, callback); },
    removeEventListener(name) { windowHandlers.delete(name); }
  };
  return { doc, win, node, selectors, timers, launches, windowHandlers };
}

test('URL detection distinguishes video, Short, live route and playlist', () => {
  const detect = context.MTDUrls.detect;
  assert.equal(detect('https://www.youtube.com/watch?v=abc&list=PL123').kind, 'video');
  assert.equal(detect('https://youtu.be/abc').kind, 'video');
  assert.equal(detect('https://www.youtube.com/shorts/abc').kind, 'short');
  assert.equal(detect('https://www.youtube.com/live/abc').kind, 'live');
  assert.equal(detect('https://www.youtube.com/playlist?list=PL123').kind, 'playlist');
});

test('unsupported page and schemes are rejected', () => {
  for (const url of ['https://example.com/watch?v=abc', 'javascript:alert(1)', 'file:///tmp/x',
    'https://youtube.com.evil.test/watch?v=abc', 'https://www.youtube.com/watch?list=PL123'])
    assert.equal(context.MTDUrls.detect(url), null);
  assert.equal(context.MTDPopup.stateForUrl('https://example.com'), 'unsupported');
  assert.equal(context.MTDPopup.stateForUrl('https://youtu.be/abc'), 'ready');
  assert.equal(context.MTDPopup.stateForUrl('https://www.youtube.com/live/abc'), 'ready');
});

test('versioned protocol payload queues a single watch video at selected quality', () => {
  const target = context.MTDUrls.protocolUrl('https://www.youtube.com/watch?v=abc&list=PL123', { quality: '720' });
  const payload = new URL(target);
  assert.equal(payload.searchParams.get('url'), 'https://www.youtube.com/watch?v=abc&list=PL123');
  assert.equal(payload.searchParams.get('quality'), '720');
  assert.equal(payload.searchParams.get('container'), 'auto');
  assert.equal(payload.searchParams.get('queue'), '1');
  assert.equal(payload.searchParams.get('open'), '0');
  assert.equal(payload.searchParams.get('playlist'), '0');
  assert.equal(payload.searchParams.get('v'), '1');
  assert.equal(context.MTDUrls.protocolUrl('https://youtu.be/abc', { quality: 'ultra' }), null);
  assert.equal(context.MTDUrls.protocolUrl('https://youtu.be/abc', { container: 'avi' }), null);
  const playlist = new URL(context.MTDUrls.protocolUrl('https://www.youtube.com/playlist?list=PL123', { quality: '1080' }));
  assert.equal(playlist.searchParams.get('playlist'), '1');
  assert.equal(playlist.searchParams.get('quality'), '1080');
  const advanced = new URL(context.MTDUrls.protocolUrl('https://youtu.be/abc', { quality: '1080', mode: 'open' }));
  assert.equal(advanced.searchParams.get('queue'), '0');
  assert.equal(advanced.searchParams.get('open'), '1');
});

test('page action opens one modal and launches only after confirmation for video, Short and playlist', async () => {
  for (const [url, selector, kind, confirmation] of [
    ['https://www.youtube.com/watch?v=abc', 'ytd-watch-metadata #actions', 'FILM', 'Dodaj do kolejki'],
    ['https://www.youtube.com/shorts/abc', 'ytd-shorts ytd-reel-video-renderer reel-action-bar-view-model', 'SHORTS', 'Dodaj do kolejki'],
    ['https://www.youtube.com/live/abc', 'ytd-watch-metadata #actions', 'LIVE / REPLAY', null],
    ['https://www.youtube.com/playlist?list=PL123', 'yt-page-header-renderer yt-flexible-actions-view-model', 'PLAYLISTA', 'Dodaj playlistę do kolejki']
  ]) {
    const page = fakePage(url, 'An example - YouTube');
    const host = page.node('div');
    page.doc.body.append(host);
    host.querySelector = () => kind === 'FILM' || kind === 'LIVE / REPLAY' ? page.node('div') : null;
    page.selectors.set(selector, [host]);
    context.MutationObserver = class { observe() {} disconnect() {} };
    const stored = [];
    const controller = context.MTDContent.createController(page.doc, page.win,
      async () => '720', async value => stored.push(value));
    controller.start();
    await Promise.resolve();
    const button = page.doc.getElementById('mtd-download-button');
    assert.ok(button, kind);
    button.click();
    assert.equal(page.launches.length, 0, `${kind} button must not queue immediately`);
    assert.equal(controller.modal.isOpen(), true);
    assert.equal(page.doc.getElementById('mtd-modal-backdrop').children.length, 1);
    assert.equal(page.doc.getElementById('mtd-modal-quality').value, '720');
    assert.equal(page.doc.getElementById('mtd-modal-heading').textContent,
      kind === 'PLAYLISTA' ? 'Dodaj playlistę' : 'Dodaj materiał');
    const dialog = page.doc.getElementById('mtd-modal-backdrop').children[0];
    assert.equal(dialog.children.find(child => child.className === 'mtd-modal-kind').textContent, kind);
    const actions = dialog.children.at(-1);
    const open = actions.children[1];
    const confirm = actions.children[2];
    assert.equal(open.textContent, 'Otwórz w aplikacji');
    assert.equal(new URL(open.href).searchParams.get('open'), '1');
    assert.equal(new URL(open.href).searchParams.get('queue'), '0');
    if (confirmation) assert.equal(confirm.textContent, confirmation);
    else assert.equal(confirm, undefined, 'LIVE must open in the app for classification, not quick queue');
    const quality = page.doc.getElementById('mtd-modal-quality');
    assert.deepEqual(quality.children.map(option => option.value),
      ['best', '2160', '1440', '1080', '720', '480', '360', 'audio']);
    quality.value = '1080';
    quality.dispatch('change');
    assert.equal(stored.at(-1), '1080');
    (confirm || open).click();
    assert.equal(page.launches.length, 1);
    const payload = new URL(page.launches[0]);
    assert.equal(payload.searchParams.get('quality'), '1080');
    assert.equal(payload.searchParams.get('queue'), confirmation ? '1' : '0');
    assert.equal(payload.searchParams.get('playlist'), kind === 'PLAYLISTA' ? '1' : '0');
    assert.equal(payload.searchParams.get('url'), url);
    page.timers.splice(0).forEach(timer => timer());
    assert.equal(controller.modal.isOpen(), false);
    controller.stop();
  }
});

test('modal supports close, focus loop and stale SPA URL protection', () => {
  const page = fakePage('https://www.youtube.com/watch?v=abc');
  const opener = page.node('button');
  page.doc.body.append(opener);
  opener.focus();
  let quality = 'best';
  const modal = context.MTDModal.createController(page.doc, page.win,
    { getQuality: () => quality, setQuality: value => { quality = value; } });
  const media = context.MTDUrls.detect(page.win.location.href);
  const open = () => {
    assert.equal(modal.open(media, opener), true);
    return page.doc.getElementById('mtd-modal-backdrop');
  };
  let root = open();
  const dialog = root.children[0];
  assert.equal(dialog.attributes.role, 'dialog');
  assert.equal(dialog.attributes['aria-modal'], 'true');
  const close = dialog.children[0].children[1];
  const select = page.doc.getElementById('mtd-modal-quality');
  const [cancel, openLink, confirm] = dialog.children.at(-1).children;
  assert.equal(page.doc.activeElement, close);
  assert.equal(page.doc.key({ key: 'Tab', shiftKey: true }).defaultPrevented, true);
  assert.equal(page.doc.activeElement, confirm);
  assert.equal(page.doc.key({ key: 'Tab' }).defaultPrevented, true);
  assert.equal(page.doc.activeElement, close);
  select.value = 'audio';
  select.dispatch('change');
  assert.equal(quality, 'audio');
  cancel.click();
  assert.equal(page.doc.activeElement, opener);
  assert.equal(modal.isOpen(), false);
  root = open();
  assert.equal(page.doc.getElementById('mtd-modal-quality').value, 'audio', 'quality persists on the next opening');
  root.dispatch('click');
  assert.equal(modal.isOpen(), false);
  root = open();
  assert.equal(page.doc.key({ key: 'Escape' }).defaultPrevented, true);
  assert.equal(modal.isOpen(), false);
  root = open();
  root.children[0].children[0].children[1].click();
  assert.equal(modal.isOpen(), false);
  root = open();
  page.win.location.href = 'https://www.youtube.com/watch?v=def';
  const staleConfirm = root.children[0].children.at(-1).children[2];
  staleConfirm.click();
  assert.equal(page.launches.length, 0);
  assert.equal(modal.isOpen(), false);
  page.win.location.href = media.url;
  root = open();
  root.children[0].children.at(-1).children[2].focus();
  assert.equal(page.doc.key({ key: ' ' }).defaultPrevented, true);
  assert.equal(page.launches.length, 1, 'Space on the focused confirmation link activates it');
  page.timers.splice(0).forEach(timer => timer());
});

test('stale content script tolerates Chrome storage context invalidation without blocking launch', async () => {
  let attemptedSaves = 0;
  const invalidated = new Error('Extension context invalidated.');
  const bridge = context.MTDContent.createStorageBridge({ local: {
    get: async () => { throw invalidated; },
    set: async () => { attemptedSaves++; throw invalidated; }
  } });
  assert.equal(await bridge.load(), 'best');

  const page = fakePage('https://www.youtube.com/watch?v=abc');
  const host = page.node('div');
  page.doc.body.append(host);
  host.querySelector = () => page.node('div');
  page.selectors.set('ytd-watch-metadata #actions', [host]);
  context.MutationObserver = class { observe() {} disconnect() {} };
  const controller = context.MTDContent.createController(page.doc, page.win, bridge.load, bridge.save);
  controller.start();
  await Promise.resolve();
  page.doc.getElementById('mtd-download-button').click();
  const quality = page.doc.getElementById('mtd-modal-quality');
  quality.value = '720';
  quality.dispatch('change');
  const confirm = page.doc.getElementById('mtd-modal-backdrop').children[0].children.at(-1).children[2];
  confirm.click();
  await new Promise(setImmediate);

  assert.equal(attemptedSaves, 2);
  assert.equal(page.launches.length, 1, 'protocol handoff still occurs despite stale storage');
  assert.equal(new URL(page.launches[0]).searchParams.get('quality'), '720');
  page.timers.splice(0).forEach(timer => timer());
  controller.stop();

  const unexpected = context.MTDContent.createStorageBridge({ local: {
    get: async () => { throw new Error('Storage quota exceeded'); },
    set: async () => { throw new Error('Storage quota exceeded'); }
  } });
  await assert.rejects(unexpected.load(), /Storage quota exceeded/);
  await assert.rejects(unexpected.save('720'), /Storage quota exceeded/);
});

test('playlist dialog includes available item count when YouTube exposes it', () => {
  const page = fakePage('https://www.youtube.com/playlist?list=PL123', 'My playlist - YouTube');
  page.selectors.set('yt-page-header-renderer #stats', [{ textContent: '12 filmów • 2026' }]);
  const modal = context.MTDModal.createController(page.doc, page.win);
  modal.open(context.MTDUrls.detect(page.win.location.href));
  const dialog = page.doc.getElementById('mtd-modal-backdrop').children[0];
  assert.equal(dialog.children.find(child => child.className === 'mtd-modal-media-title').textContent, 'My playlist');
  assert.equal(dialog.children.find(child => child.className === 'mtd-modal-count').textContent, '12 filmów');
  modal.close();
});

test('playlist dialog finds count in current YouTube metadata view model', () => {
  const page = fakePage('https://www.youtube.com/playlist?list=PL123', 'NASA playlist - YouTube');
  page.selectors.set('yt-page-header-renderer yt-content-metadata-view-model [role="text"]', [
    { textContent: 'Playlista' }, { textContent: '19 filmów' }, { textContent: '322 118 wyświetleń' }
  ]);
  const modal = context.MTDModal.createController(page.doc, page.win);
  modal.open(context.MTDUrls.detect(page.win.location.href));
  const dialog = page.doc.getElementById('mtd-modal-backdrop').children[0];
  assert.equal(dialog.children.find(child => child.className === 'mtd-modal-count').textContent, '19 filmów');
  modal.close();
});

test('open-in-app action launches analysis mode without quick queueing', () => {
  const page = fakePage('https://www.youtube.com/watch?v=advanced');
  const modal = context.MTDModal.createController(page.doc, page.win, { getQuality: () => '1440' });
  modal.open(context.MTDUrls.detect(page.win.location.href));
  const actions = page.doc.getElementById('mtd-modal-backdrop').children[0].children.at(-1);

  actions.children[1].click();

  const request = new URL(page.launches[0]);
  assert.equal(request.searchParams.get('quality'), '1440');
  assert.equal(request.searchParams.get('queue'), '0');
  assert.equal(request.searchParams.get('open'), '1');
  assert.equal(request.searchParams.get('container'), 'auto');
});

test('SPA navigation reuses one button and removes it on unsupported route', () => {
  const nodes = new Map();
  const handlers = new Map();
  const anchor = {
    append(button) { nodes.set(button.id, button); button.parentElement = anchor; },
  };
  const doc = {
    body: {},
    querySelector() { return anchor; },
    getElementById(id) { return nodes.get(id) || null; },
    createElement() {
      return {
        dataset: {}, children: [], setAttribute() {},
        append(child) { this.children.push(child); },
        addEventListener() {},
        remove() { nodes.delete(this.id); }
      };
    }
  };
  class Observer { observe() {} disconnect() {} }
  context.MutationObserver = Observer;
  const win = {
    location: { href: 'https://www.youtube.com/watch?v=abc' },
    setTimeout(callback) { callback(); },
    addEventListener(name, callback) { handlers.set(name, callback); },
    removeEventListener(name) { handlers.delete(name); }
  };
  const controller = context.MTDContent.createController(doc, win, async () => ({ ok: true }));
  controller.start();
  const original = nodes.get('mtd-download-button');
  assert.ok(original);
  win.location.href = 'https://www.youtube.com/watch?v=def';
  handlers.get('yt-navigate-finish')();
  assert.equal(nodes.get('mtd-download-button'), original);
  assert.equal(nodes.size, 1);
  win.location.href = 'https://www.youtube.com/';
  handlers.get('yt-navigate-finish')();
  assert.equal(nodes.size, 0);
  controller.stop();
});

test('watch action row uses a native-height action and moves through SPA routes without duplicates', () => {
  const nodes = new Map();
  const selectors = new Map();
  const handlers = new Map();
  function anchor(width = 100, right = 100) {
    return {
      children: [],
      getBoundingClientRect() { return { width, height: 40, right }; },
      querySelector() { return null; },
      append(button) {
        if (button.parentElement) button.parentElement.children = button.parentElement.children.filter(x => x !== button);
        this.children.push(button);
        button.parentElement = this;
        nodes.set(button.id, button);
      }
    };
  }
  const actions = anchor();
  const inner = anchor();
  actions.querySelector = () => inner;
  selectors.set('ytd-watch-metadata #actions', [actions]);
  selectors.set('ytd-watch-metadata #actions-inner', [inner]);
  const doc = {
    body: {},
    querySelectorAll(selector) { return selectors.get(selector) || []; },
    getElementById(id) { return nodes.get(id) || null; },
    createElement(tag) {
      return {
        tag, dataset: {}, children: [], attributes: {},
        setAttribute(name, value) { this.attributes[name] = value; },
        append(child) { this.children.push(child); },
        addEventListener() {},
        remove() {
          this.parentElement.children = this.parentElement.children.filter(x => x !== this);
          this.parentElement = null;
          nodes.delete(this.id);
        }
      };
    }
  };
  class Observer { observe() {} disconnect() {} }
  context.MutationObserver = Observer;
  const win = {
    location: { href: 'https://www.youtube.com/watch?v=abc' },
    setTimeout(callback) { callback(); },
    addEventListener(name, callback) { handlers.set(name, callback); },
    removeEventListener(name) { handlers.delete(name); }
  };
  const controller = context.MTDContent.createController(doc, win);
  controller.start();
  const button = nodes.get('mtd-download-button');
  assert.equal(button.parentElement, actions);
  assert.equal(button.dataset.mtdPlacement, 'watch');
  assert.equal(button.children[0].src, 'chrome-extension://test/assets/icon48.png');
  assert.equal(button.attributes['aria-label'], 'Pobierz w ModernTubeDownloader');
  assert.equal(button.tag, 'button');
  assert.equal(button.children[1].textContent, 'Pobierz');
  controller.setQuality('360');
  assert.equal(button.href, undefined, 'the toolbar button must not launch the protocol');

  const newActions = anchor();
  newActions.querySelector = () => inner;
  selectors.set('ytd-watch-metadata #actions', [newActions]);
  win.location.href = 'https://www.youtube.com/watch?v=def';
  handlers.get('yt-navigate-finish')();
  assert.equal(nodes.get('mtd-download-button'), button);
  assert.equal(button.parentElement, newActions);
  assert.equal(actions.children.length, 0);
  assert.equal(button.dataset.mtdUrl, 'https://www.youtube.com/watch?v=def');

  const watchMenu = anchor(300, 300);
  watchMenu.children.push(anchor(40, 160));
  watchMenu.parentElement = newActions;
  newActions.querySelector = () => watchMenu;
  handlers.get('resize')();
  assert.equal(button.parentElement, watchMenu);
  assert.equal(button.dataset.mtdPlacement, 'watch-menu');

  const shorts = anchor();
  selectors.set('ytd-shorts ytd-reel-video-renderer reel-action-bar-view-model', [shorts]);
  win.location.href = 'https://www.youtube.com/shorts/xyz';
  handlers.get('yt-navigate-finish')();
  assert.equal(button.parentElement, shorts);
  assert.equal(button.dataset.mtdPlacement, 'shorts');
  assert.equal(newActions.children.length, 0);
  assert.equal(watchMenu.children.includes(button), false);

  const playlistHost = anchor(312, 600);
  const row = anchor(312, 600);
  const native = anchor(40, 600);
  row.children.push(native);
  playlistHost.querySelector = () => row;
  playlistHost.parentElement = anchor(312, 600);
  selectors.set('yt-page-header-renderer yt-flexible-actions-view-model', [playlistHost]);
  win.location.href = 'https://www.youtube.com/playlist?list=PL123';
  handlers.get('yt-navigate-finish')();
  assert.equal(button.parentElement, playlistHost);
  assert.equal(button.dataset.mtdPlacement, 'playlist-stack');
  playlistHost.parentElement = anchor(400, 700);
  handlers.get('resize')();
  assert.equal(button.parentElement, row);
  assert.equal(button.dataset.mtdPlacement, 'playlist-inline');
  assert.equal(nodes.size, 1);

  win.location.href = 'https://www.youtube.com/';
  handlers.get('yt-navigate-finish')();
  assert.equal(nodes.size, 0);
  assert.equal(row.children.includes(button), false);
  controller.stop();
});

test('watch fallback does not become a full-width toolbar item', () => {
  const styles = fs.readFileSync(path.join(root, 'styles.css'), 'utf8');
  const buttonStyles = styles.split('#mtd-modal-backdrop')[0];
  const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'manifest.json'), 'utf8'));
  assert.match(styles, /#mtd-download-button\[data-mtd-placement="stack"\]/);
  assert.match(buttonStyles, /flex:\s*0 0 40px/);
  assert.match(buttonStyles, /width:\s*40px/);
  assert.match(buttonStyles, /height:\s*40px/);
  assert.match(buttonStyles, /border-radius:\s*20px/);
  assert.match(buttonStyles, /data-mtd-placement="watch-menu"/);
  assert.match(styles, /#mtd-modal-backdrop\s*\{[^}]*position:\s*fixed;[^}]*display:\s*flex;[^}]*align-items:\s*center;[^}]*justify-content:\s*center;/s);
  assert.match(styles, /html\[dark\] #mtd-modal-backdrop \.mtd-modal-quality\s*\{[^}]*color-scheme:\s*dark;[^}]*background:/s);
  assert.match(styles, /html\[dark\] #mtd-modal-backdrop \.mtd-modal-quality option/);
  assert.match(styles, /\.mtd-modal-open/);
  assert.match(styles, /html\[dark\] #mtd-download-button/);
  assert.doesNotMatch(buttonStyles, /width:\s*100%|flex:\s*1(?:\s|;)/);
  assert.ok(manifest.content_scripts[0].js.includes('src/modal.js'));
  assert.ok(manifest.web_accessible_resources.some(resource => resource.resources.includes('assets/icon48.png')));
});

test('late toolbar creation recovers after SPA render without a timed cutoff', () => {
  const nodes = new Map();
  let currentAnchor = null;
  let bodyMutation;
  const doc = {
    body: {},
    querySelectorAll(selector) {
      return selector === 'ytd-watch-metadata #actions-inner' && currentAnchor ? [currentAnchor] : [];
    },
    getElementById(id) { return nodes.get(id) || null; },
    createElement() {
      return {
        dataset: {}, setAttribute() {}, addEventListener() {}, append() {},
        remove() { nodes.delete(this.id); }
      };
    }
  };
  context.MutationObserver = class {
    constructor(callback) { this.callback = callback; }
    observe(target) { if (target === doc.body) bodyMutation = this.callback; }
    disconnect() {}
  };
  const win = {
    location: { href: 'https://www.youtube.com/watch?v=late' },
    setTimeout(callback) { callback(); },
    addEventListener() {}, removeEventListener() {}
  };
  const controller = context.MTDContent.createController(doc, win);
  controller.start();
  assert.equal(nodes.size, 0);
  currentAnchor = { append(button) { button.parentElement = this; nodes.set(button.id, button); } };
  bodyMutation([]);
  assert.equal(nodes.get('mtd-download-button').parentElement, currentAnchor);
  controller.stop();
  assert.equal(nodes.size, 0);
});

test('popup remembers quality and builds playlist quick-queue and open-in-app links', async () => {
  const elements = new Map(['page', 'launch', 'open', 'status', 'quality'].map(id => [id, {
    checked: true, handlers: {}, addEventListener(name, callback) { this.handlers[name] = callback; },
    setAttribute(name, value) { this[name] = value; }
  }]));
  const doc = { getElementById(id) { return elements.get(id); } };
  let saved;
  const api = {
    storage: { local: { get: async () => ({ quality: '720' }), set: async value => { saved = value; } } },
    tabs: { query: async () => [{ title: 'Playlist sample', url: 'https://www.youtube.com/playlist?list=PL123' }] }
  };
  await context.MTDPopup.initialize(doc, api);
  assert.equal(elements.get('quality').value, '720');
  assert.equal(elements.get('launch').textContent, 'Dodaj playlistę do kolejki');
  assert.equal(new URL(elements.get('launch').href).searchParams.get('playlist'), '1');
  assert.equal(new URL(elements.get('launch').href).searchParams.get('queue'), '1');
  assert.equal(new URL(elements.get('open').href).searchParams.get('open'), '1');
  assert.equal(elements.get('launch')['aria-disabled'], 'false');
  assert.equal(elements.get('open')['aria-disabled'], 'false');
  elements.get('quality').value = '1080';
  elements.get('quality').handlers.change();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(saved.quality, '1080');
  assert.equal(new URL(elements.get('launch').href).searchParams.get('quality'), '1080');
});

test('popup routes /live to analysis instead of unattended quick queue', async () => {
  const elements = new Map(['page', 'launch', 'open', 'status', 'quality'].map(id => [id, {
    addEventListener() {}, setAttribute(name, value) { this[name] = value; }
  }]));
  await context.MTDPopup.initialize({ getElementById: id => elements.get(id) }, {
    storage: { local: { get: async () => ({ quality: 'best' }), set: async () => {} } },
    tabs: { query: async () => [{ title: 'LIVE', url: 'https://www.youtube.com/live/abc' }] }
  });
  assert.equal(elements.get('launch').hidden, true);
  assert.equal(elements.get('launch').href, '#');
  assert.equal(new URL(elements.get('open').href).searchParams.get('open'), '1');
});

test('context menu limits targets and launches a validated protocol link', async () => {
  const menus = [];
  const launched = [];
  let onInstall;
  let onClick;
  const chrome = {
    runtime: { onInstalled: { addListener(callback) { onInstall = callback; } } },
    contextMenus: {
      create(item) { menus.push(item); },
      onClicked: { addListener(callback) { onClick = callback; } }
    },
    storage: { local: { get: async () => ({ quality: '480' }) } },
    tabs: { create: async tab => { launched.push(tab); } }
  };
  const bg = vm.createContext({ URL, URLSearchParams, chrome, importScripts() {} });
  vm.runInContext(fs.readFileSync(path.join(root, 'shared.js'), 'utf8'), bg);
  vm.runInContext(fs.readFileSync(path.join(root, 'background.js'), 'utf8'), bg);
  onInstall();
  assert.equal(menus.length, 2);
  assert.ok(menus[1].targetUrlPatterns.every(pattern => pattern.includes('youtube.com') || pattern.includes('youtu.be')));
  onClick({ menuItemId: 'mtd-send-link', linkUrl: 'https://youtu.be/abc' });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(launched.length, 1);
  assert.equal(new URL(launched[0].url).searchParams.get('quality'), '480');
  assert.equal(new URL(launched[0].url).searchParams.get('queue'), '1');
  onClick({ menuItemId: 'mtd-send-link', linkUrl: 'https://www.youtube.com/live/abc' });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(new URL(launched[1].url).searchParams.get('open'), '1');
  assert.equal(new URL(launched[1].url).searchParams.get('queue'), '0');
  onClick({ menuItemId: 'mtd-send-link', linkUrl: 'https://evil.test/' });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(launched.length, 2);
});
