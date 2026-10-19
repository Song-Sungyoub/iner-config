using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace InerConfig;
public partial class MainWindow
{
    private void OpenPreferences()
    {
        if(!CommitInline()||!Csv.CommitEdit(DataGridEditingUnit.Cell,true))return;
        var window=BuildPreferences();window.ShowDialog();
    }
    private Window BuildPreferences()
    {
        var window=new Window{Owner=this,Title="환경 설정",Width=480,Height=Math.Min(510,SystemParameters.WorkArea.Height-40),WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent};
        window.SetResourceReference(StyleProperty,typeof(Window));window.Background=Brushes.Transparent;
        var panel=new Grid{Margin=new Thickness(24)};
        panel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});panel.RowDefinitions.Add(new RowDefinition());panel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var frame=new Border{CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Child=panel};frame.SetResourceReference(BackgroundProperty,"Canvas");frame.SetResourceReference(BorderBrushProperty,"Line");window.Content=frame;
        var header=new Grid{Height=38};header.Children.Add(new TextBlock{Text="환경 설정",FontSize=22,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center});
        var close=new Button{Content="×",Width=32,Padding=new Thickness(4),ToolTip="닫기",HorizontalAlignment=WindowButtons.HorizontalAlignment};close.Click+=(_,_)=>window.Close();header.Children.Add(close);
        header.MouseLeftButtonDown+=(_,e)=>{if(Find<Button>(e.OriginalSource as DependencyObject)==null)window.DragMove();};panel.Children.Add(header);
        var body=new StackPanel{Margin=new Thickness(0,16,0,16)};var scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);panel.Children.Add(scroll);
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,10,0,0)};error.SetResourceReference(ForegroundProperty,"Muted");
        void Save(string path,string value,Action apply,Action rollback)
        {try{Directory.CreateDirectory(Path.GetDirectoryName(path)!);var tmp=path+".tmp";File.WriteAllText(tmp,value);File.Move(tmp,path,true);apply();error.Text="";}catch(Exception ex){rollback();error.Text="설정을 저장하지 못했습니다: "+ex.Message;}}
        void Heading(string text){var label=new TextBlock{Text=text,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,12)};body.Children.Add(label);}
        void Choice(string label,string first,string second,bool selected,Action<bool,Action> changed)
        {
            var row=new DockPanel{Margin=new Thickness(0,0,0,14)};
            row.Children.Add(new TextBlock{Text=label,Width=155,VerticalAlignment=VerticalAlignment.Center});
            var group=Guid.NewGuid().ToString();var a=new RadioButton{Content=first,GroupName=group,IsChecked=!selected,Margin=new Thickness(0,4,20,4)};
            var b=new RadioButton{Content=second,GroupName=group,IsChecked=selected,Margin=new Thickness(0,4,0,4)};bool updating=false,old=selected;
            void Select(bool value){if(updating||value==old)return;changed(value,()=>{updating=true;a.IsChecked=!old;b.IsChecked=old;updating=false;});old=b.IsChecked==true;}
            a.Checked+=(_,_)=>Select(false);b.Checked+=(_,_)=>Select(true);row.Children.Add(a);row.Children.Add(b);body.Children.Add(row);
        }
        Heading("화면");
        Choice("테마","라이트","다크",((SolidColorBrush)FindResource("Canvas")).Color.R<100,(value,rollback)=>Save(themeFile,value?"dark":"light",()=>SetTheme(value,false),rollback));
        Heading("Beta 기능");
        void Toggle(string title,string note,bool initial,string path,Action<bool> apply)
        {
            var check=new CheckBox{Content=title,IsChecked=initial,Margin=new Thickness(0,4,0,6)};bool busy=false,old=initial;
            check.Click+=(_,_)=>{if(busy)return;bool value=check.IsChecked==true;Save(path,value?"true":"false",()=>{apply(value);old=value;},()=>{busy=true;check.IsChecked=old;busy=false;});};
            body.Children.Add(check);var help=new TextBlock{Text=note,TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(22,0,0,16)};help.SetResourceReference(ForegroundProperty,"Muted");body.Children.Add(help);
        }
        Toggle("셀에서 바로 수정","셀에서 값을 입력하고 키보드로 이동합니다.",inlineEnabled,inlineFile,value=>{inlineEnabled=value;PositionInline();});
        Toggle("상위 경로 고정 표시","단계 보기에서 화면 밖 부모 경로를 표시합니다.",stickyEnabled,stickyFile,value=>SetSticky(value,false));
        Toggle("Python 스크립트 변환 (Beta)","Python은 앱에 포함되어 별도 설치가 필요 없습니다.\n주의: 직접 작성하거나 신뢰하는 코드만 실행해주세요.",ScriptEnabled,scriptBetaFile,_=>{});
        body.Children.Add(error);
        var footer=new DockPanel();var done=new Button{Content="닫기",IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right};done.Click+=(_,_)=>window.Close();DockPanel.SetDock(done,Dock.Right);footer.Children.Add(done);
        var hint=new TextBlock{Text="변경 사항은 자동으로 저장됩니다.",FontSize=12,VerticalAlignment=VerticalAlignment.Center};hint.SetResourceReference(ForegroundProperty,"Muted");footer.Children.Add(hint);Grid.SetRow(footer,2);panel.Children.Add(footer);
        window.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){window.Close();e.Handled=true;}};
        return window;
    }
}
