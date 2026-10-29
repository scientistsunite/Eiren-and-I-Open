using Raider.Core;
using System.Drawing.Drawing2D;
namespace Raider.App;
sealed class TrafficChart : Control
{
    TrafficPoint[] points=[];
    public TrafficChart(){DoubleBuffered=true;Height=160;Dock=DockStyle.Top;AccessibleName="Observed download and upload chart";BackColor=AppTheme.Surface;}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);Height=Math.Max(160,Font.Height*9);}
    public void Update(TrafficPoint[] values){int begin=0;for(int i=1;i<values.Length;i++)if(values[i].Utc<=values[i-1].Utc)begin=i;points=begin==0?values:values[begin..];Invalidate();}
    internal Rectangle PlotBounds()
    {
        int width=Math.Max(1,Width-24);
        int caption=TextRenderer.MeasureText("↓ download · ↑ upload · observed seconds · gaps unavailable",Font,new Size(width,int.MaxValue),TextFormatFlags.WordBreak).Height;
        int top=caption+16,bottom=Height-Font.Height-20;
        return new Rectangle(12,top,width,Math.Max(1,bottom-top));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;int width=Math.Max(1,Width-24);string legend="↓ download · ↑ upload · observed seconds · gaps unavailable";
        int caption=TextRenderer.MeasureText(g,legend,Font,new Size(width,int.MaxValue),TextFormatFlags.WordBreak).Height;
        var area=PlotBounds();
        using var line=new Pen(AppTheme.Border);g.DrawLine(line,area.Left,area.Bottom,area.Right,area.Bottom);
        TextRenderer.DrawText(g,legend,Font,new Rectangle(12,6,width,caption),AppTheme.Muted,TextFormatFlags.WordBreak);
        if(points.Length==0){TextRenderer.DrawText(g,"Waiting for observed measurements",Font,area,AppTheme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);return;}
        double maximum=Math.Max(1,points.Max(p=>Math.Max(p.Incoming,p.Outgoing)));TextRenderer.DrawText(g,$"Scale max {maximum/1024d:N1} KiB/s · amber = partial",Font,new Rectangle(12,Height-Font.Height-8,width,Font.Height+2),AppTheme.Muted,TextFormatFlags.EndEllipsis);DateTime first=points[0].Utc;double span=Math.Max(119,(points[^1].Utc-first).TotalSeconds);
        void Draw(bool rx,Color color){using var pen=new Pen(color,2);PointF? prior=null;DateTime previous=DateTime.MinValue;foreach(var p in points){if(p.Coverage<=0){prior=null;continue;}float x=area.Left+(float)((p.Utc-first).TotalSeconds/span*area.Width),y=area.Bottom-(float)((rx?p.Incoming:p.Outgoing)/maximum*area.Height);var next=new PointF(x,y);if(prior is {} old&&(p.Utc-previous).TotalSeconds is >0 and <=1.1)g.DrawLine(pen,old,next);if(!p.Complete){using var warning=new SolidBrush(AppTheme.Warning);g.FillEllipse(warning,x-2,y-2,4,4);}prior=next;previous=p.Utc;}}
        Draw(true,AppTheme.Download);Draw(false,AppTheme.Upload);
    }
}
