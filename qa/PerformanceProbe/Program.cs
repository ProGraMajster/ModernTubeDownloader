using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using SkiaSharp;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Batch;

if (args is ["--stage3-queue"]) { await Stage3Workload.MeasureQueueAsync(); return; }
if (args is ["--stage3-logs", var loggingRoot, var fakeTools]) { await Stage3Workload.MeasureLogsAsync(loggingRoot, fakeTools); return; }

if (args[0] == "--variant")
{
 var source=Path.GetFullPath(args[1]); var destination=Path.GetFullPath(args[2]);
 foreach(var directory in new[]{source,destination})
  if(!directory.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(directory).StartsWith("mtd-automation-smoke-",StringComparison.Ordinal))
   throw new InvalidOperationException("Only isolated synthetic profiles may be copied.");
 if(Directory.Exists(destination)) throw new IOException("Refusing to overwrite an existing profile.");
 Directory.CreateDirectory(destination);
 foreach(var directory in Directory.EnumerateDirectories(source,"*",SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination,Path.GetRelativePath(source,directory)));
 foreach(var file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories)) File.Copy(file,Path.Combine(destination,Path.GetRelativePath(source,file)));
 var variantPaths=AppPaths.Create(destination);
 var variantSettings=new SettingsService(variantPaths,new NullAppLogger()); await variantSettings.LoadAsync();
 variantSettings.Current.Language=args[3]; variantSettings.Current.ThemeMode=Enum.Parse<AppThemeMode>(args[4]); await variantSettings.SaveAsync();
 await new AppearanceProfileStore(Path.Combine(destination,"appearance.json")).SaveAsync(new()
 { InterfaceMode=AppInterfaceMode.ModernDashboard, Global=new(){Theme=(AppearanceTheme)(int)variantSettings.Current.ThemeMode},Typography=new(){AppTextScale=int.Parse(args[5])} });
 if(args.Length>6 && args[6]=="groups")
 {
  var persistence = new QueuePersistenceService(variantPaths,new NullAppLogger());
  var jobs=await persistence.LoadAsync();
  for(var index=0;index<jobs.Count;index++) { jobs[index].PlaylistId="performance-group";jobs[index].PlaylistTitle="SYNTHETIC playlist";jobs[index].PlaylistIndex=index+1; }
  await persistence.SaveAsync(jobs);
 }
 if(args.Length>6 && args[6]=="durability")
 {
  await Write(variantPaths.LiveSessionsFile,new[]{new LiveSession{SourceUrl="https://fixture.invalid/live",Title="SYNTHETIC completed LIVE",State=LiveSessionState.Completed,RecordedDuration=TimeSpan.FromMinutes(5),RecordingEndedAt=DateTimeOffset.UtcNow}});
  var session=new BatchImportSession{FileName="SYNTHETIC.txt",Paused=true};
  await new BatchSessionStore(Path.Combine(destination,"Batch")).CreateAsync(session,BatchRows(),default);
 }
 Console.WriteLine(JsonSerializer.Serialize(new{root=destination,language=args[3],theme=args[4],scale=int.Parse(args[5])}));
 return;
}

var root=Path.GetFullPath(args[0]);
if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) ||
    !Path.GetFileName(root).StartsWith("mtd-automation-smoke-",StringComparison.Ordinal)) throw new InvalidOperationException("An isolated QA TEMP profile is required.");
if (Directory.Exists(root)) throw new IOException("Refusing to overwrite an existing fixture.");
var count=int.Parse(args[1]); var queueCount=int.Parse(args[2]); var imageCount=int.Parse(args[3]); var fake=Path.GetFullPath(args[4]);
var mixed = args.Length > 5 && args[5] == "--scalability";
var paths=AppPaths.Create(root); paths.EnsureCreated();
var tools=Path.Combine(root,"fake-tools");Directory.CreateDirectory(tools);
foreach(var file in Directory.GetFiles(fake,"ModernTubeDownloader.FakeTool.*")) File.Copy(file,Path.Combine(tools,Path.GetFileName(file)));
foreach(var name in new[]{"yt-dlp.exe","ffmpeg.exe","ffprobe.exe","deno.exe"}) File.Copy(Path.Combine(fake,"ModernTubeDownloader.FakeTool.exe"),Path.Combine(tools,name));
var settings=new AppSettings { Language="pl",ThemeMode=AppThemeMode.Dark,QueuePaused=true,EnableWebRemote=false,BrowserIntegrationEnabled=false,
    UseCustomYtDlp=true,CustomYtDlpPath=Path.Combine(tools,"yt-dlp.exe"),UseCustomFfmpeg=true,CustomFfmpegPath=Path.Combine(tools,"ffmpeg.exe"),CustomFfprobePath=Path.Combine(tools,"ffprobe.exe"),UseCustomDeno=true,CustomDenoPath=Path.Combine(tools,"deno.exe"),
    TemporaryDirectory=Path.Combine(root,"temp"),FinalOutputDirectory=Path.Combine(root,"output"),
    Updates=new(){AutomaticChecks=false,PolicyVersion=1},Notifications=new(){Enabled=false},Startup=new(){CloseBehavior=TrayCloseBehavior.Exit,ShowTrayIcon=false} };
