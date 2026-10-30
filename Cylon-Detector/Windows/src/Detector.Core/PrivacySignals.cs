namespace Detector.Core;

// These are observations of Windows' capability-use metadata, not recordings and
// not a guarantee that every capture path/application/device can be observed.
public sealed record SensorUse(string Kind,string Application,string? Path,long StartedUtcTicks,long? StoppedUtcTicks,string Evidence,string Scope);
public sealed record SensorSnapshot(DateTime ObservedUtc,SensorUse[] Uses,bool Available,string Status);
public sealed record SensorDecodeResult(SensorUse? Use,bool Malformed,string? Issue);

public static class SensorEvidenceDecoder
{
    public static SensorDecodeResult Decode(string kind,string application,bool nonPackaged,object? started,object? stopped,DateTime observedUtc,string scope)
    {
        if(kind is not ("microphone" or "camera"))return new(null,true,"Unsupported sensor category");
        if(string.IsNullOrWhiteSpace(application)||application.Length>1024)return new(null,true,"Missing or oversized application identity");
        if(!TryNumber(started,out long first)||!TryNumber(stopped,out long last))return new(null,true,"Usage timestamps are absent or not integer metadata; state is unknown");
        if(first==0&&last==0)return new(null,false,null); // Permission-only entries do not establish usage.
        if(first<=0||last<0)return new(null,true,"Invalid usage timestamps; state is unknown");
        DateTime start,stop=default;
        try{start=DateTime.FromFileTimeUtc(first);if(last>0)stop=DateTime.FromFileTimeUtc(last);}
        catch(ArgumentOutOfRangeException){return new(null,true,"Usage timestamps exceed the Windows FILETIME range");}
        DateTime now=observedUtc.ToUniversalTime();
        if(start>now.AddMinutes(1)||(last>0&&(stop<start||stop>now.AddMinutes(1))))return new(null,true,"Usage timestamps are reversed or ahead of the observation clock; state is unknown");
        // Keep recent ended evidence for transitions missed between one-second polls.
        // Old active markers remain explicitly unverified instead of inventing a stop.
        if(last>0&&stop<now.AddMinutes(-10))return new(null,false,null);
        string? path=nonPackaged?application.Replace('#','\\'):null;
        string name=nonPackaged?path!.Split('\\').LastOrDefault()??application:application;
        if(string.IsNullOrWhiteSpace(name))name=application;
        string evidence=last==0?"Windows CapabilityAccessManager reported an active-use marker; markers can be stale and do not prove current hardware access":"Windows CapabilityAccessManager recorded a usage interval; this is metadata, not capture contents";
        if(nonPackaged)evidence+="; executable identity decoded from registry key, PID not established";
        else evidence+="; packaged-application registry identity, process PID not established";
        return new(new(kind,name,path,start.Ticks,last==0?null:stop.Ticks,evidence,scope),false,null);
    }
    static bool TryNumber(object? value,out long result)
    {
        switch(value){case long number:result=number;return true;case int number:result=number;return true;case uint number:result=number;return true;case ulong number when number<=long.MaxValue:result=(long)number;return true;default:result=0;return false;}
    }
}
