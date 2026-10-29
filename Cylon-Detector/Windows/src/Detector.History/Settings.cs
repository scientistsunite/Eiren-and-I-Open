using System.Text.Json;
namespace Detector.History;
public enum StartupMode { Off, SignIn, Boot }
public sealed record Preferences
{
    public int Schema { get; init; } = 1;
    public StartupMode Startup { get; init; }
    public bool StartInTray { get; init; }
    public bool OpenMainAtLaunch { get; init; } = true;
    public bool ContinueInTray { get; init; } = true;
    public bool AutoMonitor { get; init; }
    public bool ContinuousNetwork { get; init; }
    public int ReportIntervalMinutes { get; init; } = 60;
    public bool DetailedDiagnostics {get;init;}
    public int AttributionRecordedDays {get;init;}=15;
    public bool Indefinite { get; init; }
    public bool HourlyReports { get; init; } = true;
    public string? LogDirectory { get; init; }
    public string? ReportDirectory { get; init; }
    public int Width { get; init; } = 1080;
    public int Height { get; init; } = 680;
}
public sealed class SettingsFile(string directory)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string? Warning { get; private set; }
    public bool Writable { get; private set; } = true;
    bool recovered;
    public Preferences Load()
    {
        foreach(string suffix in new[]{"", ".bak"}) {
            string path=Path.Combine(DirectoryPath,"options.json"+suffix);
            if(!File.Exists(path)) continue;
            try {
                var value=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty settings");
                if(value.Schema!=1){Writable=false;Warning="Unsupported settings version — original configuration preserved read-only";return new();}
                if(value.ReportIntervalMinutes is <1 or >1440||value.AttributionRecordedDays is <0 or >365)throw new JsonException("Report interval or diagnostic retention is outside its supported range");
                if(suffix!=""){Warning="Recovered preferences from local backup";recovered=true;}
                return value;
            } catch(Exception ex) when(ex is IOException or JsonException) { Warning="Settings unavailable: "+ex.Message; }
        }
        return new();
    }
    public void Save(Preferences value)
    {
        if(!Writable)throw new InvalidOperationException("Cannot overwrite settings from an unsupported format");
        if(value.ReportIntervalMinutes is <1 or >1440||value.AttributionRecordedDays is <0 or >365)throw new ArgumentOutOfRangeException(nameof(value),"Report interval must be 1–1440 minutes and diagnostic retention 0–365 recorded days");
        Directory.CreateDirectory(DirectoryPath);
        string path=Path.Combine(DirectoryPath,"options.json"), temp=path+".tmp";
        using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) {
            JsonSerializer.Serialize(stream,value); stream.Flush(true);
        }
        if(File.Exists(path)&&!recovered) File.Copy(path,path+".bak",true);
        File.Move(temp,path,true);recovered=false;
    }
}
public enum HistoryState { Complete=0, Partial=1, Unavailable=2, Paused=3, Stopped=4, Interrupted=5, Recovery=6, WriterGap=7, ClockChange=8 }
public sealed record TimelineRow(long UtcTicks,int Day,int OffsetMinutes,string Zone,long Epoch,long ProcessInstance,
    string Application,string? Path,int? Pid,long? CreationTicks,long? Incoming,long? Outgoing,HistoryState State,double CoverageSeconds);
public sealed record HourRow(long Id,long UtcTicks,int Day,int OffsetMinutes,string Zone,long Incoming,long Outgoing,
    long PeakIncoming,long PeakOutgoing,double CoverageSeconds,long Incomplete,bool Finalized,bool Ambiguous,long Revision=0);
public sealed record ApplicationHour(long HourId,string Application,string? Path,long Incoming,long Outgoing,long PeakIncoming,long PeakOutgoing);
public sealed record HistoryEvent(long UtcTicks,long Epoch,HistoryState State,string Reason);

public sealed record IntegrityEvidence(long UtcTicks,long Epoch,Detector.Core.IntegrityFlags Flags,long OsLost,long Late,long Malformed,long Capacity,long DiscardedIncoming,long DiscardedOutgoing,long AffectedFromTicks,long AffectedToTicks,long UncertainThroughTicks,bool EarlierImpactUnknown,long Unsupported=0);

public sealed record RecordingProfile(string ApplicationVersion,string SourceRevision,string SourceKind,string IntegrityModel,int LatenessSeconds,bool Indefinite,bool HourlyReports,bool DetailedDiagnostics=false,int AttributionRecordedDays=15);
public sealed record RecordingSession(long Epoch,string Token,long RequestedTicks,long? FirstCoverageTicks,long? EndedTicks,string? ApplicationVersion,string? SourceRevision,string? SourceKind,string? IntegrityModel,string? Configuration,string? SourceSession=null);

public sealed record AttributionEvidence(long MinuteTicks,long Epoch,Detector.Core.AttributionReason Reason,Detector.Core.TrafficProtocol Protocol,int? RawPid,bool IdentifiedProcess,long Events,long Incoming,long Outgoing,long FirstEventTicks,long LastEventTicks,long FirstReceiptTicks,long LastReceiptTicks);
