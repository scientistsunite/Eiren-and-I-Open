using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// Process.MainWindowHandle can be zero for service-runner desktops. Enumerate
// only the launched process's own threads, then verify PID, title and class.
internal static class FixtureWindow
{
    internal sealed record Candidate(long Handle,int Thread,uint Pid,string Title,string Class);
    public static (IntPtr Handle,Candidate[] Candidates) Find(Process ui)
    {
        var found=new List<Candidate>();var clock=Stopwatch.StartNew();ui.Refresh();
        foreach(var thread in ui.Threads.Cast<ProcessThread>().Take(64))using(thread){
            if(clock.Elapsed>TimeSpan.FromSeconds(1)||found.Count>=32)break;
            EnumThreadWindows((uint)thread.Id,(window,_)=>{
                if(found.Count>=32||clock.Elapsed>TimeSpan.FromSeconds(1))return false;
                GetWindowThreadProcessId(window,out uint pid);if(pid!=ui.Id)return true;
                var title=new StringBuilder(160);var type=new StringBuilder(128);
                GetWindowText(window,title,title.Capacity);GetClassName(window,type,type.Capacity);
                found.Add(new(window.ToInt64(),thread.Id,pid,title.ToString(),type.ToString()));return true;
            },IntPtr.Zero);
        }
        var matches=found.Where(c=>c.Pid==ui.Id&&c.Title=="Cylon Detector v0.4"&&c.Class.StartsWith("WindowsForms10.Window.",StringComparison.Ordinal)).ToArray();
        return (matches.Length==1?new IntPtr(matches[0].Handle):IntPtr.Zero,found.ToArray());
    }
    delegate bool WindowCallback(IntPtr window,IntPtr state);
    [DllImport("user32.dll")]static extern bool EnumThreadWindows(uint thread,WindowCallback callback,IntPtr state);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder text,int count);
}
