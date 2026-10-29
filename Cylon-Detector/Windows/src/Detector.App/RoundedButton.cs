using System.Drawing.Drawing2D;
namespace Detector.App;

// Preserve native button activation, mnemonics and accessibility semantics.
internal sealed class RoundedButton : Button
{
    bool hovered,pressed;
    public RoundedButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;BackColor=AppTheme.Surface;ForeColor=AppTheme.Text;Padding=new(12,7,12,7);MinimumSize=new(0,34);SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
    protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hovered=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left)pressed=true;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){pressed=false;Invalidate();base.OnLostFocus(e);}
    protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space)pressed=true;Invalidate();base.OnKeyDown(e);}
    protected override void OnKeyUp(KeyEventArgs e){pressed=false;Invalidate();base.OnKeyUp(e);}
    protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
    protected override void OnPaint(PaintEventArgs e)
    {
        bool contrast=SystemInformation.HighContrast;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent?.BackColor??AppTheme.Background);
        float scale=DeviceDpi/96f;var rect=new RectangleF(scale/2,scale/2,Math.Max(1,Width-scale),Math.Max(1,Height-scale));using var shape=Shape(rect,5*scale);
        Color background=contrast?(pressed?SystemColors.Highlight:SystemColors.Control):pressed?AppTheme.Selected:hovered?AppTheme.Hover:BackColor;
        Color edge=contrast?(Focused?SystemColors.Highlight:SystemColors.ControlDark):Focused?AppTheme.Accent:AppTheme.Border;
        using(var fill=new SolidBrush(background))e.Graphics.FillPath(fill,shape);using(var pen=new Pen(edge,(Focused?2:1)*scale))e.Graphics.DrawPath(pen,shape);
        Color foreground=Enabled?(contrast?SystemColors.ControlText:ForeColor):(contrast?SystemColors.GrayText:AppTheme.Muted);
        var flags=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak;if(!ShowKeyboardCues)flags|=TextFormatFlags.HidePrefix;
        var bounds=Rectangle.FromLTRB(Padding.Left,Padding.Top,Math.Max(Padding.Left+1,Width-Padding.Right),Math.Max(Padding.Top+1,Height-Padding.Bottom));TextRenderer.DrawText(e.Graphics,Text,Font,bounds,foreground,flags);
    }
    static GraphicsPath Shape(RectangleF rect,float radius)
    {
        var path=new GraphicsPath();float diameter=Math.Min(radius*2,Math.Min(rect.Width,rect.Height));path.AddArc(rect.X,rect.Y,diameter,diameter,180,90);path.AddArc(rect.Right-diameter,rect.Y,diameter,diameter,270,90);path.AddArc(rect.Right-diameter,rect.Bottom-diameter,diameter,diameter,0,90);path.AddArc(rect.X,rect.Bottom-diameter,diameter,diameter,90,90);path.CloseFigure();return path;
    }
}
