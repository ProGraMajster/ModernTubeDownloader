"use strict";
const $ = id => document.getElementById(id);
const ui = {
  pl: {online:"Połączono",offline:"Rozłączono",queue:"Brak aktywnych pozycji.",pause:"Pauza",resume:"Wznów",cancel:"Anuluj",retry:"Ponów",retryCount:"Próba",remove:"Usuń",up:"W górę",down:"W dół",completed:"Ukończone",next:"Następne pobieranie za",invalid:"Sprawdź adres, jakość, kontener i zakres czasu.",failed:"Nie udało się wykonać operacji. Sprawdź aplikację na komputerze.",incompatible:"Zakres wyklucza napisy i SponsorBlock; usuwanie segmentów wyklucza napisy.","sponsorblock-mark-requires-mkv":"Dla SponsorBlock Mark wybierz jawnie MKV. MP4 tworzy błędne rozdziały, a WebM nie został zweryfikowany.","sponsorblock-remove-experimental":"Dodano do kolejki. Remove jest eksperymentalny: cięcie bez ponownego kodowania może mieć niedokładności czasu.","cookie-file-missing":"Nie znaleziono wybranego pliku cookies. Wskaż go ponownie w Ustawieniach.","cookie-file-unreadable":"Nie można odczytać pliku cookies. Sprawdź dostęp w Ustawieniach.","cookie-file-unsupported":"Plik nie jest obsługiwanym Netscape cookies.txt.","cookie-file-rejected":"yt-dlp odrzucił plik cookies. Wyeksportuj go ponownie.",auth:"Nieprawidłowy token.",added:"Dodano do kolejki.",full:"Całość",custom:"Niestandardowy",audio:"Tylko dźwięk",best:"Najlepsza dostępna"},
  en: {online:"Connected",offline:"Disconnected",queue:"No active items.",pause:"Pause",resume:"Resume",cancel:"Cancel",retry:"Retry",retryCount:"Attempt",remove:"Remove",up:"Move up",down:"Move down",completed:"Completed",next:"Next download in",invalid:"Check URL, quality, container and time range.",failed:"The action failed. Check the desktop app.",incompatible:"A time range excludes subtitles and SponsorBlock; removal excludes subtitles.","sponsorblock-mark-requires-mkv":"Select explicit MKV for SponsorBlock Mark. MP4 chapters are broken and WebM is unverified.","sponsorblock-remove-experimental":"Added to queue. Remove is experimental: stream-copy cuts can have timing inaccuracies.","cookie-file-missing":"The selected cookies file is missing. Choose it again in Settings.","cookie-file-unreadable":"The cookies file cannot be read. Check access in Settings.","cookie-file-unsupported":"The file is not a supported Netscape cookies.txt file.","cookie-file-rejected":"yt-dlp rejected the cookies file. Export it again.",auth:"Invalid token.",added:"Added to queue.",full:"Full media",custom:"Custom",audio:"Audio only",best:"Best available"}
};
Object.assign(ui.pl,{stopSave:"Zatrzymaj i zapisz",parts:"Części",nextLive:"Kolejne sprawdzenie za",recorded:"Nagrano",liveDesktop:"Transmisję LIVE rozpocznij w aplikacji na komputerze."});
Object.assign(ui.en,{stopSave:"Stop and save",parts:"Parts",nextLive:"Next check in",recorded:"Recorded",liveDesktop:"Start a LIVE recording in the desktop app."});
Object.assign(ui.pl,{remoteSourceScope:"Zdalne dodawanie jest obecnie ograniczone do obsługiwanych adresów YouTube."});
Object.assign(ui.en,{remoteSourceScope:"Remote adding is currently limited to supported YouTube URLs."});
Object.assign(ui.pl,{rangeInvalid:"Wpisz cyfry: godziny do 720, minuty i sekundy od 0 do 59 (maksymalnie 30 dni).",rangeRequired:"Ustaw początek lub koniec zakresu.",rangeOrder:"Koniec musi być późniejszy niż początek.",rangeBeginning:"Początek materiału",rangeEnd:"Koniec materiału"});
Object.assign(ui.en,{rangeInvalid:"Enter digits: hours up to 720, minutes and seconds from 0 to 59 (30 days maximum).",rangeRequired:"Set the start or end of the range.",rangeOrder:"The end must be later than the start.",rangeBeginning:"Start of media",rangeEnd:"End of media"});
let language = "pl", paused = false, timer = null, refreshing = false, authenticationRequired = true, admissionDeadline = 0;
const thumbnails = new Map();
const staticLabels = {
  pl: {subtitle:"Panel zdalny w sieci lokalnej",loginTitle:"Połącz z aplikacją",loginDescription:"Wpisz token wyświetlany w ustawieniach aplikacji na komputerze.",connectButton:"Połącz",queueHeading:"Kolejka",addHeading:"Dodaj materiał",urlLabel:"Adres filmu lub playlisty",qualityLabel:"Jakość",containerLabel:"Kontener",rangeLabel:"Zakres materiału",fromLabel:"Od",toLabel:"Do",rangeHint:"Wpisuj tylko cyfry. Puste „Od” oznacza początek, a puste „Do” — koniec materiału.",subtitleDefaultsLabel:"Napisy według ustawień aplikacji",sponsorDefaultsLabel:"SponsorBlock według ustawień aplikacji",addButton:"Dodaj do kolejki",footer:"Dostęp tylko w zaufanej sieci lokalnej. Połączenie HTTP nie jest szyfrowane."},
  en: {subtitle:"Local network remote",loginTitle:"Connect to the app",loginDescription:"Enter the token shown in the desktop application's settings.",connectButton:"Connect",queueHeading:"Queue",addHeading:"Add media",urlLabel:"Video or playlist URL",qualityLabel:"Quality",containerLabel:"Container",rangeLabel:"Media range",fromLabel:"From",toLabel:"To",rangeHint:"Enter digits only. Leave From empty to start at the beginning, or To empty to continue to the end.",subtitleDefaultsLabel:"Subtitles using application defaults",sponsorDefaultsLabel:"SponsorBlock using application defaults",addButton:"Add to queue",footer:"Use only on a trusted local network. HTTP traffic is not encrypted."}
};
for(const bound of ["from","to"]){
  for(const [unit,pl,en] of [["Hours","Godz.","Hours"],["Minutes","Min.","Min."],["Seconds","Sek.","Sec."]]){
    staticLabels.pl[`${bound}${unit}Label`]=pl;
    staticLabels.en[`${bound}${unit}Label`]=en;
  }
}
function t(key){return ui[language][key] || key}
document.getElementById("remoteSourceScope").textContent=t("remoteSourceScope");
function applyLanguage(){ $("remoteSourceScope").textContent=t("remoteSourceScope"); $("liveHeading").textContent="LIVE"; $("liveActiveHeading").textContent=language==="pl"?"Aktualnie nagrywane":"Active recordings"; $("liveScheduledHeading").textContent=language==="pl"?"Zaplanowane":"Scheduled"; $("livePartialHeading").textContent=language==="pl"?"Częściowe i ostatnie":"Partial and recent";for(const [id,value] of Object.entries(staticLabels[language]))$(id).textContent=value;$("quality").options[0].textContent=t("best");$("quality").options[7].textContent=t("audio");$("rangeMode").options[0].textContent=t("full");$("rangeMode").options[1].textContent=t("custom");updateRangePreview()}
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
function message(value){$("message").textContent = value}
function token(){return sessionStorage.getItem("mtdRemoteToken") || ""}
async function api(path, method="GET", body){
  const headers = {...(authenticationRequired && token()?{Authorization:`Bearer ${token()}`} : {}),...(body?{"Content-Type":"application/json"}:{})};
  const response = await fetch(path,{method,headers,body:body?JSON.stringify(body):undefined,cache:"no-store"});
  if(response.status===401){sessionStorage.removeItem("mtdRemoteToken");showLogin();throw new Error("auth")}
  if(!response.ok){
    const payload=response.headers.get("content-type")?.includes("application/json")?await response.json().catch(()=>null):null;
    const code=payload?.error;
    const known={"incompatible-options":"incompatible","sponsorblock-mark-requires-mkv":"sponsorblock-mark-requires-mkv","cookie-file-missing":"cookie-file-missing","cookie-file-unreadable":"cookie-file-unreadable","cookie-file-unsupported":"cookie-file-unsupported","cookie-file-rejected":"cookie-file-rejected","live-open-in-app":"liveDesktop"};
    throw new Error(known[code] || (response.status===400 && code!=="analysis-failed"?"invalid":"failed"));
  }
  return response.status===204?null:response.headers.get("content-type")?.includes("application/json")?response.json():null;
}
function showLogin(){$("loginPanel").hidden=false;$("dashboard").hidden=true;$("connection").textContent=t("offline");clearInterval(timer);timer=null;for(const url of thumbnails.values())URL.revokeObjectURL(url);thumbnails.clear()}
function showDashboard(){$("loginPanel").hidden=true;$("dashboard").hidden=false;$("connection").textContent=t("online");if(!timer)timer=setInterval(refresh,2000)}
function button(label,handler){const element=document.createElement("button");element.type="button";element.textContent=label;element.addEventListener("click",handler);return element}
function duration(seconds){const total=Math.max(0,Math.floor(seconds||0));return [Math.floor(total/3600),Math.floor(total%3600/60),total%60].map(value=>String(value).padStart(2,"0")).join(":")}
async function action(path,method="POST"){
  try{await api(path,method);message("");await refresh()}catch(error){message(t(error.message))}
}
async function loadThumbnail(id,img){
  if(thumbnails.has(id)){img.src=thumbnails.get(id);return}
  try{const headers=authenticationRequired&&token()?{Authorization:`Bearer ${token()}`}:{ };const response=await fetch(`/api/queue/${id}/thumbnail`,{headers,cache:"no-store"});if(!response.ok)return;const url=URL.createObjectURL(await response.blob());thumbnails.set(id,url);img.src=url}catch{}
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
  if(!compact){const transfer=document.createElement("div");transfer.className="meta";transfer.textContent=[item.speed?`${(item.speed/1048576).toFixed(1)} MiB/s`:null,item.etaSeconds?`ETA ${Math.round(item.etaSeconds)} s`:null,item.error||null].filter(Boolean).join(" · ");content.append(transfer);{const bar=document.createElement("progress");bar.className="progress";bar.max=100;bar.value=Math.max(0,Math.min(100,item.progress||0));content.append(bar)}}
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
    meta.textContent=[item.stateText,duration(item.recordedSeconds),`${(item.bytes/1048576).toFixed(1)} MiB`,item.speed?`${(item.speed/1048576).toFixed(2)} MiB/s`:null,item.quality,`${t("parts")}: ${item.partsCount}`,due?`${t("nextLive")} ${Math.max(0,Math.ceil((Date.parse(due)-Date.now())/1000))} s`:null,item.error].filter(Boolean).join(" · ");
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

function updateCountdown(){const seconds=Math.max(0,Math.ceil((admissionDeadline-Date.now())/1000));const label=$("admissionCountdown");label.hidden=seconds===0;label.textContent=seconds?`${t("next")} ${seconds} s`:""}
async function refresh(){if(document.hidden||refreshing)return;refreshing=true;try{const [status,queue,live]=await Promise.all([api("/api/status"),api("/api/queue"),api("/api/live")]);authenticationRequired=status.authenticationRequired;language=status.language==="en"?"en":"pl";document.documentElement.lang=language;applyLanguage();paused=status.paused;admissionDeadline=status.remainingAdmissionSeconds>0?Date.now()+status.remainingAdmissionSeconds*1000:0;updateCountdown();$("pauseButton").textContent=t(paused?"resume":"pause");render(queue);renderLive(live);showDashboard()}catch(error){if(error.message!=="auth")message(t("failed"))}finally{refreshing=false}}
$("loginForm").addEventListener("submit",async event=>{event.preventDefault();sessionStorage.setItem("mtdRemoteToken",$("token").value.trim());$("token").value="";try{await api("/api/status");message("");await refresh()}catch(error){message(t(error.message))}});
$("pauseButton").addEventListener("click",()=>action(paused?"/api/queue/resume":"/api/queue/pause"));
$("rangeMode").addEventListener("change",updateRangeMode);
for(const bound of ["from","to"])for(const unit of ["Hours","Minutes","Seconds"])$(`${bound}${unit}`).addEventListener("input",updateRangePreview);
$("addForm").addEventListener("submit",async event=>{
  event.preventDefault();const submit=$("addButton");submit.disabled=true;
  try{
    const rangeMode=$("rangeMode").value;
    const range=MTDTimeInput.range(rangeMode,timeParts("from"),timeParts("to"));
    const result=await api("/api/queue/add","POST",{url:$("url").value,quality:$("quality").value,container:$("container").value,rangeMode,...range,useSubtitleDefaults:$("useSubtitleDefaults").checked,useSponsorBlockDefaults:$("useSponsorBlockDefaults").checked});
    $("url").value="";await refresh();message(t(result?.warning||"added"));
  }catch(error){message(t(error.message))}finally{submit.disabled=false}
});
async function bootstrap(){try{const response=await fetch("/api/auth-mode",{cache:"no-store"});if(!response.ok)throw new Error("failed");authenticationRequired=(await response.json()).authenticationRequired;if(!authenticationRequired){sessionStorage.removeItem("mtdRemoteToken");await refresh()}else if(token())await refresh();else showLogin()}catch{showLogin();message(t("failed"))}}
updateRangeMode();applyLanguage();setInterval(updateCountdown,1000);bootstrap();
