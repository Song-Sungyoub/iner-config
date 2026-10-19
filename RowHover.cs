using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
namespace InerConfig;
internal sealed class RowHover : Adorner
{
    public RowHover(UIElement target):base(target){IsHitTestVisible=false;}
    protected override void OnRender(DrawingContext dc)
    { dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(18,70,145,235)),null,new Rect(AdornedElement.RenderSize)); }
    private static readonly DependencyProperty Attached=DependencyProperty.RegisterAttached("Attached",typeof(bool),typeof(RowHover),new PropertyMetadata(false));
    public static void Attach(FrameworkElement row)
    {
        if((bool)row.GetValue(Attached))return;row.SetValue(Attached,true);
        RowHover? overlay=null;AdornerLayer? layer=null;
        row.MouseEnter+=(_,_)=> { layer=AdornerLayer.GetAdornerLayer(row);if(layer!=null&&overlay==null){overlay=new(row);layer.Add(overlay);} };
        void Remove(){if(overlay!=null){layer?.Remove(overlay);overlay=null;}}
        row.MouseLeave+=(_,_)=>Remove();row.Unloaded+=(_,_)=>Remove();
    }
}

internal sealed class DropLine : Adorner
{
    private readonly bool after;
    public DropLine(UIElement target,bool after):base(target){this.after=after;IsHitTestVisible=false;}
    protected override void OnRender(DrawingContext dc){double y=after?Math.Max(1,AdornedElement.RenderSize.Height-1):1;dc.DrawLine(new Pen(Brushes.DodgerBlue,2),new Point(0,y),new Point(AdornedElement.RenderSize.Width,y));}
}