Directory.CreateDirectory(settings.FinalOutputDirectory);Directory.CreateDirectory(settings.TemporaryDirectory);
var inaccessible = Path.Combine(root, "file-in-place-of-directory");
if (mixed) File.WriteAllText(inaccessible, "Synthetic inaccessible parent");
var resolutions = new[] { 320, 640, 1280, 1920 };
var queue=new List<DownloadQueueItem>();var history=new List<DownloadHistoryEntry>();
var stamp=new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero);
for(int i=0;i<Math.Max(count,queueCount);i++)
{
 var bytes=new byte[16];BitConverter.GetBytes(i+1).CopyTo(bytes,0);var id=new Guid(bytes);
 var title=$"SYNTHETIC QA {i:D5} — Zażółć gęślą jaźń · 日本語 🌄 long title "+new string('x',i%5==0?100:5);
 var source = mixed && i % 23 == 0 ? 7 : i;
 var url=$"https://fixture.invalid/{source:D5}";var imageUrl=i<imageCount?$"https://fixture.invalid/{i:D5}/thumbnail":null;
 var directory = mixed && i % 17 == 0 ? inaccessible : settings.FinalOutputDirectory;
 var path=Path.Combine(directory,"missing-file-"+i+new string('x',i%3==0?140:1)+".mp4");
 if(i<queueCount) queue.Add(new(){Id=id,JobSessionId=id,SourceUrl=url,Title=title,VideoId="fixture"+i,Channel="Synthetic QA",ThumbnailUrl=imageUrl,AddedAt=stamp.AddMinutes(-i),Status=i<Math.Min(2526,queueCount)?DownloadStatus.Completed:DownloadStatus.Queued,ProgressPercent=i<2526?100:0,FinalFile=path,PlaylistId=i%4==0?"qa-playlist":null,PlaylistTitle=i%4==0?"Synthetic playlist 日本語":null,PlaylistIndex=i%4==0?i+1:null});
 if(i<count) history.Add(new(id,title,url,"fixture"+i,"Synthetic QA","1080p",path,stamp.AddMinutes(-i),"Completed",imageUrl,"1080",i%4==0?"qa-playlist":null,i%4==0?"Synthetic playlist 日本語":null,i%4==0?i+1:null,WasLiveRecording:i%7==0,WasPartial:i%21==0));
 if(imageUrl is not null)
 {
  var file = Path.Combine(paths.ThumbnailCacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl))) + ".img");
  if (mixed && i % 19 == 0) { File.WriteAllText(file, "Synthetic invalid image"); continue; }
  var width = mixed ? resolutions[i % resolutions.Length] : 640;
  using var bitmap=new SKBitmap(width,width * 9 / 16);using var canvas=new SKCanvas(bitmap);
  canvas.Clear(new SKColor((byte)(i%256),(byte)((i/256)%256),80));
  using var paint=new SKPaint{Color=SKColors.White};canvas.DrawRect(new SKRect(10+i%200,30,100+i%200,130),paint);
  using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,90);
  File.WriteAllBytes(file,data.ToArray());
 }
}
// Use the application's exact atomic serializer; reflection keeps QA tooling outside product API.
async Task Write<T>(string path,T value)
{
 var atomic=typeof(AppPaths).Assembly.GetType("ModernTubeDownloader.Infrastructure.AtomicJsonFile")!;
 var method=atomic.GetMethod("WriteAsync",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.MakeGenericMethod(typeof(T));
 await (Task)method.Invoke(null,[path,value,CancellationToken.None])!;
}
await Write(paths.SettingsFile,settings);await Write(paths.HistoryFile,history);await new QueuePersistenceService(paths,new NullAppLogger()).SaveAsync(queue);
await Write(paths.LiveSessionsFile,Array.Empty<LiveSession>());
if (mixed)
{
    var store = new BatchSessionStore(Path.Combine(root, "Batch"));
    for (var batch = 0; batch < 6; batch++)
        await store.CreateAsync(new BatchImportSession { FileName = $"SYNTHETIC-{batch}.txt", Paused = true }, BatchRows(), default);
}
await Write(paths.ToolStateFile,new ToolManagerState{LastToolUpdateCheck=DateTimeOffset.UtcNow});
// Performance fixtures represent an existing user who has finished onboarding.
// This is fixture data, not a production bypass; journey tests use fresh markers.
var version = typeof(AppPaths).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
await Write(Path.Combine(root,"experience.json"),new {schemaVersion=1,existingProfile=true,setup=2,interfaceChoiceFamily="1.3",lastObservedVersion=version,lastWhatsNewAcknowledgedVersion=version});
Console.WriteLine(JsonSerializer.Serialize(new{root,history=count,queue=queueCount,uniqueImages=imageCount,mixed,minimumImageWidth=mixed ? resolutions[0] : 640,maximumImageWidth=mixed ? resolutions[^1] : 640,hashHistory=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(paths.HistoryFile))),hashQueue=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(paths.QueueFile)))}));

static async IAsyncEnumerable<BatchImportEntry> BatchRows()
{
 await Task.CompletedTask;
 for(var i=0;i<3;i++)yield return new(){Ordinal=i+1,Url="https://fixture.invalid/00000",Title="SYNTHETIC Batch "+i,Status=i==0?BatchEntryStatus.Completed:BatchEntryStatus.Duplicate,QueueItemId=new Guid("00000001-0000-0000-0000-000000000000")};
}
