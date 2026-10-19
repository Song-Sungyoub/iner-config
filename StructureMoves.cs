using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace InerConfig;
public sealed partial class ConfigDocument
{
    public string MoveBefore(ConfigNode node,ConfigNode target,bool after=false)
    {
        if(node==target)return Text;
        if(node.Parent!=target.Parent||node.ReadOnly||target.ReadOnly||(node.Parent==null&&Format!="CSV"))throw new InvalidOperationException("같은 부모 안에서 이동하세요.");
        if(Format=="YAML")throw new InvalidOperationException("YAML 이동은 주석 보존 검증 전까지 원문에서 수행하세요.");
        if(Format=="XML"&&(node.Kind=="Attribute"||target.Kind=="Attribute"||node.Parent!.Children.Any(n=>n.Kind is "Text" or "CDATA")))
            throw new InvalidOperationException("속성·혼합 내용은 원문에서 이동하세요.");
        var nodes=(node.Parent?.Children??Roots).ToList();
        var reordered=nodes.ToList();reordered.Remove(node);reordered.Insert(reordered.IndexOf(target)+(after?1:0),node);
        int Start(ConfigNode n)=>Format=="INI"?LineStart(n.KeyStart):n.KeyStart>=0&&Format=="JSON"?n.KeyStart:n.Start;
        int End(ConfigNode n)=>Format=="CSV"?n.Start+Text[n.Start..n.End].TrimEnd('\r','\n').Length:Format=="INI"?LineEnd(n.End):n.End;
        var result=new System.Text.StringBuilder(Text[..Start(nodes[0])]);
        for(int i=0;i<nodes.Count;i++)
        {
            var n=reordered[i];result.Append(Text[Start(n)..End(n)]);
            if(i+1<nodes.Count)result.Append(Text[End(nodes[i])..Start(nodes[i+1])]);
        }
        result.Append(Text[End(nodes[^1])..]);return Checked(result.ToString());
    }
}
public partial class MainWindow
{
    private void CsvAddRowClick(object sender,RoutedEventArgs e){if(Session?.Format=="INI")AddSection();else AddCsvRow();}
    private async void AddCsvRow()
    {
        if(document==null||!RequireStructure())return;
        try
        {
            int count=Math.Max(1,document.Roots.Select(n=>n.Children.Count).DefaultIfEmpty(1).Max());
            string nl=Session!.Text.Contains("\r\n")?"\r\n":"\n";
            await Apply(document.Checked(document.Text+(document.Text.Length>0&&!document.Text.EndsWith('\n')?nl:"")+string.Join(document.Separator,Enumerable.Repeat("",count))+nl));
        }catch(Exception ex){Error(ex);}
    }
    private static readonly DependencyProperty MoveAttached=DependencyProperty.RegisterAttached("MoveAttached",typeof(bool),typeof(MainWindow),new PropertyMetadata(false));
    private void AttachMove(FrameworkElement element,ConfigNode node)
    {
        if((bool)element.GetValue(MoveAttached))return;element.SetValue(MoveAttached,true);
        ConfigNode Current()
        {
            if(element.DataContext is ConfigNode current)return current;
            if(element is DataGridRow row){int index=Csv.Items.IndexOf(row.Item);if(index>=0&&index<csvRows.Count)return csvRows[index];}
            return node;
        }
        
        // Drag only starts with Alt held so row selection and value editing retain their usual behavior.
        Point? start=null;System.Windows.Documents.AdornerLayer? layer=null;DropLine? line=null;
        void Clear(){if(line!=null){layer?.Remove(line);line=null;}}
        element.PreviewMouseLeftButtonDown+=(_,e)=>{if((Find<TextBlock>(e.OriginalSource as DependencyObject)?.Text=="⠿")&&Session?.StructureEditing==true)start=e.GetPosition(element);};
        element.PreviewMouseMove+=(_,e)=>
        {
            if(start is not {} p||e.LeftButton!=MouseButtonState.Pressed)return;
            if((e.GetPosition(element)-p).Length<8)return;start=null;
            if(Search.Text.Length>0||Session?.Draft!=null)return;
            DragDrop.DoDragDrop(element,new DataObject("InerConfig.Node",Current()),DragDropEffects.Move);
        };
        element.AllowDrop=true;
        element.DragOver+=(_,e)=>{if(e.Data.GetDataPresent("InerConfig.Node")){e.Effects=DragDropEffects.Move;e.Handled=true;
                layer=System.Windows.Documents.AdornerLayer.GetAdornerLayer(element);
                if(layer!=null){Clear();line=new DropLine(element,e.GetPosition(element).Y>element.ActualHeight/2);layer.Add(line);}
                var scroll=Find<ScrollViewer>(element);if(scroll!=null){var y=e.GetPosition(scroll).Y;if(y<24)scroll.LineUp();else if(y>scroll.ActualHeight-24)scroll.LineDown();}}};
        element.DragLeave+=(_,_)=>Clear();element.Unloaded+=(_,_)=>Clear();
        element.Drop+=async(_,e)=>
        {
            Clear();if(e.Data.GetData("InerConfig.Node") is not ConfigNode dragged)return;e.Handled=true;
            var target=Current();if(!RequireStructure()||document==null||!document.All.Contains(dragged)||!document.All.Contains(target)||Search.Text.Length>0)return;
            try{if(Session!.CsvHeader&&Session.Format=="CSV"&&(dragged==document.Roots[0]||target==document.Roots[0]))return;
                await Apply(document.MoveBefore(dragged,target,e.GetPosition(element).Y>element.ActualHeight/2));}catch(Exception ex){Error(ex);}
        };
    }
}
