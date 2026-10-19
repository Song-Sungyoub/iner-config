using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace InerConfig;

public partial class MainWindow
{
    private Guid workspaceId=Guid.NewGuid();
    private bool workspacePending=true;
    private readonly DispatcherTimer workspaceTimer=new() { Interval=TimeSpan.FromSeconds(2) };
    private WorkspaceStore? Workspace => ((App)Application.Current).Workspace;
    private void InitializeRecovery()
    {
        void Changed(object? sender,System.ComponentModel.PropertyChangedEventArgs e){workspacePending=true;UndoButton.IsEnabled=Session?.CanUndo==true;RedoButton.IsEnabled=Session?.CanRedo==true;}
        sessions.CollectionChanged+=(_,e)=>
        {
            workspacePending=true;
            if(e.OldItems!=null)foreach(FileSession tab in e.OldItems)tab.PropertyChanged-=Changed;
            if(e.NewItems!=null)foreach(FileSession tab in e.NewItems)tab.PropertyChanged+=Changed;
        };
        Files.SelectionChanged+=(_,_)=>workspacePending=true;Search.TextChanged+=(_,_)=>workspacePending=true;
        Tree.SelectionChanged+=(_,_)=>workspacePending=true;
        workspaceTimer.Tick+=(_,_)=> { if(Workspace==null||!workspacePending)return;try { Checkpoint(); }catch(Exception ex){Message.Text="작업 복원본을 저장하지 못했습니다: "+ex.Message;} };
        Loaded+=(_,_)=>workspaceTimer.Start();Closed+=(_,_)=>workspaceTimer.Stop();
    }
    internal void Checkpoint()
    {
        if(Workspace==null)return;
        CaptureCellInput();
        if(Session!=null) { Session.SearchText=Search.Text;Session.SourceMode=sourceMode; }
        Workspace.SaveWindow(new(2,workspaceId,Files.SelectedIndex,sessions.Select(s=>s.Capture()).ToList()));
        workspacePending=false;
    }
    internal async Task RestoreWorkspace(WorkspaceStore.WindowState saved)
    {
        var restored=saved.Tabs.Select(FileSession.Restore).ToList();
        workspaceId=saved.Id;
        foreach(var tab in restored)sessions.Add(tab);
        Files.SelectedIndex=sessions.Count==0?-1:Math.Clamp(saved.Selected,0,sessions.Count-1);
        await Parse();
    }
    private void RememberFile(string path)
    {
        try { Workspace?.Remember(path); }catch(Exception ex){Message.Text="최근 파일 목록을 저장하지 못했습니다: "+ex.Message;}
    }
    private void RecentFiles(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();
        try
        {
            var recent=Workspace?.Recent()??[];
            foreach(var path in recent)
            {
                var item=new MenuItem { Header=path,ToolTip=path };
                item.Click+=async(_,_)=>await OpenPathAsync(path);menu.Items.Add(item);
            }
            if(recent.Count==0)menu.Items.Add(new MenuItem { Header="최근 파일이 없습니다",IsEnabled=false });
            else { menu.Items.Add(new Separator());var clear=new MenuItem { Header="최근 파일 목록 지우기" };clear.Click+=(_,_)=> { try{Workspace?.ClearRecent();}catch(Exception ex){Error(ex);} };menu.Items.Add(clear); }
        }
        catch(Exception ex){Error(ex);return;}
        menu.PlacementTarget=(UIElement)sender;menu.IsOpen=true;
    }
}
