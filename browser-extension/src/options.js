"use strict";
globalThis.MTDOptions = (() => {
  async function initialize(doc=document, api=chrome){
    await MTDI18n.ready;
    const get=id=>doc.getElementById(id);
    let statusKey='';
    const render=()=>{MTDI18n.apply(doc);doc.title='ModernTubeDownloader — '+MTDI18n.t('extensionSettings');
      get('extensionLanguage').value=MTDI18n.preference;get('status').textContent=statusKey?MTDI18n.t(statusKey):''};
    MTDContext.own(MTDI18n.subscribe(render));
    const stored=await MTDContext.call(() => api.storage.local.get(MTDI18n.defaults), MTDI18n.defaults);
    if (!MTDContext.check()) return;
    function update(values){
      for(const key of Object.keys(MTDI18n.defaults)){
        const input=get(key),value=values[key]??MTDI18n.defaults[key];
        if(key==='showInjectedButtons'||key==='contextMenusEnabled')input.checked=value!==false;
        else input.value=value;
      }
    }
    update(stored);render();
    if(globalThis.MTDCookieOptions) await MTDCookieOptions.initialize(doc,api);
    get('version').textContent=MTDContext.sync(() => api.runtime.getManifest().version_name||api.runtime.getManifest().version, '');
    async function save(key,value){
      if (!MTDContext.check()) return;
      try{if(key==='extensionLanguage')await MTDI18n.select(value);else await MTDContext.call(() => api.storage.local.set({[key]:value}));statusKey='settingsSaved'}
      catch{statusKey='preferencesError'}render();
    }
    for(const key of Object.keys(MTDI18n.defaults))MTDContext.listen(get(key), 'change',()=>{
      void save(key,['showInjectedButtons','contextMenusEnabled'].includes(key)?get(key).checked:get(key).value);
    });
    MTDContext.subscribe(api.storage.onChanged, (changes,area)=>{
      if(area!=='local')return;
      for(const key of Object.keys(MTDI18n.defaults)){
        if(!changes[key])continue;
        const input=get(key),value=changes[key].newValue??MTDI18n.defaults[key];
        if(['showInjectedButtons','contextMenusEnabled'].includes(key))input.checked=value;else input.value=value;
      }
    });
    MTDContext.listen(get('reset'), 'click',()=>get('resetDialog').showModal());
    MTDContext.listen(get('resetCancel'), 'click',()=>get('resetDialog').close());
    MTDContext.listen(get('resetConfirm'), 'click',async()=>{
      try{
        // Never clear unrelated Chrome storage. These are the extension's only owned preferences.
        await MTDContext.call(() => api.storage.local.remove(Object.keys(MTDI18n.defaults)));
        if (!MTDContext.check()) return;
        await MTDI18n.select('auto');
        update(MTDI18n.defaults);statusKey='defaultsRestored';get('resetDialog').close();
      }catch{statusKey='preferencesError'}render();
    });
  }
  if(typeof document!=='undefined')void initialize().catch(()=>{document.getElementById('status').textContent=MTDI18n.t('preferencesError')});
  return {initialize};
})();
