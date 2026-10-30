using Detector.App;
using Detector.History;
using Detector.Service;

internal static class Program
{
    static int assertions;
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);assertions++;Console.WriteLine("PASS "+text);}
    [STAThread] static int Main(string[] args)
    {
        using var watchdog=new System.Threading.Timer(_=>{Console.Error.WriteLine("Native UI suite exceeded its three-minute deadline");Environment.Exit(1);},null,TimeSpan.FromMinutes(3),Timeout.InfiniteTimeSpan);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        Exception? failure=null;bool finished=false;using var host=new Form{ShowInTaskbar=false,Opacity=0};host.FormClosing+=(_,e)=>{if(!finished)e.Cancel=true;};
        host.Shown+=async(_,_)=>{try{if(args.Contains("--window-discovery-only"))await WindowDiscovery();else if(args.Contains("--live-only"))await RunLive();else await Run();}catch(Exception ex){failure=ex;}finally{finished=true;host.Close();}};
        Application.Run(host);if(failure!=null){Console.Error.WriteLine(failure);return 1;}Console.WriteLine($"PASS {assertions} native UI assertions; Windows-native layout ({Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT")??"local"}) is not physical multi-monitor/DPI acceptance");return 0;
    }
    static async Task WindowDiscovery()
    {
        FixtureEnvironment.Require();
        using var process=System.Diagnostics.Process.GetCurrentProcess();
        using var target=new Form{Text="Cylon Detector v0.4",ShowInTaskbar=false,Opacity=0};target.Show();await Task.Delay(30);
        var one=FixtureWindow.Find(process);
        Check(one.Handle==target.Handle,"thread-window discovery identifies exact owned form without taskbar/main-window dependency");
        Check(one.Candidates.Any(c=>c.Handle==target.Handle.ToInt64()&&c.Pid==process.Id),"native window evidence retains verified PID and thread identity");
        using(var duplicate=new Form{Text=target.Text,ShowInTaskbar=false,Opacity=0}){
            duplicate.Show();await Task.Delay(30);Check(FixtureWindow.Find(process).Handle==IntPtr.Zero,"ambiguous same-title owned windows are rejected");
        }
        target.Text="Unrelated fixture window";Check(FixtureWindow.Find(process).Handle==IntPtr.Zero,"unrelated titles cannot select a thread for suspension");
        Console.WriteLine("PASS nondestructive session-zero window-discovery controls; no collector, history, service or application data opened");
    }
    static T Find<T>(Control root,string name) where T:Control=>root.Controls.Find(name,true).OfType<T>().Single();
    static async Task Wait(Func<bool> ready){var clock=System.Diagnostics.Stopwatch.StartNew();while(!ready()){if(clock.Elapsed>TimeSpan.FromSeconds(15))throw new TimeoutException("UI condition");await Task.Delay(10);}}
    static OptionsWindow Options(Preferences preferences,Func<Preferences,Task> save,Action<Exception>? error=null)=>new(preferences,save,()=>"Test: unavailable coverage, not zero",()=>Task.CompletedTask,()=>Task.CompletedTask,()=>Task.CompletedTask,()=>{},()=>Task.CompletedTask,()=>Task.CompletedTask,()=>Task.CompletedTask,error);
    static async Task Invoke(MainWindow window,string method,params object[] args)
    {
        var task=(Task)typeof(MainWindow).GetMethod(method,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(window,args)!;
        await task.WaitAsync(TimeSpan.FromSeconds(40));
    }
    static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
    static async Task RunLive()
    {
        FixtureEnvironment.Require();
        string profile=PlatformPaths.DataRoot;if(Directory.Exists(profile)||NativeService.ProcessId()!=null)throw new IOException("Existing fixture profile or boot service; refusing interference");
        using var window=new MainWindow();window.Show();
        try{
            // This must complete after the handshake rather than await the lifetime of capture.
            await Invoke(window,"StartMonitoring",true);int helper=Field<System.Diagnostics.Process>(window,"privateHelper").Id;
            var navigation=Field<ListBox>(window,"navigation");var root=window.Controls.OfType<TableLayoutPanel>().Single();
            foreach(float scale in new[]{1f,1.25f,1.5f,2f}){
                window.Font=new Font("Segoe UI",10*scale);window.ClientSize=new Size(1280,800);window.PerformLayout();await Task.Delay(30);
                Check(navigation.Items.Cast<object>().All(item=>TextRenderer.MeasureText(item.ToString(),navigation.Font).Width+24<=navigation.ClientSize.Width),"all main-shell labels fit at simulated font scale "+scale);
                for(int page=0;page<navigation.Items.Count;page++){navigation.SelectedIndex=page;window.PerformLayout();await Task.Delay(10);Check(root.GetControlFromPosition(1,1)!.Visible,"main navigation page "+page+" remains reachable at simulated font scale "+scale);}
            }
            window.Font=new Font("Segoe UI",10);navigation.SelectedIndex=0;
            string oldFile=Path.Combine(profile,"history.db");
            bool Recorded(string file){try{return new HistoryReader(file).Days().Length>0;}catch(Exception ex)when(ex is IOException or Microsoft.Data.Sqlite.SqliteException){return false;}}
            await Wait(()=>Recorded(oldFile));Check(!Field<System.Diagnostics.Process>(window,"privateHelper").HasExited,"Start request returns while genuine capture continues");
            var preferences=Field<Preferences>(window,"preferences");
            try{await Invoke(window,"ApplyOptions",preferences with{Startup=StartupMode.Boot,LogDirectory=Path.Combine(profile,"unsafe-boot"),ReportDirectory=Path.Combine(profile,"unsafe-report")});throw new Exception("Invalid boot destination accepted");}catch(IOException){}
            Check(Field<System.Diagnostics.Process>(window,"privateHelper").Id==helper&&!Field<System.Diagnostics.Process>(window,"privateHelper").HasExited,"invalid boot folder rejected before disconnecting desktop capture or requesting UAC");
            using(var options=Options(preferences,p=>Invoke(window,"ApplyOptions",p))){options.Show();Find<TextBox>(options,"ReportDirectory").Text=Path.Combine(profile,"OtherReports");await Wait(()=>Find<Button>(options,"Apply").Enabled);Find<Button>(options,"Apply").PerformClick();options.Close();await options.ActiveOperation;Check(options.IsDisposed,"real report-destination Apply completes before deferred Options closure");}
            Check(Field<System.Diagnostics.Process>(window,"privateHelper").Id==helper,"report-only destination change preserves exact collector process");
            await Task.Delay(6000);
            var reader=new HistoryReader(oldFile);var original=reader.Days().SelectMany(d=>reader.Hours(d)).ToArray();Check(original.Sum(h=>h.CoverageSeconds)>=4,"live history keeps genuine coverage through report destination change");
            preferences=Field<Preferences>(window,"preferences");string moved=Path.Combine(profile,"RelocatedLogs");await Invoke(window,"ApplyOptions",preferences with{LogDirectory=moved});
            byte[] retained=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(oldFile));string newFile=Path.Combine(moved,"history.db");await Wait(()=>Recorded(newFile));await Task.Delay(3000);
            Check(File.Exists(oldFile)&&retained.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(oldFile))),"relocation preserves earlier detailed history without further writes");
            Check(new HistoryReader(newFile).Days().Length>0&&!Field<System.Diagnostics.Process>(window,"privateHelper").HasExited,"relocation reconnects genuine capture into the chosen store");
            Check(new HistoryReader(oldFile).Events(int.Parse(DateTime.Now.ToString("yyyyMMdd"))).Any(e=>e.State==HistoryState.Paused),"relocation interruption remains explicit in the previous history");
            var stopping=Field<System.Diagnostics.Process>(window,"privateHelper");var stopWatch=System.Diagnostics.Stopwatch.StartNew();
            await Invoke(window,"PauseMonitoring");
            Check(Field<System.IO.Pipes.PipeStream?>(window,"stopAcknowledged")!=null&&stopWatch.Elapsed<TimeSpan.FromSeconds(15),"desktop Pause receives bounded successful queue-drain acknowledgement before closing its pipe");
            await Wait(()=>stopping.HasExited);
            Check(stopping.ExitCode==0,"acknowledged collector shutdown exits cleanly without an unhandled-disposal crash");
            Check(new HistoryReader(newFile).Events(int.Parse(DateTime.Now.ToString("yyyyMMdd"))).Any(e=>e.State==HistoryState.Paused&&e.Reason.Contains("known pending ETW bytes retained")),"history explicitly records orderly Pause while tail coverage remains unavailable");
            await Invoke(window,"StartMonitoring",true);Check(!Field<System.Diagnostics.Process>(window,"privateHelper").HasExited,"fresh monitoring restarts after acknowledged helper shutdown");

        }finally{await Invoke(window,"ExitApp");if(Directory.Exists(profile))Directory.Delete(profile,true);}
    }
    static async Task Run()
    {
        await WindowDiscovery();
        string root=Path.Combine(Path.GetTempPath(),"Detector-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        // The copied writer/save concurrency fixture intentionally persists all network seconds.
        // Production default conditional persistence is checked by Detector privacy tests.
        var initial=new Preferences{LogDirectory=Path.Combine(root,"logs"),ReportDirectory=Path.Combine(root,"reports"),ContinuousNetwork=true};
        try{
            using(var self=System.Diagnostics.Process.GetCurrentProcess()){
                long created=self.StartTime.ToUniversalTime().Ticks;
                Check(ServiceHosts.ForProcess(self.Id,created).Length==0,"local read-only SCM inspection never invents a hosted service for the test viewer process");
                bool rejected=false;try{ServiceHosts.ForProcess(self.Id,created-1);}catch(IOException){rejected=true;}
                Check(rejected,"SCM inspection rejects a mismatched process creation identity");
            }
            string diagnosticFile=Path.Combine(root,"utc-diagnostics.db");var utcStart=new DateTime(2026,10,26,21,0,0,DateTimeKind.Utc);
            using(var store=new HistoryStore(diagnosticFile,TimeZoneInfo.CreateCustomTimeZone("FixtureUTC3",TimeSpan.FromHours(3),"FixtureUTC3","FixtureUTC3"))){store.Append(new Detector.Core.Measurement(1,utcStart,1,true,0,0,[],"synthetic UTC+3 idle fixture"));store.Commit();}
            using(var history=new HistoryWindow(diagnosticFile)){
                history.Show();Find<DateTimePicker>(history,"RecordedDate").Value=new DateTime(2026,10,27);
                var from=Find<DateTimePicker>(history,"DiagnosticFromUtc");var to=Find<DateTimePicker>(history,"DiagnosticToUtc");
                await Wait(()=>from.Value==utcStart&&to.Value==utcStart.AddDays(1).AddMinutes(-1));
                Check(from.CustomFormat!.Contains("yyyy-MM-dd")&&to.Value.Date!=from.Value.Date,"native History filter includes explicit UTC dates spanning the selected UTC+3 recorded day");history.Close();
            }
            using(var firstHistory=new HistoryWindow(diagnosticFile))using(var secondHistory=new HistoryWindow(diagnosticFile)){
                firstHistory.Show();secondHistory.Show();var complete=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int exports=0;
                Func<Task> pending=()=>{exports++;return complete.Task;};firstHistory.RequestExport(pending);firstHistory.RequestExport(pending);secondHistory.RequestExport(pending);
                Check(exports==1&&!firstHistory.ActiveExport.IsCompleted,"manual CSV work is bounded to one export across History windows without queued duplicates");
                firstHistory.Close();Check(!firstHistory.IsDisposed,"History closure is deferred while its CSV export is pending");complete.SetResult();await firstHistory.ActiveExport;
                secondHistory.RequestExport(()=>{exports++;return Task.CompletedTask;});Check(exports==2,"manual export slot is released after completion");HistoryWindow.ApplicationClosing(true);secondHistory.RequestExport(()=>{exports++;return Task.CompletedTask;});Check(exports==2,"application shutdown refuses new manual exports while existing work drains");HistoryWindow.ApplicationClosing(false);firstHistory.Close();secondHistory.Close();
            }
            var rules=DestinationRules.Check(StartupMode.Off,initial.LogDirectory,initial.ReportDirectory);Check(rules.Valid,"desktop accepts separate absolute local destinations without creating them");Check(!Directory.Exists(initial.LogDirectory),"validation has no provisioning side effects");
            Check(!DestinationRules.Check(StartupMode.Off,initial.LogDirectory,initial.LogDirectory+Path.DirectorySeparatorChar).Valid,"equivalent destinations rejected including trailing slash");
            Check(!DestinationRules.Check(StartupMode.Off,"relative",initial.ReportDirectory).Valid,"relative folder rejected");
            Check(!DestinationRules.Check(StartupMode.Off,@"\\server\share",initial.ReportDirectory).Valid,"UNC folder rejected");
            Check(!DestinationRules.Check(StartupMode.Boot,initial.LogDirectory,initial.ReportDirectory).Valid,"desktop folders cannot become SYSTEM destinations");
            Check(DestinationRules.Check(StartupMode.Boot,BootConfiguration.DefaultLogs,BootConfiguration.DefaultReports).Valid,"protected boot defaults accepted");
            Check(!DestinationRules.Check(StartupMode.Boot,BootConfiguration.Root+"-other/Logs",BootConfiguration.DefaultReports).Valid,"protected root prefix collision rejected");
            for(int i=0;i<10;i++){using var window=Options(initial,_=>Task.CompletedTask);window.Show();await Wait(()=>Find<Button>(window,"Apply").Enabled==false);window.Close();}
            Check(true,"ten open/close cycles with late validation do not throw");
            foreach(bool fail in new[]{false,true}){
                var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int saves=0;Exception? diagnostic=null;
                using var window=Options(initial,async _=>{saves++;await completion.Task;if(fail)throw new IOException("deliberate delayed configuration failure");},e=>diagnostic=e);window.Show();
                var text=Find<TextBox>(window,"LogDirectory");text.Text=Path.Combine(root,"changed");var button=Find<Button>(window,"Apply");await Wait(()=>button.Enabled);button.PerformClick();Task operation=window.ActiveOperation;button.PerformClick();await Wait(()=>saves==1);window.Close();
                Check(!window.IsDisposed&&window.Saving&&saves==1,"Apply/Close is deferred and duplicate Apply is prevented");
                completion.SetResult();await operation;
                Check(fail?!window.IsDisposed&&diagnostic?.Message.Contains("deliberate")==true:window.IsDisposed&&diagnostic==null,fail?"configuration failure remains visible and independently diagnosable":"delayed successful save completes before deferred Close");
                if(fail)window.Close();
            }
            {
                var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);Exception? diagnostic=null;using var window=Options(initial,async _=>{await completion.Task;throw new IOException("failure after forced disposal");},e=>diagnostic=e);window.Show();Find<TextBox>(window,"LogDirectory").Text=Path.Combine(root,"forced");await Wait(()=>Find<Button>(window,"Apply").Enabled);Find<Button>(window,"Apply").PerformClick();Task task=window.ActiveOperation;window.Dispose();completion.SetResult();await task;Check(diagnostic?.Message=="failure after forced disposal","forced disposal never targets dead owner and preserves genuine failure");
            }
            {
                // Wall-clock delay is deliberate: UI saving must not hold the history writer.
                string file=Path.Combine(root,"delayed-history.db");
                await using var writer=new HistoryWriter(file,initial);
                using var window=Options(initial,async _=>await Task.Delay(TimeSpan.FromSeconds(35)));
                window.Show();Find<TextBox>(window,"LogDirectory").Text=Path.Combine(root,"slow-save");await Wait(()=>Find<Button>(window,"Apply").Enabled);Find<Button>(window,"Apply").PerformClick();
                Task operation=window.ActiveOperation;writer.Begin();DateTime started=DateTime.UtcNow;
                for(int i=0;i<36;i++){Check(writer.Append(new Detector.Core.Measurement(1,started.AddSeconds(i),1,true,0,0,[],"confirmed idle fixture")),"one-second idle frame accepted during delayed save "+i);await Task.Delay(1000);}
                await operation;Check(writer.Healthy,"history worker remains healthy during a 35-second Options save");
                await writer.DisposeAsync();
                var timeline=new HistoryReader(file).Timeline(started.Ticks,started.AddSeconds(36).Ticks).ToArray();
                Check(timeline.Length==36&&timeline.All(r=>r.State==HistoryState.Complete&&r.Incoming==0&&r.Outgoing==0),"all 36 exact confirmed-idle seconds persisted during slow save");
                window.Close();
            }
            using(var window=Options(initial,_=>Task.CompletedTask)){
                window.Show();var mode=Find<ComboBox>(window,"StartupMode");var text=Find<TextBox>(window,"LogDirectory");text.Text=Path.Combine(root,"desktop-draft");mode.SelectedIndex=2;Check(text.Text==BootConfiguration.DefaultLogs,"boot draft defaults are shown before Apply");text.Text=Path.Combine(BootConfiguration.Root,"CustomLogs");mode.SelectedIndex=1;Check(text.Text.EndsWith("desktop-draft"),"desktop folder draft preserved across mode changes");mode.SelectedIndex=2;Check(text.Text.EndsWith("CustomLogs"),"boot folder draft preserved across mode changes");
                foreach(float scale in new[]{1f,1.25f,1.5f,2f}){window.Font=new Font("Segoe UI",10*scale);window.ClientSize=new Size(1000,700);var nav=window.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<ListBox>().Single();nav.SelectedIndex=3;window.PerformLayout();await Task.Delay(20);Check(nav.Items.Cast<object>().All(item=>TextRenderer.MeasureText(item.ToString(),window.Font).Width+24<=nav.ClientSize.Width),"all navigation labels fit at font scale "+scale+" (sidebar "+nav.ClientSize.Width+"px, longest label "+nav.Items.Cast<object>().Max(item=>TextRenderer.MeasureText(item.ToString(),window.Font).Width)+"px)");Check(text.Width>=300&&text.Right<=text.Parent!.ClientSize.Width,"readable folder width at simulated font scale "+scale);window.ClientSize=new Size(1500,900);window.PerformLayout();Check(text.Width>=500,"storage expands with available width at font scale "+scale);}
                window.Close();
            }
        }finally{Directory.Delete(root,true);}
    }
}
