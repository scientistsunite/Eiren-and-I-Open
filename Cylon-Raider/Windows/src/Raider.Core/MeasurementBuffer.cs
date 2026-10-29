using System.Threading.Channels;
namespace Raider.Core;

// Transport-only: no filesystem, UI callback, SQLite or network dependency.
public sealed class MeasurementBuffer
{
    readonly Channel<Measurement> channel;
    readonly object gate=new();
    readonly long maximumBytes;
    long bytes,dropped,peak;
    public MeasurementBuffer(int capacity=128,long maximumBytes=64*1024*1024){this.maximumBytes=maximumBytes;channel=Channel.CreateBounded<Measurement>(new BoundedChannelOptions(capacity){SingleReader=true,FullMode=BoundedChannelFullMode.Wait});}
    public long Dropped=>Interlocked.Read(ref dropped);
    public long PeakBytes=>Interlocked.Read(ref peak);
    static long Size(Measurement m)=>256+192L*m.Attribution.Length+256L*(m.Diagnostics==null?0:1)+m.Rows.Sum(r=>192L+2L*((r.Process?.Name.Length??0)+(r.Process?.Path?.Length??0)));
    public bool TryWrite(Measurement m){long n=Size(m);lock(gate){if(n<=maximumBytes-bytes&&channel.Writer.TryWrite(m)){bytes+=n;peak=Math.Max(peak,bytes);return true;}}Interlocked.Increment(ref dropped);return false;}
    public async IAsyncEnumerable<Measurement> ReadAll([System.Runtime.CompilerServices.EnumeratorCancellation]CancellationToken token=default){await foreach(var m in channel.Reader.ReadAllAsync(token)){lock(gate)bytes-=Size(m);yield return m;}}
    public void Complete()=>channel.Writer.TryComplete();
}
