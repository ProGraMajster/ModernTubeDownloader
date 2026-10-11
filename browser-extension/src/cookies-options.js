globalThis.MTDCookieOptions = (() => {
  async function initialize(doc,api) {
    const get=id=>doc.getElementById(id), send=message=>MTDContext.call(()=>api.runtime.sendMessage(message),{ok:false,status:'bridge-unavailable'});
    let selection=[];
    const profileStatus=p=>MTDI18n.t(({synced:'cookieStatusSynced','revocation-pending':'cookieStatusPending',revoked:'cookieStatusRevoked',
      'not-synced':'cookieStatusNotSynced','permission-revoked':'cookieStatusPermission','bridge-not-installed':'cookieBridgeMissing',
      'bridge-forbidden':'cookieBridgeForbidden','bridge-exited':'cookieBridgeExited','bridge-protocol':'cookieBridgeProtocol',
      'bridge-api-unavailable':'cookieBridgeApi'})[p.status]||'cookieStatusFailed');
    const status=key=>get('cookieStatus').textContent=MTDI18n.t(key);
    const scopes=()=>get('cookieMode').value==='services'
      ? [['cookieYouTube','youtube.com'],['cookieVimeo','vimeo.com'],['cookieTwitch','twitch.tv']].filter(([id])=>get(id).checked).map(([,d])=>d)
      : [...new Set(get('cookieDomains').value.toLowerCase().split(/[,\s]+/).filter(Boolean))];
    const form=()=>({browser:get('cookieBrowser').value,mode:get('cookieMode').value,scopes:scopes(),selection:selection.filter(i=>i.input.checked).map(i=>i.id),
      beforeUse:get('cookieBefore').checked,onChanged:get('cookieChanged').checked,onStartup:get('cookieStartup').checked,period:Number(get('cookiePeriod').value)});
    async function consent() {
      const p=form();
      if(!get('cookieConsent').checked || !p.scopes.length || p.scopes.length>16 || !p.scopes.every(MTDCookies.validDomain)
        || (p.mode==='current' && p.scopes.length!==1) || !Number.isInteger(p.period) || p.period<0 || p.period>1440 || p.period>0&&p.period<5){status('cookieInvalid');return false}
      // Called directly from this extension button's gesture, never from the content page or worker.
      return api.permissions.request({permissions:['cookies','nativeMessaging','alarms'],origins:MTDCookies.origins(p.scopes)});
    }
    async function refresh() {
      const result=await send({type:'cookie-status'});if(!result.ok){status('cookieFailed');return}
      get('cookieExtensionId').textContent=result.extensionId;get('cookieProfiles').replaceChildren();
      for(const p of result.profiles){
        const block=doc.createElement('article'), label=doc.createElement('p');
        label.textContent=[p.browser,...p.scopes,MTDI18n.t(p.enabled?'cookieEnabled':'cookieRevoked'),MTDI18n.t('cookieState')+': '+profileStatus(p),
          ...(p.revokeFailure?[profileStatus({status:p.revokeFailure})]:[]),
          p.lastSync?new Date(p.lastSync).toLocaleString():'—',String(p.count||0)+' cookies',String(p.excluded||0)+' partitioned'].join(' • ');
        block.append(label);
        for(const [key,type] of [['cookieSync','cookie-sync'],['cookieRevoke','cookie-revoke'],['cookieEdit','edit'],['cookieDelete','cookie-forget']]){
          const button=doc.createElement('button');button.type='button';button.textContent=MTDI18n.t(key);block.append(button);
          if(type==='cookie-sync')button.disabled=!p.enabled;
          MTDContext.listen(button,'click',async()=>{
            const deleting=type==='cookie-forget';
            const needsRevoke=type==='cookie-revoke'||type==='edit'||deleting&&(p.enabled||p.status!=='revoked');
            if(deleting||needsRevoke)if(!confirm(MTDI18n.t(deleting?'cookieDeleteConfirm':'cookieRevokeConfirm')))return;
            button.disabled=true;
            try {
            // A pending deletion may outlive the optional native permission. Explicit
            // user gesture restores only deletion transport, never cookie/site access.
            if(needsRevoke)if(!await api.permissions.request({permissions:['nativeMessaging']})){status('cookieFailed');return}
            let ack=await send({type:needsRevoke?'cookie-revoke':type,profileId:p.id});
            if(deleting&&needsRevoke&&ack.ok)ack=await send({type:'cookie-forget',profileId:p.id});
            status(ack.ok?'cookieDone':'cookieFailed');
            if(type==='edit'&&ack.ok){get('cookieMode').value='domains';get('cookieDomains').value=p.scopes.join(', ');get('cookieBrowser').value=p.browser;
              get('cookieConsent').checked=false;selection=[];get('cookieSelection').replaceChildren();get('cookieDomains').focus()}
            await refresh();
            }catch{status('cookieFailed')}
            finally{button.disabled=type==='cookie-sync'&&!p.enabled}
          });
        }
        get('cookieProfiles').append(block);
      }
    }
    MTDContext.listen(get('cookieMode'),'change',()=>{selection=[];get('cookieSelection').replaceChildren()});
    MTDContext.listen(get('cookieDomains'),'input',()=>{selection=[];get('cookieSelection').replaceChildren()});
    MTDContext.listen(get('cookieInspect'),'click',async()=>{
      try{
        if(!await consent())return;
        const result=await send({type:'cookie-list',scopes:scopes()});
        if(!result.ok){status('cookieFailed');return}selection=[];get('cookieSelection').replaceChildren();
        for(const c of result.cookies){
          const label=doc.createElement('label'),input=doc.createElement('input'),span=doc.createElement('span');
          input.type='checkbox';input.disabled=c.partitioned;label.className='check';
          span.textContent=[c.name,c.domain,c.path,c.session?MTDI18n.t('cookieSession'):new Date(c.expirationDate*1000).toLocaleString(),
            c.secure?'Secure':'',c.httpOnly?'HttpOnly':'',c.partitioned?MTDI18n.t('cookiePartitioned'):''].filter(Boolean).join(' • ');
          label.append(input,span);get('cookieSelection').append(label);selection.push({id:c.id,input});
        } status('cookieMetadataOnly');
      }catch{status('cookieFailed')}
    });
    MTDContext.listen(get('cookieConnect'),'click',async()=>{
      try{if(!await consent())return;const ack=await send({type:'cookie-configure',profile:form()});
        let synced=ack;
        if(ack.ok){synced=await send({type:'cookie-sync',profileId:ack.profileId});get('cookieConsent').checked=false;await refresh()}status(synced.ok?'cookieDone':'cookieFailed');
      }catch{status('cookieFailed')}
    });
    MTDContext.own(MTDI18n.subscribe(()=>void refresh()));
    await refresh();
  }
  return {initialize};
})();
