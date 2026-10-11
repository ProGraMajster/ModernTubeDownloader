globalThis.MTDPopup = (() => {
  const stateForUrl=url=>MTDUrls.detect(url)?'ready':'unsupported';
  async function initialize(doc=document,api=chrome) {
    await MTDI18n.ready;
    await globalThis.MTDCookieClient?.ready;
    MTDI18n.apply(doc);
    const get=id=>doc.getElementById(id), launch=get('launch'), open=get('open'), status=get('status'), quality=get('quality');
    const stored=await MTDContext.call(() => api.storage.local.get({quality:'best',showInjectedButtons:true,contextMenusEnabled:true,defaultAction:'analyze'}), MTDI18n.defaults);
    if (!MTDContext.check()) return;
    quality.value=MTDUrls.qualities.has(stored.quality)?stored.quality:'best';
    const [tab]=await MTDContext.call(() => api.tabs.query({active:true,currentWindow:true}), []);
    if (!MTDContext.check()) return;
    const media=MTDUrls.detect(tab?.url||'');
    const generic=media?.kind==='generic', live=media?.kind==='live';
    let statusKey=live?'liveHint':media?'ready':'unsupportedHint';
    function renderLanguage(){
      MTDI18n.apply(doc);
      if(!media)get('page').textContent=MTDI18n.t('unsupported');
      if(get('source'))get('source').textContent=media?MTDI18n.t('source',[new URL(media.url).hostname]):'';
      launch.textContent=MTDI18n.t(media?.kind==='playlist'?'queuePlaylist':'queue');
      status.textContent=MTDI18n.t(statusKey);
      const button=get('languageButton');
      if(button){
        button.textContent=MTDI18n.language==='pl'?'🇵🇱 PL':'🇬🇧 EN';
        button.setAttribute('aria-label',MTDI18n.t('language'));
        for(const value of ['auto','pl','en'])get('language-'+value).setAttribute('aria-pressed',String(MTDI18n.preference===value));
      }
    }
    MTDContext.own(MTDI18n.subscribe(renderLanguage));
    function closeLanguage(){if(get('languageMenu')){get('languageMenu').hidden=true;get('languageButton').setAttribute('aria-expanded','false')}}
    MTDContext.listen(get('languageButton'), 'click',()=>{const menu=get('languageMenu');menu.hidden=!menu.hidden;get('languageButton').setAttribute('aria-expanded',String(!menu.hidden))});
    for(const value of ['auto','pl','en'])MTDContext.listen(get('language-'+value), 'click',async()=>{
      try{await MTDI18n.select(value);closeLanguage();get('languageButton').focus()}
      catch{statusKey='preferencesError';renderLanguage()}
    });
    for(const id of ['settingsLink','languageSettings'])MTDContext.listen(get(id), 'click',()=>void MTDContext.call(() => api.runtime.openOptionsPage()).catch(error=>console.warn('MTD options failed:',error?.name||'Error')));
    MTDContext.listen(doc, 'keydown',event=>{if(event.key==='Escape'&&get('languageMenu')&&!get('languageMenu').hidden){closeLanguage();get('languageButton').focus()}});
    MTDContext.listen(doc, 'click',event=>{if(get('languageMenu')&&!event.target.closest?.('.language-control'))closeLanguage()});
    get('page').textContent=media?(tab.title||media.url):MTDI18n.t('unsupported');
    get('page').title=media?.url||'';
    if(get('source'))get('source').textContent=media?MTDI18n.t('source',[new URL(media.url).hostname]):'';
    if(get('genericHint'))get('genericHint').hidden=!generic;
    quality.hidden=generic;
    quality.disabled=generic;
    if(get('qualityLabel'))get('qualityLabel').hidden=generic;
    if(get('qualityHint'))get('qualityHint').hidden=generic;
    MTDContext.own(() => { launch.removeAttribute?.("href"); open.removeAttribute?.("href"); launch.setAttribute("aria-disabled","true"); open.setAttribute("aria-disabled","true"); });
    function targets(){
      if (!MTDContext.check()) return;
      launch.href=media?MTDUrls.protocolUrl(media.url,{quality:quality.value,mode:'queue'}):'#';
      open.href=media?MTDUrls.protocolUrl(media.url,{quality:quality.value,mode:'analyze'}):'#';
    }
    targets();
    launch.hidden=false;
    launch.setAttribute('aria-disabled',media?'false':'true');open.setAttribute('aria-disabled',media?'false':'true');
    launch.textContent=MTDI18n.t(media?.kind==='playlist'?'queuePlaylist':'queue');
    renderLanguage();
    async function save(value){try{await MTDContext.call(() => api.storage.local.set(value));statusKey='settingsSaved'}catch{statusKey='preferencesError'}renderLanguage()}
    MTDContext.listen(quality, 'change',()=>{targets();void save({quality:quality.value})});
    for(const key of ['showInjectedButtons','contextMenusEnabled','defaultAction']){
      const input=get(key);if(!input)continue;
      if(key==='defaultAction')input.value=stored[key]==='queue'?'queue':'analyze';else input.checked=stored[key]!==false;
      MTDContext.listen(input, 'change',()=>void save({[key]:key==='defaultAction'?input.value:input.checked}));
    }
    MTDContext.subscribe(api.storage.onChanged, (changes,area)=>{
      if(area!=='local')return;
      for(const key of Object.keys(MTDI18n.defaults)){
        if(!changes[key])continue;
        const value=changes[key].newValue??MTDI18n.defaults[key],input=get(key);
        if(!input)continue;
        if(key==='quality'||key==='defaultAction')input.value=value;else input.checked=value;
      }
      targets();
    });
    const click = (event, anchor, key) => {
      if(!MTDContext.check()||!media){event.preventDefault();return} targets();
      if(globalThis.MTDCookieClient?.prepareClick(event,window,anchor.href,media.url)) return;
      statusKey=key;renderLanguage();
    };
    MTDContext.listen(launch, 'click',event=>click(event,launch,'handoff'));
    MTDContext.listen(open, 'click',event=>click(event,open,'analyzing'));
  }
  if(typeof document!=='undefined'&&typeof chrome!=='undefined')void initialize().catch(()=>{
    const status=document.getElementById('status');if(status)status.textContent=MTDI18n.t('preferencesError');
  });
  return {stateForUrl,initialize};
})();
