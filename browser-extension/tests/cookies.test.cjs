const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),crypto=require('node:crypto');
const src=path.join(__dirname,'../src');
function fixture(){
  const state={cookieProfiles:[]},queries=[],sent=[],alarms=new Map(),removed=[];
  const id='a'.repeat(32),records=[{domain:'.example.test',path:'/',name:'synthetic',value:crypto.randomUUID(),session:true,httpOnly:true,secure:true,hostOnly:false,storeId:'0'},
    {domain:'.unrelated.test',path:'/',name:'other',value:crypto.randomUUID(),session:true,storeId:'0'}];
  let allowed=true,native=async message=>({ok:true,status:'synced',cookieCount:message.cookies.length,excludedPartitioned:0,snapshotVersion:message.snapshotVersion});
  const api={runtime:{id,getURL:p=>'chrome-extension://'+id+'/'+p,sendNativeMessage:async(_,m)=>{sent.push(m);return native(m)}},
    storage:{local:{get:async defaults=>structuredClone({...defaults,...state}),set:async values=>Object.assign(state,structuredClone(values))}},
    permissions:{contains:async()=>allowed,remove:async p=>removed.push(p)},
    cookies:{getAll:async query=>{queries.push(query);return records}},
    alarms:{create:async(name,info)=>alarms.set(name,info),clear:async name=>alarms.delete(name)},
    tabs:{get:async()=>({url:'https://www.youtube.com/watch?v=abc'})}};
  const context=vm.createContext({URL,URLSearchParams,crypto,console});
  for(const file of ['shared.js','cookies.js'])vm.runInContext(fs.readFileSync(path.join(src,file),'utf8'),context);
  const sync=context.MTDCookies.create(api),profile={mode:'domains',browser:'chrome',scopes:['example.test'],selection:[],period:0};
  return {state,api,sync,profile,records,queries,sent,alarms,removed,context,setAllowed:v=>allowed=v,setNative:f=>native=f,
    sender:{id,url:api.runtime.getURL('src/options.html')}};
}
test('no opt-in means no cookie queries, no native messages, and ordinary public flow',async()=>{
  const f=fixture();assert.equal((await f.sync.beforeUse('https://example.test/video')).ok,true);assert.equal(f.queries.length,0);assert.equal(f.sent.length,0);
});

