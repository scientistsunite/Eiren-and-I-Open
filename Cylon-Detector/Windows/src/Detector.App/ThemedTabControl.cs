using System.Drawing.Drawing2D;
namespace Detector.App;

// Keep the native tab model, focus and keyboard navigation. Painting the strip
// ourselves also covers the otherwise white native tab frame and empty margins.
internal sealed class ThemedTabControl : TabControl
{
    int hovered=-1;
    public ThemedTabControl()
    {
        Multiline=true;SizeMode=TabSizeMode.Fixed;DrawMode=TabDrawMode.OwnerDrawFixed;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        SetMetrics();
    }
    void SetMetrics(){ItemSize=new Size(Math.Max(176,(int)Math.Ceiling(190*DeviceDpi/96d)),Math.Max(Font.Height*2+14,(int)Math.Ceiling(52*DeviceDpi/96d)));}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);SetMetrics();Invalidate();}
    protected override void OnDpiChangedAfterParent(EventArgs e){base.OnDpiChangedAfterParent(e);SetMetrics();Invalidate();}
    protected override void OnSelectedIndexChanged(EventArgs e){base.OnSelectedIndexChanged(e);Invalidate();}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int next=-1;for(int i=0;i<TabCount;i++)if(GetTabRect(i).Contains(e.Location)){next=i;break;}if(next!=hovered){hovered=next;Invalidate();}}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hovered=-1;Invalidate();}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(SystemInformation.HighContrast?SystemColors.Control:AppTheme.Background);}
    protected override void OnPaint(PaintEventArgs e)
    {
        bool contrast=SystemInformation.HighContrast;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        e.Graphics.Clear(contrast?SystemColors.Control:AppTheme.Background);
        using(var frame=new Pen(contrast?SystemColors.ControlDark:AppTheme.Border))
        {var bounds=DisplayRectangle;bounds.Inflate(1,1);if(bounds.Width>0&&bounds.Height>0)e.Graphics.DrawRectangle(frame,bounds);}
        for(int i=0;i<TabCount;i++)
        {
            var bounds=GetTabRect(i);bounds.Inflate(-2,-2);bool selected=i==SelectedIndex;
            Color background=contrast?(selected?SystemColors.Highlight:SystemColors.Control):selected?AppTheme.Selected:i==hovered?AppTheme.Hover:AppTheme.Surface;
            Color ink=contrast?(selected?SystemColors.HighlightText:SystemColors.ControlText):AppTheme.Text;
            using(var fill=new SolidBrush(background))e.Graphics.FillRectangle(fill,bounds);
            using(var edge=new Pen(contrast?SystemColors.ControlDark:AppTheme.Border))e.Graphics.DrawRectangle(edge,bounds);
            var textBounds=bounds;textBounds.Inflate(-9,-4);
            TextRenderer.DrawText(e.Graphics,TabPages[i].Text,Font,textBounds,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
            if(selected){using var marker=new Pen(contrast?SystemColors.HighlightText:AppTheme.Accent,Math.Max(2,DeviceDpi/48f));e.Graphics.DrawLine(marker,bounds.Left+1,bounds.Bottom-1,bounds.Right-1,bounds.Bottom-1);}
            if(selected&&Focused&&ShowFocusCues){var focus=bounds;focus.Inflate(-4,-4);ControlPaint.DrawFocusRectangle(e.Graphics,focus,ink,background);}
        }
    }
}
