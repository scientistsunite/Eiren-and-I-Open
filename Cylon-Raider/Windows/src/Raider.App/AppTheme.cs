using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace Raider.App;

internal static class AppTheme
{
    // Silent Meteor Windows 0.4's ordinary settings palette.
    public static readonly Color Background=Color.FromArgb(25,29,38),Surface=Color.FromArgb(36,43,56),Raised=Surface,Border=Color.FromArgb(83,99,124),Text=Color.FromArgb(231,235,243),Muted=Color.FromArgb(180,191,208),Accent=Color.FromArgb(164,206,255),Download=Color.FromArgb(164,206,255),Upload=Color.FromArgb(188,173,235),Good=Color.FromArgb(112,211,168),Warning=Color.FromArgb(241,193,101);
    internal static readonly Color Hover=Color.FromArgb(51,66,90),Selected=Color.FromArgb(55,87,126);
    static readonly ConditionalWeakTable<Control,ToolTip> tips=new();
    static readonly ConditionalWeakTable<Form,object> themedWindows=new();
    static readonly ConditionalWeakTable<Control,object> themedInputs=new();
    public static Label Label(string text,bool heading=false)=>new(){Text=text,AutoSize=true,Dock=DockStyle.Top,ForeColor=heading?Text:Muted,Font=new("Segoe UI",heading?18:10,heading?FontStyle.Bold:FontStyle.Regular),Margin=new(0,8,0,12),UseMnemonic=false};
    public static Button Button(string text,Action action){var b=new RoundedButton{Text=text,AutoSize=true,Padding=new(12,7,12,7),Margin=new(0,4,8,4)};b.Click+=(_,_)=>action();return b;}
    public static void WrapLabels(Control container)
    {
        bool sizing=false;container.Layout+=(_,_)=>{
            if(sizing)return;int width=container.ClientSize.Width-container.Padding.Horizontal;if(width<80)return;
            sizing=true;try{
                foreach(Control item in container.Controls){
                    if(item is not (System.Windows.Forms.Label or CheckBox or System.Windows.Forms.Button or FlowLayoutPanel))continue;
                    int available=Math.Max(1,width-item.Margin.Horizontal);
                    if(item.MaximumSize.Width!=available)item.MaximumSize=new(available,0);
                }
            }finally{sizing=false;}
        };
    }

