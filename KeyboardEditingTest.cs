using System.IO;
using System.Windows;
using System.Windows.Input;
namespace InerConfig;

public partial class MainWindow
{
    public async Task RunKeyboardTest(string output)
    {
        closingForUpdate=true;Directory.CreateDirectory(output);
        await TestKeyboardEditing(output);
        await TestKeyboardTypes(output);

    }
    private async Task TestKeyboardEditing(string output)
    {
        var wasEnabled=inlineEnabled;inlineEnabled=false;
        KeyEventArgs Stroke(Key key)=>new(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,key);
        async Task Idle(){for(int i=0;i<100&&keyboardStructureBusy;i++)await Task.Delay(20);if(keyboardStructureBusy)throw new Exception("Keyboard operation timeout");UpdateLayout();}
        foreach(var view in new[]{EditorView.Tree,EditorView.Stages,EditorView.Groups})
        {
            var s=new FileSession("JSON","{\"group\":{\"keep\":1},\"move\":2,\"last\":3}");
            sessions.Add(s);Files.SelectedItem=s;await Parse();ChangeView(view,false);UpdateLayout();
            var move=document!.All.First(n=>n.Name=="move");SelectInline(move);var original=s.Text;
            if(!KeyboardStructureKeys(Stroke(Key.F2),ModifierKeys.None)||inlineBox!=null||s.Text!=original)throw new Exception("F2 must require structure mode");
            s.BeginStructure();await Parse();SelectInline(document!.All.First(n=>n.Name=="move"));
            KeyboardStructureKeys(Stroke(Key.F2),ModifierKeys.None);
            if(inlineBox==null||!inlineKeyEditing||inlineBox.Text!="move")throw new Exception("F2 edits key "+view);
            inlineBox.Text="last";
            if(CommitInline()||inlineBox==null||!s.Text.Contains("\"move\":2"))throw new Exception("Duplicate key loses input");
            inlineBox!.Text="renamed";
            InlineKeys(Stroke(Key.Down),ModifierKeys.None);
            if(InlineNode()?.Name!="last"||inlineBox?.Text!="last"||!inlineKeyEditing||!s.Text.Contains("\"renamed\":2"))throw new Exception("Key down continuation "+view);
            InlineKeys(Stroke(Key.Up),ModifierKeys.None);
            if(InlineNode()?.Name!="renamed"||!inlineKeyEditing)throw new Exception("Key up continuation "+view);
            InlineKeys(Stroke(Key.Enter),ModifierKeys.None);
            if(inlineBox==null||inlineKeyEditing||inlineBox.Text!="2")throw new Exception("Key Enter switches to value "+view);
            inlineBox.Text="22";
            KeyboardStructureKeys(Stroke(Key.Tab),ModifierKeys.None);await Idle();
            if(InlineNode()?.Parent?.Name!="group"||InlineNode()?.Value!="22")throw new Exception("Tab commits value and indents "+view+" "+Message.Text);
            KeyboardStructureKeys(Stroke(Key.Tab),ModifierKeys.Shift);await Idle();
            if(InlineNode()?.Parent?.Name!="$"||InlineNode()?.Name!="renamed")throw new Exception("Shift Tab outdents "+view);
            var baseline=s.Text;
            KeyboardStructureKeys(Stroke(Key.Enter),ModifierKeys.Control);await Idle();
            if(inlineBox==null||!inlineKeyEditing||InlineNode()?.Name!="newKey")throw new Exception("Ctrl Enter starts key entry "+view+" "+Message.Text);
            var siblings=document!.Roots[0].Children;
            if(siblings.FindIndex(n=>n.Name=="newKey")!=siblings.FindIndex(n=>n.Name=="renamed")+1)throw new Exception("Sibling insertion position "+view);
            inlineBox.Text="newKey2";
            var restored=FileSession.Restore(s.Capture());
            if(restored.Input is not {KeyEditing:true,Text:"newKey2"})throw new Exception("Key draft recovery");
            EndInline(true);s.Undo();await ShowSessionText();
            if(s.Text!=baseline)throw new Exception("Undo keyboard insertion "+view);
            s.Redo();await ShowSessionText();
            if(!s.Text.Contains("newKey"))throw new Exception("Redo keyboard insertion");
            s.CancelStructure();sessions.Remove(s);inlinePath=null;
        }
        inlineEnabled=wasEnabled;
        File.WriteAllText(Path.Combine(output,"keyboard-test-result.txt"),"PASS: A/B/C F2 key editing, duplicate protection, up/down key continuation, Enter value, Tab commit/indent, Shift+Tab outdent, Ctrl+Enter sibling placement, draft recovery, undo/redo; structure mode required.");
    }
}
