using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using InerConfig.Models;

namespace InerConfig;

public sealed class ScriptTransformViewModel : INotifyPropertyChanged
{
    private readonly ScriptLibrary library;
    private readonly PythonRunner runner;
    private readonly Func<string,string?> validate;
    private readonly Func<string,Task> apply;
    private readonly string original;
    private ScriptDefinition? selected;
    private string name="",description="",code="",input="",search="",result="",status="",log="",kind="";
    private bool dirty,busy,choosing,success;
    private CancellationTokenSource? cancellation;
    private Task? execution;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CloseRequested;
    public event Action<int,int>? ErrorPositionRequested;
    public Func<string,string,string[],Task<int>> Confirm {get;set;}=(_,_,_)=>Task.FromResult(-1);
    public Action<string> CopyText {get;set;}=_=>{};
    public ObservableCollection<ScriptDefinition> Scripts {get;}=[];
    private List<ScriptDefinition> all=[];
    public string Target {get;}
    public string TargetKind {get;}
    public bool HasTarget {get;}
    public bool Idle=>!busy&&!choosing;
    public bool Busy=>busy;
    public string SavedState=>dirty?"● 저장하지 않은 변경":selected?.IsExample==true?"기본 예제 · 저장하면 사용자 사본을 만듭니다":selected==null?"새 스크립트":"저장됨";
    public string CodeLines=>string.Join("\n",Enumerable.Range(1,Code.Count(c=>c=='\n')+1));
    public string InputState=>Input==original?"대상 원본":"시험 입력 변경됨";
    public string ResultKind=>success?$"{kind} · {result.Length:N0}자":"—";
    public string ApplyHint=>!HasTarget?"독립 시험 · 결과 복사만 가능":success?validate(Result)??(Result==original?"현재 값과 같습니다":"현재 자료형을 유지해 적용합니다"):"결과 적용 전까지 원래 값은 유지됩니다";
    public string RuntimeState=>runner.Available?"Python 3.13.15 · 표준 라이브러리 · 5초 제한":"Python 실행 환경 없음 · 설치 복구 필요";
    public string Name {get=>name;set{if(Set(ref name,value)){dirty=true;Refresh();}}}
    public string Description {get=>description;set{if(Set(ref description,value)){dirty=true;Refresh();}}}
    public string Code {get=>code;set{if(Set(ref code,value)){dirty=true;Invalidate();Changed(nameof(CodeLines));}}}
    public string Input {get=>input;set{if(Set(ref input,value)){Invalidate();Changed(nameof(InputState));}}}
    public string Search {get=>search;set{if(Set(ref search,value))Filter();}}
    public string Result {get=>result;private set=>Set(ref result,value);}
    public string Status {get=>status;private set=>Set(ref status,value);}
    public string Log {get=>log;private set=>Set(ref log,value);}
    public ScriptDefinition? Selected {get=>selected;set{if(value!=null&&value!=selected&&Idle)_=ChooseAsync(value);}}
    public ICommand RunCommand {get;}
    public ICommand StopCommand {get;}
    public ICommand SaveCommand {get;}
    public ICommand SaveAsCommand {get;}
    public ICommand NewCommand {get;}
    public ICommand DeleteCommand {get;}
    public ICommand ReloadCommand {get;}
    public ICommand CopyCommand {get;}
    public ICommand ApplyCommand {get;}
    public ICommand RestoreCommand {get;}
    public ICommand CloseCommand {get;}
    public ICommand ErrorCommand {get;}
    private int errorLine,errorColumn;
    public ScriptTransformViewModel(string target,string targetKind,string value,bool hasTarget,Func<string,string?> validate,Func<string,Task> apply,ScriptLibrary? library=null,PythonRunner? runner=null)
    {
        Target=target;TargetKind=targetKind;original=value;input=value;HasTarget=hasTarget;this.validate=validate;this.apply=apply;
        this.library=library??new(ScriptLibrary.DefaultRoot);this.runner=runner??new();
        RunCommand=Command(async()=>{execution=RunAsync();await execution;},()=>Idle&&this.runner.Available);
        StopCommand=Command(()=>{cancellation?.Cancel();return Task.CompletedTask;},()=>Busy);
        SaveCommand=Command(async()=>{await SaveAsync(false);},()=>Idle);
        SaveAsCommand=Command(async()=>{await SaveAsync(true);},()=>Idle);
        NewCommand=Command(async()=>{if(await ResolveDirtyAsync())Load(null);},()=>Idle);
        ReloadCommand=Command(async()=>{if(await ResolveDirtyAsync()){var id=selected?.Id;await ReadAsync();Load(all.FirstOrDefault(s=>s.Id==id)??all.FirstOrDefault());}},()=>Idle);
        DeleteCommand=Command(DeleteAsync,()=>Idle&&selected is {IsExample:false});
        CopyCommand=Command(()=>{CopyText(Result);return Task.CompletedTask;},()=>Idle&&success);
        ApplyCommand=Command(ApplyAsync,()=>Idle&&success&&HasTarget&&Result!=original&&validate(Result)==null);
        RestoreCommand=Command(()=>{Input=original;return Task.CompletedTask;},()=>Idle&&Input!=original);
        CloseCommand=Command(CloseAsync,()=>!choosing);
        ErrorCommand=Command(()=>{ErrorPositionRequested?.Invoke(errorLine,errorColumn);return Task.CompletedTask;},()=>errorLine>0);
    }
    public async Task InitializeAsync(){choosing=true;Refresh();try{await ReadAsync();Load(all.FirstOrDefault());}finally{choosing=false;Refresh();}}
    public async Task RefreshLibraryAsync()
    {
        if(!Idle||dirty||success)return;
        choosing=true;Refresh();
        try
        {
            var previous=selected;await ReadAsync();
            var current=all.FirstOrDefault(s=>s.Id==previous?.Id);
            if(previous!=current)Load(current??all.FirstOrDefault());
        }
        finally{choosing=false;Refresh();}
    }
    private async Task ReadAsync()
    {
        try{var loaded=await Task.Run(library.Load);all=loaded.Items;Status=loaded.Errors;Filter();}
        catch(Exception e){Status=e.Message;all=ScriptLibrary.Examples.ToList();Filter();}
    }
    private void Filter()
    {Scripts.Clear();foreach(var s in all.Where(s=>(s.Name+" "+s.Description).Contains(Search,StringComparison.OrdinalIgnoreCase)))Scripts.Add(s);Changed(nameof(Selected));}
    private void Load(ScriptDefinition? item)
    {
        selected=item;name=item?.Name??"새 스크립트";description=item?.Description??"";code=item?.Code??"# value를 변환해 result에 담아주세요\nresult = value";dirty=false;
        Invalidate();foreach(var p in new[]{nameof(Selected),nameof(Name),nameof(Description),nameof(Code),nameof(CodeLines)})Changed(p);
    }
    private async Task ChooseAsync(ScriptDefinition item)
    {
        choosing=true;Refresh();
        try{if(await ResolveDirtyAsync())Load(item);}catch(Exception e){Status=e.Message;}
        finally{choosing=false;Changed(nameof(Selected));Refresh();}
    }
    private ScriptDefinition Edited()=>new(1,selected?.Id??"",Name.Trim(),Description,Code,selected?.Revision??0,selected?.CreatedAt??default,default);
    public async Task<bool> SaveAsync(bool copy)
    {
        var previous=choosing;choosing=true;Refresh();
        try
        {
            var edited=Edited();var expected=selected;
            var saved=await Task.Run(()=>library.Save(edited,expected,copy));selected=saved;name=saved.Name;dirty=false;
            await ReadAsync();Changed(nameof(Name));Changed(nameof(Selected));Refresh();Status="스크립트를 보관함에 저장했습니다.";return true;
        }
        catch(Exception e){Status=e.Message;return false;}
        finally{choosing=previous;Refresh();}
    }
    private async Task<bool> ResolveDirtyAsync()
    {
        if(!dirty)return true;
        int answer=await Confirm("스크립트 변경", "저장하지 않은 코드 변경이 있습니다.",["저장","저장하지 않음","취소"]);
        return answer==1||answer==0&&await SaveAsync(false);
    }
    private async Task DeleteAsync()
    {
        var item=selected;if(item==null||item.IsExample)return;
        if(await Confirm("스크립트 삭제",item.Name+"을 삭제할까요? 현재 미저장 코드도 버려집니다.",["삭제","취소"])!=0)return;
        choosing=true;Refresh();
        try{await Task.Run(()=>library.Delete(item));await ReadAsync();Load(all.FirstOrDefault());}
        finally{choosing=false;Refresh();}
    }
    public async Task RunAsync()
    {
        if(busy)return;
        Invalidate();busy=true;Refresh();cancellation=new();var timer=System.Diagnostics.Stopwatch.StartNew();Status="실행 중 · 중지하거나 Esc를 누를 수 있습니다.";
        try
        {
            var response=await runner.RunAsync(Code,Input,cancellation.Token);
            if(cancellation.IsCancellationRequested){Status="실행을 중지했습니다.";return;}
            success=response.Success;Result=response.Text;kind=response.Kind;Log=response.Log;errorLine=response.Line;errorColumn=response.Column;
            Status=response.Success?$"✓ 변환 완료 · {timer.Elapsed.TotalSeconds:0.00}초 · 원본 유지":(errorLine>0?$"{errorLine}행 · ":"")+response.Error;
        }
        catch(Exception e){Status=e.Message;}
        finally{busy=false;cancellation.Dispose();cancellation=null;Refresh();}
    }
    private async Task ApplyAsync()
    {
        if(!success||!HasTarget||validate(Result)!=null)return;
        if(!await ResolveDirtyAsync())return;
        string value=Result;
        if(await Confirm("값에 적용",$"{Target}\n\n이전 값: {original}\n\n변환 결과: {(value.Length==0?"(빈 문자열)":value)}\n\n자료형: {TargetKind} 유지"+(Input!=original?"\n시험 입력을 수정한 결과입니다.":""),["값에 적용","취소"])!=0)return;
        if(validate(value) is {} error){Status=error;return;}
        choosing=true;Refresh();
        try{await apply(value);CloseRequested?.Invoke();}
        finally{choosing=false;Refresh();}
    }
    public async Task CloseAsync()
    {
        cancellation?.Cancel();if(execution!=null)await execution;
        if(await ResolveDirtyAsync())CloseRequested?.Invoke();
    }
    private void Invalidate(){success=false;Result="";kind="";Log="";errorLine=0;Status="미리보기를 실행하면 결과가 표시됩니다.";Refresh();}
    private void Refresh(){foreach(var n in new[]{nameof(Idle),nameof(Busy),nameof(SavedState),nameof(ApplyHint),nameof(ResultKind),nameof(RuntimeState)})Changed(n);CommandManager.InvalidateRequerySuggested();}
    private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    private bool Set<T>(ref T field,T value,[CallerMemberName]string? property=null){if(EqualityComparer<T>.Default.Equals(field,value))return false;field=value;Changed(property);return true;}
    private ICommand Command(Func<Task> action,Func<bool> can)=>new ScriptCommand(async()=>{try{await action();}catch(Exception e){Status=e.Message;}},can);
    private sealed class ScriptCommand(Func<Task> action,Func<bool> can):ICommand
    {
        private bool executing;
        public bool CanExecute(object? p)=>!executing&&can();
        public async void Execute(object? p){if(!CanExecute(p))return;executing=true;CommandManager.InvalidateRequerySuggested();try{await action();}finally{executing=false;CommandManager.InvalidateRequerySuggested();}}
        public event EventHandler? CanExecuteChanged{add=>CommandManager.RequerySuggested+=value;remove=>CommandManager.RequerySuggested-=value;}
    }
}
