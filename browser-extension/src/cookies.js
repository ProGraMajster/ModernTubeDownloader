/* Credential values live only in this service-worker module, never Chrome storage or UI DTOs. */
globalThis.MTDCookies = (() => {
  const host = 'com.programajster.moderntubedownloader.cookies', key = 'cookieProfiles';
  const matches = (host, domain) => host === domain.replace(/^\./,'') || host.endsWith('.'+domain.replace(/^\./,''));
  const validDomain = d => typeof d === 'string' && d.length <= 253 && /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$/.test(d)
    && !/^(?:\d+\.){3}\d+$/.test(d) && !['co.uk','com.au','co.jp','com.br','co.in'].includes(d);
  const origins = scopes => scopes.flatMap(d=>['https://'+d+'/*','https://*.'+d+'/*']);
  const identity = c => [c.domain,c.path,c.name,c.storeId].join('\t');
  const metadata = c => ({id:identity(c),name:c.name,domain:c.domain,path:c.path,session:c.session,
    expirationDate:c.expirationDate,secure:c.secure,httpOnly:c.httpOnly,partitioned:!!c.partitionKey});
  function create(api){
    const inFlight = new Map();
    let writes=Promise.resolve();
    let refreshPort=null, bridgeUpdates=Promise.resolve();
    const read = async()=> (await api.storage.local.get({[key]:[]}))[key] || [];
    const mutate = action => {
      const next=writes.catch(()=>{}).then(async()=>{const profiles=await read();const result=action(profiles);await api.storage.local.set({[key]:profiles});return result});
      writes=next;return next;
    };
    const permissions = p => ({permissions:['cookies','nativeMessaging','alarms'],origins:origins(p.scopes)});
    async function native(message){
      if(typeof api.runtime.sendNativeMessage!=='function')return {ok:false,status:'bridge-api-unavailable'};
      const failure=error=>{
        // Chrome's transport errors are classified, never echoed verbatim (nor is the payload).
        const reason=String(error?.message||'');
        const status=/not found/i.test(reason)?'bridge-not-installed':/forbidden|not allowed|access.+denied/i.test(reason)?'bridge-forbidden':
          /exited/i.test(reason)?'bridge-exited':/communicat|invalid|message/i.test(reason)?'bridge-protocol':'bridge-unavailable';
        return {ok:false,status};
      };
      // Official callback form also consumes runtime.lastError. Promise-capable implementations
      // and test adapters settle through the same single completion path.
      return new Promise(resolve=>{
        let done=false;
        const finish=value=>{if(done)return;done=true;resolve(value&&typeof value.ok==='boolean'?value:{ok:false,status:'bridge-protocol'})};
        try {
          const pending=api.runtime.sendNativeMessage(host,message,response=>{
            const error=api.runtime.lastError;
            finish(error?failure(error):response);
          });
          pending?.then?.(finish,error=>finish(failure(error)));
        }catch(error){finish(failure(error))}
      });
    }
    async function cookies(p){
      if(!await api.permissions.contains(permissions(p)))throw new Error('permission-revoked');
      const collected=[];
      for(const domain of p.scopes){
        const records=await api.cookies.getAll({domain,storeId:p.storeId||'0'});
        for(const c of records)if(matches(c.domain.replace(/^\./,''),domain)&&!collected.some(x=>identity(x)===identity(c)&&JSON.stringify(x.partitionKey)===JSON.stringify(c.partitionKey)))collected.push(c);
      }
      if(collected.length>1000)throw new Error('cookie-count');
      return collected;
    }
    function ensureBridge(){
      const update=bridgeUpdates.catch(()=>{}).then(async()=>{
        const profiles=(await read()).filter(p=>p.enabled&&p.beforeUse);
        const eligible=[];
        for(const p of profiles)if(await api.permissions.contains(permissions(p)))eligible.push(p);
        if(!eligible.length){const port=refreshPort;refreshPort=null;port?.disconnect();return}
        if(refreshPort||typeof api.runtime.connectNative!=='function')return;
        try{
          const port=api.runtime.connectNative(host);refreshPort=port;
          port.onMessage.addListener(message=>{if(message?.type==='refresh')void reverseRefresh(message,port).catch(()=>{})});
          port.onDisconnect.addListener(()=>{
            void api.runtime.lastError; // Consume Chrome's diagnostic; never echo it or a payload.
            if(refreshPort!==port)return;
            refreshPort=null;
            void (async()=>{if((await read()).some(p=>p.enabled&&p.beforeUse)&&await api.permissions.contains({permissions:['alarms','nativeMessaging']}))
              await api.alarms.create('mtd-cookie-bridge',{when:Date.now()+30000})})().catch(()=>{});
          });
          port.postMessage({protocolVersion:1,type:'watch',browser:eligible[0].browser,cookies:[]});
        }catch{refreshPort=null}
      });
      bridgeUpdates=update;return update;
    }
    async function reverseRefresh(request,port){
      if(request.protocolVersion!==1||typeof request.requestId!=='string'||!/^[-a-f0-9]{36}$/.test(request.requestId)||
        !Number.isSafeInteger(request.oldVersion)||Date.parse(request.deadline)<=Date.now())return;
      const p=(await read()).find(p=>p.id===request.profileId);
      let status=!p?'no-matching-profile':!p.enabled||!p.beforeUse?'disabled':null;
      if(!status&&!await api.permissions.contains(permissions(p)))status='permission-revoked';
      if(!status){
        // An earlier normal sync is not the response to this request. Reserve a NEW snapshot.
        try{await inFlight.get(p.id)}catch{}
        const ack=await sync(p.id,request.requestId,request.oldVersion);
        if(ack.ok&&ack.snapshotVersion>request.oldVersion)return; // Store correlates ACK and signals desktop.
        status=ack.status==='permission-revoked'?'permission-revoked':ack.status==='disabled'?'disabled':'rejected';
      }
      if(refreshPort===port)try{port.postMessage({protocolVersion:1,type:'refreshResult',browser:p?.browser||request.browser||'chrome',
        requestId:request.requestId,refreshStatus:status,cookies:[]})}catch{}
    }
    async function sync(id,requestId=null,minimumVersion=0){
      if(inFlight.has(id)){
        if(!requestId)return inFlight.get(id);
        try{await inFlight.get(id)}catch{}
        return sync(id,requestId,minimumVersion);
      }
      const operation=(async()=>{
        const profiles=await read(),p=profiles.find(p=>p.id===id);
        if(!p?.enabled)return {ok:false,status:'disabled'};
        try{
          const values=await cookies(p);
          const selected=values.filter(c=>p.mode!=='custom'||p.selection.includes(identity(c)));
          const version=Math.max(Date.now(),(p.version||0)+1,minimumVersion+1);
          const permitted=await mutate(current=>{
            const latest=current.find(x=>x.id===id);
            if(!latest?.enabled||latest.revision!==p.revision)return false;
            latest.version=Math.max(version,latest.version||0);return true;
          });
          if(!permitted)return {ok:false,status:'scope-changed'};
          const ack=await native({protocolVersion:1,type:'cookieSnapshot',profileId:p.id,browser:p.browser,scopeMode:p.mode,
            snapshotVersion:version,generatedAtUtc:new Date().toISOString(),scopes:p.scopes,refreshBeforeUse:!!p.beforeUse,requestId,
            cookies:selected.map(c=>({domain:c.domain,path:c.path,name:c.name,value:c.value,secure:c.secure,httpOnly:c.httpOnly,
              session:c.session,expirationDate:c.expirationDate??null,hostOnly:c.hostOnly,sameSite:c.sameSite||'unspecified',
              storeId:c.storeId,partitionKey:c.partitionKey??null}))});
          // A revoke/edit racing an ACK must not restore old consent or mark the new scope synced.
          return mutate(current=>{
            const latest=current.find(x=>x.id===id);
            if(!latest?.enabled||latest.revision!==p.revision)return {ok:false,status:'scope-changed'};
            latest.status=ack.ok?'synced':ack.status;latest.version=version;
            if(ack.ok){latest.lastSync=new Date().toISOString();latest.count=ack.cookieCount;latest.excluded=ack.excludedPartitioned}
            return ack;
          });
        }catch(error){
          await mutate(current=>{const latest=current.find(x=>x.id===id);
            if(latest?.revision===p.revision)latest.status=error.message==='permission-revoked'?'permission-revoked':'sync-failed'});
          return {ok:false,status:error.message==='permission-revoked'?'permission-revoked':'sync-failed'};
        }
      })();
      inFlight.set(id,operation);
      try{return await operation}finally{inFlight.delete(id)}
    }
    async function configure(p){
      if(!p||!['current','services','domains','custom'].includes(p.mode)||!['chrome','edge','brave','chromium'].includes(p.browser)||
        !Array.isArray(p.scopes)||p.scopes.length<1||p.scopes.length>16||!p.scopes.every(validDomain)||
        !Array.isArray(p.selection)||p.selection.length>1000||p.selection.some(s=>typeof s!=='string'||s.length>2600)||
        !Number.isInteger(p.period)||p.period<0||p.period>1440||p.period>0&&p.period<5)throw new Error('invalid-profile');
      // Options has obtained permission in the explicit user-gesture consent step, not the worker.
      if(!await api.permissions.contains(permissions(p)))return {ok:false,status:'permission-denied'};
      const profile={id:crypto.randomUUID(),enabled:true,revision:Date.now(),version:0,status:'not-synced',
        mode:p.mode,browser:p.browser,scopes:[...new Set(p.scopes)],selection:p.selection,storeId:'0',
        beforeUse:p.beforeUse!==false,onChanged:p.onChanged!==false,onStartup:p.onStartup!==false,period:p.period};
      const saved=await mutate(profiles=>{if(profiles.length>=16)return false;profiles.push(profile);return true});
      if(!saved)return {ok:false,status:'profile-limit'};
      if(profile.period)await api.alarms.create('mtd-cookie-periodic-'+profile.id,{periodInMinutes:profile.period});
      await ensureBridge();
      return {ok:true,profileId:profile.id};
    }
    async function revoke(id,removePermission=true){
      const p=await mutate(profiles=>{const found=profiles.find(x=>x.id===id);if(!found)return null;
        found.enabled=false;found.revision=(found.revision||0)+1;found.status='revocation-pending';return {...found}});if(!p)return {ok:true};
      if(await api.permissions.contains({permissions:['alarms']})){
        await api.alarms?.clear('mtd-cookie-periodic-'+id);await api.alarms?.clear('mtd-cookie-dirty-'+id);
      }
      // Wait for a prior snapshot ACK, then tombstone with a strictly newer version.
      try{await inFlight.get(id)}catch{}
      const profiles=await read();const latest=profiles.find(x=>x.id===id);
      const ack=await native({protocolVersion:1,type:'revoke',profileId:id,browser:p.browser,snapshotVersion:Math.max(Date.now(),(latest?.version||0)+1),cookies:[]});
      await mutate(current=>{const latest=current.find(x=>x.id===id);if(latest){
        latest.status=ack.ok?'revoked':'revocation-pending';latest.version=ack.snapshotVersion||latest.version;
        if(ack.ok){latest.count=0;latest.excluded=0;delete latest.revokeFailure}
        else latest.revokeFailure=['bridge-not-installed','bridge-forbidden','bridge-exited','bridge-protocol','bridge-api-unavailable'].includes(ack.status)?ack.status:'bridge-unavailable';
      }});
      if(removePermission){
        const remaining=await read(),other=remaining.filter(x=>x.enabled&&x.id!==id);
        const unused=origins(p.scopes).filter(o=>!other.some(x=>origins(x.scopes).includes(o)));
        if(unused.length)await api.permissions.remove({origins:unused});
        // Global optional permissions remain if another consented profile needs them.
        // Disabled does not mean deleted. Pending tombstones still need native transport,
        // including when several revoke buttons are used before earlier ACKs arrive.
        if(!other.length)await api.permissions.remove({permissions:remaining.some(x=>x.status==='revocation-pending')?['cookies']:['cookies','nativeMessaging','alarms']});
      }
      await ensureBridge();
      return ack;
    }
    async function beforeUse(url){
      const media=MTDUrls.detect(url);if(!media)return {ok:false,status:'unsupported'};
      const domain=new URL(media.url).hostname;
      const profiles=(await read()).filter(p=>p.enabled&&p.beforeUse&&p.scopes.some(s=>matches(domain,s)));
      for(const p of profiles){const ack=await sync(p.id);if(!ack.ok)return ack}
      return {ok:true};
    }
    async function changed(change){
      for(const p of await read())if(p.enabled&&p.onChanged&&p.scopes.some(s=>matches(change.cookie.domain.replace(/^\./,''),s)))
        await api.alarms.create('mtd-cookie-dirty-'+p.id,{when:Date.now()+5000});
    }
    async function forget(id){
      // Only an acknowledged tombstone can leave the extension's visible list.
      // The native store retains its anti-replay tombstone, never credential values.
      return mutate(profiles=>{
        const index=profiles.findIndex(p=>p.id===id);
        if(index<0)return {ok:true};
        const p=profiles[index];
        if(p.enabled||p.status!=='revoked'||p.count||p.excluded)return {ok:false,status:'revocation-pending'};
        profiles.splice(index,1);return {ok:true};
      });
    }
    async function alarm(alarm){if(alarm.name==='mtd-cookie-bridge'){await ensureBridge();return}const prefix=['mtd-cookie-dirty-','mtd-cookie-periodic-'].find(x=>alarm.name.startsWith(x));if(prefix)await sync(alarm.name.slice(prefix.length))}
    async function startup(){for(const p of await read()){
      if(p.enabled){
        // Chrome may discard alarms at browser shutdown. Restore only consented, configured ones.
        if(p.period && await api.permissions.contains(permissions(p)))await api.alarms.create('mtd-cookie-periodic-'+p.id,{periodInMinutes:p.period});
        if(p.onStartup)await sync(p.id);
      }else if(p.status==='revocation-pending')await revoke(p.id,false);
    }await ensureBridge()}
    async function removed(){for(const p of await read())if(p.enabled&&!await api.permissions.contains(permissions(p)))await revoke(p.id,false);await ensureBridge()}
    async function handle(message,sender){
      if(sender.id!==api.runtime.id)return {ok:false,status:'sender'};
      const base=api.runtime.getURL('');
      const privileged=sender.url===base+'src/options.html'||sender.url===base+'src/popup.html';
      if(message.type==='cookie-before-use'){
        if(!privileged){
          if(sender.frameId!==0||!sender.tab?.id)return {ok:false,status:'sender'};
          const current=await api.tabs.get(sender.tab.id);
          if(!MTDUrls.detectYouTube(current.url)||MTDUrls.detect(current.url)?.url!==MTDUrls.detect(message.url)?.url)return {ok:false,status:'sender-url'};
        }
        return beforeUse(message.url);
      }
      if(!privileged||sender.url!==base+'src/options.html')return {ok:false,status:'sender'};
      if(message.type==='cookie-status')return {ok:true,profiles:await read(),extensionId:api.runtime.id};
      if(message.type==='cookie-configure')return configure(message.profile);
      if(message.type==='cookie-sync')return sync(message.profileId);
      if(message.type==='cookie-revoke')return revoke(message.profileId);
      if(message.type==='cookie-forget')return forget(message.profileId);
      if(message.type==='cookie-list'){
        const p={scopes:message.scopes,storeId:'0'};
        if(!Array.isArray(p.scopes)||!p.scopes.every(validDomain)||p.scopes.length<1||p.scopes.length>16)return {ok:false,status:'scope'};
        return {ok:true,cookies:(await cookies(p)).map(metadata)};
      }
      return {ok:false,status:'message-type'};
    }
    return {read,sync,configure,revoke,forget,beforeUse,changed,alarm,startup,removed,handle,ensureBridge};
  }
  return {create,validDomain,matches,origins,identity,metadata};
})();
