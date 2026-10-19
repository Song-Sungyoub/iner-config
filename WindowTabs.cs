using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;

namespace InerConfig;

public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct ScreenPoint { public int X; public int Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out ScreenPoint point);
    private Point tabDragStart;
    private FileSession? draggedTab;
    private bool tabDragCancelled;
    private static readonly SemaphoreSlim openGate = new(1, 1);
    internal static IEnumerable<MainWindow> Editors => Application.Current.Windows.OfType<MainWindow>();
    internal void BringForward()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
    private void TabMouseDown(object sender, MouseButtonEventArgs e)
    {
        draggedTab = Find<Button>(e.OriginalSource as DependencyObject) == null
            ? Find<TabItem>(e.OriginalSource as DependencyObject)?.DataContext as FileSession : null;
        tabDragStart = e.GetPosition(Files);
        if (draggedTab != null) { Files.SelectedItem = draggedTab; Files.CaptureMouse(); }
    }
    private void TabMouseUp(object sender, MouseButtonEventArgs e) { draggedTab = null; Files.ReleaseMouseCapture(); }
    private void TabMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || draggedTab == null) return;
        var point = e.GetPosition(Files);
        if (Math.Abs(point.X - tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - tabDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var tab = draggedTab; draggedTab = null; tabDragCancelled = false;
        Files.ReleaseMouseCapture();
        var data = new DataObject(); data.SetData(typeof(FileSession), tab);
        var effect = DragDrop.DoDragDrop(Files, data, DragDropEffects.Move);
        FinishTabDrag(tab, effect);
    }
    private void FinishTabDrag(FileSession tab, DragDropEffects effect)
    {
        // A successful merge can close this window inside the OLE drag loop.
        if (effect != DragDropEffects.None || tabDragCancelled || !sessions.Contains(tab) || PresentationSource.FromVisual(Files) == null) return;
        // WPF's cached mouse position can still point to the tab after the OLE drag loop returns.
        var point = GetCursorPos(out var screen) ? Files.PointFromScreen(new Point(screen.X, screen.Y)) : tabDragStart;
        if (!new Rect(Files.RenderSize).Contains(point)) DetachTab(tab);
    }
    private void TabDragContinue(object sender, QueryContinueDragEventArgs e)
    {
        if (e.EscapePressed || (e.KeyStates & DragDropKeyStates.RightMouseButton) != 0) tabDragCancelled = true;
    }
    private void TabDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(FileSession))) return;
        e.Effects = DragDropEffects.Move; e.Handled = true;
    }
    private void OutsideTabDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(FileSession)) && !new Rect(Files.RenderSize).Contains(e.GetPosition(Files)))
        {
            e.Effects = DragDropEffects.None; e.Handled = true;
        }
    }
    private void TabDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(FileSession)) is not FileSession tab) return;
        var before = Find<TabItem>(e.OriginalSource as DependencyObject)?.DataContext as FileSession;
        var index = before == null ? sessions.Count : sessions.IndexOf(before);
        if (before != null && Find<TabItem>(e.OriginalSource as DependencyObject) is { } item && e.GetPosition(item).X > item.ActualWidth / 2) index++;
        MoveTabHere(tab, index); e.Effects = DragDropEffects.Move; e.Handled = true;
    }
    private void MoveTabHere(FileSession tab, int index)
    {
        var owner = Editors.FirstOrDefault(w => w.sessions.Contains(tab));
        if (owner == null || owner.ReferenceEditor!=null || ReferenceEditor!=null) return;
        if (owner == this)
        {
            var old = sessions.IndexOf(tab); if (index > old) index--;
            sessions.Move(old, Math.Clamp(index, 0, sessions.Count - 1)); Files.SelectedItem = tab; return;
        }
        // Move the original session so unsaved text, disk fingerprint, encoding and history stay intact.
        if (owner.Session == tab) { tab.SearchText = owner.Search.Text; tab.SourceMode = owner.sourceMode; }
        sessions.Insert(Math.Clamp(index, 0, sessions.Count), tab); Files.SelectedItem = tab;
        owner.sessions.Remove(tab);
        if (owner.sessions.Count == 0)
        {
            try { Checkpoint(); }catch(Exception ex){Error(ex);return;}
            if (Application.Current.MainWindow == owner) Application.Current.MainWindow = this;
            owner.Close();
        }
        BringForward();
    }
    private void DetachTab(FileSession tab)
    {
        var target = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = Left + 32, Top = Top + 32 };
        target.editorView = editorView; target.SetZoom(zoomPercent); target.Show(); target.MoveTabHere(tab, 0);
    }
    private void TabContextMenu(object sender, MouseButtonEventArgs e)
    {
        if (Find<TabItem>(e.OriginalSource as DependencyObject)?.DataContext is not FileSession tab) return;
        Files.SelectedItem = tab;
        var menu = new ContextMenu(); var detach = new MenuItem { Header = "새 창으로 이동" };
        detach.Click += (_, _) => DetachTab(tab); menu.Items.Add(detach);
        foreach (var window in Editors.Where(w => w != this).ToArray())
        {
            var move = new MenuItem { Header = "다른 창으로 이동 · " + (window.Session?.Title ?? "빈 창") };
            move.Click += (_, _) => window.MoveTabHere(tab, window.sessions.Count); menu.Items.Add(move);
        }
        if(tab.FilePath is string path)
        {
            menu.Items.Add(new Separator());
            var copy=new MenuItem{Header="파일 경로 복사"};copy.Click+=(_,_)=>{try{Clipboard.SetText(path);}catch(Exception ex){Error(ex);}};menu.Items.Add(copy);
            var folder=new MenuItem{Header="파일 위치 열기"};folder.Click+=(_,_)=>{try{var start=new System.Diagnostics.ProcessStartInfo("explorer.exe"){UseShellExecute=true};start.Arguments="/select,\\\""+path+"\\\"";System.Diagnostics.Process.Start(start);}catch(Exception ex){Error(ex);}};menu.Items.Add(folder);
        }
        menu.Items.Add(new Separator());
        var closeAll = new MenuItem { Header = "모든 탭 닫기" };
        closeAll.Click += (_, _) => CloseAllTabs();
        menu.Items.Add(closeAll);
        menu.PlacementTarget = Files; menu.IsOpen = true; e.Handled = true;
    }
    private bool CloseAllTabs(Func<FileSession, bool>? confirm = null)
    {
        if (ReferenceEditor != null) { ReferenceEditor.Activate(); return false; }
        if (inlineBox != null && !CommitInline()) return false;
        CaptureCellInput();
        var tabs = sessions.ToArray();
        // Confirm every tab first, so Cancel leaves all tabs open.
        foreach (var tab in tabs) if (!(confirm ?? MayClose)(tab)) return false;
        foreach (var tab in tabs) sessions.Remove(tab);
        try { Checkpoint(); } catch (Exception ex) { Error(ex); }
        return true;
    }
    private async Task TestWindowTabs(string output)
    {
        var file = System.IO.Path.Combine(output, "tab-open.json");
        System.IO.File.WriteAllText(file, "{\"host\":\"original\"}");
        var count = sessions.Count;
        await Task.WhenAll(OpenPathAsync(file), OpenPathAsync(file));
        if (sessions.Count != count + 1) throw new Exception("Duplicate concurrent open");
        var tab = Session!;tab.Change("{\"host\":\"unsaved\"}");await ShowSessionText();
        SelectNode(document!.All.First(n=>n.Name=="host"),false);Search.Text="host";sourceMode=true;SetView();
        var path=tab.SelectedPath;
        DetachTab(tab);
        var target=Editors.Single(w=>w!=this&&w.sessions.Contains(tab));await target.Parse();
        if(sessions.Contains(tab)||target.Session!=tab||!tab.Dirty||!tab.CanUndo||target.Search.Text!="host"||!target.sourceMode||tab.SelectedPath!=path)throw new Exception("Tab detach state");
        await OpenPathAsync(file);
        if(sessions.Count!=count||target.sessions.Count!=1)throw new Exception("Duplicate open across windows");
        MoveTabHere(tab,0);await Parse();
        if(PresentationSource.FromVisual(target.Files)!=null)throw new Exception("Merged source window still connected");
        target.FinishTabDrag(tab,DragDropEffects.Move);
        target.FinishTabDrag(tab,DragDropEffects.None);
        if(Editors.Contains(target)||sessions[0]!=tab||Session!=tab||!tab.CanUndo||Search.Text!="host"||!sourceMode)throw new Exception("Tab merge and last-window close");
        MoveTabHere(tab,sessions.Count);
        if(sessions[^1]!=tab)throw new Exception("Tab reorder");
        tab.Undo();if(tab.Text!="{\"host\":\"original\"}")throw new Exception("Undo after tab transfer");
        await ShowSessionText();
        var beforeClose = sessions.ToArray();
        if (CloseAllTabs(_ => false) || !sessions.SequenceEqual(beforeClose)) throw new Exception("Close all cancel preserves tabs");
        var confirmed = new List<FileSession>();
        if (!CloseAllTabs(s => { confirmed.Add(s); return true; }) || sessions.Count != 0 || !confirmed.SequenceEqual(beforeClose)) throw new Exception("Close all confirms and removes current window tabs");
        if (!CloseAllTabs(_ => throw new Exception("Empty window confirmation"))) throw new Exception("Close all empty window");
    }
}
