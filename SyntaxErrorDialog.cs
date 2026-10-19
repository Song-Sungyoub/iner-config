using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Xml;

namespace InerConfig;

public sealed class SyntaxErrorDialog : Window
{
    internal int ErrorOffset { get; }
    internal Button LocateButton { get; } = new() { Content="오류 위치로 이동", IsDefault=true };
    internal static int Locate(Exception error, string text)
    {
        if(error is YamlDotNet.Core.YamlException yaml)return (int)Math.Clamp(yaml.Start.Index,0,text.Length);
        int line, column;
        if(error is XmlException xml && xml.LineNumber>0) { line=xml.LineNumber-1;column=Math.Max(0,xml.LinePosition-1); }
        else if(error is JsonException json && json.LineNumber is long number && json.BytePositionInLine is long bytes)
        {
            line=(int)Math.Min(number,int.MaxValue);
            var content=text.Split('\n').ElementAtOrDefault(line)??"";
            var utf8=Encoding.UTF8.GetBytes(content);
            column=Encoding.UTF8.GetCharCount(utf8,0,(int)Math.Clamp(bytes,0,utf8.Length));
        }
        else return -1;
        int start=0;
        for(int i=0;i<line;i++) { int next=text.IndexOf('\n',start);if(next<0)return text.Length;start=next+1; }
        return Math.Clamp(start+column,0,text.Length);
    }
    public SyntaxErrorDialog(Window owner, string file, string text, Exception error)
    {
        ErrorOffset=Locate(error,text);
        SetResourceReference(StyleProperty,typeof(Window));Owner=owner;Title="문법 오류";
        Width=Math.Min(660,SystemParameters.WorkArea.Width-40);MaxHeight=SystemParameters.WorkArea.Height-40;
        SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=System.Windows.Media.Brushes.Transparent;ShowInTaskbar=false;
        var body=new StackPanel { Margin=new Thickness(24) };
        var frame=new Border { CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Child=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto } };
        frame.SetResourceReference(Border.BackgroundProperty,"Canvas");frame.SetResourceReference(Border.BorderBrushProperty,"Line");Content=frame;
        body.Children.Add(new TextBlock { Text="파일의 문법을 확인해 주세요",FontSize=21,FontWeight=FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text=file,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,8) });
        var lines=text.Split('\n');int line=ErrorOffset<0?-1:text[..ErrorOffset].Count(c=>c=='\n');
        int start=ErrorOffset<=0?0:text.LastIndexOf('\n',ErrorOffset-1)+1;
        body.Children.Add(new TextBlock { Text=line<0?"이 오류는 파서가 위치 정보를 제공하지 않습니다.":$"{line+1}행 · {ErrorOffset-start+1}열",FontWeight=FontWeights.SemiBold });
        body.Children.Add(new TextBox { Text=error.Message,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=120,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,10,0,10) });
        if(line>=0)
        {
            var context=string.Join("\n",Enumerable.Range(Math.Max(0,line-2),Math.Min(lines.Length-1,line+2)-Math.Max(0,line-2)+1).Select(i=>$"{(i==line?"→":" ")} {i+1,4}  {lines[i].TrimEnd('\r')}"));
            body.Children.Add(new TextBox { Text=context,IsReadOnly=true,FontFamily=new System.Windows.Media.FontFamily("Consolas"),Height=145,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
        }
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0) };
        var close=new Button { Content="닫기",IsCancel=true };close.Click+=(_,_)=>Close();buttons.Children.Add(close);
        LocateButton.IsEnabled=ErrorOffset>=0;LocateButton.Click+=(_,_)=>DialogResult=true;buttons.Children.Add(LocateButton);body.Children.Add(buttons);
    }
}
