using Raider.History;
using Raider.Core;
namespace Raider.App;
sealed class HistoryWindow : Form
{
    sealed record Choice(HourRow Hour){public override string ToString()=>new DateTimeOffset(new DateTime(Hour.UtcTicks,DateTimeKind.Utc)).ToOffset(TimeSpan.FromMinutes(Hour.OffsetMinutes)).ToString("HH:mm zzz")+(Hour.Finalized?" — finalized aggregate (quality separate)":" — open aggregate");}
    readonly HistoryReader reader;
    public string FilePath {get;}
    static bool applicationClosing;static readonly SemaphoreSlim exportGate=new(1,1);readonly List<Button> exportActions=[];
    internal Task ActiveExport {get;private set;}=Task.CompletedTask;
    int hourRequest,rowRequest,diagnosticRequest,rangeDay=-1;bool settingRange,rangeEdited;
    bool Alive=>!IsDisposed&&!Disposing&&IsHandleCreated;
    readonly DateTimePicker date=new(){Name="RecordedDate",AccessibleName="Recorded local calendar date",Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd",Width=130};
    readonly ComboBox hour=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=240};
    readonly ComboBox sort=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=140};
    readonly Label summary=AppTheme.Label("");
    readonly DataGridView applications=Grid(),seconds=Grid(),events=Grid(),attribution=Grid(),integrity=Grid(),sessions=Grid();
    readonly ComboBox reason=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=170},protocol=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=130};
    readonly TextBox pid=new(){Width=95,PlaceholderText="Raw PID (all)",AccessibleName="Diagnostic raw PID filter"};
    readonly DateTimePicker fromTime=new(){Name="DiagnosticFromUtc",AccessibleName="Diagnostic start UTC date and time",Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd HH:mm",ShowUpDown=true,Width=180,Value=DateTime.Today},toTime=new(){Name="DiagnosticToUtc",AccessibleName="Diagnostic end UTC date and time",Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd HH:mm",ShowUpDown=true,Width=180,Value=DateTime.Today.AddHours(23).AddMinutes(59)};
    readonly Label diagnosticNotice=AppTheme.Label("Diagnostics are aggregate reason records, separate from bucket completeness. Legacy recordings may have no diagnostic metadata.");
    readonly Label notice=AppTheme.Label("Outside recorded coverage is unobserved. Preview is limited to 10,000 rows; export streams the selected hour.");
    public HistoryWindow(string file)
    {
        FilePath=Path.GetFullPath(file);reader=new(FilePath);Text="Cylon Raider — History";Width=1280;Height=820;MinimumSize=new(760,520);AutoScaleMode=AutoScaleMode.Dpi;Font=new("Segoe UI",10);BackColor=Color.FromArgb(22,25,31);ForeColor=Color.Gainsboro;Icon=PlatformPaths.OfficialIcon();
        var controls=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Padding=new(12)};var refresh=new RoundedButton{Text="Refresh",AutoSize=true};var export=new RoundedButton{Text="Export seconds CSV",AutoSize=true};var reports=new RoundedButton{Text="Export hourly CSV",AutoSize=true};
        exportActions.AddRange([export,reports]);
        sort.Items.AddRange(["Combined","Downloads","Uploads"]);sort.SelectedIndex=0;controls.Controls.AddRange([date,hour,sort,refresh,export,reports]);
        var tabs=new TabControl{Dock=DockStyle.Fill};foreach(var entry in new[]{("Applications",applications),("One-second details",seconds),("Coverage / lifecycle events",events)}){var tab=new TabPage(entry.Item1);tab.Controls.Add(entry.Item2);tabs.TabPages.Add(tab);}
        var diagnosticTab=new TabPage("Unattributed diagnostics");
        var filters=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Padding=new(8)};AppTheme.WrapLabels(filters);
        reason.Items.Add("All reasons");reason.Items.AddRange(Enum.GetNames<AttributionReason>());reason.SelectedIndex=0;
        protocol.Items.Add("All protocols");protocol.Items.AddRange(Enum.GetNames<TrafficProtocol>());protocol.SelectedIndex=0;
        var diagnosticExport=AppTheme.Button("Export diagnostic CSV",()=>RequestExport(()=>ExportDiagnostics(false)));var integrityExport=AppTheme.Button("Export integrity CSV",()=>RequestExport(()=>ExportDiagnostics(true)));exportActions.AddRange([diagnosticExport,integrityExport]);
        filters.Controls.AddRange([AppTheme.Label("Recorded local date above · UTC range below (inclusive minutes)"),fromTime,toTime,reason,protocol,pid,AppTheme.Button("Apply filters",()=>_=RefreshDiagnostics()),diagnosticExport,integrityExport]);
        diagnosticTab.Controls.Add(attribution);diagnosticTab.Controls.Add(diagnosticNotice);diagnosticNotice.Dock=DockStyle.Bottom;diagnosticTab.Controls.Add(filters);tabs.TabPages.Add(diagnosticTab);
        foreach(var entry in new[]{("Integrity evidence",integrity),("Recording sessions / provenance",sessions)}){var tab=new TabPage(entry.Item1);tab.Controls.Add(entry.Item2);tabs.TabPages.Add(tab);}
        var source=AppTheme.Label("Recorded history: "+FilePath);source.AccessibleName="History source file";
        notice.Dock=DockStyle.Bottom;Controls.Add(tabs);Controls.Add(summary);Controls.Add(controls);Controls.Add(notice);Controls.Add(source);
        void FitText(){int width=Math.Max(1,ClientSize.Width-24);summary.MaximumSize=new(width,0);notice.MaximumSize=new(width,0);diagnosticNotice.MaximumSize=new(width,0);source.MaximumSize=new(width,0);}
        SizeChanged+=(_,_)=>FitText();FitText();
        FormClosing+=(_,e)=>{if(!ActiveExport.IsCompleted){e.Cancel=true;notice.Text="A CSV export is still running. Close again after it finishes; its destination is preserved until completion.";}};
        refresh.Click+=async(_,_)=>await RefreshHours();date.ValueChanged+=async(_,_)=>await RefreshHours();hour.SelectedIndexChanged+=async(_,_)=>await RefreshRows();sort.SelectedIndexChanged+=async(_,_)=>await RefreshRows();
        export.Click+=(_,_)=>RequestExport(()=>Export(false));reports.Click+=(_,_)=>RequestExport(()=>Export(true));Shown+=async(_,_)=>await RefreshHours();fromTime.ValueChanged+=(_,_)=>{if(!settingRange)rangeEdited=true;};toTime.ValueChanged+=(_,_)=>{if(!settingRange)rangeEdited=true;};AppTheme.Apply(this);
    }
    internal static void ApplicationClosing(bool value)
    {
        applicationClosing=value;foreach(var window in Application.OpenForms.OfType<HistoryWindow>())foreach(var button in window.exportActions)button.Enabled=!value&&window.ActiveExport.IsCompleted;
    }
    internal void RequestExport(Func<Task> operation)
    {
        if(applicationClosing){notice.Text="Application is closing; no new CSV export was started.";return;}
        if(!exportGate.Wait(0)){notice.Text="A manual CSV export is already running. Wait for it to finish; collection and automatic reports continue independently.";return;}
        foreach(var button in exportActions)button.Enabled=false;ActiveExport=RunExport(operation);
    }
    async Task RunExport(Func<Task> operation)
    {
        try{await operation();}catch(Exception ex){if(Alive)notice.Text="CSV export failed: "+ex.Message;}finally{exportGate.Release();if(Alive&&!applicationClosing)foreach(var button in exportActions)button.Enabled=true;}
    }
    static DataGridView Grid(){var grid=AppTheme.Grid();grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.DisplayedCells;return grid;}
    int Day=>date.Value.Year*10000+date.Value.Month*100+date.Value.Day;
    async Task RefreshHours()
    {
        int request=++hourRequest;rowRequest++;
        try {int selected=Day;var h=await Task.Run(()=>reader.Hours(selected));if(!Alive||request!=hourRequest)return;SetDiagnosticRange(selected,h);hour.Items.Clear();hour.Items.AddRange(h.Select(x=>(object)new Choice(x)).ToArray());if(h.Length>0)hour.SelectedIndex=0;else{applications.DataSource=null;seconds.DataSource=null;summary.Text="No recorded measurements for this date; absence is not zero traffic.";}var eventRows=await Task.Run(()=>reader.Events(selected).Select(x=>new{UTC=new DateTime(x.UtcTicks,DateTimeKind.Utc).ToString("O"),x.Epoch,x.State,x.Reason}).ToArray());if(Alive&&request==hourRequest)events.DataSource=eventRows;}
        catch(Exception ex){if(Alive&&request==hourRequest)summary.Text="History unavailable: "+ex.Message;}
        await RefreshDiagnostics();
    }
    void SetDiagnosticRange(int day,HourRow[] hours)
    {
        if(day==rangeDay&&rangeEdited)return;settingRange=true;
        try{var bounds=DiagnosticRange.ForDay(day,hours);fromTime.Value=bounds.FromUtc;toTime.Value=bounds.ToUtc.AddMinutes(-1);rangeDay=day;rangeEdited=false;}finally{settingRange=false;}
    }
    async Task RefreshRows()
    {
        if(hour.SelectedItem is not Choice choice)return;var h=choice.Hour;int order=sort.SelectedIndex;int request=++rowRequest;
        try {
            var appRows=await Task.Run(()=>reader.Applications(h.Id).OrderByDescending(x=>order==1?x.Incoming:order==2?x.Outgoing:x.Incoming+x.Outgoing).ToArray());
            var secondRows=await Task.Run(()=>reader.Timeline(h.UtcTicks,h.UtcTicks+TimeSpan.TicksPerHour,10000,h.Id).Select(x=>new{UTC=new DateTime(x.UtcTicks,DateTimeKind.Utc).ToString("O"),x.OffsetMinutes,x.Zone,x.Epoch,x.ProcessInstance,x.Application,x.Path,x.Pid,x.CreationTicks,x.Incoming,x.Outgoing,x.State,x.CoverageSeconds}).ToArray());
            if(!Alive||request!=rowRequest)return;applications.DataSource=appRows;seconds.DataSource=secondRows;
            summary.Text=h.CoverageSeconds<=0?"No measured coverage: download/upload unavailable; diagnostic/lifecycle events remain recorded.":$"Observed download {h.Incoming:N0} B; upload {h.Outgoing:N0} B. Coverage {h.CoverageSeconds:F3} s; incomplete buckets {h.Incomplete}. Peaks ↓{h.PeakIncoming:N0} B/s ↑{h.PeakOutgoing:N0} B/s. "+(h.Ambiguous?"CLOCK OVERLAP — coverage intervals may overlap.":"Unobserved parts of this clock hour are not zero.");
        }catch(Exception ex){if(Alive&&request==rowRequest)summary.Text="History query failed: "+ex.Message;}
    }
    (int Day,int Reason,int Protocol,int Pid,long From,long To) DiagnosticSelection()
    {
        int raw=-1;if(pid.Text.Trim().Length>0&&(!int.TryParse(pid.Text,out raw)||raw<=0))throw new ArgumentException("Raw PID must be a positive integer or empty for all.");
        long first=DateTime.SpecifyKind(fromTime.Value,DateTimeKind.Utc).Ticks/TimeSpan.TicksPerMinute*TimeSpan.TicksPerMinute;
        long last=DateTime.SpecifyKind(toTime.Value,DateTimeKind.Utc).Ticks/TimeSpan.TicksPerMinute*TimeSpan.TicksPerMinute+TimeSpan.TicksPerMinute;
        if(last<=first)throw new ArgumentException("End UTC minute must follow the start minute. The recorded local date remains a separate filter.");
        return(Day,reason.SelectedIndex-1,protocol.SelectedIndex-1,raw,first,last);
    }
    async Task RefreshDiagnostics()
    {
        int request=++diagnosticRequest;
        try{var selection=DiagnosticSelection();
            var rows=await Task.Run(()=>reader.Attribution(selection.Day,selection.Reason,selection.Protocol,selection.Pid,selection.From,selection.To));
            var quality=await Task.Run(()=>reader.Integrity(selection.Day));var recordings=await Task.Run(()=>reader.Sessions());
            if(!Alive||request!=diagnosticRequest)return;
            attribution.DataSource=rows.Select(r=>new{UTCMinute=new DateTime(r.MinuteTicks,DateTimeKind.Utc).ToString("O"),r.Epoch,r.Reason,r.Protocol,r.RawPid,Identity=r.IdentifiedProcess?"Instance identified; partial metadata":"Unattributed (raw PID is not instance identity)",r.Events,r.Incoming,r.Outgoing,FirstEventUTC=new DateTime(r.FirstEventTicks,DateTimeKind.Utc).ToString("O"),LastReceiptUTC=new DateTime(r.LastReceiptTicks,DateTimeKind.Utc).ToString("O"),Endpoint="not retained"}).ToArray();
            integrity.DataSource=quality;sessions.DataSource=recordings;
            diagnosticNotice.Text=$"{rows.Length:N0} reason rows shown (preview cap 10,000; full filtered export streams pages). Integrity is separate; session preview includes latest 1,000 epochs. Unknown historical metadata cannot be reconstructed. Raw PID detail may be disabled or expired.";
        }catch(Exception ex){if(Alive&&request==diagnosticRequest)diagnosticNotice.Text="Diagnostics unavailable: "+ex.Message;}
    }
    async Task ExportDiagnostics(bool quality)
    {
        try{var selection=DiagnosticSelection();using var dialog=new SaveFileDialog{Filter="CSV files|*.csv",FileName=$"Raider-{(quality?"integrity":"attribution")}-{selection.Day}.csv"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            PlatformPaths.RequireLocal(dialog.FileName);string destination=dialog.FileName;
            await Task.Run(()=>{if(quality)reader.ExportIntegrity(destination,selection.Day);else reader.ExportAttribution(destination,selection.Day,selection.Reason,selection.Protocol,selection.Pid,selection.From,selection.To);});
            if(Alive)diagnosticNotice.Text=quality?"Whole selected date integrity exported; includes loss/recovery evidence independently of attribution filters.":"Filtered attribution aggregates exported. Compare with timeline/integrity exports for coverage and unknown losses; missing diagnostic metadata is not zero.";
        }catch(Exception ex){if(Alive)diagnosticNotice.Text="Export failed: "+ex.Message;}
    }
    async Task Export(bool reports)
    {
        if(!reports&&hour.SelectedItem is not Choice)return;
        using var dialog=new SaveFileDialog{Filter="CSV files|*.csv",FileName=reports?$"Raider-hours-{Day}.csv":$"Raider-seconds-{Day}.csv"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try {PlatformPaths.RequireLocal(dialog.FileName);int day=Day;var h=(hour.SelectedItem as Choice)?.Hour;await Task.Run(()=>{if(reports)reader.ExportHours(dialog.FileName,day);else reader.ExportCsv(dialog.FileName,h!.UtcTicks,h.UtcTicks+TimeSpan.TicksPerHour,h.Id);});if(Alive)notice.Text="CSV exported locally: "+dialog.FileName;}catch(Exception ex){if(Alive)notice.Text="Export failed: "+ex.Message;}
    }
}
