using System.IO;
using System.Windows;
using System.Windows.Controls;
using InerConfig.Models;

namespace InerConfig;
public partial class MainWindow
{
    private static readonly string scriptBetaFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"InerConfig","script-beta.txt");
    private bool ScriptEnabled=>ReadScriptEnabled(scriptBetaFile);
    private static bool ReadScriptEnabled(string path){try{return File.ReadAllText(path)=="true";}catch{return false;}}
    private bool scriptOpening;
    internal static Button? FindButtonForScript(DependencyObject? node)=>Find<Button>(node);
    private async void OpenScript(ConfigNode? node)
    {
        if(!ScriptEnabled||scriptOpening)return;
        if(ReferenceEditor!=null){ReferenceEditor.Activate();return;}
        scriptOpening=true;
        var openingSession=Session;
        try
        {
            try
            {
                var probe=await new PythonRunner().RunAsync("result = 'ready'","",CancellationToken.None);
                if(!probe.Success||probe.Text!="ready")throw new IOException("내장 Python 확인에 실패했습니다.");
            }
            catch(Exception e)
            {
                if(ScriptTransformWindow.Ask(this,"Python 실행 환경 확인","내장 Python을 실행할 수 없습니다. 별도로 Python을 설치할 필요는 없습니다.\nInerConfig 업데이트 또는 재설치를 확인해주세요.\n\n"+e.Message,["업데이트 확인","닫기"])==0)await CheckUpdates(true);
                return;
            }
            if(!ScriptEnabled||Session!=openingSession||ReferenceEditor!=null)return;
            if(node!=null&&document!=null&&!document.All.Contains(node)){Message.Text="대상 항목이 변경되었습니다. 다시 선택해주세요.";return;}
            if(!CommitInline()||!Csv.CommitEdit(DataGridEditingUnit.Cell,true)||!Csv.CommitEdit(DataGridEditingUnit.Row,true))return;
            if(Session is {} current&&(current.Input!=null||current.Draft!=null||current.PendingDeletes.Count>0)){Message.Text="미적용 입력·추가·삭제 항목을 먼저 확정하거나 취소해주세요.";return;}
            if(Session?.Format=="CSV"&&Csv.SelectedCells.Count>1){Message.Text="스크립트 변환은 셀 하나를 선택해주세요.";return;}
            // CSV commit reparses asynchronously; refresh before capturing a target.
            if(Session!=null&&document?.Text!=Session.Text)await Parse();
            if(Session!=openingSession){Message.Text="대상 탭이 변경되었습니다. 다시 선택해주세요.";return;}
            node=document?.All.FirstOrDefault(n=>n.Path==node?.Path);
            if(sourceMode||Session?.RawEditing==true||node is not {Editable:true,Group:false}||node.Kind=="Null")node=null;
            var vm=CreateScriptModel(node);
            var window=new ScriptTransformWindow(this,vm,WindowButtons.HorizontalAlignment==HorizontalAlignment.Right);
            await ShowReferenceWindow(window,()=>
            {
                var done=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                window.Closed+=(_,_)=>done.TrySetResult(true);window.Show();return done.Task;
            });
        }
        catch(Exception e){Message.Text=e.Message;}
        finally{scriptOpening=false;}
    }
    private ScriptTransformViewModel CreateScriptModel(ConfigNode? node,ScriptLibrary? library=null)
    {
            var session=Session;var doc=document;var baseline=session?.Text;var target=node;
            var format=session?.Format;var separator=session?.Separator;var header=session?.CsvHeader;
            var structure=session?.StructureEditing;var raw=session?.RawEditing;
            string? Validate(string value)
            {
                if(target==null||session==null||doc==null)return "독립 시험에서는 결과 복사만 가능합니다.";
                if(session!=Session||session.Text!=baseline||session.Format!=format||session.Separator!=separator||session.CsvHeader!=header||session.StructureEditing!=structure||session.RawEditing!=raw||session.Input!=null||session.Draft!=null||session.PendingDeletes.Count>0)return "대상 문서가 변경되었습니다. 다시 열어주세요.";
                try{var candidate=doc.Change(target,value);_ = new ConfigDocument(candidate,format!,session.Separator);return null;}
                catch(Exception e){return "현재 자료형에 적용할 수 없습니다: "+e.Message;}
            }
            return new ScriptTransformViewModel(target==null?"독립 시험 · 대상 없음":(session?.Title??"")+" / "+target.Path,target?.DisplayKind??"String",target?.Value??"",target!=null,Validate,async value=>
            {
                if(Validate(value) is {} error)throw new IOException(error);
                await Apply(doc!.Change(target!,value));
                if(session!.Format=="CSV"){Csv.UpdateLayout();var n=csvRows.SelectMany(r=>r.Children).FirstOrDefault(n=>n.Path==target!.Path);if(n!=null){int row=csvRows.IndexOf(n.Parent!),col=n.Parent!.Children.IndexOf(n);Csv.CurrentCell=new DataGridCellInfo(Csv.Items[row],Csv.Columns[col]);Csv.ScrollIntoView(Csv.Items[row],Csv.Columns[col]);}}
                else if(document?.All.FirstOrDefault(n=>n.Path==target!.Path) is {} n)SelectNode(n,true);
            },library);
    }
}
