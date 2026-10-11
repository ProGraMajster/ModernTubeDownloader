"use strict";
const $ = id => document.getElementById(id);
const i18n = MTDRemoteI18n;
let messageKey = "", polling = false, queueSignature = null, liveSignature = null, sessionRevision = 0;
function setText(node,value){if(node.textContent!==value)node.textContent=value}
let language = i18n.language, paused = false, timer = null, refreshing = false, authenticationRequired = true, admissionDeadline = 0;
const thumbnails = new Map();
function t(key){return i18n.t(key)}
function applyLanguage(){
  if(document.documentElement.lang!==language)document.documentElement.lang=language;
  document.title=`ModernTubeDownloader — ${t("subtitle")}`;
  for(const id of Object.keys(i18n.resources.en)){const element=$(id);if(element&&id!=="queue")setText(element,t(id))}
  $("quality").options[0].textContent=t("best");$("quality").options[7].textContent=t("audio");
  $("container").options[0].textContent=t("auto");
  $("rangeMode").options[0].textContent=t("full");$("rangeMode").options[1].textContent=t("custom");
  $("footer").textContent=t(location.protocol==="https:"?"footerHttps":"footerHttp");
  $("logout").hidden=!authenticationRequired||$("dashboard").hidden;$("authDisabled").hidden=authenticationRequired;
  $("connection").textContent=t($("dashboard").hidden?"offline":"online");
  $("pauseButton").textContent=t(paused?"resume":"pause");
  $("language").value=i18n.preference;
  $("language").options[0].textContent=language==="pl"?"🔄 Z aplikacji":"🔄 Follow app";
  $("language").options[3].textContent=language==="pl"?"🌐 Język przeglądarki":"🌐 Browser language";
  $("message").textContent=messageKey?t(messageKey):"";
  updateRangePreview();updateCountdown();
}
function timeParts(bound){return {hours:$(`${bound}Hours`).value,minutes:$(`${bound}Minutes`).value,seconds:$(`${bound}Seconds`).value}}
function updateRangePreview(){
  if($("rangeMode").value!=="custom"){$("rangePreview").textContent="";return}
  try{
    const range=MTDTimeInput.range("custom",timeParts("from"),timeParts("to"));
    $("rangePreview").textContent=`${range.from||t("rangeBeginning")} → ${range.to||t("rangeEnd")}`;
  }catch(error){$("rangePreview").textContent=t(error.message)}
}
function updateRangeMode(){
  const custom=$("rangeMode").value==="custom";
  $("rangeFields").hidden=!custom;
  for(const bound of ["from","to"])for(const unit of ["Hours","Minutes","Seconds"])$(`${bound}${unit}`).disabled=!custom;
  updateRangePreview();
}
function message(key){messageKey=key;$("message").textContent=key?t(key):""}
async function api(path, method="GET", body){
  const headers = {"X-MTD-Request":"1","X-MTD-Language":language,...(body?{"Content-Type":"application/json"}:{})};
  const response = await fetch(path,{method,headers,body:body?JSON.stringify(body):undefined,cache:"no-store",credentials:"same-origin"});
  if(response.status===401){showLogin();throw new Error("auth")}
  if(!response.ok){
    const payload=response.headers.get("content-type")?.includes("application/json")?await response.json().catch(()=>null):null;
    const code=payload?.error;
    const known={"incompatible-options":"incompatible","sponsorblock-mark-requires-mkv":"sponsorblock-mark-requires-mkv","cookie-file-missing":"cookie-file-missing","cookie-file-unreadable":"cookie-file-unreadable","cookie-file-unsupported":"cookie-file-unsupported","cookie-file-rejected":"cookie-file-rejected","live-open-in-app":"liveDesktop"};
    throw new Error(known[code] || (i18n.resources.en[code]?code:response.status===400 && code!=="analysis-failed"?"invalid":"failed"));
  }
  return response.status===204?null:response.headers.get("content-type")?.includes("application/json")?response.json():null;
}
function showLogin(){sessionRevision++;queueSignature=null;liveSignature=null;$("loginPanel").hidden=false;$("dashboard").hidden=true;$("logout").hidden=true;$("connection").textContent=t("offline");for(const url of thumbnails.values())URL.revokeObjectURL(url);thumbnails.clear()}
function showDashboard(){$("loginPanel").hidden=true;$("dashboard").hidden=false;$("logout").hidden=!authenticationRequired;$("connection").textContent=t("online");}
function button(label,handler){const element=document.createElement("button");element.type="button";element.textContent=label;element.addEventListener("click",handler);return element}
function duration(seconds){const total=Math.max(0,Math.floor(seconds||0));return [Math.floor(total/3600),Math.floor(total%3600/60),total%60].map(value=>String(value).padStart(2,"0")).join(":")}
async function action(path,method="POST"){
  try{await api(path,method);message("");await refresh()}catch(error){message(error.message)}
}
async function loadThumbnail(id,img){
  if(thumbnails.has(id)){img.src=thumbnails.get(id);return}
  try{const headers={ };const response=await fetch(`/api/queue/${id}/thumbnail`,{headers,cache:"no-store"});if(!response.ok)return;const url=URL.createObjectURL(await response.blob());thumbnails.set(id,url);img.src=url}catch{}
}
function renderItem(item,compact){
  const row=document.createElement("article");row.className=`queueItem${compact?" compact":""}`;
  const content=document.createElement("div");const title=document.createElement("div");title.className="queueTitle";title.textContent=`${item.position}. ${item.title || "—"}`;
  const meta=document.createElement("div");meta.className="meta";meta.textContent=[item.statusText,compact?null:`${Math.round(item.progress||0)}%`,item.quality,item.container,item.range,item.retryAttempt>1?`${t("retryCount")} ${item.retryAttempt}/${item.maximumAttempts}`:null].filter(Boolean).join(" · ");
  if(!compact){const img=document.createElement("img");img.className="thumb";img.alt="";if(item.hasThumbnail)loadThumbnail(item.id,img);row.append(img)}
  const actions=document.createElement("div");actions.className="actions";const base=`/api/queue/${item.id}`;
  if(["Waiting","DownloadingVideo","DownloadingAudio","Merging","Finalizing"].includes(item.status))actions.append(button(t("cancel"),()=>action(base+"/cancel")));
  if(["Failed","Cancelled","Partial","Interrupted"].includes(item.status))actions.append(button(t("retry"),()=>action(base+"/retry")));
  if(!["Waiting","DownloadingVideo","DownloadingAudio","Merging","Finalizing"].includes(item.status))actions.append(button(t("remove"),()=>action(base,"DELETE")));
  if(item.status==="Queued"){actions.append(button(t("up"),()=>action(base+"/up")));actions.append(button(t("down"),()=>action(base+"/down")))}
  content.append(title,meta);
  if(!compact){const transfer=document.createElement("div");transfer.className="meta";transfer.textContent=[item.speed?`${(item.speed/1048576).toFixed(1)} MiB/s`:null,item.etaSeconds?`${t("eta")} ${Math.round(item.etaSeconds)} ${t("seconds")}`:null,item.error||null].filter(Boolean).join(" · ");content.append(transfer);{const bar=document.createElement("progress");bar.className="progress";bar.max=100;bar.value=Math.max(0,Math.min(100,item.progress||0));content.append(bar)}}
  content.append(actions);row.append(content);return row;
}
function render(items){
  const present=new Set(items.map(item=>item.id));for(const [id,url] of thumbnails)if(!present.has(id)){URL.revokeObjectURL(url);thumbnails.delete(id)}
  const active=items.filter(item=>item.status!=="Completed"),completed=items.filter(item=>item.status==="Completed");
  const host=$("queue");host.replaceChildren(...active.map(item=>renderItem(item,false)));
  if(!active.length){const empty=document.createElement("div");empty.className="empty";empty.textContent=t("queue");host.append(empty)}
  $("completedSection").hidden=completed.length===0;
  $("completedSummary").textContent=`${t("completed")} (${completed.length})`;
  $("completedQueue").replaceChildren(...completed.map(item=>renderItem(item,true)));
}

