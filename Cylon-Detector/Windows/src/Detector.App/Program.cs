using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Detector.Core;
using Detector.History;
using Detector.Service;
namespace Detector.App;
internal static class Program
{
    [STAThread]static void Main()
    {
        bool smoke=Environment.GetCommandLineArgs().Contains("--offline-smoke");StartupEvidence.Enable(smoke);
        StartupEvidence.Mark("application configuration begin");ApplicationConfiguration.Initialize();StartupEvidence.Mark("application configuration complete");using var single=new Mutex(true,@"Local\Eiren.CylonDetector.UI.v01",out bool owner);
        if(!owner){StartupEvidence.Mark("UI session singleton already owned");var window=PlatformPaths.FindWindow(null,PlatformPaths.WindowTitle);if(window!=IntPtr.Zero)PlatformPaths.PostMessage(window,PlatformPaths.RestoreMessage,IntPtr.Zero,IntPtr.Zero);else MessageBox.Show("Cylon Detector is already running. Open it from its tray icon.");return;}
        using var account=new Mutex(true,@"Global\Eiren.CylonDetector.UI.v02."+WindowsIdentity.GetCurrent().User!.Value,out bool accountOwner);
        if(!accountOwner){StartupEvidence.Mark("UI account singleton already owned");MessageBox.Show("Cylon Detector is already running in another Windows session for this account; no second history writer was started.");return;}
        StartupEvidence.Mark("main window construction begin");
        var mainWindow=new MainWindow(smoke);StartupEvidence.Mark("main window construction complete");
        Application.Run(mainWindow);StartupEvidence.Mark("message loop exited");
    }
}
sealed partial class MainWindow : Form
{
    readonly Label totals=new(){AutoSize=true,Text="Download: unavailable    Upload: unavailable"};
    readonly Label status=new(){AutoSize=true,Text="Stopped — only the collector or boot configuration requests elevation."};
    readonly Label monitoringIndicator=new(){AutoSize=true,Dock=DockStyle.Top,Font=new("Segoe UI",13,FontStyle.Bold),Text="● MONITORING OFF",AccessibleName="Monitoring status",Margin=new(0,4,0,6)};
    readonly Label logging=new(){AutoSize=true,Text="Opening local history"};
    readonly TrafficTable table=new();
    readonly ComboBox sort=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=180};
    readonly Button start=new RoundedButton(){Text="Start monitoring",AutoSize=true},stopButton=new RoundedButton(){Text="Pause",AutoSize=true,Enabled=false};
    readonly NotifyIcon tray;readonly ToolStripMenuItem trayMonitoring;
    readonly System.Windows.Forms.Timer health=new(){Interval=1000};
    readonly SettingsFile settings=new(PlatformPaths.DataRoot);
    Preferences preferences;HistoryWriter? writer;HourlyReportWriter? reports;BootConfiguration? boot;
    Task? previewReader;volatile PipeStream? stoppingPreview,stopAcknowledged;
    Process? privateHelper;PipeStream? pipe;CancellationTokenSource? cancel;volatile Measurement? latest;long lastFrame;
    volatile ServicePreview? serviceHealth;
    volatile string servicePersistence="",serviceReports="",serviceCapture="";
    readonly SemaphoreSlim configurationGate=new(1,1),helperDrainGate=new(1,1);bool configurationBusy,starting;
    string? storageError;bool exiting,loggingWarningShown;HistoryWindow? historyWindow;OptionsWindow? optionsWindow;
    string HistoryFile=>boot!=null?Path.Combine(boot.Options.LogDirectory??BootConfiguration.DefaultLogs,"history.db"):writer?.FilePath??Path.Combine(PlatformPaths.DataRoot,"history.db");
    public MainWindow(bool smoke=false)
    {
        StartupEvidence.Mark("settings load begin");preferences=settings.Load();StartupEvidence.Mark("settings loaded; profile protection begin");try{PlatformPaths.Protect(PlatformPaths.DataRoot);}catch(Exception ex){storageError=ex.Message;}
        StartupEvidence.Mark("profile protection complete; boot resolution begin");ResolveBoot();StartupEvidence.Mark("boot resolution complete");if(boot==null&&NativeService.ProcessId()==null&&preferences.Startup!=StartupMode.Boot)OpenPersonalStorage();else if(boot==null&&preferences.Startup==StartupMode.Boot)storageError??="Boot configuration is missing or inaccessible; re-enable it in Options";
        Text=PlatformPaths.WindowTitle;Width=Math.Clamp(preferences.Width,Math.Min(1000,Screen.PrimaryScreen?.WorkingArea.Width??1920),Screen.PrimaryScreen?.WorkingArea.Width??1920);Height=Math.Clamp(preferences.Height,Math.Min(650,Screen.PrimaryScreen?.WorkingArea.Height??1080),Screen.PrimaryScreen?.WorkingArea.Height??1080);MinimumSize=new(Math.Min(1000,Screen.PrimaryScreen?.WorkingArea.Width??1920),Math.Min(650,Screen.PrimaryScreen?.WorkingArea.Height??1080));StartPosition=FormStartPosition.CenterScreen;Icon=PlatformPaths.OfficialIcon();
        BackColor=Color.FromArgb(22,25,31);ForeColor=Color.Gainsboro;table.BackColor=Color.FromArgb(29,33,41);table.ForeColor=ForeColor;table.Font=new("Segoe UI",10);
        AutoScaleMode=AutoScaleMode.Font;Font=new("Segoe UI",10);
        StartupEvidence.Mark("shell construction begin");BuildShell();StartupEvidence.Mark("shell construction complete");
        start.Click+=async(_,_)=>{if(starting||pipe!=null)await PauseMonitoring();else await StartMonitoring();};stopButton.Click+=async(_,_)=>await PauseMonitoring();sort.SelectedIndexChanged+=(_,_)=>Render();
        var menu=new ContextMenuStrip();menu.Items.Add("Open Cylon Detector",null,(_,_)=>Restore());trayMonitoring=new ToolStripMenuItem("Start Monitoring");trayMonitoring.Click+=async(_,_)=>{if(pipe==null)await StartMonitoring();else await PauseMonitoring();};menu.Items.Add(trayMonitoring);menu.Items.Add("View Logs",null,(_,_)=>ShowHistory());menu.Items.Add("Options",null,(_,_)=>ShowOptions());menu.Items.Add("Exit",null,async(_,_)=>await ExitApp());
        tray=new NotifyIcon{Icon=Icon,Text="Cylon Detector — stopped",Visible=true,ContextMenuStrip=menu};tray.MouseClick+=(_,e)=>{if(e.Button==MouseButtons.Left)Restore();};
        // A hidden top-level window is already absent from the taskbar. Changing
        // ShowInTaskbar recreates its HWND; WinForms reapplies maximized/minimized
        // placement during that recreation and can show the just-hidden window.
        FormClosing+=(_,e)=>{if(exiting)return;e.Cancel=true;if(preferences.ContinueInTray){Hide();}else BeginInvoke((Action)(()=>_=ExitApp()));};
        health.Tick+=(_,_)=>{
            UpdateMonitoringIndicator();
            logging.Text=(boot!=null?servicePersistence+" | "+serviceReports:writer?.Status+" | "+writer?.PrivacyStatus+" | "+reports?.Status)+(storageError==null?"":" | "+storageError)+(settings.Warning==null?"":" | "+settings.Warning);
            if((writer?.Healthy==false||servicePersistence.StartsWith("NOT LOGGING",StringComparison.Ordinal))&&!loggingWarningShown){loggingWarningShown=true;tray.Text="Cylon Detector — NOT LOGGING";string error=writer?.Status??servicePersistence;tray.ShowBalloonTip(5000,"Cylon Detector is not logging",error.Length>200?error[..200]:error,ToolTipIcon.Warning);}
            if(Visible&&WindowState!=FormWindowState.Minimized){Render();optionsWindow?.RefreshMonitoring();}
            if(boot!=null&&(serviceCapture.StartsWith("Capture unavailable",StringComparison.Ordinal)||serviceCapture.StartsWith("Collector interruption",StringComparison.Ordinal)))status.Text=serviceCapture;
            if(pipe!=null&&Interlocked.Read(ref lastFrame)!=0&&Stopwatch.GetElapsedTime(Interlocked.Read(ref lastFrame))>TimeSpan.FromSeconds(6)){
                if(latest!=null){writer?.State(HistoryState.Interrupted,"Collector heartbeat missing; unobserved interval is not zero");latest=null;}status.Text="Unavailable — no fresh collector measurement";totals.Text="Download: unavailable    Upload: unavailable";table.Clear();
            }
        };health.Start();Microsoft.Win32.SystemEvents.TimeChanged+=ClockChanged;
        Shown+=async(_,_)=>{
            FitWindowToWorkArea();StartupEvidence.Mark("window shown");
            if(!smoke&&Environment.GetCommandLineArgs().Contains("--startup")&&(preferences.StartInTray||!preferences.OpenMainAtLaunch)){Hide();}
            if(smoke){var timer=new System.Windows.Forms.Timer{Interval=75000};timer.Tick+=async(_,_)=>{timer.Stop();timer.Dispose();await ExitApp();};timer.Start();}
            if(smoke||preferences.AutoMonitor||boot!=null)await StartMonitoring(false);
        };
    }
    void FitWindowToWorkArea()
    {
        if(IsDisposed||Disposing)return;
        var area=Screen.FromControl(this).WorkingArea;
        MinimumSize=new(Math.Min(MinimumSize.Width,area.Width),Math.Min(MinimumSize.Height,area.Height));
        if(WindowState!=FormWindowState.Normal)return;
        int width=Math.Min(Width,area.Width),height=Math.Min(Height,area.Height);
        Bounds=new(Math.Clamp(Left,area.Left,area.Right-width),Math.Clamp(Top,area.Top,area.Bottom-height),width,height);
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);if(IsHandleCreated)BeginInvoke((Action)FitWindowToWorkArea);
    }
    void ResolveBoot()
    {
        try{boot=BootConfiguration.ForCurrentUser();if(boot!=null)preferences=preferences with{Startup=StartupMode.Boot,LogDirectory=boot.Options.LogDirectory,ReportDirectory=boot.Options.ReportDirectory,Indefinite=boot.Options.Indefinite,HourlyReports=boot.Options.HourlyReports};}
        catch(Exception ex){boot=null;storageError="Boot monitoring unavailable: "+ex.Message;}
    }
    void OpenPersonalStorage()
    {
        StartupEvidence.Mark("personal storage scheduling begin");
        try{
            string logs=StoragePaths.Logs(preferences,PlatformPaths.DataRoot),output=StoragePaths.Reports(preferences,PlatformPaths.DataRoot);
            if(logs.Equals(output,StringComparison.OrdinalIgnoreCase))throw new IOException("Logs and summary report files require separate directories");
            writer=new HistoryWriter(Path.Combine(logs,"history.db"),preferences,factory:file=>{PlatformPaths.PrepareStorage(logs);return new HistoryStore(file,availableBytes:()=>new DriveInfo(Path.GetPathRoot(file)!).AvailableFreeSpace);});
            reports=new HourlyReportWriter(writer.FilePath,output,preferences,prepareDirectory:folder=>{PlatformPaths.PrepareStorage(folder,true);});
        }catch(Exception ex){storageError=ex.Message;writer=new(Path.Combine(PlatformPaths.DataRoot,"history.db"),preferences,factory:_=>throw new IOException("Storage destination unavailable: "+ex.Message));}
        StartupEvidence.Mark("personal storage workers scheduled",writer?.Status);
    }
    void ClockChanged(object? sender,EventArgs args){TimeZoneInfo.ClearCachedData();writer?.State(HistoryState.ClockChange,"System clock/timezone change; no missing coverage is fabricated");}
    protected override void WndProc(ref Message message){if(message.Msg==PlatformPaths.RestoreMessage){Restore();return;}if(message.Msg==PlatformPaths.ShutdownMessage){_=ExitApp();return;}base.WndProc(ref message);}
    void Restore(){if(exiting||IsDisposed||Disposing)return;Show();Render();if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;Activate();}
    void ShowHistory(){Restore();navigation.SelectedIndex=4;OpenHistoryWindow();}
    void ShowOptions(){Restore();navigation.SelectedIndex=8;}
    void EnsureOptions(int section)
    {
        if(optionsWindow==null||optionsWindow.IsDisposed){
            optionsWindow=new OptionsWindow(preferences,ApplyOptions,()=>status.Text+"\n"+logging.Text+"\n"+writer?.PrivacyStatus,()=>StartMonitoring(),PauseMonitoring,RestartMonitoring,ShowHistory,ClearHistory,RetryLogging,Uninstall,ex=>{storageError="Options operation failed: "+ex.Message;if(!IsDisposed&&!Disposing)logging.Text=storageError;},Restore);
            optionsWindow.AddExitAction(()=>_=ExitApp());
            optionsWindow.EmbedInShell(index=>navigation.SelectedIndex=index switch{1=>3,2=>4,3=>6,4=>7,5=>8,6=>9,_=>0});
        }
        var viewport=sections[navigation.SelectedIndex];
        if(optionsWindow.Parent!=viewport){optionsWindow.Parent=viewport;optionsWindow.Dock=DockStyle.Fill;}
        optionsWindow.SelectSection(section);optionsWindow.Show();
    }
    async Task ClosePersonalStorage(){await DrainPrivateCollector();if(writer!=null){await writer.DisposeAsync();writer=null;}if(reports!=null){await reports.DisposeAsync();reports=null;}}
    async Task DrainPrivateCollector()
    {
        await helperDrainGate.WaitAsync();
        try{
            var helper=privateHelper;if(helper==null)return;
            try{await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));}
            catch(TimeoutException){if(!IsDisposed)status.Text="Private collector is still stopping; wait before restarting or replacing application files";throw;}
            if(ReferenceEquals(privateHelper,helper))privateHelper=null;helper.Dispose();
        }finally{helperDrainGate.Release();}
    }
    async Task Admin(string operation,BootConfiguration? value=null)
    {
        if(!PlatformPaths.Installed)throw new NotSupportedException("Pre-login monitoring requires the normal protected Program Files installation");
        string? payload=null;
        try{
            var info=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"CylonDetector.Service.exe")){UseShellExecute=true,Verb="runas"};info.ArgumentList.Add(operation);
            if(value!=null){payload=Path.Combine(PlatformPaths.DataRoot,"boot-request-"+Guid.NewGuid().ToString("N")+".json");File.WriteAllText(payload,JsonSerializer.Serialize(value));info.ArgumentList.Add(payload);}
            using var process=Process.Start(info)??throw new IOException("Administrative helper did not start");await process.WaitForExitAsync();if(process.ExitCode!=0)throw new IOException("Boot configuration did not complete (exit "+process.ExitCode+"); existing history was preserved");
        }finally{if(payload!=null)File.Delete(payload);}
    }
    async Task ApplyOptions(Preferences value)
    {
        if(!await configurationGate.WaitAsync(0))throw new InvalidOperationException("Another settings operation is in progress");
        configurationBusy=true;
        try{if(exiting)throw new InvalidOperationException("Detector is closing; settings were not changed");await ApplyOptionsCore(value);if(storageError?.StartsWith("Options operation failed:",StringComparison.Ordinal)==true)storageError=null;}
        finally{configurationBusy=false;configurationGate.Release();}
    }
    async Task ApplyOptionsCore(Preferences value)
    {
        if(!settings.Writable)throw new InvalidOperationException("Unsupported configuration is read-only");
        string requestedLogs=StoragePaths.Logs(value,value.Startup==StartupMode.Boot?BootConfiguration.DefaultLogs:PlatformPaths.DataRoot);
        string requestedReports=StoragePaths.Reports(value,value.Startup==StartupMode.Boot?BootConfiguration.Root:PlatformPaths.DataRoot);
        if(requestedLogs.Equals(requestedReports,StringComparison.OrdinalIgnoreCase))throw new IOException("Logs and reports require separate destinations");
        // Reject invalid destinations before disconnecting capture or requesting elevation.
        var check=await Task.Run(()=>DestinationRules.Check(value.Startup,requestedLogs,requestedReports));
        if(!check.Valid)throw new IOException(check.LogError??check.ReportError);
        if(value.Startup!=StartupMode.Boot)await Task.Run(()=>{PlatformPaths.PrepareStorage(requestedLogs);PlatformPaths.PrepareStorage(requestedReports,true);});
        // Policy-only changes never stop capture, disconnect the preview or reopen SQLite.
        if(value.DetailedDiagnostics==preferences.DetailedDiagnostics&&value.Startup!=StartupMode.Boot&&boot==null&&writer!=null&&requestedLogs.Equals(Path.GetDirectoryName(writer.FilePath),StringComparison.OrdinalIgnoreCase)){
            await Task.Run(()=>{PlatformPaths.PrepareStorage(requestedReports,true);});PlatformPaths.Startup(value.Startup);settings.Save(value);
            bool moved=!requestedReports.Equals(StoragePaths.Reports(preferences,PlatformPaths.DataRoot),StringComparison.OrdinalIgnoreCase);
            preferences=value;writer.Configure(value);
            if(moved){var old=reports;reports=new HourlyReportWriter(writer.FilePath,requestedReports,value,prepareDirectory:folder=>{PlatformPaths.PrepareStorage(folder,true);});if(old!=null)await old.DisposeAsync();}else reports?.Configure(value);
            return;
        }
        if(value.Startup==StartupMode.Boot&&boot!=null&&requestedLogs.Equals(boot.Options.LogDirectory,StringComparison.OrdinalIgnoreCase)&&requestedReports.Equals(boot.Options.ReportDirectory,StringComparison.OrdinalIgnoreCase)){
            using var identity=WindowsIdentity.GetCurrent();var config=new BootConfiguration(identity.User!.Value,Environment.ProcessId,value);await Admin("/configure",config);
            PlatformPaths.Startup(StartupMode.SignIn);settings.Save(value);preferences=value;boot=BootConfiguration.Load();return;
        }
        bool connected=pipe!=null;var previous=preferences;var previousBoot=boot;
        await DisconnectPreview();await ClosePersonalStorage();
        try{
            if(value.Startup==StartupMode.Boot){
                value=value with{LogDirectory=string.IsNullOrWhiteSpace(value.LogDirectory)?BootConfiguration.DefaultLogs:value.LogDirectory,ReportDirectory=string.IsNullOrWhiteSpace(value.ReportDirectory)?BootConfiguration.DefaultReports:value.ReportDirectory};
                using var identity=WindowsIdentity.GetCurrent();var config=new BootConfiguration(identity.User!.Value,Environment.ProcessId,value);config.Validate();
                bool policyOnly=previousBoot!=null&&previousBoot.Options.LogDirectory==value.LogDirectory&&previousBoot.Options.ReportDirectory==value.ReportDirectory;
                await Admin(policyOnly?"/configure":"/enable",config);
            }else{
                if(NativeService.ProcessId()!=null)await Admin("/remove");
                string logs=StoragePaths.Logs(value,PlatformPaths.DataRoot),output=StoragePaths.Reports(value,PlatformPaths.DataRoot);if(logs.Equals(output,StringComparison.OrdinalIgnoreCase))throw new IOException("Logs and reports require separate directories");
            }
            PlatformPaths.Startup(value.Startup==StartupMode.Boot?StartupMode.SignIn:value.Startup);settings.Save(value);preferences=value;storageError=null;boot=null;ResolveBoot();
            if(boot==null)OpenPersonalStorage();
            if(connected||(boot!=null&&previous.Startup!=StartupMode.Boot))_=StartMonitoring(false);
        }catch{
            preferences=previous;boot=null;ResolveBoot();if(boot==null&&NativeService.ProcessId()==null)OpenPersonalStorage();if(connected)_=StartMonitoring(false);throw;
        }
    }
    async Task MutateConfiguration(Func<Task> operation)
    {
        if(!await configurationGate.WaitAsync(0)){if(!IsDisposed)status.Text="Another settings operation is in progress";return;}
        configurationBusy=true;
        try{if(!exiting)await operation();}
        finally{configurationBusy=false;configurationGate.Release();}
    }
    Task ClearHistory()=>MutateConfiguration(ClearHistoryCore);
    async Task ClearHistoryCore()
    {
        if(MessageBox.Show(this,"Permanently delete detailed history and automatic activity summary report files? Settings and manually exported CSV files remain. The RAM prehistory and familiarity baseline are also cleared; new five-minute context accumulates afterward. Boot mode requires UAC; ongoing collection continues.","Clear History",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        try{if(boot!=null||preferences.Startup==StartupMode.Boot)await Admin("/clear-history");else{if(reports!=null)await reports.ClearAsync(()=>writer?.ClearAsync()??Task.CompletedTask);else if(writer!=null)await writer.ClearAsync();}}catch(Exception ex){Failure("History could not be cleared",ex);}
    }
    Task RetryLogging()=>MutateConfiguration(RetryLoggingCore);
    async Task RetryLoggingCore(){if(boot!=null){await Admin("/restart-if-enabled");return;}bool connected=pipe!=null;await DisconnectPreview();await ClosePersonalStorage();loggingWarningShown=false;OpenPersonalStorage();if(connected)await StartMonitoring(false);}
    void Failure(string operation,Exception error){storageError=operation+": "+error.Message;if(!IsDisposed&&!Disposing)logging.Text=storageError;}
    async Task Uninstall()
    {
        if(!PlatformPaths.Installed)return;
        if(MessageBox.Show(this,"Open the normal Cylon Detector uninstaller? Existing logs/settings are preserved by default.","Uninstall Cylon Detector",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        string? path=PlatformPaths.RegisteredUninstaller();if(path==null){MessageBox.Show(this,"Use Windows Settings → Apps; registered uninstaller unavailable.");return;}
        try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});await ExitApp();}catch(Exception ex){Failure("Uninstall could not start",ex);}
    }
    async Task StartMonitoring(bool explicitRequest=true)
    {
        if(pipe!=null||starting||exiting||(configurationBusy&&explicitRequest))return;StartupEvidence.Mark("monitoring request begin",writer?.Status);starting=true;UpdateMonitoringIndicator();start.Enabled=false;stopButton.Enabled=true;Interlocked.Exchange(ref lastFrame,0);
        if(explicitRequest){ledger.Reset();sessionApplications.Reset();connectedClock.Reset();rendered=null;}
        var tokenSource=cancel=new();var token=tokenSource.Token;PipeStream? channel=null;bool handedOff=false;
        try{
            await DrainPrivateCollector();token.ThrowIfCancellationRequested();
            bool service=NativeService.ProcessId()!=null;if(!service&&preferences.Startup==StartupMode.Boot)throw new IOException("Boot service is not installed; re-enable it in Options. No duplicate desktop collector was started");
            if(service){
                ResolveBoot();if(boot==null)throw new UnauthorizedAccessException(storageError??"Boot history is not authorized for this account");
                if(NativeService.ProcessId()==0&&explicitRequest)await Admin("/restart-if-enabled");
                serviceHealth=null;channel=pipe=await NativeService.Connect(token);status.Text="Connected to pre-login capture; service owns persistence";
            }else{
                writer?.Begin();status.Text="Waiting for collector permission…";string name="CylonDetector-"+Guid.NewGuid().ToString("N");var server=LocalPipe.Create(name);channel=pipe=server;
                StartupEvidence.Mark("private helper launch begin",writer?.Status);
                privateHelper=Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"CylonDetector.Collector.exe"),name+(preferences.DetailedDiagnostics?" --diagnostic-details":"")){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden});
                StartupEvidence.Mark("private helper launch returned",privateHelper==null?"no process":"pid="+privateHelper.Id);
                await server.WaitForConnectionAsync(token).WaitAsync(TimeSpan.FromSeconds(30),token);status.Text="Collector connected — waiting for completed seconds";
            }
            StartupEvidence.Mark("collector handshake complete",writer?.Status);connectedClock.Start();trayMonitoring.Text="Pause Monitoring";tray.Text="Cylon Detector — monitoring";var destination=writer;
            handedOff=true;previewReader=ReadPreview(channel,service,destination,token);
        }catch(OperationCanceledException){}catch(Exception ex){StartupEvidence.Mark("collector startup failed",ex.GetType().Name+": "+ex.Message);if(!IsDisposed){status.Text="Unavailable: "+ex.Message;writer?.State(HistoryState.Interrupted,"Collector unavailable: "+ex.Message);}}
        finally{starting=false;if(!handedOff)FinishPreview(channel);UpdateMonitoringIndicator();}
    }
    async Task ReadPreview(PipeStream channel,bool service,HistoryWriter? destination,CancellationToken token)
    {
        try{await Task.Run(async()=>{
                using var reader=new StreamReader(channel);var lines=new BoundedLineReader(reader,2_000_000);
                while(!token.IsCancellationRequested){
                    string? line=await lines.ReadAsync(token);if(line==null){if(ReferenceEquals(stopAcknowledged,channel))break;throw new IOException("Collector disconnected without a completed drain");}token.ThrowIfCancellationRequested();
                    if(!service&&line=="CylonDetector.StopComplete.v1"){if(!ReferenceEquals(stoppingPreview,channel))throw new IOException("Unexpected collector stop acknowledgement");stopAcknowledged=channel;continue;}
                    Measurement? frame;
                    if(service){var preview=JsonSerializer.Deserialize<ServicePreview>(line)??throw new IOException("Invalid service preview");if(preview.Schema!=1)throw new IOException("Unsupported service protocol");servicePersistence=preview.Persistence;serviceReports=preview.Reports;serviceCapture=preview.Capture;serviceHealth=preview;frame=preview.Frame;}
                    else{frame=JsonSerializer.Deserialize<Measurement>(line)??throw new IOException("Invalid collector frame");if(frame.Schema!=1)throw new IOException("Unsupported collector schema");destination?.Append(frame);}
                    if(frame!=null){ledger.Observe(frame);sessionApplications.Observe(frame);latest=frame;Interlocked.Exchange(ref lastFrame,Stopwatch.GetTimestamp());}
                }
            },token);}
        catch(Exception) when(token.IsCancellationRequested){}
        catch(Exception ex){if(ReferenceEquals(pipe,channel)&&!IsDisposed){status.Text="Unavailable: "+ex.Message;if(!service)destination?.State(HistoryState.Interrupted,"Collector unavailable: "+ex.Message);}}
        finally{FinishPreview(channel);}
    }
    void FinishPreview(PipeStream? channel)
    {
        if(!ReferenceEquals(pipe,channel))return;
        channel?.Dispose();pipe=null;latest=null;
        if(IsDisposed||Disposing)return;
        totals.Text="Download: unavailable    Upload: unavailable";table.Clear();start.Enabled=true;stopButton.Enabled=false;tray.Text="Cylon Detector — preview stopped";trayMonitoring.Text="Start Monitoring";UpdateMonitoringIndicator();
    }
    async Task DisconnectPreview()
    {
        connectedClock.Stop();var channel=pipe;bool drained=false;
        if(channel!=null&&boot==null&&channel.IsConnected&&previewReader!=null){
            stoppingPreview=channel;stopAcknowledged=null;
            try{
                using var stopTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await channel.WriteAsync(new byte[]{1},stopTimeout.Token);
                await previewReader.WaitAsync(stopTimeout.Token);drained=ReferenceEquals(stopAcknowledged,channel);
            }catch(Exception ex)when(ex is IOException or OperationCanceledException or ObjectDisposedException){
                writer?.State(HistoryState.Interrupted,"Graceful collector drain failed; undelivered tail remains unavailable: "+ex.Message);
            }finally{stoppingPreview=null;}
        }
        if(channel!=null&&boot==null)writer?.State(HistoryState.Paused,drained?"Monitoring paused; known pending ETW bytes retained with unavailable tail coverage":"Monitoring paused; undelivered tail remains unavailable");
        cancel?.Cancel();pipe?.Dispose();pipe=null;cancel?.Dispose();cancel=null;latest=null;table.Clear();start.Enabled=true;stopButton.Enabled=false;status.Text=boot==null?"Paused — no current measurement":"Live preview disconnected; independent boot collection continues";totals.Text="Download: unavailable    Upload: unavailable";trayMonitoring.Text="Start Monitoring";UpdateMonitoringIndicator();
    }
    Task PauseMonitoring()=>MutateConfiguration(PauseMonitoringCore);
    async Task PauseMonitoringCore(){await DisconnectPreview();if(NativeService.ProcessId()!=null)try{await Admin("/stop");status.Text="Boot service paused; automatic collection resumes at the next Windows boot unless disabled in Options";}catch(Exception ex){Failure("Boot service was not stopped",ex);}}
    async Task RestartMonitoring(){await PauseMonitoring();await StartMonitoring();}
    void UpdateMonitoringIndicator()
    {
        if(IsDisposed||Disposing)return;
        bool stale=Interlocked.Read(ref lastFrame)==0||Stopwatch.GetElapsedTime(Interlocked.Read(ref lastFrame))>TimeSpan.FromSeconds(6);
        bool unavailable=boot!=null&&(serviceCapture.StartsWith("Capture unavailable",StringComparison.Ordinal)||serviceCapture.StartsWith("Collector interruption",StringComparison.Ordinal));
        string state=exiting?"◌ CLOSING — saving and stopping preview":starting?"◌ MONITORING WAITING — connecting":pipe!=null&&!stale&&!unavailable?"● MONITORING ON — receiving measurements":pipe!=null?"◌ MONITORING WAITING — no fresh measurements":boot!=null?"◌ PREVIEW OFF — check background service in Monitoring":"○ MONITORING OFF — no desktop capture";
        monitoringIndicator.Text=state;monitoringIndicator.AccessibleDescription=state;
        monitoringIndicator.ForeColor=SystemInformation.HighContrast?SystemColors.ControlText:state.Contains("MONITORING ON")?AppTheme.Good:state.Contains("MONITORING OFF")?Color.FromArgb(239,157,157):AppTheme.Warning;
        start.Text=starting||pipe!=null?"Stop monitoring":"Start monitoring";
        string explanation=starting||pipe!=null?"Stop monitoring. Missing intervals will remain unobserved; recorded history is preserved.":"Start monitoring. An administrator permission prompt may appear.";
        if(start.AccessibleDescription!=explanation)AppTheme.SetExplanation(start,explanation);
        start.Enabled=!exiting;stopButton.Enabled=(starting||pipe!=null)&&!exiting;
    }
    void Render()=>RenderModern();
    static string Rate(long bytes)=>$"{bytes/1024d:F1} KiB/s";
    async Task ExitApp()
    {
        if(exiting)return;exiting=true;UpdateMonitoringIndicator();await configurationGate.WaitAsync();configurationGate.Release();HistoryWindow.ApplicationClosing(true);await DisconnectPreview();health.Stop();var bounds=WindowState==FormWindowState.Normal?Bounds:RestoreBounds;
        try{settings.Save(preferences with{Width=bounds.Width,Height=bounds.Height});}catch(Exception ex){logging.Text="Preferences not saved: "+ex.Message;}
        try{await Task.WhenAll(Application.OpenForms.OfType<HistoryWindow>().Select(window=>window.ActiveExport)).WaitAsync(TimeSpan.FromSeconds(30));await ClosePersonalStorage();}catch(Exception ex){exiting=false;HistoryWindow.ApplicationClosing(false);Failure("Exit is waiting for collector/history/CSV shutdown; retry Exit",ex);health.Start();return;}
        Microsoft.Win32.SystemEvents.TimeChanged-=ClockChanged;optionsWindow?.Dispose();historyWindow?.Dispose();tray.Visible=false;tray.Dispose();health.Dispose();Close();
    }
}
