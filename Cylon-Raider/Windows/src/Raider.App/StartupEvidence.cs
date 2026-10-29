using System.Diagnostics;
using System.Text.Json;

namespace Raider.App;

// Opt-in smoke-fixture evidence only. Normal desktop launches do not write traces.
// Redirected stderr is drained by the test host; no file, payload or endpoint log.
internal static class StartupEvidence
{
    static bool enabled;
    static readonly Stopwatch clock=new();
    static int remaining=64;
    public static void Enable(bool smoke)
    {
        enabled=smoke&&Console.IsErrorRedirected;
        if(enabled){clock.Start();Mark("managed entry");}
    }
    public static void Mark(string stage,string? state=null)
    {
        if(!enabled||Interlocked.Decrement(ref remaining)<0)return;
        try{Console.Error.WriteLine(JsonSerializer.Serialize(new{startupEvidence=1,pid=Environment.ProcessId,utc=DateTime.UtcNow,elapsedMs=clock.Elapsed.TotalMilliseconds,stage,state=state is {Length:>512}?state[..512]:state}));}
        catch(IOException){}catch(ObjectDisposedException){}
    }
}
