using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InerConfig;
public sealed partial class FileSession
{
    public HashSet<string> PendingDeletes {get;}=[];
    public string? StructureBaseline { get; private set; }
    public bool StructureEditing => StructureBaseline!=null;
    public bool RawEditing { get; private set; }
    private sealed record EditState(string Text,string[] Deletes);
    private readonly Stack<EditState> editUndo=[],editRedo=[];
    private EditState EditSnapshot()=>new(Text,PendingDeletes.ToArray());
    private void PushEditUndo()
    {
        if(editUndo.Count>=30){var keep=editUndo.Take(29).Reverse().ToArray();editUndo.Clear();foreach(var s in keep)editUndo.Push(s);}
        editUndo.Push(EditSnapshot());
    }
    private void RestoreEdit(EditState state){Text=state.Text;PendingDeletes.Clear();PendingDeletes.UnionWith(state.Deletes);Notify();}
    public void ToggleDeletion(string path)
    {
        PushEditUndo();editRedo.Clear();
        if(!PendingDeletes.Remove(path))PendingDeletes.Add(path);Notify();
    }
    public void BeginStructure(bool raw=false) { if(StructureEditing)return;StructureBaseline=Text;RawEditing=raw;editUndo.Clear();editRedo.Clear();Notify(); }
    public void CompleteStructure()
    {
        if(!StructureEditing)return;
        if(Input!=null)throw new InvalidOperationException("미적용 입력을 복원하거나 취소하세요.");
        if(Draft!=null)throw new InvalidOperationException("추가 중인 항목을 확정하거나 취소하세요.");
        _=new ConfigDocument(Text,Format,Separator);
        Text=WithPendingDeletes();PendingDeletes.Clear();var baseline=StructureBaseline!;StructureBaseline=null;RawEditing=false;
        if(Text!=baseline){undo.Push(baseline);redo.Clear();}
        editUndo.Clear();editRedo.Clear();Notify();
    }
    public void CancelStructure()
    {
        if(!StructureEditing)return;
        Draft=null;Input=null;PendingDeletes.Clear();Text=StructureBaseline!;StructureBaseline=null;RawEditing=false;editUndo.Clear();editRedo.Clear();Notify();
    }
    public string WithPendingDeletes()
    {
        var doc=new ConfigDocument(Text,Format,Separator);var current=Text;
        var nodes=doc.All.Where(n=>PendingDeletes.Contains(n.Path)&&!Ancestors(n).Any(a=>PendingDeletes.Contains(a.Path))).OrderByDescending(n=>n.Start).ToList();
        if(PendingDeletes.Any(path=>!doc.All.Any(n=>n.Path==path)))throw new InvalidOperationException("삭제 대상이 변경되었습니다.");
        foreach(var n in nodes){var parsed=new ConfigDocument(current,Format,Separator);var target=parsed.All.First(x=>x.Path==n.Path);current=parsed.Delete(target);}
        return current;
    }
    private static IEnumerable<ConfigNode> Ancestors(ConfigNode n){for(var p=n.Parent;p!=null;p=p.Parent)yield return p;}
}
public partial class MainWindow
{
    private bool RequireStructure()
    {
        if(Session?.Draft!=null){Message.Text="추가 중인 항목을 먼저 확정하거나 취소하세요.";return false;}
        if(Session?.PendingDeletes.Count>0){Message.Text="삭제 예정 작업을 완료하거나 복구한 뒤 다른 구조를 변경하세요.";return false;}
        if(Session?.StructureEditing==true&&!Session.RawEditing)return true;
        Message.Text="구조 변경은 구조 편집 모드에서 가능합니다.";return false;
    }
    private void SyncStructure()
    {
        if(StructureButton==null)return;
        bool active=Session?.StructureEditing==true;
        Csv.RowHeaderWidth=active?25:0;
        CsvAddRowButton.Content=Session?.Format=="INI"?"＋ 섹션 추가":"＋ 행 추가";
        CsvAddRowButton.Visibility=active&&Session?.Format is "CSV" or "INI"?Visibility.Visible:Visibility.Collapsed;
        ContinueInputButton.Visibility=Session?.Input!=null?Visibility.Visible:Visibility.Collapsed;
        StructureButton.Content=Session?.Draft!=null?"추가 입력 계속":active?(Session!.RawEditing?"원문 구조 편집 중":"구조 편집 중"):"구조 편집";
        StructureButton.IsEnabled=Session!=null;
        FinishStructureButton.Visibility=CancelStructureButton.Visibility=active?Visibility.Visible:Visibility.Collapsed;
        Source.IsReadOnly=!active||Session?.RawEditing!=true;
        CsvHeader.IsEnabled=CsvSeparator.IsEnabled=!active;
    }
    private async void StructureClick(object sender,RoutedEventArgs e)
    {
        if(Session==null)return;
        if(Session.Draft!=null){ShowAddDraft(StructureButton);return;}
        if(Session.StructureEditing&&sourceMode&&!Session.RawEditing)
        {
            var decision=MessageBox.Show(this,"트리 편집을 완료하고 원문을 수정할까요?\\n아니요: 트리 편집 전체 취소 · 취소: 전환하지 않음","원문 편집 전환",MessageBoxButton.YesNoCancel);
            if(decision==MessageBoxResult.Cancel)return;
            try{if(decision==MessageBoxResult.Yes)Session.CompleteStructure();else{Session.CancelStructure();await ShowSessionText();}}catch(Exception ex){Error(ex);return;}
        }
        if(!Session.StructureEditing)Session.BeginStructure(sourceMode);
        SyncStructure();await Parse();
    }
    private async void FinishStructure(object sender,RoutedEventArgs e)
    {
        if(Session==null)return;
        try{Session.CompleteStructure();SyncStructure();await Parse();Message.Text="편집 완료 · 파일에 저장하려면 Ctrl+S";}
        catch(Exception ex){Error(ex);}
    }
    private async void CancelStructure(object sender,RoutedEventArgs e)
    {
        if(Session==null)return;
        if(Session.Text!=Session.StructureBaseline&&MessageBox.Show(this,"구조 편집 이후의 추가·삭제·값 수정을 모두 취소합니다. 진입 이전 변경은 유지됩니다.","편집 전체 취소",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        Session.CancelStructure();SyncStructure();await ShowSessionText();
    }
    private static Brush TypeColor(string kind)
    {
        var hex=kind switch{"String" or "CDATA"=>"#33966D","Number"=>"#528FDD","Boolean"=>"#C48935","Array" or "Sequence"=>"#299DA5","Object" or "Mapping" or "Element" or "Section"=>"#9A73CE",_=>"#8894A5"};
        return (Brush)new BrushConverter().ConvertFromString(hex)!;
    }
}