function refreshConnection(f){
  const messages=[],handlers={};let disconnected=false;
  const port={postMessage:m=>messages.push(m),disconnect:()=>{disconnected=true;handlers.disconnect?.()},
    onMessage:{addListener:f=>handlers.message=f},onDisconnect:{addListener:f=>handlers.disconnect=f}};
  f.api.runtime.connectNative=()=>port;
  return {messages,port,handlers,get disconnected(){return disconnected}};
}
const desktopRequest=p=>({protocolVersion:1,type:'refresh',requestId:crypto.randomUUID(),profileId:p.profileId,oldVersion:1,deadline:new Date(Date.now()+5000).toISOString()});
test('desktop reverse request uses consented port and new correlated snapshot, not a cached ACK',async()=>{
  const f=fixture(),connection=refreshConnection(f),p=await f.sync.configure(f.profile);
  assert.equal(connection.messages[0].type,'watch');assert.equal(connection.messages[0].cookies.length,0);
  const request=desktopRequest(p);connection.handlers.message(request);
  await new Promise(setImmediate);await new Promise(setImmediate);
  const snapshot=f.sent.at(-1);assert.equal(snapshot.requestId,request.requestId);assert.ok(snapshot.snapshotVersion>request.oldVersion);
  assert.equal(snapshot.refreshBeforeUse,true);assert.equal(snapshot.cookies.length,1);
  assert.ok(!JSON.stringify(connection.messages).includes(f.records[0].value));
  assert.ok(!JSON.stringify(f.state).includes(f.records[0].value));
});
test('no JIT consent means no reverse connection; unknown/disabled/revoked profiles never read cookies',async()=>{
  const f=fixture(),connection=refreshConnection(f),p=await f.sync.configure({...f.profile,beforeUse:false});
  assert.equal(connection.messages.length,0);assert.equal(f.queries.length,0);
  f.state.cookieProfiles[0].beforeUse=true;await f.sync.ensureBridge();
  for(const mode of ['no-matching-profile','disabled','permission-revoked']){
    f.state.cookieProfiles[0].enabled=mode!=='disabled';f.setAllowed(mode!=='permission-revoked');
    connection.handlers.message(desktopRequest(mode==='no-matching-profile'?{profileId:crypto.randomUUID()}:p));
    await new Promise(setImmediate);await new Promise(setImmediate);
    assert.equal(connection.messages.at(-1).refreshStatus,mode);
  }
  assert.equal(f.queries.length,0);
});
test('revoking the last JIT profile closes metadata-only port and does not reconnect without consent',async()=>{
  const f=fixture(),connection=refreshConnection(f),p=await f.sync.configure(f.profile);
  await f.sync.revoke(p.profileId);assert.equal(connection.disconnected,true);
  assert.ok(!f.alarms.has('mtd-cookie-bridge'));
});
test('reverse request waits for an earlier sync then produces a distinct version and request correlation',async()=>{
  const f=fixture(),connection=refreshConnection(f),p=await f.sync.configure(f.profile);let finish;
  f.setNative(m=>f.sent.length===1?new Promise(resolve=>finish=()=>resolve({ok:true,cookieCount:1,snapshotVersion:m.snapshotVersion})):
    Promise.resolve({ok:true,cookieCount:1,snapshotVersion:m.snapshotVersion}));
  const ordinary=f.sync.sync(p.profileId);await new Promise(setImmediate);
  const request=desktopRequest(p);request.oldVersion=f.sent[0].snapshotVersion;connection.handlers.message(request);
  await new Promise(setImmediate);assert.equal(f.sent.length,1);finish();await ordinary;
  await new Promise(setImmediate);await new Promise(setImmediate);
  assert.equal(f.sent.length,2);assert.equal(f.sent[1].requestId,request.requestId);assert.ok(f.sent[1].snapshotVersion>f.sent[0].snapshotVersion);
});
test('permission denial stops configuration and all cookie reads',async()=>{
  const f=fixture();f.setAllowed(false);assert.equal((await f.sync.configure(f.profile)).ok,false);assert.equal(f.queries.length,0);
});
test('selected domains filter even unexpected browser records and storage holds metadata only',async()=>{
  const f=fixture(),p=await f.sync.configure(f.profile);assert.equal(p.ok,true);await f.sync.sync(p.profileId);
  assert.deepEqual(f.queries.map(q=>q.domain),['example.test']);assert.equal(f.sent[0].cookies.length,1);
  assert.equal(f.sent[0].cookies[0].domain,'.example.test');assert.ok(!JSON.stringify(f.state).includes(f.records[0].value));
  const list=await f.sync.handle({type:'cookie-list',scopes:['example.test']},f.sender);
  assert.equal(list.cookies[0].httpOnly,true);assert.equal(list.cookies[0].value,undefined);
});
test('custom selection contains cookie identities only and no hidden cookie values',async()=>{
  const f=fixture();f.profile.mode='custom';f.profile.selection=[f.context.MTDCookies.identity(f.records[0])];
  const p=await f.sync.configure(f.profile);await f.sync.sync(p.profileId);assert.equal(f.sent[0].cookies.length,1);
  f.state.cookieProfiles[0].selection=[];await f.sync.sync(p.profileId);assert.equal(f.sent[1].cookies.length,0);
});
test('remove/set changes coalesce to one MV3 alarm and one snapshot',async()=>{
  const f=fixture(),p=await f.sync.configure(f.profile);await f.sync.changed({cookie:f.records[0],removed:true});await f.sync.changed({cookie:f.records[0],removed:false});
  assert.equal(f.alarms.size,1);assert.equal(f.sent.length,0);await f.sync.alarm({name:[...f.alarms.keys()][0]});assert.equal(f.sent.length,1);
});
test('parallel sync for one profile awaits one ACK; JIT does not complete first',async()=>{
  const f=fixture(),p=await f.sync.configure(f.profile);let finish;
  f.setNative(m=>new Promise(resolve=>finish=()=>resolve({ok:true,cookieCount:1,snapshotVersion:m.snapshotVersion})));
  let completed=false;const first=f.sync.beforeUse('https://example.test/video').then(()=>completed=true),second=f.sync.sync(p.profileId);
  await new Promise(setImmediate);assert.equal(completed,false);assert.equal(f.sent.length,1);finish();await Promise.all([first,second]);assert.equal(completed,true);
});
test('revocation wins over in-flight ACK and sends newer tombstone before removing native permission',async()=>{
  const f=fixture(),p=await f.sync.configure(f.profile);let finish;
  f.setNative(m=>m.type==='cookieSnapshot'?new Promise(resolve=>finish=()=>resolve({ok:true,cookieCount:1})):Promise.resolve({ok:true,snapshotVersion:m.snapshotVersion}));
  const first=f.sync.sync(p.profileId);await new Promise(setImmediate);const revoke=f.sync.revoke(p.profileId);await new Promise(setImmediate);
  assert.equal(f.state.cookieProfiles[0].enabled,false);finish();await Promise.all([first,revoke]);
  assert.equal(f.state.cookieProfiles[0].enabled,false);assert.equal(f.state.cookieProfiles[0].status,'revoked');assert.equal(f.sent.at(-1).type,'revoke');
  assert.ok(f.sent.at(-1).snapshotVersion>f.sent[0].snapshotVersion);assert.ok(f.removed.some(p=>p.permissions?.includes('nativeMessaging')));
});
test('failed deletion is visibly pending, never silently confirmed, and retains retry authority',async()=>{
  const f=fixture(),p=await f.sync.configure(f.profile);f.setNative(async()=>({ok:false,status:'bridge-unavailable'}));
  assert.equal((await f.sync.revoke(p.profileId)).ok,false);assert.equal(f.state.cookieProfiles[0].status,'revocation-pending');
  assert.ok(!f.removed.some(p=>p.permissions?.includes('nativeMessaging')));
});

