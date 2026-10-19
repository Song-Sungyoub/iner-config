using System.Windows;
using System.Windows.Media;

namespace InerConfig;

public sealed class TreeGuides : FrameworkElement
{
    public double Step { get; set; } = 20;
    public IReadOnlyList<double>? ColumnWidths { get; set; }
    private double Offset(int depth) => ColumnWidths == null ? depth * Step : ColumnWidths.Take(depth).Sum();
    public static readonly DependencyProperty NodeProperty = DependencyProperty.Register(nameof(Node), typeof(ConfigNode), typeof(TreeGuides), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(TreeGuides), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public ConfigNode? Node { get => (ConfigNode?)GetValue(NodeProperty); set => SetValue(NodeProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    protected override void OnRender(DrawingContext context)
    {
        if (Node == null) return;
        var pen = new Pen(Stroke, 1);
        var middle = ActualHeight / 2;
        if (Node.HasVisibleChildren)
            context.DrawLine(pen, new Point(Offset(Node.Depth) + 11, middle + 8), new Point(Offset(Node.Depth) + 11, ActualHeight));
        if (Node.Parent == null) return;
        var x = Offset(Node.Depth - 1) + 11;
        context.DrawLine(pen, new Point(x, 0), new Point(x, Node.Kind is "Comment" or "Add" || !Node.LastVisibleSibling ? ActualHeight : middle));
        if(Node.Kind is not ("Comment" or "Add"))context.DrawLine(pen, new Point(x, middle), new Point(Offset(Node.Depth) + 2, middle));
        for (var ancestor = Node.Parent; ancestor.Parent != null; ancestor = ancestor.Parent)
            if (!ancestor.LastVisibleSibling)
            {
                x = Offset(ancestor.Depth - 1) + 11;
                context.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
            }
    }
}
