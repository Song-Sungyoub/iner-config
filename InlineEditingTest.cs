using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
namespace InerConfig;
public partial class MainWindow
{
    private async Task TestInline(string output)
    {
        var enabled=inlineEnabled;inlineEnabled=true;
        var session=new FileSession("JSON","{\"left\":{\"a\":1,\"b\":2},\"right\":{\"c\":3}}");
        sessions.Add(session);Files.SelectedItem=session;await Parse();
        foreach(var view in new[]{EditorView.Tree,EditorView.Stages,EditorView.Groups})
        {
            ChangeView(view,false);UpdateLayout();
            if(view!=EditorView.Groups)
            {
                foreach(bool value in new[]{true,false})
                {
                    SelectInline(document!.All.First(n=>n.Name=="b"),value);
                    foreach(var expected in new[]{"right","c"})
                    {
                        InlineKeys(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Down),ModifierKeys.None);
                        if(InlineNode()?.Name!=expected||inlineValue!=value||InlineAnchor()==null)
                            throw new Exception("Column preserved across group "+view+" "+expected);
                    }
                    InlineKeys(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Up),ModifierKeys.None);
                    InlineKeys(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Up),ModifierKeys.None);
                    if(InlineNode()?.Name!="b"||inlineValue!=value)throw new Exception("Column preserved upward "+view);
                }
            }
            var a=document!.All.First(n=>n.Name=="a");var b=document.All.First(n=>n.Name=="b");
            SelectInline(a);BeginInline(a);UpdateLayout();
            if(inlineBox==null||inlineFrame.Visibility!=Visibility.Visible)throw new Exception("Inline editor visibility "+view);
            string? copied=null;
            DataObject.AddCopyingHandler(inlineBox,(_,e)=>{copied=e.DataObject.GetData(DataFormats.UnicodeText) as string;e.CancelCommand();});
            var editingBox=inlineBox;
            foreach(var key in new[]{Key.LeftCtrl,Key.RightCtrl,Key.Left,Key.Right,Key.Home,Key.End})
            {
                if(InlineKeys(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,key),ModifierKeys.Control)||inlineBox!=editingBox||inlineBox.SelectionLength!=1)
                    throw new Exception("Ctrl preserves editing and selection "+view+" "+key);
            }
            if(!InlineKeys(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.C),ModifierKeys.Control)||copied!="1"||inlineBox!=editingBox)
                throw new Exception("Ctrl then C copies without ending edit "+view);
            ApplicationCommands.Copy.Execute(null,Keyboard.FocusedElement);
            if(copied!="1")throw new Exception("Inline copy selection "+view+" focus="+Keyboard.FocusedElement+" enabled="+ApplicationCommands.Copy.CanExecute(null,Keyboard.FocusedElement));
            inlineBox.Text="236.44.46.45";inlineBox.Select(4,2);
            Stage.Focus();copied=null;CopyInlineSelection();
            if(copied!="44"||inlineBox.Text!="236.44.46.45"||session.Text.Contains("236.44"))throw new Exception("Inline partial copy preserves draft "+view);
            inlineBox.SelectAll();copied=null;CopyInlineSelection();
            if(copied!="236.44.46.45")throw new Exception("Inline full copy "+view);
            inlineBox.Select(0,0);copied=null;CopyInlineSelection();
            if(copied!=null)throw new Exception("Empty selection overwrites clipboard "+view);
            inlineBox.Focus();
            inlineBox.Text="invalid";
            if(CommitInline()||inlineBox==null||!session.Text.Contains("\"a\":1"))throw new Exception("Invalid input lost");
            inlineBox.Text="11";
            if(session.Input?.Text!="11")throw new Exception("Draft capture");
            var restored=FileSession.Restore(session.Capture());
            if(restored.Input?.Text!="11")throw new Exception("Draft recovery");
            if(!CommitInline()||document!.All.First(n=>n.Name=="a").Value!="11")throw new Exception("Inline commit "+view+" "+Message.Text+" "+session.Text);
            b=document.All.First(n=>n.Name=="b");
            if((NextInline(b,1)==null)!=(view==EditorView.Groups))throw new Exception("Group boundary "+view);
            BeginInline(b);inlineBox!.Text="99";EndInline(true);
            if(document.All.First(n=>n.Name=="b").Value!="2"||session.Input!=null)throw new Exception("Escape discards only draft");
            session.Undo();await ShowSessionText();
        }
        ChangeView(EditorView.Stages,false);SelectInline(document!.All.First(n=>n.Name=="a"));
        BeginInline(InlineNode()!);inlineBox!.Text="123";UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"inline-edit.png")))encoder.Save(file);
        EndInline(true);
        var first=document!.All.First(n=>n.Name=="a");BeginInline(first);inlineBox!.Text="55";
        var enter=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent};
        if(!InlineKeys(enter)||InlineNode()?.Name!="b"||inlineBox==null||inlineBox.SelectionLength!=1)throw new Exception("Enter continuous editing");
        foreach(var mode in new[]{EditorView.Tree,EditorView.Stages,EditorView.Groups})
        {
            EndInline(true);ChangeView(mode,false);BeginInline(document!.All.First(n=>n.Name=="a"));
            inlineBox!.Text="bad";
            InlineKeys(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Down));
            if(InlineNode()?.Name!="a"||inlineBox?.Text!="bad")throw new Exception("Arrow invalid input protection");
            inlineBox!.Text="56";InlineKeys(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Down));
            if(InlineNode()?.Name!="b"||inlineBox==null)throw new Exception("Arrow down "+mode);
            inlineBox.Text="22";InlineKeys(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Up));
            if(InlineNode()?.Name!="a"||inlineBox==null||document!.All.First(n=>n.Name=="b").Value!="22")throw new Exception("Arrow up "+mode);
        }
        EndInline(true);
        var csv=new FileSession("CSV","one,two\nthree,four\n");sessions.Add(csv);Files.SelectedItem=csv;await Parse();UpdateLayout();
        Csv.CurrentCell=new System.Windows.Controls.DataGridCellInfo(Csv.Items[0],Csv.Columns[0]);Csv.ScrollIntoView(Csv.Items[0]);Csv.Focus();Csv.BeginEdit();UpdateLayout();
        var box=Visuals<System.Windows.Controls.TextBox>(Csv).FirstOrDefault();
        if(box==null)throw new Exception("CSV edit start");box.Text="changed";
        box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        for(int i=0;i<30&&(document==null||document.Text!=csv.Text||FindFocusedCsv()==null);i++)await Task.Delay(30);
        await Task.Delay(100);
        if(!csv.Text.StartsWith("changed,")||Csv.Items.IndexOf(Csv.CurrentCell.Item)!=1||FindFocusedCsv()==null)throw new Exception("CSV Enter continuation "+csv.Text+" row="+Csv.Items.IndexOf(Csv.CurrentCell.Item)+" focus="+Keyboard.FocusedElement+" "+Message.Text);
        FindFocusedCsv()!.Text="up-edited";
        FindFocusedCsv()!.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Up){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        await Task.Delay(300);
        if(Csv.Items.IndexOf(Csv.CurrentCell.Item)!=0||FindFocusedCsv()==null||!csv.Text.Contains("up-edited"))throw new Exception("CSV arrow up");
        FindFocusedCsv()!.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Down){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        await Task.Delay(300);
        if(Csv.Items.IndexOf(Csv.CurrentCell.Item)!=1||FindFocusedCsv()==null)throw new Exception("CSV arrow down");
        Csv.CancelEdit();sessions.Remove(csv);
        var longSession=new FileSession("JSON",System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0,160).ToDictionary(i=>"value"+i,i=>i)));
        sessions.Add(longSession);Files.SelectedItem=longSession;await Parse();ChangeView(EditorView.Stages,false);UpdateLayout();
        var top=document!.All.First(n=>n.Name=="value0");BeginInline(top);inlineBox!.Text="800";
        var scroll=Visuals<System.Windows.Controls.ScrollViewer>(Stage).First();scroll.ScrollToEnd();UpdateLayout();PositionInline();
        if(!inlineSuspended||inlineFrame.Visibility!=Visibility.Collapsed||longSession.Input?.Text!="800")throw new Exception("Scroll draft suspension");
        InlineKeys(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        if(inlineSuspended||inlineBox?.Text!="800"||inlineFrame.Visibility!=Visibility.Visible)throw new Exception("Scroll resume");
        longSession.Change(longSession.Text+" ");inlineBox!.Text="801";
        if(CommitInline()||longSession.Input?.Text!="801")throw new Exception("Stale draft protection");
        EndInline(true);sessions.Remove(longSession);
        sessions.Remove(session);inlinePath=null;inlineEnabled=enabled;
        File.WriteAllText(Path.Combine(output,"inline-test-result.txt"),"PASS: A/B/C inline placement, validation, draft recovery, commit, cancel, group boundaries, Enter continuation, CSV next-row editing, scroll suspension/resume, stale draft protection");
    }
}