    public static TableLayoutPanel Page(string title){var p=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new(24,16,24,24)};p.ColumnStyles.Add(new(SizeType.Percent,100));WrapLabels(p);Add(p,Label(title,true));return p;}
    public static void Add(TableLayoutPanel p,Control c){p.RowCount++;p.RowStyles.Add(new(SizeType.AutoSize));p.Controls.Add(c,0,p.RowCount-1);}
    public static DataGridView Grid()=>new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize,BackgroundColor=Surface,BorderStyle=BorderStyle.None,EnableHeadersVisualStyles=false,ColumnHeadersDefaultCellStyle=new(){BackColor=Raised,ForeColor=Text,Padding=new(6)},DefaultCellStyle=new(){BackColor=Surface,ForeColor=Text,SelectionBackColor=Selected,SelectionForeColor=Text,Padding=new(6)},GridColor=Border,RowTemplate={Height=34}};
    public static ListBox Navigation(string[] items)
    {
        var nav=new ListBox{Dock=DockStyle.Fill,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=48,IntegralHeight=false,BorderStyle=BorderStyle.None,BackColor=Surface,ForeColor=Text,AccessibleName="Application sections"};nav.Items.AddRange(items);
        nav.DrawItem+=(_,e)=>{if(e.Index<0)return;bool contrast=SystemInformation.HighContrast;using var fill=new SolidBrush((e.State&DrawItemState.Selected)!=0?(contrast?SystemColors.Highlight:Selected):(contrast?SystemColors.Window:Surface));e.Graphics.FillRectangle(fill,e.Bounds);string title=nav.Items[e.Index].ToString()??"";var bounds=new Rectangle(e.Bounds.X+12,e.Bounds.Y,e.Bounds.Width-20,e.Bounds.Height);int split=title.IndexOf("  ",StringComparison.Ordinal);if(split>=0){TextRenderer.DrawText(e.Graphics,title[..split],nav.Font,bounds,contrast?SystemColors.WindowText:Accent,TextFormatFlags.VerticalCenter);bounds.X+=TextRenderer.MeasureText(title[..(split+2)],nav.Font).Width;bounds.Width=Math.Max(1,e.Bounds.Right-bounds.X-8);title=title[(split+2)..];}TextRenderer.DrawText(e.Graphics,title,nav.Font,bounds,contrast?SystemColors.WindowText:Text,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);if((e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();};
        var tip=Tips(nav);int previous=-1;nav.MouseMove+=(_,e)=>{int index=nav.IndexFromPoint(e.Location);if(index==previous)return;previous=index;tip.SetToolTip(nav,index<0?null:NavigationHelp(nav.Items[index].ToString()??""));};nav.MouseLeave+=(_,_)=>{previous=-1;tip.Hide(nav);};return nav;
    }
    public static void FitNavigation(TableLayoutPanel root,ListBox nav){bool changing=false;root.Layout+=(_,_)=>{if(changing)return;float needed=Math.Max(240,nav.Items.Cast<object>().Select(item=>TextRenderer.MeasureText(item.ToString(),nav.Font).Width).DefaultIfEmpty(200).Max()+nav.Margin.Horizontal+SystemInformation.VerticalScrollBarWidth+40);if(Math.Abs(root.ColumnStyles[0].Width-needed)<.5)return;changing=true;try{root.ColumnStyles[0].Width=needed;nav.ItemHeight=Math.Max(48,nav.Font.Height*2);}finally{changing=false;}};}
    public static void Apply(Control root)
    {
        if(root is Form form&&themedWindows.TryAdd(form,new object())){WrapLabels(form);form.HandleCreated+=(_,_)=>ThemeTitleBar(form);if(form.IsHandleCreated)ThemeTitleBar(form);}
        bool contrast=SystemInformation.HighContrast;root.BackColor=contrast?SystemColors.Control:Background;root.ForeColor=contrast?SystemColors.ControlText:Text;ApplyChildren(root,Tips(root),contrast);
    }
    static void ApplyChildren(Control root,ToolTip tip,bool contrast)
    {
        foreach(Control c in root.Controls){c.BackColor=contrast?SystemColors.Window:c is TextBox or ComboBox or ListBox or DataGridView or NumericUpDown?Surface:Background;c.ForeColor=contrast?SystemColors.WindowText:Text;
            if(c is Button b){b.FlatStyle=FlatStyle.Flat;b.BackColor=contrast?SystemColors.Control:Raised;b.FlatAppearance.BorderColor=contrast?SystemColors.ControlDark:Border;b.Padding=new(12,7,12,7);b.MinimumSize=new(0,34);b.Invalidate();}
            if(c is ComboBox combo){combo.FlatStyle=contrast?FlatStyle.Standard:FlatStyle.Flat;if(themedInputs.TryAdd(combo,new object())){combo.DrawMode=DrawMode.OwnerDrawFixed;combo.DrawItem+=(_,e)=>{if(e.Index<0)return;bool highContrast=SystemInformation.HighContrast,selected=(e.State&DrawItemState.Selected)!=0;Color background=highContrast?(selected?SystemColors.Highlight:SystemColors.Window):selected?Selected:Surface,ink=highContrast?(selected?SystemColors.HighlightText:SystemColors.WindowText):Text;using var brush=new SolidBrush(background);e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,combo.GetItemText(combo.Items[e.Index]),combo.Font,e.Bounds,ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);if((e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();};}}
            if(c is DateTimePicker date){date.CalendarMonthBackground=contrast?SystemColors.Window:Surface;date.CalendarForeColor=contrast?SystemColors.WindowText:Text;date.CalendarTitleBackColor=contrast?SystemColors.Highlight:Selected;date.CalendarTitleForeColor=contrast?SystemColors.HighlightText:Text;date.CalendarTrailingForeColor=contrast?SystemColors.GrayText:Muted;}
            if(c is DataGridView g){g.EnableHeadersVisualStyles=contrast;g.BackgroundColor=contrast?SystemColors.Window:Surface;g.ColumnHeadersDefaultCellStyle.BackColor=contrast?SystemColors.Control:Raised;g.ColumnHeadersDefaultCellStyle.ForeColor=contrast?SystemColors.ControlText:Text;g.DefaultCellStyle.BackColor=contrast?SystemColors.Window:Surface;g.DefaultCellStyle.ForeColor=contrast?SystemColors.WindowText:Text;g.DefaultCellStyle.SelectionBackColor=contrast?SystemColors.Highlight:Selected;g.DefaultCellStyle.SelectionForeColor=contrast?SystemColors.HighlightText:Text;g.GridColor=contrast?SystemColors.ControlDark:Border;}
            if(c is System.Windows.Forms.Button or CheckBox or ComboBox or NumericUpDown or TextBox or DateTimePicker){string explanation=ControlHelp(c);if(string.IsNullOrWhiteSpace(c.AccessibleName))c.AccessibleName=c.Text.Replace("&","");if(string.IsNullOrWhiteSpace(c.AccessibleDescription))c.AccessibleDescription=explanation;tip.SetToolTip(c,explanation);}
            if(!contrast&&c is TextBox or ListBox or DataGridView or ComboBox or DateTimePicker or NumericUpDown){c.HandleCreated+=(_,_)=>SetWindowTheme(c.Handle,"DarkMode_Explorer",null);if(c.IsHandleCreated)SetWindowTheme(c.Handle,"DarkMode_Explorer",null);}
            ApplyChildren(c,tip,contrast);
        }
    }
    static ToolTip Tips(Control root)=>tips.GetValue(root,c=>{var tip=new ToolTip{InitialDelay=1000,ReshowDelay=1000,AutoPopDelay=20000,ShowAlways=true,BackColor=Surface,ForeColor=Text};c.Disposed+=(_,_)=>tip.Dispose();return tip;});
    static string NavigationHelp(string title)
    {
        string name=title.Contains("  ",StringComparison.Ordinal)?title[(title.IndexOf("  ",StringComparison.Ordinal)+2)..]:title;
        return name.ToLowerInvariant() switch {
            "dashboard"=>"See current observed download and upload rates, session totals, recent measurement alerts and recording status.",
            "live traffic"=>"Inspect current application and process traffic. Filter the list or double-click an application to expand its processes.",
            "top applications observed"=>"See every application observed during this viewer session, including applications no longer transferring data. Scrolling stays where you leave it.",
            "monitoring"=>"Start, pause or restart capture, and choose whether desktop capture begins automatically at launch.",
            "history"=>"Browse recorded hours, process timelines, integrity and attribution evidence; export local CSV files and view hourly reports.",
            "measurement health"=>"Review coverage, late or lost events, unattributed traffic and recording health. Missing measurements are never shown as zero traffic.",
            "storage"=>"Choose separate local destinations for detailed history and hourly CSV reports, inspect disk use, or open their folders.",
            "startup service" or "startup & service"=>"Choose desktop-only capture, launch at sign-in or the optional pre-login Windows service. Boot service configuration requires administrator approval.",
            "settings"=>"Configure activity metadata privacy, retention, automatic hourly reports and window or tray preferences. Apply saves pending changes.",
            "about maintenance" or "about & maintenance"=>"View the application version and privacy information; clear recorded history, retry failed logging or open the normal uninstaller.",
            _=>"Open "+name+". Use arrow keys to move between sections."};
    }
    static string ControlHelp(Control c)
    {
        string text=(c.AccessibleName??c.Text).Replace("&","").Trim();string label=c.Text.Replace("&","").Trim().ToLowerInvariant();
        if(c is TextBox&&c.AccessibleName is {} field&&field.Contains("logs",StringComparison.OrdinalIgnoreCase))return "Local destination for detailed history.db recordings. Apply validates the path and opens that store; earlier files stay in their previous folder.";
        if(c is TextBox&&c.AccessibleName is {} report&&report.Contains("reports",StringComparison.OrdinalIgnoreCase))return "Separate local destination for automatic hourly CSV reports. This must differ from the detailed history folder.";
        return label switch {
            "apply" or "apply settings"=>"Validate and save pending settings across all sections. Changing detailed storage or capture metadata may reconnect monitoring; earlier recordings stay in their original folder.",
            "discard unsaved changes"=>"Restore the last saved settings across all sections, including storage and startup drafts. This does not change saved settings, capture or recorded history.",
            "close"=>"Close this settings view after any active operation finishes.",
            "exit" or "exit cylon raider"=>"Exit Cylon Raider after pending history writes and exports finish. The independent pre-login service continues unless you pause it.",
            "start monitoring"=>"Begin observing traffic with the existing collector. Desktop monitoring may request administrator approval. A new explicit start begins a new viewer session.",
            "pause" or "pause monitoring"=>"Stop capture using the existing monitoring controls. Unobserved intervals remain marked as missing coverage.",
            "restart monitoring"=>"Pause and start the existing collector again. The interruption remains visible in recorded measurement evidence.",
            "browse…"=>"Choose a destination for "+text.Split('—')[0].Trim()+". Browsing alone does not change permissions or save the setting.",
            "use default"=>"Restore the default destination for "+text.Split('—')[0].Trim()+". Select Apply to save the change.",
            "open folder"=>"Open the selected existing local folder. A new folder must first be validated and created by Apply.",
            "clear history / automatic reports…"=>"Ask for confirmation before permanently deleting recorded detailed history and automatic hourly reports. Settings and manually exported CSV files remain.",
            "retry logging after a failure"=>"Retry the existing history writer or restart the configured service after a logging failure. Check recording status afterward.",
            "uninstall cylon raider…"=>"Open the normal Windows uninstaller after confirmation. Existing logs and settings are preserved by default.",
            "refresh"=>"Reload recorded hours and rows for the selected local date from the current history store.",
            "apply filters"=>"Reload recorded attribution diagnostics using the selected local date, UTC time range, failure reason, protocol and raw PID. This does not change collection settings.",
            "open hourly report folder"=>"Open the existing local folder containing automatically generated hourly CSV reports. Configure its destination under Storage.",
            "refresh selected process details"=>"Read local Windows service descriptions for the selected process. Association does not prove which hosted service transferred bytes.",
            "start in the system tray"=>"Use the tray at automatic sign-in launch. Opening Cylon Raider explicitly from the Start menu still shows its application window.",
            "open the main window at launch"=>"Show the application window at automatic launch. Explicit Start-menu launches always open the window.",
            "continue in tray when the main window closes"=>"Keep desktop monitoring running in the tray when this window closes. Use Exit to end the application.",
            "keep history indefinitely"=>"Prevent age-based deletion of recorded history. Turning this off uses the newest 15 recorded calendar days; deleted history cannot be recovered.",
            "generate automatic hourly csv reports"=>"Create CSV reports for finalized recorded hours in the separate report destination. Finalized reports can contain incomplete coverage.",
            _ when label.Contains("raw-pid",StringComparison.Ordinal)=>"Retain bounded raw process-ID attribution details, which can reveal sensitive application activity. Aggregate failure summaries remain available without this option.",
            _ when c is ComboBox=>string.IsNullOrWhiteSpace(c.AccessibleDescription)?"Choose "+(string.IsNullOrWhiteSpace(text)?"an option":text)+". See the adjacent explanation for its effect.":c.AccessibleDescription,
            _ when c is DateTimePicker=>string.IsNullOrWhiteSpace(c.AccessibleDescription)?"Choose "+(string.IsNullOrWhiteSpace(text)?"a recorded date or time range":text)+" to inspect recorded history. This does not change collection settings.":c.AccessibleDescription,
            _ when c is NumericUpDown=>"Choose attribution diagnostic retention in distinct recorded days. Zero follows overall history retention, including indefinite retention.",
            _ when label.Contains("export",StringComparison.Ordinal)=>"Export the selected recorded data to a local CSV file. Choose the destination in the save dialog; capture continues independently.",
            _=>string.IsNullOrWhiteSpace(c.AccessibleDescription)?(string.IsNullOrWhiteSpace(text)?"Edit this field; see the adjacent explanation for its meaning.":text+". See the adjacent explanation for details."):c.AccessibleDescription};
    }
    static void ThemeTitleBar(Form form)
    {
        if(SystemInformation.HighContrast||!form.IsHandleCreated||!form.TopLevel)return;
        int dark=1,caption=ColorTranslator.ToWin32(Surface),ink=ColorTranslator.ToWin32(Text);
        _=DwmSetWindowAttribute(form.Handle,20,ref dark,sizeof(int));_=DwmSetWindowAttribute(form.Handle,35,ref caption,sizeof(int));_=DwmSetWindowAttribute(form.Handle,36,ref ink,sizeof(int));
    }
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
    [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)]static extern int SetWindowTheme(nint window,string? subApp,string? subId);
}
