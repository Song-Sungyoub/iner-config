using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
namespace InerConfig;
public partial class MainWindow
{
    private async Task TestStructureUi(string output)
    {
        async Task Shot(string name,FrameworkElement? target=null)
        {
            UpdateLayout();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            target??=this;target.UpdateLayout();var b=new RenderTargetBitmap((int)target.ActualWidth,(int)target.ActualHeight,96,96,PixelFormats.Pbgra32);b.Render(target);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(b));using var f=File.Create(Path.Combine(output,name));encoder.Save(f);
        }
        foreach(bool dark in new[]{false,true})
        {
            SetTheme(dark,false);
            await Shot(dark?"caption-dark.png":"caption-light.png");
            var preferences=BuildPreferences();preferences.Show();await Shot(dark?"preferences-dark.png":"preferences-light.png",preferences);
            if(Visuals<CheckBox>(preferences).Count()!=3||Visuals<RadioButton>(preferences).Count()!=2||!Visuals<CheckBox>(preferences).Any(c=>c.Content as string=="Python 스크립트 변환 (Beta)"&&c.IsChecked==ScriptEnabled))throw new Exception("Preferences controls");
            preferences.Close();
            var about=new AboutDialog(this);about.Show();await Shot("about-"+(dark?"dark":"light")+".png",about);about.Close();
            var menu=new ContextMenu {PlacementTarget=StructureButton};
            var chosen=new MenuItem{Header="단계 보기",IsCheckable=true,IsChecked=true};
            menu.Items.Add(new MenuItem{Header="편집기 보기 방식",IsEnabled=false});menu.Items.Add(chosen);menu.Items.Add(new Separator());menu.Items.Add(new MenuItem{Header="화면 예시로 보기 선택…"});
            menu.IsOpen=true;await Shot("chrome-menu-"+(dark?"dark":"light")+".png",menu);
            if(chosen.Template.FindName("Check",chosen) is not FrameworkElement check||check.Visibility!=Visibility.Visible)throw new Exception("Menu check style");
            menu.IsOpen=false;
            var scroll=new ScrollViewer{Width=250,Height=100,HorizontalScrollBarVisibility=ScrollBarVisibility.Visible,VerticalScrollBarVisibility=ScrollBarVisibility.Visible,Content=new Border{Width=750,Height=500,Background=(Brush)FindResource("Property")}};
            var test=new Window{Owner=this,Width=300,Height=170,Content=scroll,ShowInTaskbar=false};
            test.Show();await Shot("chrome-scroll-"+(dark?"dark":"light")+".png",test);
            scroll.ScrollToHorizontalOffset(80);scroll.ScrollToVerticalOffset(90);test.UpdateLayout();
            if(scroll.HorizontalOffset!=80||scroll.VerticalOffset!=90)throw new Exception("Styled scrolling");
            test.Close();
        }
        if(WindowButtons.HorizontalAlignment!=HorizontalAlignment.Right || !WindowButtons.Children.OfType<Button>().SequenceEqual(new[]{MinimizeButton,MaximizeButton,CloseWindowButton}))throw new Exception("Windows caption order");
        Maximize(this,new RoutedEventArgs());await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if(WindowState!=WindowState.Maximized || MaximizeButton.ToolTip as string!="복원")throw new Exception("Restore caption");
        Maximize(this,new RoutedEventArgs());await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if(WindowState!=WindowState.Normal || MaximizeButton.ToolTip as string!="최대화")throw new Exception("Maximize caption");
        SetTheme(false,false);
        var scrolling=new FileSession("JSON",System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0,150).ToDictionary(i=>i==0?new string('W',100):"flag"+i,i=>true)));
        sessions.Add(scrolling);Files.SelectedItem=scrolling;await Parse();
        foreach(var mode in Enum.GetValues<EditorView>())
        {
            sourceMode=false;ChangeView(mode,false);await Shot("boolean-"+mode+".png");
            ScrollViewer? GetScroll(DependencyObject root)
            {
                if(root is ScrollViewer scroll)return scroll;
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=GetScroll(VisualTreeHelper.GetChild(root,i));if(found!=null)return found;}
                return null;
            }
            var view=mode==EditorView.Groups?Groups:GetScroll(mode==EditorView.Tree?Tree:Stage)!;
            view.ScrollToVerticalOffset(mode==EditorView.Groups?700:45);UpdateLayout();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if(mode==EditorView.Stages)
            {
                AutoFitStage(1);if(stageWidths[1]<=600)throw new Exception("Auto fit includes offscreen long key");
                ResizeStage(1,StageWidth);SetSticky(true,false);UpdateLayout();
                await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);UpdateSticky();
                if(stickyLayer==null||stickyLayer.Children.Count==0||Stage.ColumnHeaderHeight<61)throw new Exception("Sticky parent missing");
                await Shot("sticky-scrolled.png");
                SetSticky(false,false);UpdateLayout();
            }
            double before=view.VerticalOffset;
            if(before<=0)throw new Exception("Boolean viewport fixture: "+mode);
            var node=document!.All.First(n=>n.Name=="flag50");SelectNode(node,false);
            var update=Apply(document.Change(node,"false"));
            if(mode==EditorView.Groups?GroupContent.Children.Count==0:mode==EditorView.Tree?Tree.Items.Count==0:Stage.Items.Count==0)throw new Exception("Boolean clears view while parsing");
            await update;await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if(Math.Abs(view.VerticalOffset-before)>0.1)throw new Exception("Boolean viewport moved: "+mode);
            if(document!.All.First(n=>n.Name=="flag50").Value!="false")throw new Exception("Boolean value update");
            await Apply(document.Change(document.All.First(n=>n.Name=="flag50"),"true"));
        }
        sessions.Remove(scrolling);
        var s=new FileSession("JSON","{\"Enabled\":true,\"devices\":[{\"Id\":1,\"Name\":\"Device-A\"}]}");
        sessions.Add(s);Files.SelectedItem=s;await Parse();
        if(!Source.IsReadOnly||CreateTypePicker(document!.Roots[0].Children[0]) is not ComboBox)throw new Exception("normal mode structure lock");
        var normalPicker=(ComboBox)CreateTypePicker(document!.Roots[0].Children[0]);
        normalPicker.SelectedItem="String";await Parse();
        if(document!.Roots[0].Children[0].Kind!="String"||s.StructureEditing)throw new Exception("Normal type change");
        normalPicker=(ComboBox)CreateTypePicker(document.Roots[0].Children[0]);var unchanged=s.Text;
        normalPicker.SelectedItem="Number";
        if(s.Text!=unchanged||normalPicker.SelectedItem as string!="String")throw new Exception("Normal invalid type rollback");
        normalPicker.SelectedItem="Boolean";await Parse();
        if(document!.Roots[0].Children[0].Kind!="Boolean")throw new Exception("Normal type restore");
        s.BeginStructure();await Parse();
        foreach(var mode in Enum.GetValues<EditorView>()){ChangeView(mode,false);await Shot("structure-"+mode+".png");}
        var originalWidth=Width;Width=900;ChangeView(EditorView.Stages,false);
        await Shot("toolbar-narrow-light.png");SetTheme(true,false);await Shot("toolbar-narrow-dark.png");SetTheme(false,false);Width=originalWidth;
        var parent=document!.All.First(n=>n.Kind=="Array");
        OpenAddPalette(parent,StructureButton);await Shot("structure-add-palette.png",(FrameworkElement)addPopup!.Child);addPopup.IsOpen=false;
        s.Draft=new(){ParentPath=parent.Path,Kind="Object",Entered=true,Children=[new(){Name="Name",Kind="String",Example="Device-A"}]};
        ShowAddDraft(StructureButton);await Shot("structure-add-draft.png",(FrameworkElement)addPopup!.Child);addPopup.IsOpen=false;
        bool rejected=false;try{BuildAddDraft();}catch{rejected=true;}if(!rejected)throw new Exception("placeholder saved");
        var restored=FileSession.Restore(s.Capture());if(restored.Draft!.Children[0].Entered)throw new Exception("placeholder restore");
        s.Draft.Children[0].Value="Device-B";s.Draft.Children[0].Entered=true;await CommitAddDraft();
        if(!s.Text.Contains("Device-B"))throw new Exception("list draft add");
        s.CancelStructure();await ShowSessionText();if(s.Text.Contains("Device-B"))throw new Exception("cancel draft addition");
        sessions.Remove(s);
        var csv=new FileSession("CSV","name,code\r\nDevice-A,001\r\nDevice-B,002\r\n"){CsvHeader=true};
        sessions.Add(csv);Files.SelectedItem=csv;await Parse();
        if(Csv.Columns[0].Header.ToString()!="name"||Csv.ClipboardCopyMode!=DataGridClipboardCopyMode.ExcludeHeader)throw new Exception("CSV header policy");
        await Shot("structure-csv-before.png");
        Csv.CurrentCell=new DataGridCellInfo(Csv.Items[0],Csv.Columns[0]);
        Csv.SelectedCells.Clear();
        Csv.SelectedCells.Add(new DataGridCellInfo(Csv.Items[0],Csv.Columns[0]));
        if(!Csv.SelectedCells.Contains(new DataGridCellInfo(Csv.Items[0],Csv.Columns[1])))Csv.SelectedCells.Add(new DataGridCellInfo(Csv.Items[0],Csv.Columns[1]));
        if(document==null||CsvRange()==null)throw new Exception("CSV selection missing: doc="+(document!=null)+" cells="+Csv.SelectedCells.Count+" items="+Csv.Items.Count+" current="+Csv.CurrentCell.IsValid);
        await PasteCsv(true);
        if(new ConfigDocument(csv.Text,"CSV").Roots[1].Children.Any(n=>n.Value!=""))throw new Exception("CSV range clear: "+csv.Text+" / "+Message.Text);
        csv.Undo();await ShowSessionText();if(!csv.Text.Contains("001"))throw new Exception("CSV range undo");
        await Shot("structure-csv.png");
        Csv.CurrentCell=new DataGridCellInfo(Csv.Items[0],Csv.Columns[1]);Csv.Focus();Csv.BeginEdit();Csv.UpdateLayout();
        if(Csv.Columns[1].GetCellContent(Csv.Items[0]) is not TextBox cellEditor)throw new Exception("CSV inline editor");
        cellEditor.Text="0007";
        Csv.CommitEdit(DataGridEditingUnit.Cell,true);
        await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await Parse();
        if(new ConfigDocument(csv.Text,"CSV").Roots[1].Children[1].Value!="0007"||csv.Input!=null)throw new Exception("CSV inline commit preserves string and clears draft");

        sessions.Remove(csv);
        File.WriteAllText(Path.Combine(output,"structure-ui-result.txt"),"PASS: mode lock, A/B/C add rows, placeholder rejection/recovery, list add/cancel, CSV headers/range clear/undo");
    }
}
