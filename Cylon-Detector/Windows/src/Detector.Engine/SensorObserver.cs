using System.Security;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Security.Principal;
using Microsoft.Win32;
using Detector.Core;
namespace Detector.Engine;

// Read-only usage metadata. Never opens a camera/microphone, acquires samples,
// enumerates unrelated users, loads a hive, or contacts another machine.
public sealed class SensorObserver : IDisposable
{
    const int MaximumKeysPerCategory=512,MaximumRecords=256;
    const string Store=@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    const string Scope="Selected Windows user; loaded capability-use metadata only";
    const string Limitation="Observable Windows application metadata only; unsupported apps, drivers, unloaded user hives and stale active markers can be unobservable. Absence is not proof of inactivity.";
    readonly string? ownerSid;
    readonly string? scopeError;
    readonly object lifecycle=new();
    SensorSnapshot latest=new(DateTime.UtcNow,[],false,"Sensor observations are starting; "+Limitation);
    CancellationTokenSource? cancellation;
    Task? worker;
    public SensorObserver(string? ownerSid=null)
    {
        try{
            string? sid=ownerSid;
            if(sid==null){using var identity=WindowsIdentity.GetCurrent();sid=identity.User?.Value;}
            if(sid==null)throw new SecurityException("The selected user identity is unavailable");
            var identitySid=new SecurityIdentifier(sid);
            if(identitySid.Value is "S-1-5-18" or "S-1-5-19" or "S-1-5-20")throw new SecurityException("A service account is not the selected user's capability scope; configure an owner SID");
            this.ownerSid=identitySid.Value;
        }catch(Exception ex)when(ex is ArgumentException or SecurityException or UnauthorizedAccessException or PlatformNotSupportedException or System.ComponentModel.Win32Exception){scopeError="Selected user identity unavailable: "+ex.GetType().Name;}
    }
    public bool IsStopped {get{lock(lifecycle)return worker?.IsCompleted!=false;}}
    public void Start()
    {
        lock(lifecycle){
            if(worker?.IsCompleted==false)throw new InvalidOperationException("Sensor metadata observer is already running or stopping");
            cancellation?.Dispose();cancellation=new();var token=cancellation.Token;
            worker=Task.Run(async()=>{
                using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
                try{do{Volatile.Write(ref latest,Read(DateTime.UtcNow));}while(await timer.WaitForNextTickAsync(token));}
                catch(OperationCanceledException)when(token.IsCancellationRequested){}
                catch(Exception ex){Volatile.Write(ref latest,new(DateTime.UtcNow,[],false,"Sensor metadata observer interrupted: "+ex.GetType().Name+"; "+Limitation));}
            });
        }
    }
    public SensorSnapshot Snapshot()
    {
        var snapshot=Volatile.Read(ref latest);DateTime now=DateTime.UtcNow;
        if((now-snapshot.ObservedUtc).Duration()>TimeSpan.FromSeconds(5))return snapshot with{Available=false,Status="Sensor metadata sample is stale; previous markers are historical, not a current state. "+Limitation};
        return snapshot;
    }
    SensorSnapshot Read(DateTime now)
    {
        if(ownerSid==null)return new(now,[],false,(scopeError??"Selected user scope unavailable")+"; "+Limitation);
        var uses=new List<SensorUse>();var problems=new List<string>();
        try{
            using var owner=Registry.Users.OpenSubKey(ownerSid,false);
            if(owner==null)return new(now,[],false,"Selected user's registry hive is not loaded; pre-sign-in metadata may be unavailable. "+Limitation);
            foreach(var entry in new[]{(Key:"microphone",Kind:"microphone"),(Key:"webcam",Kind:"camera")}){
                try{
                using var category=owner.OpenSubKey(Store+"\\"+entry.Key,false);
                if(category==null){problems.Add(entry.Kind+": capability-use source absent; state unknown");continue;}
                int read=0;
                void ReadEntry(RegistryKey parent,string name,bool nonPackaged)
                {
                    if(++read>MaximumKeysPerCategory){if(read==MaximumKeysPerCategory+1)problems.Add(entry.Kind+": metadata key limit reached; coverage incomplete");return;}
                    try{
                        using var key=parent.OpenSubKey(name,false);
                        if(key==null){problems.Add(entry.Kind+": application entry changed during observation");return;}
                        object? start=key.GetValue("LastUsedTimeStart",null,RegistryValueOptions.DoNotExpandEnvironmentNames),stop=key.GetValue("LastUsedTimeStop",null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                        // Permission-only keys commonly have neither timestamp.
                        if(start==null&&stop==null)return;
                        var decoded=SensorEvidenceDecoder.Decode(entry.Kind,name,nonPackaged,start,stop,now,Scope);
                        if(decoded.Malformed){problems.Add(entry.Kind+": malformed/unknown usage metadata");return;}
                        if(decoded.Use!=null)uses.Add(decoded.Use);
                    }catch(Exception ex)when(ex is UnauthorizedAccessException or SecurityException or IOException){problems.Add(entry.Kind+": application entry unreadable ("+ex.GetType().Name+")");}
                }
                try{
                    foreach(string name in Names(category)){
                        if(name.Equals("NonPackaged",StringComparison.OrdinalIgnoreCase)){
                            using var unpackaged=category.OpenSubKey(name,false);
                            if(unpackaged==null){problems.Add(entry.Kind+": desktop metadata scope unavailable");continue;}
                            foreach(string application in Names(unpackaged))ReadEntry(unpackaged,application,true);
                        }else ReadEntry(category,name,false);
                    }
                }catch(Exception ex)when(ex is UnauthorizedAccessException or SecurityException or IOException){problems.Add(entry.Kind+": category unreadable ("+ex.GetType().Name+")");}
                }catch(Exception ex)when(ex is UnauthorizedAccessException or SecurityException or IOException){problems.Add(entry.Kind+": source unreadable ("+ex.GetType().Name+")");}
            }
        }catch(Exception ex)when(ex is UnauthorizedAccessException or SecurityException or IOException){problems.Add("Selected-user capability source unavailable ("+ex.GetType().Name+")");}
        if(uses.Count>MaximumRecords){problems.Add("Usage record limit reached; coverage incomplete");uses=uses.OrderBy(use=>use.StoppedUtcTicks.HasValue).ThenByDescending(use=>use.StartedUtcTicks).Take(MaximumRecords).ToList();}
        string status=(problems.Count==0?"Read-only microphone/camera capability metadata available. ":string.Join("; ",problems.Distinct().Take(8))+". ")+Limitation;
        return new(now,uses.ToArray(),problems.Count==0,status);
    }
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode)]
    static extern int RegEnumKeyEx(SafeRegistryHandle key,uint index,StringBuilder name,ref uint length,nint reserved,nint className,nint classLength,nint lastWriteTime);
    static IEnumerable<string> Names(RegistryKey key)
    {
        // Enumerate only a bounded prefix instead of allocating every registry name.
        for(uint index=0;index<=MaximumKeysPerCategory;index++){
            var name=new StringBuilder(256);uint length=256;
            int result=RegEnumKeyEx(key.Handle,index,name,ref length,0,0,0,0);
            if(result==259)yield break;
            if(result!=0)throw new IOException("Bounded capability metadata enumeration failed ("+result+")");
            yield return name.ToString();
        }
    }
    public void Stop()
    {
        Task? pending;lock(lifecycle){cancellation?.Cancel();pending=worker;}
        try{pending?.Wait(TimeSpan.FromSeconds(5));}catch(AggregateException){}
        lock(lifecycle)if(worker?.IsCompleted!=false){worker=null;cancellation?.Dispose();cancellation=null;}
    }
    public void Dispose()=>Stop();
}
