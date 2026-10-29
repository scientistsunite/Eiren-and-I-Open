using System.Diagnostics;
using Detector.Core;
using Detector.History;

namespace Detector.App;

sealed partial class MainWindow
{
    readonly Label sensorObservation=AppTheme.Label("Microphone/camera observation unavailable — awaiting Windows usage metadata. Unavailable evidence does not establish that devices are unused.");
    readonly Label awarenessSummary=AppTheme.Label("Awareness observations are loading. Unfamiliar processes and unusual network changes are review indicators, not threat determinations.");
    readonly Label networkPolicy=AppTheme.Label("Five-minute network memory window pending; monitoring must be running to observe activity.");
    readonly DataGridView observedDevices=AppTheme.Grid(),activityGrid=AppTheme.Grid();
    readonly Label activityNotice=AppTheme.Label("Latest 500 device, context and anomaly observations from the unified main log. Unfamiliar activity is an observation for review, not an automatic threat verdict.");
    readonly ComboBox activityViewSelector=new(){Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName="Activity history view",AccessibleDescription="Choose the unified device/anomaly timeline or detailed recorded network history and CSV exports. Both read the same main log."};
    bool activityReading;long activityReadAt;string renderedSensors="";

    void BuildActivityHistory(Panel historyPage)
    {
        ConfigurePrivacyGrid(observedDevices);ConfigurePrivacyGrid(activityGrid);
        activityGrid.CellFormatting+=(_,e)=>{if(!SystemInformation.HighContrast&&e.RowIndex>=0&&activityGrid.Columns.Contains("Category")&&activityGrid.Rows[e.RowIndex].Cells["Category"].Value?.ToString()?.Contains("Anomaly",StringComparison.OrdinalIgnoreCase)==true)e.CellStyle.ForeColor=AppTheme.Warning;};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
        layout.ColumnStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.Percent,100));
        activityViewSelector.Items.AddRange(["Device, anomaly and network-context timeline","Recorded network details and CSV export"]);
        var tools=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true};
        tools.Controls.Add(AppTheme.Button("Refresh activity timeline",()=>_=ReadActivity(true)));
        var timeline=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};timeline.ColumnStyles.Add(new(SizeType.Percent,100));timeline.RowStyles.Add(new(SizeType.AutoSize));timeline.RowStyles.Add(new(SizeType.AutoSize));timeline.RowStyles.Add(new(SizeType.Percent,100));AppTheme.WrapLabels(timeline);
        timeline.Controls.Add(activityNotice,0,0);timeline.Controls.Add(tools,0,1);activityGrid.Dock=DockStyle.Fill;timeline.Controls.Add(activityGrid,0,2);
        var views=new Panel{Dock=DockStyle.Fill};
        var historyHost=new Panel{Dock=DockStyle.Fill,Name="HistoryHost",Visible=false};
        views.Controls.Add(historyHost);views.Controls.Add(timeline);
        layout.Controls.Add(AppTheme.Input(activityViewSelector),0,0);layout.Controls.Add(views,0,1);historyPage.Controls.Add(layout);
        activityViewSelector.SelectedIndexChanged+=(_,_)=>{bool network=activityViewSelector.SelectedIndex==1;timeline.Visible=!network;historyHost.Visible=network;if(network)OpenHistoryWindow();else _=ReadActivity(true);};
        activityViewSelector.SelectedIndex=0;
    }

    void RenderPrivacy(Measurement? frame)
    {
        networkPolicy.Text=(preferences.ContinuousNetwork?"CONTINUOUS NETWORK LOGGING: every observed second is saved. ":"DEVICE-TRIGGERED NETWORK LOGGING: five minutes before activity changes through five minutes after the last active device second. ")+
            "A rolling five-minute network context is maintained in RAM while monitoring is running; coverage or resource gaps are explicit. "+(boot==null?writer?.PrivacyStatus??"Writer unavailable; check recording status.":"The independent boot service owns the main log and memory window.");
        if(frame?.Sensors is not {} snapshot)
        {
            sensorObservation.Text="Microphone/camera observation unavailable — no current collector metadata. Previous evidence is available in Activity History; this does not establish that devices are unused.";observedDevices.DataSource=null;renderedSensors="";
        }
        else
        {
            string observation=snapshot.Available?"WINDOWS DEVICE EVIDENCE":"DEVICE EVIDENCE UNAVAILABLE / LIMITED";
            int active=snapshot.Uses.Count(use=>use.StoppedUtcTicks is null or <=0);
            sensorObservation.Text=$"{observation} · observed {snapshot.ObservedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}. "+
                (snapshot.Available?(active>0?$"{active} application/device usage record(s) currently reported active. ":"No active use is reported in this evidence. "):"")+snapshot.Status+
                " Windows does not expose every device access path; no evidence is not a guarantee that a device is unused. Detector collects no audio, images or video.";
            string signature=snapshot.Available+"|"+string.Join("\n",snapshot.Uses.Select(use=>$"{use.Kind}|{use.Application}|{use.Path}|{use.StartedUtcTicks}|{use.StoppedUtcTicks}|{use.Evidence}|{use.Scope}"));
            if(signature!=renderedSensors)
            {
                renderedSensors=signature;
                observedDevices.DataSource=snapshot.Uses.OrderByDescending(use=>use.StoppedUtcTicks is null or <=0).ThenByDescending(use=>use.StartedUtcTicks).Select(use=>new{
                    Device=use.Kind,Application=use.Application,State=use.StoppedUtcTicks is null or <=0?(snapshot.Available?"Windows reports active":"Prior active evidence; current state unknown"):"Windows reports ended",StartLocal=SensorTime(use.StartedUtcTicks),EndLocal=use.StoppedUtcTicks is {} stop?SensorTime(stop):"No stop reported",Executable=use.Path??"Not exposed",use.Evidence,use.Scope
                }).ToArray();
            }
        }
        if(navigation.SelectedIndex==0||navigation.SelectedIndex==4&&activityViewSelector.SelectedIndex==0)_=ReadActivity();
    }
    static void ConfigurePrivacyGrid(DataGridView grid)
    {
        grid.DataBindingComplete+=(_,_)=>{
            foreach(DataGridViewColumn column in grid.Columns){column.MinimumWidth=column.Name switch{"Detail"=>520,"Evidence" or "Executable"=>260,"Application" or "Change"=>200,_=>145};column.HeaderText=column.Name switch{"StartLocal"=>"Reported start (local)","EndLocal"=>"Reported end (local)","ObservedLocal"=>"Observed (local)","ActivityLocal"=>"Activity evidence (local)",_=>column.HeaderText};}
        };
    }
    static string SensorTime(long ticks)=>ticks>0&&ticks<=DateTime.MaxValue.Ticks?new DateTime(ticks,DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"):"Unknown";

    async Task ReadActivity(bool force=false)
    {
        if(activityReading||IsDisposed||Disposing||!force&&Stopwatch.GetTimestamp()-activityReadAt<Stopwatch.Frequency*5)return;
        activityReading=true;activityReadAt=Stopwatch.GetTimestamp();string file=HistoryFile;
        try
        {
            var rows=await Task.Run(()=>new DetectorActivityReader(file).Recent(500));
            if(IsDisposed||Disposing)return;
            var indicators=rows.Where(row=>row.Category.Contains("Anomaly",StringComparison.OrdinalIgnoreCase)).ToArray();
            awarenessSummary.Text=indicators.Length==0?"No anomaly observations in the latest retained activity preview. This is not a threat assessment or a guarantee of normal behavior.":$"{indicators.Length} review indicator(s) in the latest retained activity preview. "+string.Join(" · ",indicators.Take(3).Select(row=>$"{row.ObservedUtc.ToLocalTime():HH:mm:ss} {row.Application}: {row.Change}"))+". Unfamiliar or unusual does not imply a threat.";
            awarenessSummary.ForeColor=!SystemInformation.HighContrast&&indicators.Length>0?AppTheme.Warning:SystemInformation.HighContrast?SystemColors.ControlText:AppTheme.Muted;
            int first=activityGrid.FirstDisplayedScrollingRowIndex;
            activityGrid.DataSource=rows.Select(row=>new{
                ObservedLocal=row.ObservedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),ActivityLocal=row.ActivityUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")??"Not established",row.Category,row.Application,row.Change,row.Detail
            }).ToArray();
            if(first>=0&&first<activityGrid.RowCount)try{activityGrid.FirstDisplayedScrollingRowIndex=first;}catch(InvalidOperationException){}
            activityNotice.Text=$"Latest {rows.Length} retained device, context and anomaly observations. The activity time records Windows evidence; observation time records when Detector saw it. Coverage gaps and unfamiliar behavior are observations for review, not threat verdicts. Main log: {file}";
        }
        catch(Exception ex){if(!IsDisposed&&!Disposing){awarenessSummary.Text="Awareness history is unavailable; no normality or threat conclusion is inferred. Check recording health.";activityNotice.Text="Activity history is unavailable: "+ex.Message+". No activity is inferred from missing data.";}}
        finally{activityReading=false;}
    }
}
