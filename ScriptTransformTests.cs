using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InerConfig.Models;

namespace InerConfig;
public partial class MainWindow
{
    private async Task TestScripts(string output)
    {
        int checks=0;
        void Check(bool ok,string name){if(!ok)throw new Exception("Script: "+name);checks++;}
        var betaPath=Path.Combine(output,"beta-test-"+Guid.NewGuid()+".txt");
        Check(!ReadScriptEnabled(betaPath),"Beta defaults off");
        File.WriteAllText(betaPath,"true");Check(ReadScriptEnabled(betaPath),"Beta enable persisted");
        File.WriteAllText(betaPath,"false");Check(!ReadScriptEnabled(betaPath),"Beta disable persisted");File.Delete(betaPath);
        var missingRunner=new PythonRunner(Path.Combine(output,"missing-python-"+Guid.NewGuid()));
        Check(!missingRunner.Available,"missing embedded runtime");
        try{await missingRunner.RunAsync("result = 1","",CancellationToken.None);throw new Exception("missing runtime accepted");}
        catch(IOException){Check(true,"missing runtime blocked");}
        var runner=new PythonRunner();
        async Task<PythonResult> Run(string code,string input="")=>await runner.RunAsync(code,input,CancellationToken.None);
        var ip=await Run(ScriptLibrary.Examples[0].Code,"236.44.46.45");
        Check(ip.Success&&ip.Text=="192.0.2.1","real embedded IP transform");
        var unicode="한글 \"\\\n\t 끝 ";Check((await Run("result = value",unicode)).Text==unicode,"Unicode and whitespace");
        Check((await Run("result = ''")).Success,"empty result");
        Check((await Run("result = 123456789012345678901234567890")).Text=="123456789012345678901234567890","integer precision");
        Check((await Run("result = True")).Text=="true","Boolean result");
        var syntax=await Run("x = 1\nresult = (");Check(!syntax.Success&&syntax.Line==2,"syntax line");
        var failure=await Run("x = 1\nresult = 1 / 0");Check(!failure.Success&&failure.Line==2,"runtime line");
        foreach(var code in new[]{"print('diagnostic')","result = None","result = {}","result = float('nan')","result = float('inf')","result = b'bytes'"})Check(!(await Run(code)).Success,"result contract "+code);
        Check((await Run("print('한글 로그')\nresult = value","ok")).Log.Contains("한글 로그"),"print separated");
        var multibyte=await Run("print('한글' * 5000)\nresult = 'ok'");Check(multibyte.Log.TrimEnd()==string.Concat(Enumerable.Repeat("한글",5000)),"UTF-8 diagnostic chunks");
        Check((await Run("result = input() ")).Success==false,"input has EOF");
        async Task Reject(string code,string expected,CancellationToken token=default)
        {
            try{await runner.RunAsync(code,"",token);throw new Exception("accepted "+expected);}
            catch(IOException e){Check(e.Message.Contains(expected),"reject "+expected+" actual="+e.Message);}
        }
        await Reject("while True: pass","5초");
        using(var cancel=new CancellationTokenSource(150))await Reject("while True: pass","중지",cancel.Token);
        await Reject("while True: print('x'*4096)","64 KiB");
        Check(!(await Run("result = 'x' * 1048577")).Success,"result size limit");
        Check(!(await Run("result = bytearray(512 * 1024 * 1024)")).Success,"memory limit");
        var child=await Run("import subprocess, sys\np = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'])\nresult = p.pid");
        bool exited;try{using var process=Process.GetProcessById(int.Parse(child.Text));exited=process.HasExited;}catch(ArgumentException){exited=true;}
        Check(child.Success&&exited,"child process terminated with parent completion");
        Check((await Run("result = value","after cancellation")).Text=="after cancellation","run after timeout/cancel");
        var root=Path.Combine(output,"script-library");var library=new ScriptLibrary(root);
        var saved=library.Save(ScriptLibrary.Examples[0],ScriptLibrary.Examples[0]);
        Check(!saved.IsExample&&saved.Revision==1,"example copy");
        var updated=library.Save(saved with{Name="저장 검사"},saved);
        Check(updated.Id==saved.Id&&updated.Revision==2,"update stable ID");
        try{library.Save(saved,saved);throw new Exception("lost update accepted");}catch(IOException){checks++;}
        try{library.Delete(saved);throw new Exception("stale delete accepted");}catch(IOException){checks++;}
        var copy=library.Save(updated,updated,true);Check(copy.Id!=updated.Id,"save as identity");
        File.WriteAllText(Path.Combine(root,"broken.json"),"bad json");
        Check(library.Load().Errors.Contains("broken.json"),"corrupt file isolated");
        library.Delete(copy);Check(!library.Load().Items.Any(s=>s.Id==copy.Id),"delete only selected");

        string? applied=null,copied=null;
        var model=new ScriptTransformViewModel("Setting.xml / ConfigPath / LocalIp","String","236.44.46.45",true,_=>null,value=>{applied=value;return Task.CompletedTask;},library,runner);
        var window=new ScriptTransformWindow(this,model,true);window.Show();
        for(int i=0;i<50&&model.Selected==null;i++)await Task.Delay(20);
        model.Confirm=(_,_,_)=>Task.FromResult(0);model.CopyText=s=>copied=s;
        await model.RunAsync();Check(model.Result==ip.Text&&model.ApplyCommand.CanExecute(null),"VM successful preview");
        model.CopyCommand.Execute(null);Check(copied==ip.Text&&applied==null,"copy without applying");
        model.Input="1";Check(!model.CopyCommand.CanExecute(null)&&!model.ApplyCommand.CanExecute(null),"stale input result invalidation");
        model.Input="236.44.46.45";await model.RunAsync();
        window.UpdateLayout();await Task.Delay(80);
        async Task Shot(string name)
        {
            window.UpdateLayout();await Task.Delay(50);
            var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name));png.Save(file);
        }
        await Shot("script-light.png");SetTheme(true,false);await Shot("script-dark.png");SetTheme(false,false);
        window.Width=820;window.Height=620;await Shot("script-small.png");
        model.ApplyCommand.Execute(null);await Task.Delay(50);Check(applied==ip.Text&&!window.IsVisible,"VM apply close");

        // Shared reference window accepts the script window and preserves behind-window scrolling.
        var referenceModel=new ScriptTransformViewModel("시험","String","",false,_=>"no target",_=>Task.CompletedTask,library,runner);
        var reference=new ScriptTransformWindow(this,referenceModel,false);
        var done=new TaskCompletionSource<bool>();reference.Closed+=(_,_)=>done.TrySetResult(true);
        var waiting=ShowReferenceWindow(reference,()=>{reference.Show();return done.Task;});
        Check(ReferenceEditor==reference,"reference lock shared with script window");
        await referenceModel.CloseAsync();await waiting;Check(ReferenceEditor==null,"reference guard released");
        foreach(var (format,text) in new[]{("JSON","{\"host\":\"old\"}"),("XML","<host>old</host>"),("YAML","host: old\n"),("INI","host=old\n"),("CSV","old,other\n")})
        {
            var tab=new FileSession(format,text);sessions.Add(tab);Files.SelectedItem=tab;await Parse();
            var node=document!.All.First(n=>n.Editable&&n.Value=="old");
            var editing=CreateScriptModel(node,library);editing.Code="result = 'changed'";editing.Confirm=(_,_,labels)=>Task.FromResult(labels.Length==3?1:0);
            await editing.RunAsync();Check(editing.ApplyCommand.CanExecute(null),format+" apply available");
            var closed=new TaskCompletionSource();editing.CloseRequested+=()=>closed.TrySetResult();editing.ApplyCommand.Execute(null);await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(tab.Text.Contains("changed")&&tab.CanUndo,format+" host apply/history");
            tab.Undo();await ShowSessionText();Check(tab.Text==text,format+" host undo");
            node=document!.All.First(n=>n.Editable&&n.Value=="old");
            var stale=CreateScriptModel(node,library);stale.Code="result = 'new'";await stale.RunAsync();tab.Change(tab.Text+" ");
            Check(!stale.ApplyCommand.CanExecute(null),format+" changed target blocked");
            sessions.Remove(tab);
        }
        var numberTab=new FileSession("JSON","{\"n\":1}");sessions.Add(numberTab);Files.SelectedItem=numberTab;await Parse();
        var number=CreateScriptModel(document!.All.First(n=>n.Name=="n"),library);number.Code="result = 'not a number'";await number.RunAsync();
        Check(number.CopyCommand.CanExecute(null)&&!number.ApplyCommand.CanExecute(null),"number remains number");
        numberTab.BeginStructure();number=CreateScriptModel(document!.All.First(n=>n.Name=="n"),library);number.Code="result = 2";number.Confirm=(_,_,labels)=>Task.FromResult(labels.Length==3?1:0);await number.RunAsync();
        var structured=new TaskCompletionSource();number.CloseRequested+=()=>structured.TrySetResult();number.ApplyCommand.Execute(null);await structured.Task.WaitAsync(TimeSpan.FromSeconds(3));numberTab.Undo();
        Check(numberTab.Text=="{\"n\":1}","structure edit undo");sessions.Remove(numberTab);

        var cancelModel=new ScriptTransformViewModel("시험","String","",false,_=>"",_=>Task.CompletedTask,library,runner);
        cancelModel.Code="while True: pass";cancelModel.Confirm=(_,_,_)=>Task.FromResult(1);cancelModel.RunCommand.Execute(null);
        await Task.Delay(100);await cancelModel.CloseAsync();Check(!cancelModel.Busy&&cancelModel.Status.Contains("중지"),"closing cancels actual execution");
        File.WriteAllText(Path.Combine(output,"script-test-result.txt"),$"PASS: {checks} script checks; real embedded Python, limits/cancellation/job children, library conflicts, VM and WPF renders");
    }
}
