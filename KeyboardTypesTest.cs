using System.IO;
using System.Windows;
using System.Windows.Input;
namespace InerConfig;

public partial class MainWindow
{
    private async Task TestKeyboardTypes(string output)
    {
        KeyEventArgs Stroke(Key key)=>new(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,key);
        void Check(bool ok,string message){if(!ok)throw new Exception("Keyboard types: "+message);}
        var wasEnabled=inlineEnabled;inlineEnabled=false;
        foreach(var format in new[]{"JSON","YAML"})
        foreach(var view in new[]{EditorView.Tree,EditorView.Stages,EditorView.Groups})
        {
            var s=new FileSession(format,"{\"group\":{\"value\":\"12\",\"flag\":true}}");
            sessions.Add(s);Files.SelectedItem=s;await Parse();ChangeView(view,false);UpdateLayout();
            var node=document!.All.First(n=>n.Name=="value");SelectInline(node);
            KeyboardTypeKeys(Stroke(Key.Down),ModifierKeys.Alt);
            Check(typeChoice==null&&s.Text.Contains("\"12\""),"requires structure mode");
            s.BeginStructure();await Parse();node=document!.All.First(n=>n.Name=="value");BeginInline(node);
            var box=inlineBox!;box.Text="34";box.Select(0,1);var before=s.Text;
            Check(KeyboardTypeKeys(Stroke(Key.Down),ModifierKeys.Alt),"Alt down handled");
            Check(typeChoice?.Choices[typeChoiceIndex]=="Number"&&s.Text==before&&inlineBox==box&&box.Text=="34","selection must not apply "+view);
            Check(typeChoicePopup?.IsOpen==true,"type popup requested");
            KeyboardTypeKeys(Stroke(Key.Up),ModifierKeys.Alt);
            Check(typeChoice?.Choices[typeChoiceIndex]=="String","Alt up reverses selection");
            KeyboardTypeKeys(Stroke(Key.Escape),ModifierKeys.None);
            Check(typeChoice==null&&inlineBox==box&&box.Text=="34"&&box.SelectionLength==1&&s.Text==before,"Escape preserves draft/selection");
            KeyboardTypeKeys(Stroke(Key.Down),ModifierKeys.Alt);await ApplyTypeChoice();
            Check(document!.All.First(n=>n.Name=="value").Kind=="Number"&&inlineBox?.Text=="34","draft converted on Enter "+view);
            EndInline(true);s.Undo();await ShowSessionText();
            Check(s.Text==before,"conversion is a single undo");s.Redo();await ShowSessionText();
            Check(document!.All.First(n=>n.Name=="value").Kind=="Number","conversion redo");
            node=document.All.First(n=>n.Name=="value");BeginInline(node);inlineBox!.Text="invalid";
            KeyboardTypeKeys(Stroke(Key.Down),ModifierKeys.Alt); // Number -> Boolean
            before=s.Text;await ApplyTypeChoice();
            Check(typeChoice!=null&&inlineBox?.Text=="invalid"&&s.Text==before&&s.Input?.Text=="invalid","invalid conversion retains draft");
            inlineBox!.Text="true";await ApplyTypeChoice();
            Check(document!.All.First(n=>n.Name=="value").Kind=="Boolean"&&typeChoice==null,"retry succeeds");
            KeyboardTypeKeys(Stroke(Key.Down),ModifierKeys.Alt); // Boolean -> Null
            Check(s.Text.Contains("true"),"Null selection remains pending");
            await ApplyTypeChoice();Check(document!.All.First(n=>n.Name=="value").Kind=="Null","Null conversion");
            SelectInline(document.Roots[0]);KeyboardTypeKeys(Stroke(Key.Down),ModifierKeys.Alt);
            Check(typeChoice==null,"group conversion blocked");
            s.CancelStructure();sessions.Remove(s);inlinePath=null;
        }
        var xml=new FileSession("XML","<root><value>hello</value></root>");sessions.Add(xml);Files.SelectedItem=xml;await Parse();xml.BeginStructure();await Parse();
        BeginInline(document!.All.First(n=>n.Name=="value"));inlineBox!.Text="]]>";
        KeyboardTypeKeys(Stroke(Key.Down),ModifierKeys.Alt);await ApplyTypeChoice();
        Check(typeChoice?.Choices[typeChoiceIndex]=="CDATA"&&inlineBox?.Text=="]]>"&&!xml.Text.Contains("CDATA"),"invalid CDATA retained");
        inlineBox!.Text="hello <world>";await ApplyTypeChoice();
        Check(document!.All.First(n=>n.Name=="value").DisplayKind=="CDATA"&&xml.Text.Contains("<![CDATA[hello <world>]]>"),"XML CDATA applied");
        EndInline(true);xml.CancelStructure();sessions.Remove(xml);inlinePath=null;inlineEnabled=wasEnabled;
        File.WriteAllText(Path.Combine(output,"keyboard-types-result.txt"),"PASS: JSON/YAML A/B/C Alt up/down pending selection, Escape preserves draft/caret, Enter conversion, invalid value/retry, Null, group/mode guards, single undo/redo, XML CDATA validation.");
    }
}