function renderLive(sessions){
  const grouped={liveActive:[],liveScheduled:[],livePartial:[]};
  for(const item of sessions){
    const row=document.createElement("article");row.className="queueItem compact";
    const content=document.createElement("div"),title=document.createElement("div"),meta=document.createElement("div"),actions=document.createElement("div");
    title.className="queueTitle";title.textContent=item.title||"—";meta.className="meta";actions.className="actions";
    const due=item.nextCheckAt||item.retryAt;
    meta.textContent=[item.stateText,duration(item.recordedSeconds),`${(item.bytes/1048576).toFixed(1)} MiB`,item.speed?`${(item.speed/1048576).toFixed(2)} MiB/s`:null,item.quality,`${t("parts")}: ${item.partsCount}`,due?`${t("nextLive")} ${Math.max(0,Math.ceil((Date.parse(due)-Date.now())/1000))} ${t("seconds")}`:null,item.error].filter(Boolean).join(" · ");
    const base=`/api/live/${item.id}`;
    if(item.isActive)actions.append(button(t("stopSave"),()=>action(base+"/stop")));
    if(item.isActive||["Pending","WaitingForLive"].includes(item.state))actions.append(button(t("cancel"),()=>action(base+"/cancel")));
    if(item.canResume)actions.append(button(t("resume"),()=>action(base+"/resume")));
    content.append(title,meta,actions);row.append(content);
    grouped[item.isActive||item.state==="Pending"?"liveActive":item.state==="WaitingForLive"?"liveScheduled":"livePartial"].push(row);
  }
  for(const [id,rows] of Object.entries(grouped)){
    if(!rows.length){const empty=document.createElement("div");empty.className="empty";empty.textContent=t("queue");rows.push(empty)}
    $(id).replaceChildren(...rows);
  }
}

