using Raider.History;
using Raider.Service;
namespace Raider.App;

sealed class OptionsWindow : Form
{
    readonly Func<Preferences,Task> save;
    readonly Action<Exception> reportFailure;
    readonly Func<string> monitoring;
    Preferences saved;
    readonly ListBox navigation=new(){Dock=DockStyle.Fill,IntegralHeight=false,BorderStyle=BorderStyle.None,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=48,AccessibleName="Options sections"};
    readonly Panel content=new(){Dock=DockStyle.Fill};
    readonly List<Panel> pages=[];
    readonly ComboBox startup=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,Name="StartupMode",AccessibleName="Monitoring startup mode"};
    readonly TextBox logs=new(){Dock=DockStyle.Fill,Name="LogDirectory"},reports=new(){Dock=DockStyle.Fill,Name="ReportDirectory"};
    readonly Label storageInfo=TextLabel("Storage information is loading…");
    readonly Label logError=TextLabel(""),reportError=TextLabel(""),location=TextLabel(""),feedback=TextLabel(""),monitorState=TextLabel("");
    readonly Button apply=new RoundedButton(){Text="&Apply",AutoSize=true,Name="Apply"},close=new RoundedButton(){Text="&Close",AutoSize=true,Name="Close"};
    readonly Button discard=new RoundedButton(){Text="Discard unsaved changes",AutoSize=true,Name="DiscardChanges"};
    readonly System.Windows.Forms.Timer validationTimer=new(){Interval=200};
    readonly CheckBox details=Check("Retain bounded raw-PID attribution details (sensitive activity metadata)");
    readonly NumericUpDown diagnosticDays=new(){Minimum=0,Maximum=365,Width=120,AccessibleName="Attribution diagnostic retention in recorded days; 0 uses the traffic history retention setting"};
    readonly CheckBox tray=Check("Start in the system tray"),main=Check("Open the main window at launch"),keep=Check("Continue in tray when the main window closes"),auto=Check("Begin desktop monitoring when Raider launches (UAC may appear)"),indefinite=Check("Keep history indefinitely"),hourly=Check("Generate automatic hourly CSV reports");
    bool loading=true,busy,closeRequested,validating,validationRunning;
    int validationVersion;
    bool embedded;Action<int>? shellNavigation;
    DestinationCheck? validation;
    string desktopLogs,desktopReports,bootLogs,bootReports;
    StartupMode displayedMode;
    internal Task ActiveOperation {get;private set;}=Task.CompletedTask;
    internal bool Saving=>busy;
    bool CanNotify=>!IsDisposed&&!Disposing&&IsHandleCreated;
    StartupMode Mode=>(StartupMode)Math.Max(0,startup.SelectedIndex);

    public OptionsWindow(Preferences current,Func<Preferences,Task> save,Func<string> monitoring,Func<Task> start,Func<Task> pause,Func<Task> restart,Action history,Func<Task> clear,Func<Task> retryLogging,Func<Task> uninstall,Action<Exception>? reportFailure=null,Action? dashboard=null)
    {
        this.save=save;this.monitoring=monitoring;this.reportFailure=reportFailure??(_=>{});saved=current;
        Text="Cylon Raider — Settings";Name="OptionsWindow";AutoScaleMode=AutoScaleMode.Font;Font=new("Segoe UI",10);ClientSize=new(1120,760);MinimumSize=new(860,600);StartPosition=FormStartPosition.CenterParent;Icon=PlatformPaths.OfficialIcon();BackColor=Color.FromArgb(22,25,31);ForeColor=Color.Gainsboro;
        desktopLogs=current.Startup==StartupMode.Boot?PlatformPaths.DataRoot:(current.LogDirectory??PlatformPaths.DataRoot);
        desktopReports=current.Startup==StartupMode.Boot?Path.Combine(PlatformPaths.DataRoot,"Reports"):(current.ReportDirectory??Path.Combine(PlatformPaths.DataRoot,"Reports"));
        bootLogs=current.Startup==StartupMode.Boot?(current.LogDirectory??BootConfiguration.DefaultLogs):BootConfiguration.DefaultLogs;
        bootReports=current.Startup==StartupMode.Boot?(current.ReportDirectory??BootConfiguration.DefaultReports):BootConfiguration.DefaultReports;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Padding=new(12)};
        root.ColumnStyles.Add(new(SizeType.Absolute,248));FontChanged+=(_,_)=>{navigation.ItemHeight=Math.Max(44,Font.Height*2);root.ColumnStyles[0].Width=Math.Max(248,navigation.Items.Cast<object>().Select(item=>TextRenderer.MeasureText(item.ToString(),Font).Width+40).DefaultIfEmpty(248).Max());};root.ColumnStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.AutoSize));root.Controls.Add(navigation,0,0);root.Controls.Add(content,1,0);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,Padding=new(8)};footer.ColumnStyles.Add(new(SizeType.Percent,100));AppTheme.WrapLabels(footer);
        var footerActions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true};footerActions.Controls.AddRange([discard,apply,close]);
        footer.Controls.Add(feedback,0,0);footer.Controls.Add(footerActions,0,1);root.Controls.Add(footer,0,1);root.SetColumnSpan(footer,2);Controls.Add(root);CancelButton=close;
        navigation.Items.AddRange(["◉  Dashboard","▶  Monitoring","▤  History & Reports","▣  Storage","⚙  Startup & Service","☷  Settings","ⓘ  About & Maintenance"]);
        // Font/DPI autoscaling can run after FontChanged. Reconcile the sidebar
        // against its actual inherited font at layout time, without recursive growth.
        bool fittingNavigation=false;
        root.Layout+=(_,_)=>{
            if(fittingNavigation||embedded)return;
            float needed=Math.Max(248,navigation.Items.Cast<object>().Select(item=>TextRenderer.MeasureText(item.ToString(),navigation.Font).Width+navigation.Margin.Horizontal+SystemInformation.VerticalScrollBarWidth+40).DefaultIfEmpty(248).Max());
            if(Math.Abs(root.ColumnStyles[0].Width-needed)<0.5)return;
            fittingNavigation=true;try{root.ColumnStyles[0].Width=needed;}finally{fittingNavigation=false;}
        };
        navigation.DrawItem+=(_,e)=>{if(e.Index<0)return;bool selected=(e.State&DrawItemState.Selected)!=0;using var brush=new SolidBrush(selected?Color.FromArgb(70,42,48):navigation.BackColor);e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,navigation.Items[e.Index].ToString(),Font,new Rectangle(e.Bounds.X+10,e.Bounds.Y,e.Bounds.Width-14,e.Bounds.Height),ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);if((e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();};
        var dash=Page("Dashboard");Add(dash,TextLabel("The live monitor shows current rates, process identities, collector coverage and recording health. Collection and logging continue independently of this window."));ActionButton(dash,"Open live &monitor",()=>{dashboard?.Invoke();return Task.CompletedTask;});ActionButton(dash,"View &history / export CSV",()=>{history();return Task.CompletedTask;});
        var monitoringPage=Page("Monitoring");Add(monitoringPage,monitorState);Add(monitoringPage,TextLabel("Desktop monitoring uses a separately elevated collector. Boot monitoring runs independently of the GUI. Missing measurements are never zero traffic."));ActionButton(monitoringPage,"&Start monitoring",start);ActionButton(monitoringPage,"&Pause monitoring",pause);ActionButton(monitoringPage,"&Restart monitoring",restart);
        var hist=Page("History & Reports");Add(hist,TextLabel("Default: newest 15 calendar days containing recorded monitoring coverage. Confirmed zero-only days count; days without monitoring do not. Indefinite retention prevents age deletion, and cannot recover deleted history."));ActionButton(hist,"View history / export CSV",()=>{history();return Task.CompletedTask;});ActionButton(hist,"Open hourly report folder",()=>OpenDestination(reports.Text));
        var storage=Page("Storage");Add(storage,location);Destination(storage,"Detailed logs (history.db)",logs,logError,()=>Mode==StartupMode.Boot?BootConfiguration.DefaultLogs:PlatformPaths.DataRoot);Destination(storage,"Hourly reports (separate CSV files)",reports,reportError,()=>Mode==StartupMode.Boot?BootConfiguration.DefaultReports:Path.Combine(PlatformPaths.DataRoot,"Reports"));Add(storage,storageInfo);Add(storage,TextLabel("Apply opens the selected store; earlier files remain where they were. Report and retention changes keep capture running. Moving detailed storage flushes and reconnects capture; any interruption remains explicit."));
        var service=Page("Startup & Background Service");startup.Items.AddRange(["Off — desktop monitoring only","At Windows user sign-in","At Windows boot — before sign-in (UAC required)"]);Add(service,AppTheme.Input(startup));Add(service,TextLabel("Desktop/sign-in: separate local folders you control. Boot: SYSTEM writes only below the protected location shown in Storage. Apply provisions new permitted subfolders through the installed administrator helper; browsing never changes permissions."));Add(service,TextLabel("Boot collection requires the normal Program Files installation and explicit UAC. Selected-owner isolation, protected ACLs and reparse-point checks remain enforced. Closing the GUI leaves boot capture running; Pause stops the service."));var storageLink=AppTheme.Button("Configure storage destinations",()=>{if(shellNavigation!=null)shellNavigation(3);else navigation.SelectedIndex=3;});Add(service,storageLink);
        var settings=Page("Settings");Add(settings,auto);Add(settings,indefinite);Add(settings,TextLabel("Keep the newest 15 recorded calendar days by default, or retain history indefinitely. Deleted recordings cannot be recovered."));Add(settings,hourly);Add(settings,details);Add(settings,TextLabel("Aggregate failure reasons remain useful without individual PID detail. No endpoint/domain history or DNS lookups. Changing detailed capture reconnects the collector; prior recordings remain. Attribution diagnostic retention (recorded days): enter 0 to use the same retention as traffic history — normally the newest 15 days with recordings, or indefinitely if Keep history indefinitely is selected. Enter 1–365 to set the diagnostic retention separately. A recorded day is a calendar day containing monitoring coverage. Older minute-level reason summaries may expire while original traffic/quality history remains."));Add(settings,AppTheme.Input(diagnosticDays));Add(settings,tray);Add(settings,main);Add(settings,keep);
        var about=Page("About & Maintenance");Add(about,TextLabel("Cylon Raider Windows v0.5.2 prerelease\nEiren & I Apps\nLive monitoring, economical local history and separate hourly reports."));Add(about,TextLabel("No application network communication, DNS, telemetry or updater. Counts and metadata only; no packet contents. Hosted Windows tests do not replace physical acceptance."));Add(about,TextLabel($"Version: {Raider.Core.BuildIdentity.Version} · Source: {Raider.Core.BuildIdentity.SourceRevision}\nIntegrity model: {Raider.Core.BuildIdentity.IntegrityModel}"));Add(about,TextLabel("Original artwork preserved. Distribution documentation includes .NET, TraceEvent and SQLite notices. Installed mode: "+PlatformPaths.Installed));ActionButton(about,"Clear history / automatic reports…",clear);ActionButton(about,"Retry logging after a failure",retryLogging);ActionButton(about,"Uninstall Cylon Raider…",uninstall,PlatformPaths.Installed);
        details.Checked=current.DetailedDiagnostics;diagnosticDays.Value=Math.Clamp(current.AttributionRecordedDays,0,365);tray.Checked=current.StartInTray;main.Checked=current.OpenMainAtLaunch;keep.Checked=current.ContinueInTray;auto.Checked=current.AutoMonitor;indefinite.Checked=current.Indefinite;hourly.Checked=current.HourlyReports;startup.SelectedIndex=(int)current.Startup;displayedMode=current.Startup;SetDestinations();
        tray.CheckedChanged+=(_,_)=>{if(tray.Checked)main.Checked=false;};main.CheckedChanged+=(_,_)=>{if(main.Checked)tray.Checked=false;};
        foreach(var check in new[]{tray,main,keep,auto,indefinite,hourly,details})check.CheckedChanged+=(_,_)=>Changed();
        diagnosticDays.ValueChanged+=(_,_)=>Changed();
        logs.TextChanged+=(_,_)=>QueueValidation();reports.TextChanged+=(_,_)=>QueueValidation();
        startup.SelectedIndexChanged+=(_,_)=>{if(displayedMode==StartupMode.Boot){bootLogs=logs.Text;bootReports=reports.Text;}else{desktopLogs=logs.Text;desktopReports=reports.Text;}displayedMode=Mode;SetDestinations();QueueValidation();};
        validationTimer.Tick+=(_,_)=>{validationTimer.Stop();_=ValidateAsync();};
        discard.Click+=(_,_)=>{if(!busy)DiscardChanges();};
        apply.Click+=(_,_)=>{if(!busy)ActiveOperation=ApplyAsync();};close.Click+=(_,_)=>Close();
        FormClosing+=(_,e)=>{if(busy){e.Cancel=true;closeRequested=true;feedback.Text="Finishing the current operation before closing…";}};
        navigation.SelectedIndexChanged+=(_,_)=>{for(int i=0;i<pages.Count;i++)pages[i].Visible=i==navigation.SelectedIndex;monitorState.Text=monitoring();};
        Shown+=(_,_)=>{var area=Screen.FromControl(this).WorkingArea;if(MinimumSize.Width>area.Width||MinimumSize.Height>area.Height)MinimumSize=new(Math.Min(MinimumSize.Width,area.Width),Math.Min(MinimumSize.Height,area.Height));if(Width>area.Width||Height>area.Height)Bounds=new(area.X,area.Y,Math.Min(Width,area.Width),Math.Min(Height,area.Height));QueueValidation();};
        ApplyTheme(this);AppTheme.Apply(this);saved=Snapshot();loading=false;navigation.SelectedIndex=0;QueueValidation();
    }
    internal void AddExitAction(Action exit){var page=(TableLayoutPanel)pages[6].Controls[0];Add(page,AppTheme.Button("Exit Cylon Raider",exit));}
    internal void EmbedInShell(Action<int> navigate)
    {
        embedded=true;shellNavigation=navigate;TopLevel=false;FormBorderStyle=FormBorderStyle.None;
        MinimumSize=Size.Empty;ShowInTaskbar=false;close.Visible=false;CancelButton=null;
        var root=(TableLayoutPanel)Controls[0];navigation.Visible=false;root.ColumnStyles[0].Width=0;
        root.Padding=Padding.Empty;root.SetColumnSpan(content,2);root.SetColumn(content,0);
        foreach(var button in pages[1].Controls[0].Controls.OfType<Button>().Where(button=>button.Text is "&Start monitoring" or "&Pause monitoring"))button.Visible=false;
        apply.Text="&Apply settings";apply.AccessibleDescription="Validate and save all pending settings, including changes made on other sections. Monitoring continues independently.";
        AppTheme.WrapLabels((Control)pages[5].Controls[0]);
    }
    protected override void Dispose(bool disposing){if(disposing){validationVersion++;validationTimer.Stop();validationTimer.Dispose();}base.Dispose(disposing);}
    internal void SelectSection(int index){if(!busy&&index>=0&&index<navigation.Items.Count){navigation.SelectedIndex=index;RefreshMonitoring();}}
    internal void RefreshMonitoring(){if(!IsDisposed)monitorState.Text=monitoring();}
    static CheckBox Check(string text)=>new(){Text=text,AutoSize=true,Dock=DockStyle.Top,Margin=new(0,8,0,8)};
    static Label TextLabel(string text)=>new(){Text=text,AutoSize=true,Dock=DockStyle.Top,Margin=new(0,6,0,8),UseMnemonic=false};
    TableLayoutPanel Page(string title){var viewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Visible=false,Padding=new(20,4,12,12)};var page=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=0};page.ColumnStyles.Add(new(SizeType.Percent,100));AppTheme.WrapLabels(page);viewport.Controls.Add(page);content.Controls.Add(viewport);pages.Add(viewport);Add(page,new Label{Text=title,Font=new(Font.FontFamily,18,FontStyle.Bold),AutoSize=true,Dock=DockStyle.Top,Margin=new(0,0,0,16)});return page;}
    static void Add(TableLayoutPanel page,Control control){page.RowCount++;page.RowStyles.Add(new(SizeType.AutoSize));page.Controls.Add(control,0,page.RowCount-1);}
    void ActionButton(TableLayoutPanel page,string text,Func<Task> action,bool enabled=true){var button=new RoundedButton{Text=text,AutoSize=true,Enabled=enabled,Anchor=AnchorStyles.Left,Margin=new(0,6,0,6)};button.Click+=(_,_)=>{if(!busy)ActiveOperation=RunAction(action);};Add(page,button);}
    async Task RunAction(Func<Task> action){busy=true;closeRequested=false;SetBusy();try{await action();if(CanNotify){feedback.Text="Request finished; check monitoring and recording status.";monitorState.Text=monitoring();}}catch(Exception ex){Failure(ex);}finally{FinishOperation();}}
    void Destination(TableLayoutPanel page,string title,TextBox text,Label error,Func<string> defaultPath){text.AccessibleName=title;Add(page,TextLabel(title));var row=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1};row.ColumnStyles.Add(new(SizeType.Percent,100));AppTheme.WrapLabels(row);row.Controls.Add(AppTheme.Input(text),0,0);var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Margin=new(0,4,0,0)};var browse=new RoundedButton{Text="Browse…",AutoSize=true};var useDefault=new RoundedButton{Text="Use Default",AutoSize=true};var open=new RoundedButton{Text="Open Folder",AutoSize=true};browse.Click+=(_,_)=>{try{using var picker=new FolderBrowserDialog{Description=Mode==StartupMode.Boot?"Choose a protected subfolder below "+BootConfiguration.Root:title,SelectedPath=BrowseStart(text.Text),ShowNewFolderButton=Mode!=StartupMode.Boot};if(picker.ShowDialog(this)==DialogResult.OK)text.Text=picker.SelectedPath;}catch(Exception ex){Failure(ex);}};useDefault.Click+=(_,_)=>text.Text=defaultPath();open.Click+=(_,_)=>{if(!busy)ActiveOperation=RunAction(()=>OpenDestination(text.Text));};browse.AccessibleName=title+" — Browse";useDefault.AccessibleName=title+" — Use Default";open.AccessibleName=title+" — Open Folder";actions.Controls.AddRange([browse,useDefault,open]);row.Controls.Add(actions,0,1);Add(page,row);Add(page,error);}
    string BrowseStart(string text)
    {
        // Reject UNC/network/reparse input before Directory.Exists can touch it.
        try{string local=Mode==StartupMode.Boot?BootConfiguration.Destination(text):StoragePaths.LocalDirectory(text);if(Directory.Exists(local))return local;}catch(Exception){}
        return Mode==StartupMode.Boot?Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData):PlatformPaths.DataRoot;
    }
    Task OpenDestination(string path){var check=DestinationRules.Check(Mode,logs.Text,reports.Text);string? error=path==logs.Text?check.LogError:check.ReportError;if(error!=null)throw new IOException(error);if(!Directory.Exists(path))throw new DirectoryNotFoundException("Folder does not exist yet. Apply provisions a new destination; no folder was opened or redirected.");PlatformPaths.OpenExistingFolder(path);return Task.CompletedTask;}
    void SetDestinations(){logs.Text=Mode==StartupMode.Boot?bootLogs:desktopLogs;reports.Text=Mode==StartupMode.Boot?bootReports:desktopReports;location.Text=Mode==StartupMode.Boot?"BOOT MONITORING — permitted root: "+BootConfiguration.Root+"\nChoose separate subfolders. New protected subfolders are created through UAC on Apply.":"DESKTOP / SIGN-IN MONITORING — choose separate local destinations you control.";}
    Preferences Snapshot()=>saved with{DetailedDiagnostics=details.Checked,AttributionRecordedDays=(int)diagnosticDays.Value,Startup=Mode,StartInTray=tray.Checked,OpenMainAtLaunch=main.Checked&&!tray.Checked,ContinueInTray=keep.Checked,AutoMonitor=auto.Checked,Indefinite=indefinite.Checked,HourlyReports=hourly.Checked,LogDirectory=logs.Text,ReportDirectory=reports.Text};
    void DiscardChanges()
    {
        loading=true;
        try{
            details.Checked=saved.DetailedDiagnostics;diagnosticDays.Value=Math.Clamp(saved.AttributionRecordedDays,0,365);
            tray.Checked=saved.StartInTray;main.Checked=saved.OpenMainAtLaunch;keep.Checked=saved.ContinueInTray;
            auto.Checked=saved.AutoMonitor;indefinite.Checked=saved.Indefinite;hourly.Checked=saved.HourlyReports;
            startup.SelectedIndex=(int)saved.Startup;displayedMode=saved.Startup;
            desktopLogs=saved.Startup==StartupMode.Boot?PlatformPaths.DataRoot:(saved.LogDirectory??PlatformPaths.DataRoot);
            desktopReports=saved.Startup==StartupMode.Boot?Path.Combine(PlatformPaths.DataRoot,"Reports"):(saved.ReportDirectory??Path.Combine(PlatformPaths.DataRoot,"Reports"));
            bootLogs=saved.Startup==StartupMode.Boot?(saved.LogDirectory??BootConfiguration.DefaultLogs):BootConfiguration.DefaultLogs;
            bootReports=saved.Startup==StartupMode.Boot?(saved.ReportDirectory??BootConfiguration.DefaultReports):BootConfiguration.DefaultReports;
            SetDestinations();
        }finally{loading=false;}
        QueueValidation();feedback.Text="Unsaved changes discarded. Saved settings are unchanged.";
    }
    void Changed(){if(!loading&&!busy)feedback.Text=Snapshot()==saved?"No unsaved changes":"Unsaved changes";UpdateApply();}
    void QueueValidation(){if(loading)return;validationVersion++;validation=null;validating=true;validationTimer.Stop();validationTimer.Start();Changed();}
    async Task ValidateAsync()
    {
        if(validationRunning)return;
        validationRunning=true;int version=validationVersion;var mode=Mode;string a=logs.Text,b=reports.Text;
        try{
            var (result,usage)=await Task.Run(()=>{
                var check=DestinationRules.Check(mode,a,b);string info="Choose valid local paths to inspect detailed storage.";
                if(check.Valid)try{long bytes=new[]{"history.db","history.db-wal","history.db-shm"}.Sum(name=>{string file=Path.Combine(check.Logs!,name);StoragePaths.LocalDirectory(file);return File.Exists(file)?new FileInfo(file).Length:0;});long free=new DriveInfo(Path.GetPathRoot(check.Logs!)!).AvailableFreeSpace;info=(free<256*1024*1024?"LOW DISK SPACE — ":"")+$"Detailed store: {bytes/1048576d:F2} MiB; available disk: {free/1073741824d:F2} GiB.";}catch(Exception ex){info="Storage usage unavailable: "+ex.Message;}
                return (check,info);
            });
            if(!CanNotify||version!=validationVersion)return;
            validation=result;validating=false;storageInfo.Text=usage;logs.AccessibleDescription=result.LogError??"Local detailed-log folder";reports.AccessibleDescription=result.ReportError??"Separate hourly-report folder";
            logError.Text=result.LogError??"Local path accepted; Apply verifies storage and permissions.";
            reportError.Text=result.ReportError??"Separate local report destination accepted.";
            logError.ForeColor=result.LogError==null?Color.Silver:Color.Salmon;
            reportError.ForeColor=result.ReportError==null?Color.Silver:Color.Salmon;UpdateApply();
        }catch(Exception ex){if(CanNotify&&version==validationVersion){validating=false;Failure(ex);UpdateApply();}}
        finally{validationRunning=false;if(CanNotify&&version!=validationVersion)validationTimer.Start();}
    }
    void UpdateApply(){if(IsDisposed)return;apply.Enabled=!busy&&!validating&&validation?.Valid==true&&Snapshot()!=saved;}
    async Task ApplyAsync(){if(busy)return;busy=true;closeRequested=false;var requested=Snapshot();SetBusy();try{var check=await Task.Run(()=>DestinationRules.Check(requested.Startup,requested.LogDirectory!,requested.ReportDirectory!));if(!check.Valid)throw new IOException(check.LogError??check.ReportError);requested=requested with{LogDirectory=check.Logs,ReportDirectory=check.Reports};await save(requested);saved=requested;if(CanNotify){loading=true;logs.Text=requested.LogDirectory;reports.Text=requested.ReportDirectory;loading=false;feedback.Text="Settings saved. Earlier recordings remain in their original folders.";}}catch(Exception ex){closeRequested=false;Failure(ex);}finally{FinishOperation();}}
    void SetBusy(){content.Enabled=false;navigation.Enabled=false;apply.Enabled=false;discard.Enabled=false;feedback.ForeColor=Color.Gainsboro;feedback.Text="Working… monitoring and recording run independently.";}
    void FinishOperation(){busy=false;if(!CanNotify)return;content.Enabled=true;navigation.Enabled=true;discard.Enabled=true;UpdateApply();if(closeRequested)Close();}
    void Failure(Exception ex){reportFailure(ex);if(CanNotify){feedback.ForeColor=Color.Salmon;feedback.Text="Not completed: "+ex.Message;}}
    static void ApplyTheme(Control parent){foreach(Control child in parent.Controls){child.BackColor=child is TextBox or ComboBox or ListBox?Color.FromArgb(31,35,43):Color.FromArgb(22,25,31);child.ForeColor=Color.Gainsboro;if(child is Button button){button.FlatStyle=FlatStyle.Flat;button.BackColor=Color.FromArgb(43,48,58);}ApplyTheme(child);}}
}
