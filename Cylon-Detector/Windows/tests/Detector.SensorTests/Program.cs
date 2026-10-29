using Detector.Core;

static class Program
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks++;Console.WriteLine("PASS "+message);}
    static int Main()
    {
        DateTime now=new(2026,10,30,12,0,0,DateTimeKind.Utc);long started=now.AddMinutes(-2).ToFileTimeUtc(),stopped=now.AddSeconds(-5).ToFileTimeUtc();
        SensorDecodeResult Decode(object? first,object? last,bool desktop=true,string name=@"C:#Synthetic#meeting.exe",string kind="microphone")=>SensorEvidenceDecoder.Decode(kind,name,desktop,first,last,now,"Synthetic selected-user scope");
        var active=Decode(started,0L);
        Check(active.Use is{Kind:"microphone",Application:"meeting.exe",Path:@"C:\Synthetic\meeting.exe",StoppedUtcTicks:null},"Registry-encoded desktop identity preserves application path without inventing a PID");
        Check(active.Use!.StartedUtcTicks==now.AddMinutes(-2).Ticks,"FILETIME occurrence timestamp is converted independently of observation time");
        Check(active.Use.Evidence.Contains("stale")&&active.Use.Evidence.Contains("do not prove"),"Active marker is explicitly evidence of Windows metadata, not guaranteed hardware state");
        var ended=Decode(started,stopped,false,"Synthetic.Package_abc","camera");
        Check(ended.Use is{Kind:"camera",Application:"Synthetic.Package_abc",Path:null},"Packaged identity is not guessed into an executable or process");
        Check(ended.Use!.StoppedUtcTicks==now.AddSeconds(-5).Ticks,"Explicit stop occurrence is preserved");
        Check(Decode(0L,0L).Use==null&&!Decode(0L,0L).Malformed,"Permission-only zero timestamps never invent activity");
        Check(Decode(null,null).Malformed&&Decode(started,null).Malformed,"Missing stop metadata is unknown rather than active");
        Check(Decode("123",0L).Malformed&&Decode(started,"0").Malformed,"Text values do not masquerade as integer usage evidence");
        Check(Decode(-1L,0L).Malformed&&Decode(started,-1L).Malformed,"Negative usage times are rejected");
        Check(Decode(started,long.MaxValue).Malformed&&Decode(long.MaxValue,0L).Malformed,"Out-of-range FILETIME values are rejected");
        Check(Decode(started,now.AddMinutes(-3).ToFileTimeUtc()).Malformed,"Stop-before-start intervals remain unknown");
        Check(Decode(now.AddHours(1).ToFileTimeUtc(),0L).Malformed,"Future occurrence times remain unknown after clock anomalies");
        Check(Decode(started,now.AddHours(1).ToFileTimeUtc()).Malformed,"Future stop timestamps remain unknown");
        Check(Decode(now.AddHours(-1).ToFileTimeUtc(),now.AddMinutes(-11).ToFileTimeUtc()).Use==null,"Old ended records are omitted from the recent observation window");
        Check(Decode(now.AddDays(-3).ToFileTimeUtc(),0L).Use!=null,"An old active marker remains qualified evidence; age does not fabricate a stop");
        Check(Decode(started,stopped,true,"Synthetic#camera.exe","speaker").Malformed,"Unsupported device categories are not silently remapped");
        Check(Decode(started,0L,true,new string('x',1025)).Malformed,"Oversized registry identities do not enter bounded frames");
        var snapshot=new SensorSnapshot(now,[active.Use],false,"camera scope inaccessible; partial observation");
        var frame=new Measurement(1,now,1,true,0,0,[],"Synthetic"){Sensors=snapshot};
        var json=System.Text.Json.JsonSerializer.Serialize(frame);var roundtrip=System.Text.Json.JsonSerializer.Deserialize<Measurement>(json)!;
        Check(roundtrip.Sensors is{Available:false}&&roundtrip.Sensors.Uses.Length==1,"Partial source availability keeps readable records without claiming complete coverage");
        Check(roundtrip.Sensors!.ObservedUtc==now&&roundtrip.Sensors.Uses[0].StartedUtcTicks!=now.Ticks,"Transport preserves observation and occurrence times distinctly");
        Console.WriteLine($"PASS {checks} synthetic sensor metadata checks; no registry, camera, microphone or device was opened.");return 0;
    }
}
