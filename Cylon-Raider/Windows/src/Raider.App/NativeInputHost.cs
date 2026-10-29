namespace Raider.App;

// Native Win32 inputs can be taller than their WinForms preferred-size result
// after a font/DPI change. Reserve their real height in the outer layout while
// retaining the original control, handlers, accessible identity and tab order.
internal sealed class NativeInputHost : Panel
{
    readonly Control input;
    readonly bool stretch;
    int naturalWidth;
    bool arranging,notifying;

    internal NativeInputHost(Control input)
    {
        this.input=input;naturalWidth=input.Width;
        stretch=input.Dock is DockStyle.Top or DockStyle.Fill;
        Margin=input.Margin;Padding=new(0,2,0,2);TabStop=false;
        AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
        if(stretch)Dock=DockStyle.Top;
        input.Dock=DockStyle.None;input.Anchor=AnchorStyles.Top|AnchorStyles.Left;
        input.Margin=System.Windows.Forms.Padding.Empty;
        Controls.Add(input);
        input.SizeChanged+=NativeChanged;input.FontChanged+=NativeChanged;
        input.HandleCreated+=NativeChanged;
        Size=GetPreferredSize(System.Drawing.Size.Empty);
    }

    int InputHeight()
    {
        int fontHeight=input.Font.Height+Math.Max(8,(int)Math.Ceiling(8*DeviceDpi/96d));
        return Math.Max(Math.Max(input.Height,input.GetPreferredSize(System.Drawing.Size.Empty).Height),fontHeight);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width=naturalWidth+Padding.Horizontal;
        if(MaximumSize.Width>0)width=Math.Min(width,MaximumSize.Width);
        if(Parent is {} parent){int available=parent.ClientSize.Width-parent.Padding.Horizontal-Margin.Horizontal;if(available>=80)width=Math.Min(width,available);}
        return new(Math.Max(1,width),InputHeight()+Padding.Vertical);
    }

    protected override void ScaleControl(SizeF factor,BoundsSpecified specified)
    {
        naturalWidth=Math.Max(1,(int)Math.Round(naturalWidth*factor.Width));
        base.ScaleControl(factor,specified);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);if(arranging||input is null)return;
        arranging=true;
        try{
            int available=Math.Max(1,ClientSize.Width-Padding.Horizontal);
            int width=stretch?available:Math.Min(naturalWidth,available);
            input.SetBounds(Padding.Left,Padding.Top,width,InputHeight());
        }finally{arranging=false;}
    }

    void NativeChanged(object? sender,EventArgs e)
    {
        if(arranging||notifying||IsDisposed||Disposing)return;
        notifying=true;
        try{PerformLayout();Parent?.PerformLayout(this,nameof(PreferredSize));}
        finally{notifying=false;}
    }
}
