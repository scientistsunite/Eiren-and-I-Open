using Detector.Core;
namespace Detector.App;
sealed class TrafficTable : UserControl
{
    sealed record Entry(string Key,string Application,string Pid,string Download,string Upload,string TotalDownload,string TotalUpload,string Path,string Confidence,Identity? Identity,bool Group);
    readonly DataGridView grid=AppTheme.Grid();readonly HashSet<string> expanded=[];Entry[] entries=[];Measurement? current;LedgerSnapshot? snapshot;int order;string filter="";bool grouped=true;
    public event Action<Identity?>? ProcessSelected;
    public TrafficTable()
    {
        Dock=DockStyle.Fill;grid.VirtualMode=true;foreach(string title in new[]{"Application / process","PID / start UTC","↓ observed KiB/s","↑ observed KiB/s","Session ↓ bytes","Session ↑ bytes","Executable / details","Attribution"})grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=title,SortMode=DataGridViewColumnSortMode.NotSortable,MinimumWidth=90});grid.Columns[0].FillWeight=180;grid.Columns[6].FillWeight=220;
        grid.CellValueNeeded+=(_,e)=>{if(e.RowIndex>=entries.Length)return;var r=entries[e.RowIndex];e.Value=e.ColumnIndex switch{0=>r.Application,1=>r.Pid,2=>r.Download,3=>r.Upload,4=>r.TotalDownload,5=>r.TotalUpload,6=>r.Path,_=>r.Confidence};};
        grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex<0||e.RowIndex>=entries.Length)return;var row=entries[e.RowIndex];if(row.Group){if(!expanded.Add(row.Key))expanded.Remove(row.Key);Rebuild();}};
        grid.CellFormatting+=(_,e)=>{if(e.RowIndex>=0&&e.RowIndex<entries.Length&&e.ColumnIndex==0&&entries[e.RowIndex].Group&&e.CellStyle!=null){e.CellStyle.ForeColor=SystemInformation.HighContrast?SystemColors.WindowText:AppTheme.Accent;e.CellStyle.SelectionForeColor=SystemInformation.HighContrast?SystemColors.HighlightText:AppTheme.Accent;}};
        grid.SelectionChanged+=(_,_)=>{int index=grid.CurrentCell?.RowIndex??-1;if(index>=0&&index<entries.Length)ProcessSelected?.Invoke(entries[index].Identity);};Controls.Add(grid);
    }
    public void Clear(){current=null;entries=[];grid.RowCount=0;}
    public void Configure(int sort,string text,bool grouping){order=sort;filter=text.Trim();grouped=grouping;Rebuild();}
    public void UpdateFrame(Measurement frame,LedgerSnapshot totals){current=frame;snapshot=totals;Rebuild();}
    void Rebuild()
    {
        if(current==null||snapshot==null)return;string? selected=grid.CurrentCell is {} cell&&cell.RowIndex<entries.Length?entries[cell.RowIndex].Key:null;
        var processTotals=snapshot.Processes.Where(p=>p.SourceSession==current.SourceSession).ToDictionary(p=>(p.Pid,p.CreationTicks));var appTotals=snapshot.Applications.ToDictionary(p=>p.Key);
        var result=new List<Entry>();long Sort(Row r)=>order switch{1=>r.Incoming,2=>r.Outgoing,_=>r.Incoming+r.Outgoing};
        bool Match(Row r)=>filter.Length==0||(r.Process?.Name??"Unattributed").Contains(filter,StringComparison.OrdinalIgnoreCase)||(r.Process?.Path??"").Contains(filter,StringComparison.OrdinalIgnoreCase)||(r.Process?.Pid.ToString()??"").Contains(filter,StringComparison.OrdinalIgnoreCase);
        Entry Process(Row r){var p=r.Process;var total=processTotals.GetValueOrDefault((p?.Pid,p?.StartUtcTicks));string key=$"p:{current.SourceSession}:{p?.Pid}:{p?.StartUtcTicks}";return new(key,(grouped?"    ":"")+(p?.Name??"Unattributed — identity unknown"),p==null?"unknown":$"{p.Pid} / {new DateTime(p.StartUtcTicks,DateTimeKind.Utc):HH:mm:ss}",(r.Incoming/1024d).ToString("N1"),(r.Outgoing/1024d).ToString("N1"),total?.Incoming.ToString("N0")??"—",total?.Outgoing.ToString("N0")??"—",p?.Path??"Path unknown",p==null?"Unattributed":p.Path==null?"Identified instance; path unknown":"Identified instance",p,false);}
        foreach(var group in ApplicationGrouping.Group(current.Rows.Where(Match)).OrderByDescending(g=>g.Processes.Sum(Sort))){
            if(result.Count>=1000)break;
            if(grouped){var member=group.Processes.FirstOrDefault()?.Process;string totalKey=member is {Path:null}?$"unresolved:{current.SourceSession??"legacy"}:{member.Pid}:{member.StartUtcTicks}":group.Key;var total=appTotals.GetValueOrDefault(totalKey);result.Add(new(group.Key,(expanded.Contains(group.Key)?"▾ ":"▸ ")+group.Name,"Application group",(group.Incoming/1024d).ToString("N1"),(group.Outgoing/1024d).ToString("N1"),total?.Incoming.ToString("N0")??"—",total?.Outgoing.ToString("N0")??"—",group.Processes.FirstOrDefault()?.Process?.Path??"Unknown / multiple instances","Double-click for processes",null,true));if(!expanded.Contains(group.Key))continue;}
            foreach(var row in group.Processes.OrderByDescending(Sort)){if(result.Count>=1000)break;result.Add(Process(row));}
        }
        entries=result.ToArray();if(grid.RowCount!=entries.Length)grid.RowCount=entries.Length;
        if(selected!=null){int found=Array.FindIndex(entries,r=>r.Key==selected);if(found>=0&&grid.Columns.Count>0)grid.CurrentCell=grid[0,found];}
        if(grid.DisplayedRowCount(false)>0){int first=Math.Max(0,grid.FirstDisplayedScrollingRowIndex);for(int i=first;i<Math.Min(entries.Length,first+grid.DisplayedRowCount(false)+1);i++)grid.InvalidateRow(i);}
    }
}
