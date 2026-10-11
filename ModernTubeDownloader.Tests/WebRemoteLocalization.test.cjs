const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const directory=path.join(__dirname,'../ModernTubeDownloader/WebRemote/wwwroot');
const source=fs.readFileSync(path.join(directory,'i18n.js'),'utf8');
function create(preference,languages){const values=new Map(preference?[['mtdRemoteLanguage',preference]]:[]);const context=vm.createContext({navigator:{languages},localStorage:{getItem:key=>values.get(key),setItem:(key,value)=>values.set(key,value)}});vm.runInContext(source,context);return {i18n:context.MTDRemoteI18n,values}}
test('Follow app is default; browser mode, legacy Auto and persisted overrides remain explicit',()=>{
  const follow=create(null,['pl-PL']);
  assert.equal(follow.i18n.preference,'app');
  assert.equal(follow.i18n.setDesktopLanguage('pl'),'pl');
  assert.equal(follow.i18n.setDesktopLanguage('en'),'en');
  assert.equal(create('browser',['pl-PL','en']).i18n.language,'pl');assert.equal(create('browser',['en-GB','pl']).i18n.language,'en');
  assert.equal(create('browser',['de-DE']).i18n.language,'en');assert.equal(create('browser',[]).i18n.language,'en');
  assert.equal(create('auto',['pl-PL']).i18n.preference,'browser');
  const page=create(null,['pl-PL']);page.i18n.select('en');assert.equal(page.values.get('mtdRemoteLanguage'),'en');
  assert.equal(create(page.values.get('mtdRemoteLanguage'),['pl-PL']).i18n.language,'en');
  assert.equal(page.i18n.setDesktopLanguage('pl'),'en');
  assert.equal(page.i18n.select('pl'),'pl');
  assert.equal(page.i18n.setDesktopLanguage('en'),'pl');
  assert.equal(page.i18n.select('app'),'en');
  assert.equal(page.i18n.select('browser'),'pl');assert.equal(page.i18n.select('invalid'),'en');
});
test('Both resources cover static controls, actions, failures and time units',()=>{
  const i18n=create(null,['en']).i18n;
  assert.deepEqual(Object.keys(i18n.resources.pl).sort(),Object.keys(i18n.resources.en).sort());
  for(const language of ['pl','en']){
    i18n.select(language);
    for(const [key,value]of Object.entries(i18n.resources[language]))assert.ok(value.length>0,key);
    for(const key of ['stopSave','rangeHint','pairing-invalid','pairing-expired','pairing-rate-limit','auth','footerHttp','footerHttps','connectionFailed','authDisabled','qualityLabel','logout'])
      assert.notEqual(i18n.t(key),key);
  }
  const app=fs.readFileSync(path.join(directory,'app.js'),'utf8');
  for(const match of app.matchAll(/\bt\("([^"]+)"\)/g))assert.ok(i18n.resources.en[match[1]],match[1]);
  assert.doesNotMatch(app,/language=status\.language|Authorization|sessionStorage\.setItem/);
  assert.match(app,/X-MTD-Language/);assert.match(app,/\/api\/pair/);assert.match(app,/\/api\/logout/);
});

function remotePage(preference,desktop='pl'){
  const elements=new Map(),values=new Map(preference?[['mtdRemoteLanguage',preference]]:[]);
  const element=id=>{
    if(!elements.has(id))elements.set(id,{value:'',textContent:'',hidden:false,options:Array.from({length:8},()=>({})),listeners:{},replacements:0,
      append(){},replaceChildren(){this.replacements++},addEventListener(name,handler){this.listeners[name]=handler}});
    return elements.get(id);
  };
  element('dashboard').hidden=true;element('rangeMode').value='full';
  const state={desktop,authenticated:false,authRequired:true,requests:[]};
  const context=vm.createContext({navigator:{languages:['en-GB']},location:{protocol:'https:'},
    localStorage:{getItem:key=>values.get(key),setItem:(key,value)=>values.set(key,value)},
    sessionStorage:{removeItem(){}},URL:{revokeObjectURL(){}},setInterval(){return 1},clearInterval(){},
    MTDTimeInput:require(path.join(directory,'time-input.js')),
    document:{hidden:false,documentElement:{},getElementById:element,createElement:()=>({append(){},addEventListener(){}})},
    fetch:async(url,options)=>{
      state.requests.push({url,options});
      let status=200,payload;
      if(url==='/api/auth-mode')payload={authenticationRequired:state.authRequired,desktopLanguage:state.desktop};
      else if(url==='/api/pair'){
        if(JSON.parse(options.body).code==='valid'){state.authenticated=true;payload={}}
        else {status=400;payload={error:'pairing-invalid'}}
      }else if(url==='/api/logout'){state.authenticated=false;payload={}}
      else if(!state.authenticated&&state.authRequired){status=401;payload={}}
      else payload=url==='/api/status'?{authenticationRequired:state.authRequired,paused:true}: [];
      return {ok:status===200,status,headers:{get:()=> 'application/json'},json:async()=>payload};
    }});
  vm.runInContext(source,context);vm.runInContext(fs.readFileSync(path.join(directory,'app.js'),'utf8'),context);
  return {state,element,context,values,poll:()=>vm.runInContext('poll()',context)};
}
test('unpaired screen polls safe bootstrap, follows desktop live and retranslates errors and footer',async()=>{
  const page=remotePage();
  await new Promise(setImmediate);
  assert.equal(page.element('dashboard').hidden,true);
  assert.equal(page.element('loginPanel').hidden,false);
  assert.equal(page.context.document.documentElement.lang,'pl');
  page.element('pairingCode').value='wrong';
  await page.element('loginForm').listeners.submit({preventDefault(){}});
  assert.equal(page.element('message').textContent,'Nieprawidłowy kod parowania.');
  page.state.desktop='en';await page.poll();
  assert.equal(page.element('loginTitle').textContent,'Pair with the app');
  assert.equal(page.element('message').textContent,'Incorrect pairing code.');
  assert.match(page.element('footer').textContent,/HTTPS encrypts/);
  assert.equal(page.element('logout').hidden,true);
});
test('valid session refresh skips pairing, logout restores form; manual language survives desktop polling',async()=>{
  const page=remotePage();
  await new Promise(setImmediate);
  page.element('pairingCode').value='valid';
  await page.element('loginForm').listeners.submit({preventDefault(){}});
  assert.equal(page.element('dashboard').hidden,false);assert.equal(page.element('logout').hidden,false);
  for(const manual of ['en','pl']){
    page.element('language').value=manual;page.element('language').listeners.change();
    await new Promise(setImmediate);
    page.state.desktop=manual==='pl'?'en':'pl';await page.poll();
    assert.equal(page.context.document.documentElement.lang,manual);
    assert.equal(page.values.get('mtdRemoteLanguage'),manual);
  }
  page.element('language').value='app';page.element('language').listeners.change();await new Promise(setImmediate);
  assert.equal(page.context.document.documentElement.lang,page.state.desktop);
  await page.element('logout').listeners.click();
  assert.equal(page.element('dashboard').hidden,true);assert.equal(page.element('loginPanel').hidden,false);
  await page.poll();assert.equal(page.element('dashboard').hidden,true);
});
test('unchanged polling preserves queue/LIVE nodes, edits and focus rather than redrawing the page',async()=>{
  const page=remotePage();
  await new Promise(setImmediate);
  page.element('pairingCode').value='valid';
  await page.element('loginForm').listeners.submit({preventDefault(){}});
  page.element('url').value='https://www.youtube.com/watch?v=sample';
  const hosts=['queue','completedQueue','liveActive','liveScheduled','livePartial'];
  const before=hosts.map(id=>page.element(id).replacements);
  page.context.document.activeElement=page.element('url');
  for(let i=0;i<5;i++)await page.poll();
  assert.deepEqual(hosts.map(id=>page.element(id).replacements),before);
  assert.equal(page.context.document.activeElement,page.element('url'));
  assert.equal(page.element('url').value,'https://www.youtube.com/watch?v=sample');
  page.state.desktop='en';await page.poll();
  assert.equal(page.element('queue').replacements,before[0]+1);
  await page.poll();assert.equal(page.element('queue').replacements,before[0]+1);
});