test('multiple pending revocations retain native permission until every tombstone is acknowledged',async()=>{
  const f=fixture(),a=await f.sync.configure(f.profile),b=await f.sync.configure(f.profile),c=await f.sync.configure(f.profile);
  f.state.cookieProfiles.forEach(p=>{p.enabled=false;p.status='revocation-pending';p.count=1});
  await f.sync.revoke(a.profileId);
  assert.ok(!f.removed.some(p=>p.permissions?.includes('nativeMessaging')));
  assert.equal(f.state.cookieProfiles[0].count,0);
  await f.sync.revoke(b.profileId);
  assert.ok(!f.removed.some(p=>p.permissions?.includes('nativeMessaging')));
  await f.sync.revoke(c.profileId);
  assert.ok(f.removed.some(p=>p.permissions?.includes('nativeMessaging')));
});

test('pending delete can reach native transport after cookies and alarms permissions were removed',async()=>{
  const f=fixture(),p=await f.sync.configure(f.profile);f.setAllowed(false);
  f.api.alarms.clear=async()=>{throw new Error('No alarms permission')};
  assert.equal((await f.sync.revoke(p.profileId)).ok,true);
  assert.equal(f.sent[0].type,'revoke');assert.equal(f.queries.length,0);
});
test('browser restart sync and periodic OFF defaults survive a new worker',async()=>{
  const f=fixture();await f.sync.configure(f.profile);assert.equal(f.alarms.size,0);
  const replacement=f.context.MTDCookies.create(f.api);await replacement.startup();assert.equal(f.sent.length,1);
});
test('periodic interval is bounded and uses alarms, never a worker setInterval',async()=>{
  const f=fixture();await assert.rejects(()=>f.sync.configure({...f.profile,period:1}));
  await f.sync.configure({...f.profile,period:15});assert.equal([...f.alarms.values()][0].periodInMinutes,15);
});

test('worker startup restores a configured periodic alarm even with startup sync disabled',async()=>{
  const f=fixture();await f.sync.configure({...f.profile,period:15,onStartup:false});f.alarms.clear();
  const replacement=f.context.MTDCookies.create(f.api);await replacement.startup();
  assert.equal([...f.alarms.values()][0].periodInMinutes,15);assert.equal(f.sent.length,0);
});

