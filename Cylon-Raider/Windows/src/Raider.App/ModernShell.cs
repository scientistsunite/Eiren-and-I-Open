using System.Diagnostics;
using Raider.Core;
using Raider.History;
namespace Raider.App;
sealed partial class MainWindow
{
    readonly TrafficLedger ledger=new();readonly SessionApplications sessionApplications=new();readonly Stopwatch connectedClock=new();
    readonly TrafficChart chart=new(),unknownChart=new(){AccessibleName="Observed unattributed traffic over time"};
    readonly Label unknownSummary=AppTheme.Label("Unattributed totals pending");readonly Label sessionSummary=AppTheme.Label("No received measurements"),overview=AppTheme.Label("Collector unavailable — awaiting real observations"),storageSummary=AppTheme.Label("Storage usage pending"),reportSummary=AppTheme.Label("Report status pending"),liveNotice=AppTheme.Label("Double-click an application group to expand its processes. Preview is limited to 1,000 rows; filter or use History for recorded detail.");
    readonly SessionApplicationTable topApps=new();readonly DataGridView reasonGrid=AppTheme.Grid();readonly ListBox recentAlerts=new(){Height=110,Dock=DockStyle.Top,IntegralHeight=false,HorizontalScrollbar=true};
    readonly TextBox healthDetails=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Top,Height=260,BorderStyle=BorderStyle.FixedSingle};
    readonly TextBox filter=new(){Width=300,PlaceholderText="Filter name, PID or executable",AccessibleName="Live traffic filter",AccessibleDescription="Immediately filter current traffic by application name, process ID or executable path. This changes only the displayed rows."};
    readonly CheckBox grouping=new(){Text="Group applications",AccessibleDescription="Group the currently observed processes by application. Double-click a group to expand or collapse its processes.",Checked=true,AutoSize=true};
    ListBox navigation=null!;readonly Panel pageHost=new(){Dock=DockStyle.Fill};readonly List<Panel> sections=[];
    readonly TextBox processDetails=new(){Text="Select a process row to inspect identity. Service lists describe hosts, not proof of which service transferred a byte.",Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Top,Height=110,AccessibleName="Selected process identity and hosted service details"};
    Identity? inspectedProcess,selectedProcess;bool inspectingService;
    Measurement? rendered;long renderedFrames=-1,storageReadAt;bool storageReading;int serviceRequest;
    void BuildShell()
    {
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2};root.ColumnStyles.Add(new(SizeType.Absolute,240));root.ColumnStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.Absolute,120));root.RowStyles.Add(new(SizeType.Percent,100));
        navigation=AppTheme.Navigation(["Dashboard","Live Traffic","Top Applications Observed","Monitoring","History","Measurement Health","Storage","Startup Service","Settings","About & Maintenance"]);
        root.Controls.Add(navigation,0,0);root.SetRowSpan(navigation,2);AppTheme.FitNavigation(root,navigation);
        var headerViewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true};var header=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,Padding=new(20,8,20,4)};header.ColumnStyles.Add(new(SizeType.Percent,100));
        var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true};
        actions.Controls.AddRange([start,stopButton,AppTheme.Button("Exit Cylon Raider",()=>_=ExitApp())]);
        AppTheme.WrapLabels(header);header.Controls.Add(actions,0,0);header.Controls.Add(status,0,1);header.Controls.Add(logging,0,2);
        foreach(var label in new[]{status,logging}){label.Dock=DockStyle.Top;label.Margin=new(0,2,0,2);}
        headerViewport.Controls.Add(header);root.Controls.Add(headerViewport,1,0);root.Controls.Add(pageHost,1,1);Controls.Add(root);
        bool fittingHeader=false;void FitHeader(){if(fittingHeader||headerViewport.ClientSize.Width<80)return;fittingHeader=true;try{float desired=header.GetPreferredSize(new(headerViewport.ClientSize.Width,0)).Height;float height=Math.Min(desired,Math.Max(80,ClientSize.Height*.23f));if(Math.Abs(root.RowStyles[0].Height-height)>.5)root.RowStyles[0].Height=height;}finally{fittingHeader=false;}}
        header.SizeChanged+=(_,_)=>FitHeader();root.SizeChanged+=(_,_)=>FitHeader();
        TableLayoutPanel Page(string title){var viewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Visible=false};var body=AppTheme.Page(title);viewport.Controls.Add(body);pageHost.Controls.Add(viewport);sections.Add(viewport);return body;}
        Panel SpaciousPage(string title,Control list,params Control[] tools){var viewport=new Panel{Dock=DockStyle.Fill,Visible=false,Padding=new(20,8,20,12)};
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};layout.ColumnStyles.Add(new(SizeType.Percent,100));
            layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.Absolute,100));layout.RowStyles.Add(new(SizeType.Percent,100));
            layout.Controls.Add(AppTheme.Label(title,true),0,0);
            var toolViewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true};var toolBody=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1};toolBody.ColumnStyles.Add(new(SizeType.Percent,100));AppTheme.WrapLabels(toolBody);
            foreach(var tool in tools)AppTheme.Add(toolBody,tool);toolViewport.Controls.Add(toolBody);layout.Controls.Add(toolViewport,0,1);
            bool fitting=false;void FitTools(){if(fitting)return;fitting=true;try{float height=Math.Min(toolBody.GetPreferredSize(new(Math.Max(1,toolViewport.ClientSize.Width),0)).Height,Math.Max(48,viewport.ClientSize.Height*.4f));if(Math.Abs(layout.RowStyles[1].Height-height)>.5)layout.RowStyles[1].Height=height;}finally{fitting=false;}}
            viewport.SizeChanged+=(_,_)=>FitTools();toolBody.SizeChanged+=(_,_)=>FitTools();
            list.Dock=DockStyle.Fill;layout.Controls.Add(list,0,2);viewport.Controls.Add(layout);pageHost.Controls.Add(viewport);sections.Add(viewport);return viewport;}
        void SettingsPage(){var viewport=new Panel{Dock=DockStyle.Fill,Visible=false};pageHost.Controls.Add(viewport);sections.Add(viewport);}
        var dashboard=Page("Dashboard");totals.Font=new("Segoe UI",25,FontStyle.Bold);totals.Dock=DockStyle.Top;totals.Margin=new(0,8,0,14);
        AppTheme.Add(dashboard,totals);AppTheme.Add(dashboard,overview);AppTheme.Add(dashboard,sessionSummary);AppTheme.Add(dashboard,chart);
        AppTheme.Add(dashboard,AppTheme.Button("View all applications observed this session",()=>navigation.SelectedIndex=2));
        AppTheme.Add(dashboard,storageSummary);AppTheme.Add(dashboard,AppTheme.Label("Recent integrity evidence"));AppTheme.Add(dashboard,recentAlerts);
        AppTheme.Add(dashboard,AppTheme.Button("Inspect Measurement Health",()=>navigation.SelectedIndex=5));
        AppTheme.Add(dashboard,AppTheme.Label("Alt+Home: Dashboard · Ctrl+1…9: sections · Ctrl+0: About & Maintenance. Select a section with the mouse or arrow keys."));
        var liveControls=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true};sort.AccessibleName="Current traffic sort order";sort.AccessibleDescription="Immediately sort visible live traffic by combined, download or upload rate. Recording is unaffected.";sort.Items.AddRange(["Combined observed rate","Download rate","Upload rate"]);sort.SelectedIndex=0;
        liveControls.Controls.AddRange([filter,sort,grouping]);
        var detailsPanel=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true};detailsPanel.Controls.Add(AppTheme.Button("Refresh selected process details",()=>{inspectedProcess=null;_=InspectProcess(selectedProcess);}));
        SpaciousPage("Live Traffic",table,liveControls,liveNotice,processDetails,detailsPanel);
        filter.TextChanged+=(_,_)=>table.Configure(sort.SelectedIndex,filter.Text,grouping.Checked);grouping.CheckedChanged+=(_,_)=>table.Configure(sort.SelectedIndex,filter.Text,grouping.Checked);sort.SelectedIndexChanged+=(_,_)=>table.Configure(sort.SelectedIndex,filter.Text,grouping.Checked);table.ProcessSelected+=p=>_=InspectProcess(p);
        SpaciousPage("Top Applications Observed",topApps,AppTheme.Label("All applications observed in this viewer session, including applications that are no longer active. Click a column heading to sort current totals. Rows stay in place while values update; scrolling keeps your position."));
        SettingsPage(); // Monitoring: existing operations, embedded in the same window.
        var historyPage=new Panel{Dock=DockStyle.Fill,Visible=false,Padding=new(8)};
        var historyHost=new Panel{Dock=DockStyle.Fill,Name="HistoryHost"};historyPage.Controls.Add(historyHost);pageHost.Controls.Add(historyPage);sections.Add(historyPage);
        var healthPage=Page("Measurement Health");AppTheme.Add(healthPage,healthDetails);AppTheme.Add(healthPage,AppTheme.Label("Unattributed traffic diagnostics",true));reasonGrid.Height=400;reasonGrid.Dock=DockStyle.Top;
        AppTheme.Add(healthPage,unknownSummary);AppTheme.Add(healthPage,unknownChart);AppTheme.Add(healthPage,reasonGrid);
        AppTheme.Add(healthPage,AppTheme.Label("An identified process can have an unknown executable path. Unknown-process bytes are counted once under Unattributed. Lost or unsupported events have unknown byte counts. Late means an event arrived after its event-time bucket closed; partial observations may undercount actual activity. Finalized reports can still have partial coverage."));
        AppTheme.Add(healthPage,AppTheme.Button("Inspect historical integrity and attribution evidence",ShowHistory));
        SettingsPage();SettingsPage();SettingsPage();SettingsPage();
        int previous=0;
        navigation.SelectedIndexChanged+=(_,_)=>{
            if(optionsWindow?.Saving==true){navigation.SelectedIndex=previous;return;}
            int selected=navigation.SelectedIndex;if(selected<0)return;previous=selected;
            for(int i=0;i<sections.Count;i++)sections[i].Visible=i==selected;
            if(selected is 3 or 6 or 7 or 8 or 9)EnsureOptions(selected switch{3=>1,6=>3,7=>4,8=>5,_=>6});
            if(selected==4)OpenHistoryWindow();
            rendered=null;RenderModern();
        };
        navigation.AccessibleDescription="Dashboard, live traffic, all session applications, monitoring, history, measurement health, storage, startup service, settings and maintenance. Arrow keys select sections; Ctrl+1 through Ctrl+9 and Ctrl+0 open sections directly.";
        navigation.SelectedIndex=0;AppTheme.Apply(this);
    }
    protected override bool ProcessCmdKey(ref Message message,Keys keyData)
    {
        if(keyData==(Keys.Alt|Keys.Home)){navigation.SelectedIndex=0;return true;}
        int digit=(int)(keyData&Keys.KeyCode)-(int)Keys.D1;
        if(keyData==(Keys.Control|Keys.D0)){navigation.SelectedIndex=9;return true;}
        if((keyData&Keys.Modifiers)==Keys.Control&&digit>=0&&digit<9){navigation.SelectedIndex=digit;return true;}
        return base.ProcessCmdKey(ref message,keyData);
    }
    void OpenHistoryWindow()
    {
        if(exiting)return;
        var host=sections[4].Controls.Find("HistoryHost",true).Single();
        if(historyWindow==null||historyWindow.IsDisposed||(!string.Equals(historyWindow.FilePath,Path.GetFullPath(HistoryFile),StringComparison.OrdinalIgnoreCase)&&historyWindow.ActiveExport.IsCompleted)){
            historyWindow?.Dispose();historyWindow=new HistoryWindow(HistoryFile,()=>{try{PlatformPaths.OpenExistingFolder(StoragePaths.Reports(preferences,PlatformPaths.DataRoot));}catch(Exception ex){Failure("Report folder unavailable",ex);}}){TopLevel=false,FormBorderStyle=FormBorderStyle.None,Dock=DockStyle.Fill,MinimumSize=Size.Empty,ShowInTaskbar=false};host.Controls.Add(historyWindow);
        }
        historyWindow.UpdateReportStatus(reportSummary.Text);historyWindow.Show();
    }
    async Task InspectProcess(Identity? process)
    {
        if(process!=null&&Equals(process,selectedProcess)&&(inspectingService||Equals(process,inspectedProcess)))return;
        selectedProcess=process;serviceRequest++;
        if(process==null){processDetails.Text="Select an expanded process row. Unknown bytes are never assigned to a service or convenient application.";return;}
        if(inspectingService){processDetails.Text="Latest selection queued for the single local service-information reader…";return;}
        inspectingService=true;
        try{for(;;){
            int request=serviceRequest;var selected=selectedProcess;if(selected==null||IsDisposed)break;
            processDetails.Text=$"PID {selected.Pid} · created {new DateTime(selected.StartUtcTicks,DateTimeKind.Utc):O}\n{selected.Path??"Executable path unavailable; instance identity retained"}\nInspecting local Service Control Manager…";
            try{var services=await Task.Run(()=>ServiceHosts.ForProcess(selected.Pid,selected.StartUtcTicks));if(IsDisposed)break;
                if(request==serviceRequest)processDetails.Text+="\n"+(services.Length==0?"No hosted Windows service was established for this instance.":(services.Length>1?"Shared host: service-level responsibility is ambiguous.\n":"Hosted service association is not proof of responsibility for an individual transfer.\n")+string.Join("\n",services.Take(32).Select(s=>$"{s.Name} — {s.DisplayName}: {s.Description}")));
            }catch(Exception ex){if(!IsDisposed&&request==serviceRequest)processDetails.Text+="\nService information unavailable: "+ex.Message;}
            if(request==serviceRequest){inspectedProcess=selected;break;}
        }}finally{inspectingService=false;}
    }
    void RenderModern()
    {
        if(IsDisposed)return;var data=ledger.Snapshot();var frame=latest;
        long? reportTicks=boot!=null?serviceHealth?.LastReportUtcTicks:reports?.LastGeneratedUtc?.Ticks;
        string lastReport=reportTicks is >0&&reportTicks<=DateTime.MaxValue.Ticks?new DateTime(reportTicks.Value,DateTimeKind.Utc).ToString("O"):"unknown / none recorded by this index";
        reportSummary.Text=(boot!=null?serviceReports:reports?.Status??"Reports unavailable")+"\nLast completed report UTC: "+lastReport+"\nDetailed and report destinations are separate; failures are independent.";
        historyWindow?.UpdateReportStatus(reportSummary.Text);
        if(Stopwatch.GetTimestamp()-storageReadAt>Stopwatch.Frequency*30&&!storageReading)_=ReadStorage();
        if(frame==null){if(navigation.SelectedIndex==2)topApps.UpdateApplications(sessionApplications.Snapshot());overview.Text="Current rates unavailable — no fresh collector measurement. Previous observed session totals remain historical.";totals.Text="↓ unavailable     ↑ unavailable";
            if(navigation.SelectedIndex==5)healthDetails.Text=$"Collector currently unavailable. Previous observations remain historical; missing intervals are not zero or unattributed traffic.\r\nLast complete bucket UTC: {data.LastCompleteUtc?.ToString("O")??"none received"}\r\nPreviously received complete / partial / unavailable buckets: {data.Complete} / {data.Partial} / {data.Unavailable}\r\nPersistence: {(boot!=null?servicePersistence:writer?.Status??"unavailable")}\r\nReports: {reportSummary.Text}";
            return;}
        bool fresh=!ReferenceEquals(frame,rendered)||data.Frames!=renderedFrames;
        if(!fresh)return;rendered=frame;renderedFrames=data.Frames;
        long rx=frame.Rows.Sum(r=>r.Incoming),tx=frame.Rows.Sum(r=>r.Outgoing);double age=(DateTime.UtcNow-frame.StartUtc).TotalSeconds;
        totals.Text=frame.CoverageSeconds<=0?"↓ unavailable     ↑ unavailable":$"↓ {Rate(rx)}     ↑ {Rate(tx)}"+(frame.Complete?"":"  · observed lower bounds");
        status.Text=frame.CoverageSeconds<=0?"Coverage unavailable":frame.Complete?(rx+tx==0?"Confirmed zero observed ETW traffic":"Monitoring · complete under bounded delivery policy"):"Monitoring · partial/uncertain observed bytes";
        overview.Text=$"Measurement {frame.StartUtc.ToLocalTime():HH:mm:ss} · 1-second resolution · age {Math.Max(0,age):F1}s · bounded delivery window {frame.Diagnostics?.LatenessSeconds.ToString()??"unknown"}s";
        long all=data.Incoming+data.Outgoing,unknown=data.UnknownIncoming+data.UnknownOutgoing;
        sessionSummary.Text=$"Viewer-session observed totals: ↓ {data.Incoming:N0} B   ↑ {data.Outgoing:N0} B\nConnected time {connectedClock.Elapsed:hh\\:mm\\:ss} · received one-second buckets {data.Frames:N0} · unknown-process ↓ {data.UnknownIncoming:N0} B / ↑ {data.UnknownOutgoing:N0} B ({(all==0?"ratio n/a — no observed bytes":(unknown*100d/all).ToString("F2")+"% of observed bytes")})";
        if(navigation.SelectedIndex==1)table.UpdateFrame(frame,data);
        if(navigation.SelectedIndex==2)topApps.UpdateApplications(sessionApplications.Snapshot());
        if(navigation.SelectedIndex==0){chart.Update(data.Chart);
        recentAlerts.BeginUpdate();recentAlerts.Items.Clear();recentAlerts.Items.AddRange(data.Alerts.TakeLast(8).Cast<object>().ToArray());recentAlerts.EndUpdate();}
        if(navigation.SelectedIndex!=5)return;
        reasonGrid.DataSource=data.Reasons.OrderByDescending(r=>r.Incoming+r.Outgoing).Select(r=>new{Failure=r.Reason,Protocol=r.Protocol,Attribution=r.IdentifiedProcess?"Identified process; partial details":"Unattributed process",r.Events,r.Incoming,r.Outgoing,FirstEventUTC=new DateTime(r.FirstEventTicks,DateTimeKind.Utc).ToString("O"),LastEventUTC=new DateTime(r.LastEventTicks,DateTimeKind.Utc).ToString("O"),LastProcessingUTC=new DateTime(r.LastReceiptTicks,DateTimeKind.Utc).ToString("O"),Endpoint="not collected",Domain="unknown; no lookup"}).ToArray();
        unknownSummary.Text=$"Observed unattributed ↓ {data.UnknownIncoming:N0} B · ↑ {data.UnknownOutgoing:N0} B · {(all==0?"percentage unavailable — no observed bytes":(unknown*100d/all).ToString("F2")+"% of observed bytes")} · last 120 received buckets below; gaps are not zero.";
        unknownChart.Update(data.Chart.Select(p=>p with{Incoming=p.UnknownIncoming,Outgoing=p.UnknownOutgoing}).ToArray());
        var q=frame.Quality;var d=frame.Diagnostics;
        healthDetails.Text=$"Collector: {status.Text}\r\nLast observed bucket UTC: {frame.StartUtc:O}\r\nLast complete bucket UTC: {data.LastCompleteUtc?.ToString("O")??"none received"}\r\nSource session: {frame.SourceSession??"unknown (legacy)"}\r\nObserved bucket coverage: {frame.CoverageSeconds:F3}s · complete={frame.Complete}\r\nReceived complete / partial / unavailable buckets: {data.Complete} / {data.Partial} / {data.Unavailable}\r\nCurrent collector cumulative late / actual OS lost: {frame.LateEvents} / {frame.LostEvents}\r\nNew signal late / OS lost / malformed / unsupported / capacity: {q?.LateDelta??0} / {q?.OsLostDelta??0} / {q?.MalformedDelta??0} / {q?.UnsupportedDelta??0} / {q?.CapacityDelta??0}\r\nKnown discarded bytes ↓{d?.DiscardedIncoming??0} ↑{d?.DiscardedOutgoing??0}; lost-event byte counts are unknown.\r\nDelivery max {d?.MaximumDeliveryMs:F2}ms · callback max {d?.MaximumCallbackMs:F3}ms · metadata resolution max {d?.MaximumResolutionMs:F3}ms · aggregation lock wait max {d?.MaximumLockWaitMs:F3}ms\r\nDelivery histogram ≤1/10/50/100/250/500/1000/2000/3000/4000/>4000ms: {string.Join(" / ",d?.DeliveryBins??[])}\r\nPersistence: {(boot!=null?servicePersistence:writer?.Status)}\r\nQueue: {(boot!=null?(serviceHealth?.QueuedFrames is {} count?$"{count:N0} queued measurement frames · estimated budget charge {serviceHealth.QueuedBytes:N0} B · omitted frames {serviceHealth.Dropped:N0} · maximum delay {serviceHealth.MaximumQueueDelayMs:F1}ms · writer healthy {serviceHealth.WriterHealthy}":"service queue detail unavailable; see persistence status"):(writer==null?"unavailable":$"{writer.QueuedFrames:N0} queued measurement frames · estimated budget charge {writer.QueuedBytes:N0} B · omitted frames {writer.Dropped:N0} · maximum delay {writer.MaximumQueueDelayMs:F1}ms"))}\r\nReports: {reportSummary.Text}\r\nObserved total = process-attributed + process-unattributed ({all:N0} = {all-unknown:N0} + {unknown:N0} B).\r\nEndpoint/domain history: not enabled. Presentation limits: {(data.PresentationLimited?"Additional processes grouped; use recorded history for detail":"within bounded preview")}.\r\n{frame.Status}";
    }
    async Task ReadStorage()
    {
        storageReading=true;storageReadAt=Stopwatch.GetTimestamp();string file=HistoryFile;
        try{var result=await Task.Run(()=>{long bytes=new[]{file,file+"-wal",file+"-shm"}.Sum(f=>{StoragePaths.LocalDirectory(f);return File.Exists(f)?new FileInfo(f).Length:0;});long free=new DriveInfo(Path.GetPathRoot(file)!).AvailableFreeSpace;return(bytes,free);});if(!IsDisposed)storageSummary.Text=$"Detailed SQLite store {result.bytes/1048576d:F2} MiB · available disk {result.free/1073741824d:F2} GiB";}catch(Exception ex){if(!IsDisposed)storageSummary.Text="Storage information unavailable: "+ex.Message;}finally{storageReading=false;}
    }
}
