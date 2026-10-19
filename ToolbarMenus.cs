using System.Windows;
using System.Windows.Controls;
namespace InerConfig;
public partial class MainWindow
{
    private void ShowToolbarMenu(object sender, ContextMenu menu) { menu.PlacementTarget=(UIElement)sender;menu.IsOpen=true; }
    private static void CommandItem(ContextMenu menu,string title,RoutedEventHandler action,bool enabled=true)
    { var item=new MenuItem{Header=title,IsEnabled=enabled};item.Click+=action;menu.Items.Add(item); }
    private void OpenTabs(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();
        foreach(var tab in sessions){var item=new MenuItem{Header=tab.Title,ToolTip=tab.FilePath,IsCheckable=true,IsChecked=tab==Session};item.Click+=(_,_)=>Files.SelectedItem=tab;menu.Items.Add(item);}
        if(sessions.Count==0)menu.Items.Add(new MenuItem{Header="열린 파일이 없습니다",IsEnabled=false});
        ShowToolbarMenu(sender,menu);
    }
    private void FileMenu(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();
        var create=new MenuItem{Header="새 파일"};PopulateNewFileMenu(create);menu.Items.Add(create);
        CommandItem(menu,"열기…    Ctrl+O",OpenFile);
        var recent=new MenuItem{Header="최근 파일"};menu.Items.Add(recent);
        try{foreach(var path in Workspace?.Recent()??[]){var item=new MenuItem{Header=path};item.Click+=async(_,_)=>await OpenPathAsync(path);recent.Items.Add(item);}
            if(recent.Items.Count==0)recent.Items.Add(new MenuItem{Header="최근 파일이 없습니다",IsEnabled=false});
            else {recent.Items.Add(new Separator());var clear=new MenuItem{Header="목록 지우기"};clear.Click+=(_,_)=>{try{Workspace?.ClearRecent();}catch(Exception ex){Error(ex);}};recent.Items.Add(clear);}}
        catch(Exception ex){recent.Items.Add(new MenuItem{Header=ex.Message,IsEnabled=false});}
        menu.Items.Add(new Separator());CommandItem(menu,"저장    Ctrl+S",SaveFile,Session!=null);CommandItem(menu,"다른 이름으로 저장…",SaveAs,Session!=null);
        CommandItem(menu,"파일 위치 열기",(_,_)=>{if(Session?.FilePath is {} path)try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe"){Arguments="/select,\""+path+"\"",UseShellExecute=true});}catch(Exception ex){Error(ex);}},Session?.FilePath!=null);
        ShowToolbarMenu(sender,menu);
    }
    private void ShowViewChooser(object sender,RoutedEventArgs e)
    {if(!CommitInline())return;var chooser=new ViewChooser(this,editorView,false);if(chooser.ShowDialog()==true){ChangeView(chooser.SelectedView);sourceMode=false;SetView();}}
    private void SaveOptions(object sender,RoutedEventArgs e)
    {var menu=new ContextMenu();CommandItem(menu,"다른 이름으로 저장…",SaveAs,Session!=null);ShowToolbarMenu(sender,menu);}
    private void ViewOptions(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();
        foreach(var (mode,title) in new[]{(EditorView.Stages,"단계 보기"),(EditorView.Tree,"트리 보기"),(EditorView.Groups,"그룹 보기")})
        {var item=new MenuItem{Header=title,IsCheckable=true,IsChecked=editorView==mode,IsEnabled=Session?.Format!="CSV"};item.Click+=(_,_)=>{ChangeView(mode);sourceMode=false;SetView();};menu.Items.Add(item);}
        menu.Items.Add(new Separator());CommandItem(menu,"화면 예시로 선택…",ShowViewChooser);CommandItem(menu,"검색    Ctrl+F",(_,_)=>OpenSearch(),Session!=null);
        if(Session?.Format=="CSV")
        {
            menu.Items.Add(new Separator());
            var header=new MenuItem{Header="첫 행 헤더",IsCheckable=true,IsChecked=CsvHeader.IsChecked==true,IsEnabled=CsvHeader.IsEnabled};
            header.Click+=(_,_)=>CsvHeader.IsChecked=header.IsChecked;menu.Items.Add(header);
            foreach(var (name,index) in new[]{("쉼표",0),("탭",1),("세미콜론",2)})
            {var item=new MenuItem{Header="구분자 · "+name,IsCheckable=true,IsChecked=CsvSeparator.SelectedIndex==index,IsEnabled=CsvSeparator.IsEnabled};item.Click+=(_,_)=>CsvSeparator.SelectedIndex=index;menu.Items.Add(item);}
        }
        ShowToolbarMenu(sender,menu);
    }
    private void ToolbarTools(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();
        foreach(var action in new[]{"암호화","복호화","SHA-256"})CommandItem(menu,action=="SHA-256"?"해시 계산…":action+"…",(_,_)=>new CryptoWindow(this,"",action).ShowDialog());
        if(ScriptEnabled)CommandItem(menu,"스크립트로 변환…",(_,_)=>OpenScript(Session?.Format=="CSV"?CsvNode():Tree.SelectedItem as ConfigNode));
        if(CsvAddRowButton.Visibility==Visibility.Visible)CommandItem(menu,Session?.Format=="INI"?"섹션 추가…":"행 추가…",CsvAddRowClick);
        ShowToolbarMenu(sender,menu);
    }
}
