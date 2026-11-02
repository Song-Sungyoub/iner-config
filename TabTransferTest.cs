using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InerConfig;

public partial class MainWindow
{
    private async Task WaitForTabLoad()
    {
        for(int i=0;i<500&&tabContentPending;i++)await Task.Delay(20);
        if(tabContentPending)throw new Exception("Tab content transfer timed out");
    }
    public async Task RunTabTransferTest(string output)
    {
        closingForUpdate=true;Directory.CreateDirectory(output);
        ((App)Application.Current).Workspace=new WorkspaceStore(Path.Combine(output,"workspace"));
        void Check(bool value,string name){if(!value)throw new Exception("Tab transfer: "+name);}
        sessions.Add(new FileSession("JSON","{}"));Files.SelectedIndex=0;await Parse();
        await TestWindowTabs(output);
        var baseline=new FileSession("JSON","{\"small\":1}");
        sessions.Add(baseline);Files.SelectedItem=baseline;await Parse();ChangeView(EditorView.Stages,false);
        string largeText=JsonSerializer.Serialize(Enumerable.Range(0,3000).ToDictionary(i=>"value"+i,i=>i));
        var many=Enumerable.Range(0,40).Select(_=>new FileSession("JSON",largeText)).ToArray();
        foreach(var tab in many)sessions.Add(tab);
        UpdateLayout();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);var original=document;
        var item=(TabItem)Files.ItemContainerGenerator.ContainerFromItem(many[0]);
        TabMouseDown(Files,new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent,Source=item});
        Check(draggedTab==many[0]&&Session==baseline&&ReferenceEquals(document,original),"header press does not select or parse another file: dragged="+(draggedTab==many[0])+",selected="+(Session==baseline)+",parsed="+ReferenceEquals(document,original)+",container="+(item!=null)+",context="+(item?.DataContext is FileSession));
        draggedTab=null;Files.ReleaseMouseCapture();
        activeTabDrags++;
        var deferred=Parse(false,original);
        Check(!deferred.IsCompleted,"view rebuild waits for drag completion");
        activeTabDrags--;await deferred;
        foreach(bool dark in new[]{false,true})
        {
            SetTheme(dark,false);
            var preview=new TabDragPreview("large-example.json •","JSON");preview.Show();preview.UpdateLayout();
            Check(preview.IsVisible&&!preview.IsHitTestVisible&&!preview.ShowActivated&&!preview.ShowInTaskbar,"preview is visible without document or focus");
            var bitmap=new RenderTargetBitmap((int)preview.ActualWidth,(int)preview.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(preview);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var file=File.Create(Path.Combine(output,dark?"tab-preview-dark.png":"tab-preview-light.png")))encoder.Save(file);
            preview.Close();
        }
        var tabToMove=many[0];Files.SelectedItem=tabToMove;await Parse();
        tabToMove.Change(tabToMove.Text.Replace(":0,",":99,"));await ShowSessionText();
        Search.Text="value";sourceMode=true;SetView();
        var prepared=document!;
        var watch=System.Diagnostics.Stopwatch.StartNew();
        DetachTab(tabToMove);
        var target=Editors.Single(w=>w!=this&&w.sessions.Contains(tabToMove));
        Check(target.tabContentPending&&target.tabLoadingOverlay!=null,"new window loading is staged before content");
        target.Checkpoint();
        var recovered=target.Workspace!.LoadWindows([]).Single(w=>w.Id==target.workspaceId).Tabs.Single();
        Check(recovered.SourceMode&&recovered.SearchText=="value"&&recovered.Text==tabToMove.Text,"checkpoint during loading preserves incoming session state");
        await target.WaitForTabLoad();await WaitForTabLoad();watch.Stop();
        Check(ReferenceEquals(target.document,prepared),"selected parsed document reused without parsing again");
        Check(target.sessions.Count==1&&sessions.Count==40&&target.Session==tabToMove&&tabToMove.CanUndo,"one original session moved, forty remaining tabs preserved");
        Check(MoveTabHere(tabToMove,0),"merge accepted");await WaitForTabLoad();
        Check(ReferenceEquals(document,prepared)&&!Editors.Contains(target),"merge reuses parsed structure and closes emptied source");
        tabToMove.Undo();Check(tabToMove.Text==largeText,"history survives large transfer");
        foreach(var tab in sessions.ToArray())sessions.Remove(tab);
        File.WriteAllText(Path.Combine(output,"tab-transfer-result.txt"),"PASS: existing detach/merge/reorder/close regressions; 41 tabs, 3000 values, original session/history and parsed structure reused; deferred tab selection/view loading; light/dark non-activating previews. Transfer including rendering: "+watch.ElapsedMilliseconds+" ms (single local measurement).");
    }
}