function updateCountdown(){const seconds=Math.max(0,Math.ceil((admissionDeadline-Date.now())/1000));const label=$("admissionCountdown");label.hidden=seconds===0;label.textContent=seconds?`${t("next")} ${seconds} ${t("seconds")}`:""}
async function refresh(){if(document.hidden||refreshing)return;refreshing=true;const requestedLanguage=language,requestedSession=sessionRevision;try{const [status,queue,live]=await Promise.all([api("/api/status"),api("/api/queue"),api("/api/live")]);if(requestedLanguage!==language||requestedSession!==sessionRevision)return;const authChanged=authenticationRequired!==status.authenticationRequired;authenticationRequired=status.authenticationRequired;if(authChanged)applyLanguage();paused=status.paused;admissionDeadline=status.remainingAdmissionSeconds>0?Date.now()+status.remainingAdmissionSeconds*1000:0;updateCountdown();setText($("pauseButton"),t(paused?"resume":"pause"));const nextQueue=JSON.stringify([language,queue]),nextLive=JSON.stringify([language,live]);if(nextQueue!==queueSignature){render(queue);queueSignature=nextQueue}if(nextLive!==liveSignature){renderLive(live);liveSignature=nextLive}if($("dashboard").hidden)showDashboard()}catch(error){if(error.message!=="auth")message("connectionFailed")}finally{refreshing=false;if(requestedLanguage!==language&&!$("dashboard").hidden)void refresh()}}
$("loginForm").addEventListener("submit",async event=>{
  event.preventDefault();const submit=$("connectButton");submit.disabled=true;
  try{await api("/api/pair","POST",{code:$("pairingCode").value.trim()});$("pairingCode").value="";message("");await refresh()}
  catch(error){message(error.message)}finally{submit.disabled=false}
});
$("logout").addEventListener("click",async()=>{try{await api("/api/logout","POST");showLogin();message("")}catch(error){message(error.message)}});
$("language").value=i18n.preference;
$("language").addEventListener("change",()=>{language=i18n.select($("language").value);applyLanguage();message("");if(!$("dashboard").hidden)refresh()});
// Remove obsolete credentials from older versions. Only the non-secret language preference persists.
try{sessionStorage.removeItem("mtdRemoteToken")}catch{}
$("pauseButton").addEventListener("click",()=>action(paused?"/api/queue/resume":"/api/queue/pause"));
$("rangeMode").addEventListener("change",updateRangeMode);
for(const bound of ["from","to"])for(const unit of ["Hours","Minutes","Seconds"])$(`${bound}${unit}`).addEventListener("input",updateRangePreview);
$("addForm").addEventListener("submit",async event=>{
  event.preventDefault();const submit=$("addButton");submit.disabled=true;
  try{
    const rangeMode=$("rangeMode").value;
    const range=MTDTimeInput.range(rangeMode,timeParts("from"),timeParts("to"));
    const result=await api("/api/queue/add","POST",{url:$("url").value,quality:$("quality").value,container:$("container").value,rangeMode,...range,useSubtitleDefaults:$("useSubtitleDefaults").checked,useSponsorBlockDefaults:$("useSponsorBlockDefaults").checked});
    $("url").value="";await refresh();message(result?.warning||"added");
  }catch(error){message(error.message)}finally{submit.disabled=false}
});
// One polling cadence serves both the unpaired screen and dashboard.
async function poll(){
  if(document.hidden||polling)return;
  polling=true;
  try{
    const response=await fetch("/api/auth-mode",{cache:"no-store",credentials:"same-origin"});
    if(!response.ok)throw new Error("failed");
    const config=await response.json();
    const previousLanguage=language,previousAuthentication=authenticationRequired;
    authenticationRequired=config.authenticationRequired===true;
    language=i18n.setDesktopLanguage(config.desktopLanguage);
    if(language!==previousLanguage||authenticationRequired!==previousAuthentication)applyLanguage();
    await refresh();
  }catch{showLogin();message("connectionFailed")}
  finally{polling=false}
}
async function bootstrap(){await poll();if(!timer)timer=setInterval(poll,2000)}
updateRangeMode();applyLanguage();setInterval(updateCountdown,1000);bootstrap();
