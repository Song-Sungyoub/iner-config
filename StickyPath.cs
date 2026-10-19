using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace InerConfig;
public partial class MainWindow
{
    private bool stickyEnabled;
    private Grid? stickyLayer;
    private string stickySignature="";
    private readonly string stickyFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"InerConfig","sticky-path.txt");
    private static IEnumerable<DataGridRow> RealizedRows(DependencyObject root)
    {
        if(root is DataGridRow row){yield return row;yield break;}
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in RealizedRows(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    private void SetSticky(bool enabled,bool persist=true)
    {
        stickyEnabled=enabled;Stage.ColumnHeaderHeight=enabled?61:35;
        if(persist)try{Directory.CreateDirectory(Path.GetDirectoryName(stickyFile)!);File.WriteAllText(stickyFile,enabled?"true":"false");}catch(Exception ex){Error(ex);}
        if(Stage.Columns[0].Header is Grid header&&header.RowDefinitions.Count==2)
        {header.Height=enabled?61:35;header.RowDefinitions[1].Height=new GridLength(enabled?26:0);}
        stickySignature="";UpdateSticky();
    }
    private void UpdateSticky()
    {
        if(!stickyEnabled||stickyLayer==null||Stage.Visibility!=Visibility.Visible||!Stage.IsLoaded)return;
        double top=(Stage.Columns[0].Header as FrameworkElement)?.ActualHeight??61;
        var rows=RealizedRows(Stage).Where(r=>r.Item is ConfigNode).Select(r=>(Row:r,Y:r.TranslatePoint(new Point(),Stage).Y)).Where(r=>r.Y+r.Row.ActualHeight>top&&r.Y<Stage.ActualHeight).OrderBy(r=>r.Y).ToArray();
        var first=rows.Select(r=>(ConfigNode)r.Row.Item).FirstOrDefault(n=>n.Kind is not ("Comment" or "Add"));
        var visible=rows.Select(r=>((ConfigNode)r.Row.Item).Path).ToHashSet();
        var parents=new List<ConfigNode>();
        for(var parent=first?.Parent;parent!=null;parent=parent.Parent)if(!visible.Contains(parent.Path))parents.Add(parent);
        var signature=string.Join("|",parents.Select(n=>n.Path));
        if(signature==stickySignature)return;stickySignature=signature;stickyLayer.Children.Clear();
        foreach(var parent in parents)
        {
            if(parent.Depth>=StageCount)continue;
            var label=new TextBlock{Text=parent.Name,ToolTip=parent.Path,FontSize=12,FontWeight=FontWeights.SemiBold,Margin=new Thickness(12,0,8,0),VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
            label.SetResourceReference(ForegroundProperty,"Muted");Grid.SetColumn(label,parent.Depth);stickyLayer.Children.Add(label);
        }
    }
    private void AutoFitStage(int index)
    {
        if(index<0||index>=StageCount)return;
        double size=100;
        double Measure(string value,double fontSize,FontWeight weight)=>value.Replace("\r","").Split('\n').Select(line=>new FormattedText(line,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(FontFamily,FontStyles.Normal,weight,FontStretches.Normal),fontSize,Brushes.Black,VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace).DefaultIfEmpty(0).Max();
        foreach(var n in Stage.Items.OfType<ConfigNode>().Where(n=>n.Kind!="Add"))
        {
            if(!n.Group&&n.Depth==index)size=Math.Max(size,Measure(n.Name,n.Kind=="Comment"?12:FontSize,FontWeights.Normal)+60);
            if(!n.Group&&n.Depth+1==index)size=Math.Max(size,Measure(n.Display,n.Kind=="Comment"?12:FontSize,FontWeights.Normal)+(n.Kind=="Boolean"?54:30));
        }
        ResizeStage(index,Math.Ceiling(size));
    }
}
