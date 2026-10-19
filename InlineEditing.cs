using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace InerConfig;

public partial class MainWindow
{
    private bool inlineEnabled, inlineComposing, inlineSuspended;
    private string? inlinePath;
    private string inlineBaseline="";
    private bool inlineValue=true;
    private FileSession? inlineSession;
    private TextBox? inlineBox;
    private readonly Canvas inlineCanvas=new(){IsHitTestVisible=true,ClipToBounds=true};
    private readonly Border inlineFrame=new(){BorderThickness=new Thickness(1),Visibility=Visibility.Collapsed};
    private readonly string inlineFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"InerConfig","inline.txt");
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {var child=VisualTreeHelper.GetChild(root,i);if(child is T item)yield return item;foreach(var nested in Visuals<T>(child))yield return nested;}
    }
    private void InitializeInline()
    {
        try{inlineEnabled=File.ReadAllText(inlineFile)=="true";}catch{}
        Groups.Focusable=true;Panel.SetZIndex(inlineCanvas,5);EditorArea.Children.Add(inlineCanvas);inlineCanvas.Children.Add(inlineFrame);
        inlineFrame.SetResourceReference(BorderBrushProperty,"Blue");
        EditorArea.LayoutUpdated+=(_,_)=>PositionInline();
        PreviewMouseDown+=InlineMouse;
        PreviewTextInput+=(_,e)=>{
            if(!inlineEnabled||inlineBox!=null||sourceMode||Session?.Format=="CSV"||!EditorArea.IsKeyboardFocusWithin)return;
            if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)||string.IsNullOrEmpty(e.Text))return;
            var n=InlineNode();if(n?.Editable!=true||!inlineValue||n.Kind=="Boolean")return;
            BeginInline(n,e.Text);e.Handled=true;
        };
        TextCompositionManager.AddPreviewTextInputStartHandler(this,(_,e)=>{
            if(inlineBox!=null){inlineComposing=true;return;}
            if(inlineEnabled&&!sourceMode&&Session?.Format!="CSV"&&EditorArea.IsKeyboardFocusWithin&&inlineValue&&InlineNode() is {Editable:true} n)
            {BeginInline(n);inlineComposing=true;}
        });
        TextCompositionManager.AddPreviewTextInputHandler(this,(_,_)=>{if(inlineComposing)Dispatcher.BeginInvoke(()=>inlineComposing=false);});
    }
    private ConfigNode? InlineNode()=>document?.All.FirstOrDefault(n=>n.Path==(inlinePath??Session?.SelectedPath));
        private FrameworkElement? InlineAnchor()
    {
        if(inlinePath==null)return null;
        // Group rows have no value cell; keep the requested column for the next leaf.
        bool value=inlineValue&&InlineNode()?.Group!=true;
        if(editorView==EditorView.Tree)
        {
            var row=Visuals<DataGridRow>(Tree).FirstOrDefault(r=>r.Item is ConfigNode n&&n.Path==inlinePath);
            return row==null?null:Visuals<DataGridCell>(row).FirstOrDefault(c=>c.Column==Tree.Columns[value?1:0]);
        }
        var root=editorView==EditorView.Stages?(DependencyObject)Stage:Groups;
        return Visuals<Border>(root).FirstOrDefault(b=>b.Tag as string==(value?"inline-value":"inline-key")&&b.DataContext is ConfigNode n&&n.Path==inlinePath);
    }
    private void PositionInline()
    {
        if(!inlineEnabled||sourceMode||Session?.Format=="CSV"||inlinePath==null){inlineFrame.Visibility=Visibility.Collapsed;return;}
        if(inlineSession!=null&&inlineSession!=Session){EndInline(false);return;}
        var anchor=InlineAnchor();
        double top=editorView==EditorView.Stages?Stage.ColumnHeaderHeight:editorView==EditorView.Tree?Tree.ColumnHeaderHeight:0;
        if(double.IsNaN(top))top=35;
        Point point=anchor?.TranslatePoint(new Point(),EditorArea)??new Point(-1,-1);
        bool visible=anchor!=null&&anchor.IsVisible&&point.Y>=top-1&&point.Y+anchor.ActualHeight<=EditorArea.ActualHeight+1&&point.X>=0&&point.X<EditorArea.ActualWidth;
        if(!visible)
        {
            inlineFrame.Visibility=Visibility.Collapsed;
            if(inlineBox!=null&&!inlineSuspended){inlineSuspended=true;Keyboard.ClearFocus();Message.Text="입력 중인 값이 화면 밖에 있습니다 · F2/Enter 또는 입력 복원으로 돌아가기 · Esc 취소";}
            return;
        }
        if(inlineSuspended)return;
        Canvas.SetLeft(inlineFrame,point.X);Canvas.SetTop(inlineFrame,point.Y);
        inlineFrame.Width=Math.Max(1,Math.Min(anchor!.ActualWidth,EditorArea.ActualWidth-point.X));inlineFrame.Height=anchor.ActualHeight;
        inlineFrame.Visibility=Visibility.Visible;
        inlineFrame.IsHitTestVisible=inlineBox!=null;
    }
    private void SelectInline(ConfigNode n,bool value=true,bool reveal=true)
    {
        inlinePath=n.Path;inlineValue=value;SelectNode(n,reveal);
        EditorArea.UpdateLayout();if(reveal&&InlineAnchor() is {} anchor){anchor.BringIntoView(new Rect(0,0,Math.Min(180,anchor.ActualWidth),anchor.ActualHeight));EditorArea.UpdateLayout();}PositionInline();
        if(inlineBox==null){if(editorView==EditorView.Tree)Tree.Focus();else if(editorView==EditorView.Stages)Stage.Focus();else Groups.Focus();}
    }
    private void BeginInline(ConfigNode n,string? replacement=null)
    {
        if(!inlineEnabled||n.Group||!n.Editable||document==null||Session==null)return;
        if(inlineBox!=null&&!CommitInline())return;
        n=document.All.FirstOrDefault(item=>item.Path==n.Path)??n;
        if(Session.Input is {} other&&(other.Path!=n.Path||other.Baseline!=Session.Text))
        {Message.Text="미적용 입력을 먼저 복원하거나 취소하세요.";return;}
        if(n.Value.IndexOfAny(['\r','\n'])>=0){Edit(n,false,true);return;}
        if(n.Kind is "Boolean" or "Null"){SelectInline(n);return;}
        SelectInline(n);inlineSession=Session;inlineSuspended=false;
        var pending=Session.Input;inlineBaseline=pending?.Baseline??Session.Text;
        inlineBox=new TextBox{Text=replacement??pending?.Text??n.Value,Padding=new Thickness(10,0,4,0),VerticalContentAlignment=VerticalAlignment.Center,BorderThickness=new Thickness(0),AcceptsReturn=false,AcceptsTab=false};
        inlineBox.SetResourceReference(BackgroundProperty,"Surface");inlineBox.SetResourceReference(ForegroundProperty,"Text");
        inlineFrame.Child=inlineBox;inlineFrame.SetResourceReference(BorderBrushProperty,"Blue");
        inlineBox.TextChanged+=(_,_)=>{
            if(inlineSession==null)return;
            inlineSession.Input=new(n.Path,inlineBox!.Text,inlineBaseline,n.Kind);inlineSession.Notify();SyncStructure();
        };
        inlineBox.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Tab)e.Handled=true;};
        PositionInline();inlineBox.Focus();inlineBox.SelectAll();
        if(replacement!=null){inlineSession.Input=new(n.Path,replacement,inlineBaseline,n.Kind);inlineSession.Notify();inlineBox.CaretIndex=replacement.Length;}
        Message.Text="셀 편집 · ↑↓ 이전/다음 값 · Enter 다음 값 · Shift+Enter 이전 값 · Esc 취소 · Ctrl+S 저장";
    }
    private void EndInline(bool discard)
    {
        if(discard&&inlineSession!=null){inlineSession.Input=null;inlineSession.Notify();}
        inlineFrame.Child=null;inlineBox=null;inlineSession=null;inlineSuspended=false;inlineComposing=false;
        inlineFrame.SetResourceReference(BorderBrushProperty,"Blue");SyncStructure();
    }
    private bool CommitInline()
    {
        if(inlineBox==null)return true;
        var s=inlineSession;var n=InlineNode();
        try
        {
            if(s==null||s!=Session||document==null||n==null||!n.Editable||s.Text!=inlineBaseline||document.Text!=s.Text||s.Input is {} pending&&pending.Baseline!=s.Text)
                throw new InvalidOperationException("편집 대상 문서가 변경되었습니다. 입력을 취소하거나 복원 내용을 확인하세요.");
            var text=document.Change(n,inlineBox.Text);
            var parsed=new ConfigDocument(text,s.Format,s.Separator);
            var scrolls=Visuals<ScrollViewer>(EditorArea).Select(v=>(v,v.HorizontalOffset,v.VerticalOffset)).ToArray();
            EndInline(true);
            if(text!=s.Text)
            {
                s.Change(text);switching=true;Source.Text=text;switching=false;revision++;parseTimer.Stop();
                document=parsed;
                var saved=new Dictionary<string,string>();try{foreach(var old in new ConfigDocument(s.SavedText,s.Format,s.Separator).All)saved.TryAdd(old.Path,old.Value);}catch{}
                foreach(var item in parsed.All){item.Open=!s.Collapsed.Contains(item.Path);item.State=saved.TryGetValue(item.Path,out var old)&&old==item.Value?"":"변경";for(var p=item;p!=null;p=p.Parent)if(s.PendingDeletes.Contains(p.Path))item.PendingDelete=true;}
                Populate();EditorArea.UpdateLayout();
                foreach(var (v,x,y) in scrolls){v.ScrollToHorizontalOffset(x);v.ScrollToVerticalOffset(y);}
            }
            UpdateStatus();PositionInline();return true;
        }
        catch(Exception ex){inlineFrame.BorderBrush=Brushes.IndianRed;Message.Text=ex.Message+" · Esc: 현재 입력 취소";inlineBox?.Focus();return false;}
    }
    private void InlineMouse(object sender,MouseButtonEventArgs e)
    {
        if(!inlineEnabled||ReferenceEditor!=null)return;
        var origin=e.OriginalSource as DependencyObject;
        if(origin==null||inlineFrame.IsAncestorOf(origin)||origin==inlineFrame||Find<ScrollBar>(origin)!=null)return;
        if(inlineBox!=null&&Find<Button>(origin)==ContinueInputButton){inlineSuspended=false;RevealSelection();EditorArea.UpdateLayout();PositionInline();inlineBox.Focus();e.Handled=true;return;}
        if(inlineBox!=null&&!CommitInline()){e.Handled=true;return;}
        if(sourceMode||Session?.Format=="CSV"||!EditorArea.IsAncestorOf(origin))return;
        var element=origin as FrameworkElement;
        var n=element?.DataContext as ConfigNode??Find<DataGridRow>(origin)?.Item as ConfigNode;
        n=document?.All.FirstOrDefault(item=>item.Path==n?.Path);
        if(n==null)return;
        var cell=Find<DataGridCell>(origin);
        var value=editorView==EditorView.Tree?cell?.Column==Tree.Columns[1]:FindTagged(origin)=="inline-value";
        inlinePath=n.Path;inlineValue=value&&!n.Group;
        if(e.ChangedButton==MouseButton.Left&&Find<Button>(origin)==null&&Find<CheckBox>(origin)==null&&Find<ComboBox>(origin)==null)
        {
            SelectInline(n,value,false);
            if(e.ClickCount==2&&value){BeginInline(n);e.Handled=true;}
        }
    }
    private static string? FindTagged(DependencyObject? node)
    {
        while(node!=null){if(node is FrameworkElement f&&f.Tag is string tag&&tag.StartsWith("inline-"))return tag;node=node is Visual?VisualTreeHelper.GetParent(node):null;}return null;
    }
    private ConfigNode? NextInline(ConfigNode current,int direction)
    {
        var nodes=Tree.Items.OfType<ConfigNode>().Where(n=>n.Editable&&n.Kind is not ("Comment" or "Null")&&(editorView!=EditorView.Groups||n.Parent?.Path==current.Parent?.Path)).ToList();
        int index=nodes.FindIndex(n=>n.Path==current.Path);
        if(index<0)return null;int next=index+direction;return next>=0&&next<nodes.Count?nodes[next]:null;
    }
    private void CopyInlineSelection()
    {
        if(inlineBox==null||inlineBox.SelectionLength==0)return;
        try { inlineBox.Copy(); }
        catch(Exception ex) { Message.Text="복사하지 못했습니다: "+ex.Message; }
    }
    private bool InlineKeys(KeyEventArgs e)=>InlineKeys(e,Keyboard.Modifiers);
    private bool InlineKeys(KeyEventArgs e,ModifierKeys modifiers)
    {
        if(!inlineEnabled||ReferenceEditor!=null)return false;
        if(inlineBox!=null)
        {
            if(inlineComposing||e.Key==Key.ImeProcessed)return false;
            if(e.Key==Key.Escape){EndInline(true);if(InlineNode() is {} selected)SelectInline(selected);PositionInline();return true;}
            if(inlineSuspended)
            {if(e.Key is Key.Enter or Key.F2){inlineSuspended=false;RevealSelection();EditorArea.UpdateLayout();PositionInline();inlineBox?.Focus();return true;}return false;}
            if(modifiers==ModifierKeys.Control)
            {
                if(e.Key==Key.C){CopyInlineSelection();return true;}
                if(e.Key==Key.S){if(CommitInline())Save(Session,false);return true;}
                if(e.Key is not (Key.O or Key.F or Key.D0 or Key.NumPad0 or Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract))return false;
                if(!CommitInline())return true;
            }
            else if(e.Key is Key.Enter or Key.Tab || (e.Key is Key.Up or Key.Down)&&modifiers==ModifierKeys.None)
            {
                var current=InlineNode();var next=current==null?null:NextInline(current,e.Key==Key.Up?-1:e.Key==Key.Down?1:modifiers.HasFlag(ModifierKeys.Shift)?-1:1);
                if(!CommitInline())return true;
                if(InlineNode() is {} selected)SelectInline(selected,inlineValue,false);
                if(next!=null&&document?.All.FirstOrDefault(n=>n.Path==next.Path) is {} target&&Tree.Items.OfType<ConfigNode>().Any(n=>n.Path==target.Path))
                {SelectInline(target);if(target.Value.IndexOfAny(['\r','\n'])<0)BeginInline(target);}
                return true;
            }
            else return false;
        }
        if(sourceMode||Session?.Format=="CSV"||!EditorArea.IsKeyboardFocusWithin)return false;
        var node=InlineNode()??Tree.SelectedItem as ConfigNode;if(node==null)return false;
        if(e.Key is Key.Enter or Key.F2){if(!node.Group)BeginInline(node);else if(node.Group)ToggleBranch(node,false);return true;}
        if(e.Key==Key.Space&&inlineValue&&node.Kind=="Boolean"){ToggleBool(new CheckBox{DataContext=node},new());return true;}
        if(modifiers!=ModifierKeys.None&&modifiers!=ModifierKeys.Shift)return false;
        if(e.Key==Key.Tab){var next=NextInline(node,modifiers.HasFlag(ModifierKeys.Shift)?-1:1);if(next!=null)SelectInline(next);return true;}
        if(e.Key is Key.Left or Key.Right)
        {
            if(editorView==EditorView.Tree){if(!inlineValue&&node.Group&&node.Open==(e.Key==Key.Left))ToggleBranch(node,false);}
            else SelectInline(node,e.Key==Key.Right);
            return true;
        }
        if(e.Key is Key.Up or Key.Down)
        {
            var nodes=Tree.Items.OfType<ConfigNode>().Where(n=>n.Kind!="Add"&&(editorView!=EditorView.Groups||n.Parent?.Path==node.Parent?.Path)).ToList();
            int index=nodes.FindIndex(n=>n.Path==node.Path)+(e.Key==Key.Up?-1:1);
            if(index>=0&&index<nodes.Count)SelectInline(nodes[index],inlineValue);
            return true;
        }
        return false;
    }
}