test('cookie options localization keys and desktop catalog have PL/EN parity',()=>{
  const pl=JSON.parse(fs.readFileSync(path.join(__dirname,'../_locales/pl/messages.json'))),en=JSON.parse(fs.readFileSync(path.join(__dirname,'../_locales/en/messages.json')));
  assert.deepEqual(Object.keys(pl).sort(),Object.keys(en).sort());
  for(const file of ['cookies-options.js','cookies-client.js','cookie-handoff.js']){
    const code=fs.readFileSync(path.join(src,file),'utf8');
    for(const match of code.matchAll(/(?:MTDI18n\.t|status)\('([^']+)'\)/g))assert.ok(pl[match[1]]&&en[match[1]],match[1]);
  }
});
test('content script cannot list records or configure permissions, nor forge another video JIT',async()=>{
  const f=fixture(),sender={id:f.api.runtime.id,url:'https://www.youtube.com/watch?v=abc',frameId:0,tab:{id:1}};
  assert.equal((await f.sync.handle({type:'cookie-list',scopes:['example.test']},sender)).ok,false);
  assert.equal((await f.sync.handle({type:'cookie-before-use',url:'https://www.youtube.com/watch?v=other'},sender)).ok,false);
  assert.equal((await f.sync.handle({type:'cookie-status'},{...f.sender,id:'other'})).ok,false);
});
test('partition identity is retained until native exclusion, never deduped as an ordinary cookie',async()=>{
  const f=fixture();f.records.push({...f.records[0],partitionKey:{topLevelSite:'https://other.test'}});
  const p=await f.sync.configure(f.profile);await f.sync.sync(p.profileId);assert.equal(f.sent[0].cookies.length,2);assert.ok(f.sent[0].cookies[1].partitionKey);
});
test('manifest declares only optional credential permissions and exact origins are requested',()=>{
  const manifest=JSON.parse(fs.readFileSync(path.join(__dirname,'../manifest.json')));
  assert.deepEqual(manifest.permissions,['activeTab','contextMenus','storage']);assert.ok(manifest.optional_permissions.includes('cookies'));assert.equal(manifest.host_permissions,undefined);
  const f=fixture();assert.deepEqual([...f.context.MTDCookies.origins(['example.test'])],['https://example.test/*','https://*.example.test/*']);
});
test('concurrent different profiles do not lose status updates',async()=>{
  const f=fixture(),a=await f.sync.configure(f.profile),b=await f.sync.configure(f.profile);
  await Promise.all([f.sync.sync(a.profileId),f.sync.sync(b.profileId)]);assert.equal(f.state.cookieProfiles.filter(p=>p.status==='synced').length,2);
});

test('native callback transport consumes lastError and never exposes its raw diagnostic',async()=>{
  const f=fixture(),p=await f.sync.configure(f.profile);
  f.api.runtime.sendNativeMessage=(_,message,callback)=>{
    f.api.runtime.lastError={message:'Specified native messaging host not found. '+f.records[0].value};callback();delete f.api.runtime.lastError;
  };
  const result=await f.sync.sync(p.profileId);assert.equal(result.status,'bridge-not-installed');
  assert.ok(!JSON.stringify(f.state).includes(f.records[0].value));
});

test('forget removes only acknowledged zero-count profiles and survives worker restart without permissions',async()=>{
  const f=fixture(),a=await f.sync.configure(f.profile),b=await f.sync.configure(f.profile);
  await f.sync.revoke(a.profileId);f.setAllowed(false);
  const messages=f.sent.length,reads=f.queries.length;
  assert.equal((await f.sync.handle({type:'cookie-forget',profileId:a.profileId},f.sender)).ok,true);
  assert.equal(f.state.cookieProfiles.length,1);assert.equal(f.state.cookieProfiles[0].id,b.profileId);
  const replacement=f.context.MTDCookies.create(f.api);
  assert.equal((await replacement.read()).some(p=>p.id===a.profileId),false);
  assert.equal((await replacement.forget(a.profileId)).ok,true);
  assert.equal(f.sent.length,messages);assert.equal(f.queries.length,reads);
});
test('forget refuses active, pending, or inconsistent nonzero profiles rather than hiding deletion failures',async()=>{
  const f=fixture(),p=await f.sync.configure(f.profile);
  assert.equal((await f.sync.forget(p.profileId)).ok,false);
  for(const patch of [{enabled:false,status:'revocation-pending',count:1},
    {enabled:false,status:'revoked',count:1},{enabled:false,status:'revoked',count:0,excluded:1}]){
    Object.assign(f.state.cookieProfiles[0],patch);
    assert.equal((await f.sync.forget(p.profileId)).ok,false);assert.equal(f.state.cookieProfiles.length,1);
  }
  assert.equal(f.sent.length,0);assert.equal(f.queries.length,0);
});
test('content and popup cannot forget cookie profiles',async()=>{
  const f=fixture(),p=await f.sync.configure(f.profile);await f.sync.revoke(p.profileId);
  for(const sender of [{id:f.api.runtime.id,url:'https://www.youtube.com/watch?v=abc',frameId:0,tab:{id:1}},
    {...f.sender,url:f.api.runtime.getURL('src/popup.html')}])
    assert.equal((await f.sync.handle({type:'cookie-forget',profileId:p.profileId},sender)).ok,false);
  assert.equal(f.state.cookieProfiles.length,1);
});
test('forget cannot race a snapshot ACK to restore consent or hide an unacknowledged revoke',async()=>{
  const f=fixture(),p=await f.sync.configure(f.profile);let finish;
  f.setNative(m=>m.type==='cookieSnapshot'?new Promise(resolve=>finish=()=>resolve({ok:true,cookieCount:1})):
    Promise.resolve({ok:true,snapshotVersion:m.snapshotVersion}));
  const sync=f.sync.sync(p.profileId);await new Promise(setImmediate);
  const revoke=f.sync.revoke(p.profileId);await new Promise(setImmediate);
  assert.equal((await f.sync.forget(p.profileId)).ok,false);
  finish();await Promise.all([sync,revoke]);
  assert.equal((await f.sync.forget(p.profileId)).ok,true);assert.equal(f.state.cookieProfiles.length,0);
  assert.equal((await f.sync.sync(p.profileId)).status,'disabled');assert.equal(f.sent.at(-1).type,'revoke');
});

async function optionsFixture(f,language='pl'){
  const nodes=new Map();
  function node(){return {children:[],textContent:'',disabled:false,handlers:{},append(...items){this.children.push(...items)},
    replaceChildren(...items){this.children=items},addEventListener(type,handler){this.handlers[type]=handler},focus(){},
    async click(){await this.handlers.click?.({target:this})}}}
  const doc={getElementById:id=>{if(!nodes.has(id))nodes.set(id,node());return nodes.get(id)},createElement:()=>node()};
  const permissions=[],messages=[];let accepted=true;
  f.api.permissions.request=async value=>{permissions.push(value);return true};
  f.api.runtime.sendMessage=async message=>{messages.push(message);return f.sync.handle(message,f.sender)};
  const catalog=JSON.parse(fs.readFileSync(path.join(__dirname,'../_locales/'+language+'/messages.json')));
  Object.assign(f.context,{confirm:()=>accepted,MTDI18n:{t:key=>catalog[key]?.message||key,subscribe:()=>()=>{}},
    MTDContext:{call:action=>action(),listen:(target,type,handler)=>target.addEventListener(type,handler),own:()=>{}}});
  vm.runInContext(fs.readFileSync(path.join(src,'cookies-options.js'),'utf8'),f.context);
  await f.context.MTDCookieOptions.initialize(doc,f.api);
  return {doc,permissions,messages,setConfirmed:value=>accepted=value,
    deleteButton:()=>doc.getElementById('cookieProfiles').children[0].children.at(-1)};
}
for(const language of ['pl','en'])test('Options '+language+' deletes old acknowledged profile without native/cookie permissions',async()=>{
  const f=fixture(),p=await f.sync.configure(f.profile);await f.sync.revoke(p.profileId);f.setAllowed(false);
  const ui=await optionsFixture(f,language),messages=f.sent.length;
  assert.equal(ui.deleteButton().textContent,language==='pl'?'Usuń profil':'Delete profile');
  await ui.deleteButton().click();assert.equal(ui.doc.getElementById('cookieProfiles').children.length,0);
  assert.equal(ui.permissions.length,0);assert.equal(f.sent.length,messages);assert.equal(f.queries.length,0);
  assert.ok(ui.messages.some(m=>m.type==='cookie-forget'));
});
test('Options delete active profile confirms, revokes, awaits native ACK and then removes it',async()=>{
  const f=fixture();await f.sync.configure(f.profile);const ui=await optionsFixture(f);
  ui.setConfirmed(false);await ui.deleteButton().click();assert.equal(f.state.cookieProfiles.length,1);assert.equal(f.sent.length,0);
  ui.setConfirmed(true);await ui.deleteButton().click();assert.equal(f.state.cookieProfiles.length,0);
  assert.equal(f.sent.length,1);assert.equal(f.sent[0].type,'revoke');assert.equal(f.queries.length,0);
  assert.deepEqual(JSON.parse(JSON.stringify(ui.permissions)),[{permissions:['nativeMessaging']}]);
  assert.deepEqual(ui.messages.filter(m=>m.type!=='cookie-status').map(m=>m.type),['cookie-revoke','cookie-forget']);
});
test('Options failed deletion keeps the profile visible and retryable, never sends forget',async()=>{
  const f=fixture();await f.sync.configure(f.profile);f.setNative(async()=>({ok:false,status:'bridge-unavailable'}));
  const ui=await optionsFixture(f);await ui.deleteButton().click();
  assert.equal(f.state.cookieProfiles[0].status,'revocation-pending');assert.equal(ui.doc.getElementById('cookieProfiles').children.length,1);
  assert.equal(ui.messages.some(m=>m.type==='cookie-forget'),false);assert.equal(ui.deleteButton().disabled,false);
});
