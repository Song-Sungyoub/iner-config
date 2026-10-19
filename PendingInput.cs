using System.Windows;
using System.Windows.Controls;
namespace InerConfig;
public sealed record PendingInput(string Path,string Text,string Baseline,string Kind);
public sealed partial class FileSession { public PendingInput? Input {get;set;} }
public partial class MainWindow
{
    private void CaptureCellInput()
    {
        if(Session==null||Session.Format!="CSV")return;
        var box=FindFocusedCsv();
        if(box==null)return;
        var n=CsvNode();if(n==null)return;
        Session.Input=new(n.Path,box.Text,Session.Text,"String");Session.Notify();
    }
    private TextBox? FindFocusedCsv()=>System.Windows.Input.Keyboard.FocusedElement is TextBox b&&Csv.IsAncestorOf(b)?b:null;
    private void ContinueInput(object sender,RoutedEventArgs e)
    {
        if(Session?.Input is not {} pending||document==null)return;
        if(pending.Baseline!=Session.Text){
            var dialog=new EditDialog(this,"복원 입력 확인","기준 문서가 달라 자동 적용할 수 없습니다. 내용을 복사한 뒤 적용을 누르면 초안만 정리합니다.",pending.Text,"String");
            if(dialog.ShowDialog()==true){Session.Input=null;Session.Notify();SyncStructure();}return;}
        var node=document.All.FirstOrDefault(n=>n.Path==pending.Path);
        if(node!=null)Edit(node);
    }
}
