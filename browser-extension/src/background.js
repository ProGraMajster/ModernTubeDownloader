importScripts('context.js', 'shared.js', 'i18n.js', 'cookies.js');

const cookieSync = MTDCookies.create(chrome);
MTDContext.subscribe(chrome.runtime.onMessage, (message, sender, reply) => {
  if(typeof message?.type !== 'string' || !message.type.startsWith('cookie-')) return;
  void cookieSync.handle(message, sender).then(reply, () => reply({ok:false,status:'rejected'}));
  return true;
});
const safely = work => { void work.catch(() => {}); };
let cookiesHooked=false, alarmsHooked=false;
function hookOptionalEvents() {
  if(!cookiesHooked && chrome.cookies) { cookiesHooked=true; MTDContext.subscribe(chrome.cookies.onChanged, change => safely(cookieSync.changed(change))); }
  if(!alarmsHooked && chrome.alarms) { alarmsHooked=true; MTDContext.subscribe(chrome.alarms.onAlarm, alarm => safely(cookieSync.alarm(alarm))); }
}
hookOptionalEvents();
void cookieSync.ensureBridge().catch(()=>{});
if(chrome.permissions?.onAdded) MTDContext.subscribe(chrome.permissions.onAdded, () => { hookOptionalEvents(); safely(cookieSync.ensureBridge()); });
if(chrome.permissions) MTDContext.subscribe(chrome.permissions.onRemoved, () => safely(cookieSync.removed()));
MTDContext.subscribe(chrome.runtime.onStartup, () => safely(cookieSync.startup()));

const pageMenuId = 'mtd-send-page', linkMenuId = 'mtd-send-link';
let menuRevision = 0;
let menuUpdates = Promise.resolve();
function updateMenus() {
  const revision = ++menuRevision;
  menuUpdates = menuUpdates.catch(() => {}).then(async () => {
    await MTDI18n.ready;
    const preferences = await MTDContext.call(() => chrome.storage.local.get({ contextMenusEnabled:true, defaultAction:'analyze' }), {contextMenusEnabled:false});
    if (!MTDContext.check() || revision !== menuRevision) return;
    await MTDContext.call(() => chrome.contextMenus.removeAll());
    if (!MTDContext.check() || revision !== menuRevision) return;
    if (!preferences.contextMenusEnabled) return;
    const queue = preferences.defaultAction === 'queue';
    MTDContext.sync(() => chrome.contextMenus.create({id:pageMenuId,title:MTDI18n.t(queue?'menuPageQueue':'menuPage'),contexts:['page'],documentUrlPatterns:['http://*/*','https://*/*']}));
    MTDContext.sync(() => chrome.contextMenus.create({id:linkMenuId,title:MTDI18n.t(queue?'menuLinkQueue':'menuLink'),contexts:['link'],targetUrlPatterns:['http://*/*','https://*/*']}));
  });
  return menuUpdates;
}
const refreshMenus = () => { void updateMenus().catch(error => console.warn('MTD menu setup failed:', error?.name || 'Error')); };
MTDContext.subscribe(chrome.runtime.onInstalled, refreshMenus);
MTDContext.subscribe(chrome.runtime.onStartup, refreshMenus);
MTDContext.subscribe(chrome.storage.onChanged, (changes,area)=>{if(area==='local'&&(changes.contextMenusEnabled||changes.defaultAction))refreshMenus()});
MTDContext.own(MTDI18n.subscribe(refreshMenus));
MTDContext.own(() => { menuRevision++; });

async function launch(sourceUrl) {
  const preferences = await MTDContext.call(() => chrome.storage.local.get({quality:'best',defaultAction:'analyze',contextMenusEnabled:true}), {contextMenusEnabled:false});
  if (!preferences.contextMenusEnabled) return {ok:false,reason:'disabled'};
  const media = MTDUrls.detect(sourceUrl);
  const target = MTDUrls.protocolUrl(sourceUrl, {quality:MTDUrls.qualities.has(preferences.quality)?preferences.quality:'best',
    mode:media?.kind==='live'?'analyze':preferences.defaultAction==='queue'?'queue':'analyze'});
  if (!target) return {ok:false,reason:'unsupported'};
  const ack = await cookieSync.beforeUse(sourceUrl);
  const destination = ack.ok ? target : chrome.runtime.getURL('src/cookie-handoff.html')+'?target='+encodeURIComponent(target);
  try { await MTDContext.call(() => chrome.tabs.create({url:destination,active:!ack.ok})); return {ok:MTDContext.check()}; }
  catch { return {ok:false,reason:'protocol'}; }
}
MTDContext.subscribe(chrome.contextMenus.onClicked, (info,tab)=>{
  const url=info.menuItemId===pageMenuId?info.pageUrl||tab?.url:info.menuItemId===linkMenuId?info.linkUrl:null;
  if(url)void launch(url).catch(error=>console.warn('MTD handoff failed:',error?.name||'Error'));
});
globalThis.MTDBackground={updateMenus,launch};
