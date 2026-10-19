using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace InerConfig;
public partial class MainWindow : Window
{
    private readonly ObservableCollection<FileSession> sessions=[];
    private static bool updateRunning;
    private bool closingForUpdate;
    public async Task CheckUpdates(bool manual = false)
    {
        if (!UpdateService.IsConfigured) { if (manual) Message.Text="InerConfig 배포 채널을 준비 중입니다."; return; }
        if (updateRunning || Editors.Any(w=>w.ReferenceEditor!=null)) return;
        if (!UpdateService.IsInstalled) { if (manual) MessageBox.Show(this,"자동 업데이트는 기본 경로에 설치한 앱에서 사용할 수 있습니다. GitHub Releases의 최신 MSI를 설치하세요.","업데이트"); return; }
        updateRunning = true;
        var accepted = false;
        try
        {
            if (manual) Message.Text = "업데이트 확인 중…";
            var package = await UpdateService.FindLatest();
            if (!IsLoaded || Editors.Any(w=>w.ReferenceEditor!=null)) return;
            if (package == null) { if (manual) Message.Text = $"InerConfig {UpdateService.Current} · 최신 버전입니다."; return; }
            if (new UpdateDialog(this, UpdateService.Current, package.Version).ShowDialog() != true) return;
            accepted = true;
            Message.Text = "업데이트 파일 다운로드·검증 중…";
            var local = await UpdateService.Prepare(package);
            if (!IsLoaded || Editors.Any(w=>w.ReferenceEditor!=null)) return;
            var windows = Editors.ToArray();
            if(Workspace==null)throw new IOException("작업 복원 저장소를 사용할 수 없어 업데이트를 중단했습니다. 파일을 저장한 뒤 다시 실행하세요.");
            foreach (var editor in windows) editor.Checkpoint();
            UpdateService.Install(local, package.Version);
            foreach (var editor in windows) { editor.closingForUpdate = true; editor.Close(); }
        }
        catch (Exception ex) { if ((manual || accepted) && IsLoaded) Message.Text="업데이트를 확인하거나 준비하지 못했습니다: " + ex.Message; }
        finally { updateRunning = false; }
    }
    private async void UpdateClick(object sender,RoutedEventArgs e) => await CheckUpdates(true);
    public void ShowUpdateResult() { try { var result=UpdateService.Result(); if(result!=null) Message.Text=result; } catch { } }
    private FileSession? Session => Files.SelectedItem as FileSession;
    private ConfigDocument? document;
    private bool switching,sourceMode;
    private int revision;
    private readonly DispatcherTimer parseTimer=new(){Interval=TimeSpan.FromMilliseconds(350)};
    private readonly List<ConfigNode> csvRows=[];
    private readonly string themeFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"InerConfig","theme.txt");
    public MainWindow()
    {
        InitializeComponent();Tree.LoadingRow+=(_,e)=>{RowHover.Attach(e.Row);if(e.Row.Item is ConfigNode node)AttachMove(e.Row,node);};Csv.LoadingRow+=(_,e)=>RowHover.Attach(e.Row);InitializeCsvEditing();Files.ItemsSource=sessions;LoadView();InitializeRecovery();InitializeInline();
        Activated += (_, _) => ((App)Application.Current).ActiveEditor = this;
        Closed += (_, _) => { parseTimer.Stop(); revision++; };
        parseTimer.Tick+=async(_,_)=>{parseTimer.Stop();await Parse();};
        PreviewKeyDown+=Keys;
        bool dark=false;try{dark=File.Exists(themeFile)&&File.ReadAllText(themeFile)=="dark";}catch{ }SetTheme(dark,false);
        Status.Text="JSON · XML · CSV · YAML · INI  |  InerConfig 0.1";
    }
    public async void OpenPath(string path) => await OpenPathAsync(path);
    public async Task OpenPathAsync(string path)
    {
        await openGate.WaitAsync();
        try
        {
            path=Path.GetFullPath(path);
            foreach(var editor in Editors)
            {
                var existing=editor.sessions.FirstOrDefault(s=>string.Equals(s.FilePath,path,StringComparison.OrdinalIgnoreCase));
                if(existing!=null){editor.Files.SelectedItem=existing;editor.RememberFile(path);editor.BringForward();return;}
            }
            FileSession loaded;
            try{loaded=await Task.Run(()=>FileSession.Load(path));}
            catch(DecoderFallbackException)
            {
                if(MessageBox.Show(this,"UTF-8로 읽을 수 없습니다. CP949로 다시 읽을까요?","문자 인코딩",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
                loaded=await Task.Run(()=>FileSession.Load(path,Encoding.GetEncoding(949,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback)));
            }
            var target=IsLoaded?this:Editors.FirstOrDefault();
            if(target!=null){target.sessions.Add(loaded);target.Files.SelectedItem=loaded;target.RememberFile(path);}
        }
        catch(Exception ex){Error(ex);}
        finally { openGate.Release(); }
    }
    private void OpenFile(object sender,RoutedEventArgs e)
    {var d=new OpenFileDialog{Filter="설정 파일|*.json;*.xml;*.config;*.yaml;*.yml;*.ini;*.cfg;*.csv;*.tsv|모든 파일|*.*",Multiselect=true};if(d.ShowDialog(this)==true)foreach(string p in d.FileNames)OpenPath(p);}
    private void FileDrop(object sender,DragEventArgs e){if(e.Data.GetData(DataFormats.FileDrop) is string[] paths)foreach(string p in paths)OpenPath(p);}
    private void NewFile(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();PopulateNewFileMenu(menu);menu.PlacementTarget=(UIElement)sender;menu.IsOpen=true;
    }
    private void PopulateNewFileMenu(ItemsControl menu)
    {
        foreach(var format in new[]{"JSON","XML","YAML","INI","CSV"}){var item=new MenuItem{Header=format};item.Click+=(_,_)=>{var text=format switch{"JSON"=>"{\n  \"server\": {\n    \"host\": \"localhost\",\n    \"port\": 8080,\n    \"enabled\": true\n  }\n}\n","XML"=>"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<configuration>\n  <!-- Example Config -->\n  <server host=\"localhost\" port=\"8080\" />\n</configuration>\n","YAML"=>"# 서버 설정\nserver:\n  host: localhost\n  port: 8080\n","INI"=>"; 서버 설정\n[server]\nhost=localhost\nport=8080\n",_=>"name,host,port\nReceiver-A,localhost,08080\n"};var s=new FileSession(format,text);sessions.Add(s);Files.SelectedItem=s;};menu.Items.Add(item);}
    }
    private async void TabChanged(object sender,SelectionChangedEventArgs e)
    {
        if(e.Source!=Files)return;EndInline(false);inlinePath=null;addPopup?.SetCurrentValue(System.Windows.Controls.Primitives.Popup.IsOpenProperty,false);
        if(e.RemovedItems.OfType<FileSession>().FirstOrDefault() is { } previous){previous.SearchText=Search.Text;previous.SourceMode=sourceMode;}
        switching=true;sourceMode=Session?.SourceMode??false;
        Source.Text=Session?.Text??"";Source.IsUndoEnabled=false;Source.IsUndoEnabled=true;CsvHeader.IsChecked=Session?.CsvHeader??false;CsvSeparator.SelectedIndex=Session?.Separator=='\t'?1:Session?.Separator==';'?2:0;Search.Text=Session?.SearchText??"";SearchPanel.Visibility=Search.Text.Length>0?Visibility.Visible:Visibility.Collapsed;switching=false;
        await Parse();
    }
    private Exception? syntaxError;
    private async Task Parse(bool preserveViewport = false)
    {
        syntaxError=null;parseTimer.Stop();var s=Session;int rev=++revision;
        var scrolls=new List<(ScrollViewer View,double X,double Y)>();
        void CaptureScrolls(DependencyObject root)
        {
            if(root is ScrollViewer view)scrolls.Add((view,view.HorizontalOffset,view.VerticalOffset));
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)CaptureScrolls(VisualTreeHelper.GetChild(root,i));
        }
        void ClearViews(){document=null;Tree.ItemsSource=null;Stage.ItemsSource=null;GroupContent.Children.Clear();Csv.ItemsSource=null;}
        if(preserveViewport){CaptureScrolls(EditorArea);document=null;}else ClearViews();
        if(s==null){ClearViews();UpdateStatus();return;}
        string text=s.Text;Message.Text="구조를 읽는 중…";
        try
        {
            var parsed=await Task.Run(()=>new ConfigDocument(text,s.Format,s.Separator));
            if(rev!=revision||s!=Session)return;document=parsed;
            var oldValues=new Dictionary<string,string>();
            try{foreach(var n in new ConfigDocument(s.SavedText,s.Format,s.Separator).All)oldValues.TryAdd(n.Path,n.Value);}catch{ }
            foreach(var n in parsed.All){n.Open=!s.Collapsed.Contains(n.Path);n.State=n.ReadOnly?"원문 편집":oldValues.TryGetValue(n.Path,out var old)&&old==n.Value?"":s.Dirty?"변경":"";}
            foreach(var n in parsed.All){for(var p=n;p!=null;p=p.Parent)if(s.PendingDeletes.Contains(p.Path)){n.PendingDelete=true;n.State="삭제 예정";break;}}
            Message.Text=s.Format=="INI"?"INI: 전체 줄 주석 지원 · 인라인 ;/#는 값으로 처리합니다.":s.Format=="YAML"?"YAML: 기본 1.2 표기 · 앵커·별칭·블록 문자열은 원문 편집을 사용하세요.":"값 더블클릭 · F2 수정 · ⋯ 메뉴 · Ctrl+S 저장";
            Populate();
            if(preserveViewport)
            {
                EditorArea.UpdateLayout();
                foreach(var (view,x,y) in scrolls){view.ScrollToHorizontalOffset(x);view.ScrollToVerticalOffset(y);}
                EditorArea.UpdateLayout();
            }
        }
        catch(Exception ex)
        {
            if(rev!=revision||s!=Session)return;ClearViews();syntaxError=ex;Message.Text="문법 오류: "+ex.Message;sourceMode=true;SetView();
        }
        UpdateStatus();
    }
    private void Populate()
    {
        if(document==null)return;
        if(document.Format=="CSV"){PopulateCsv();SetView();return;}
        string query=Search.Text;var list=new List<ConfigNode>();
        bool Match(ConfigNode n)=>n.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||n.Value.Contains(query,StringComparison.OrdinalIgnoreCase)||n.Children.Any(Match);
        void Visit(IEnumerable<ConfigNode> nodes){foreach(var n in nodes){if(query.Length>0&&!Match(n))continue;list.Add(n);if(n.Open||query.Length>0){Visit(n.Children);if(n.Group&&!n.ReadOnly&&!n.PendingDelete&&Session?.StructureEditing==true&&!Session.RawEditing&&query.Length==0)list.Add(new ConfigNode{Parent=n,Name="＋ 항목 추가",Kind="Add",ReadOnly=true});}}}
        Visit(document.Roots);foreach(var n in list)n.HasVisibleChildren=false;foreach(var siblings in list.GroupBy(n=>n.Parent)){foreach(var n in siblings)n.LastVisibleSibling=false;siblings.Last().LastVisibleSibling=true;if(siblings.Key!=null)siblings.Key.HasVisibleChildren=true;}Tree.ItemsSource=list;RenderViews(list);SetView();
    }
    private void PopulateCsv()
    {
        if(document==null||Session==null)return;Csv.Columns.Clear();csvRows.Clear();var table=new DataTable();
        int columns=document.Roots.Select(n=>n.Children.Count).DefaultIfEmpty(0).Max();
        for(int i=0;i<columns;i++){string name="C"+i;table.Columns.Add(name);string header=Session.CsvHeader&&document.Roots.Count>0&&i<document.Roots[0].Children.Count?document.Roots[0].Children[i].Value:$"열 {i+1}";Csv.Columns.Add(new DataGridTextColumn{Header=string.IsNullOrEmpty(header)?"제목 없음":header,HeaderStyle=new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),(Style)FindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader))){Setters={new Setter(FontWeightProperty,FontWeights.SemiBold)}},Binding=new System.Windows.Data.Binding(name){Converter=new CsvDisplayValue()},ElementStyle=(Style)FindResource("GridText"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});}
        foreach(var row in document.Roots.Skip(Session.CsvHeader?1:0))
        {if(Search.Text.Length>0&&!row.Children.Any(n=>n.Value.Contains(Search.Text,StringComparison.OrdinalIgnoreCase)))continue;csvRows.Add(row);var r=table.NewRow();for(int i=0;i<row.Children.Count;i++)r[i]=row.Children[i].Value;table.Rows.Add(r);}
        Csv.ItemsSource=table.DefaultView;
    }
    private void SetView()
    {
        workspacePending=true;SyncStructure();
        Tree.Visibility=!sourceMode&&Session?.Format!="CSV"&&editorView==EditorView.Tree?Visibility.Visible:Visibility.Collapsed;
        Stage.Visibility=!sourceMode&&Session?.Format!="CSV"&&editorView==EditorView.Stages?Visibility.Visible:Visibility.Collapsed;
        Groups.Visibility=!sourceMode&&Session?.Format!="CSV"&&editorView==EditorView.Groups?Visibility.Visible:Visibility.Collapsed;
        Csv.Visibility=!sourceMode&&Session?.Format=="CSV"?Visibility.Visible:Visibility.Collapsed;
        Source.Visibility=sourceMode?Visibility.Visible:Visibility.Collapsed;
        CsvHeader.Visibility=CsvSeparator.Visibility=!sourceMode&&Session?.Format=="CSV"?Visibility.Visible:Visibility.Collapsed;
        GridButton.Content=Session?.Format=="CSV"?"▦ 표 보기":editorView switch{EditorView.Tree=>"▦ 트리 보기",EditorView.Stages=>"▦ 단계 보기",_=>"▦ 그룹 보기"};GridButton.SetResourceReference(Control.BackgroundProperty,sourceMode?"Property":"Surface");SourceButton.SetResourceReference(Control.BackgroundProperty,sourceMode?"Surface":"Property");Search.IsEnabled=!sourceMode;GridButton.FontWeight=!sourceMode?FontWeights.Bold:FontWeights.Normal;SourceButton.FontWeight=sourceMode?FontWeights.Bold:FontWeights.Normal;
    }
    private void UpdateStatus()
    {UndoButton.IsEnabled=Session?.CanUndo==true;RedoButton.IsEnabled=Session?.CanRedo==true;EmptyState.Visibility=Session==null?Visibility.Visible:Visibility.Collapsed;EditorArea.Visibility=Session==null?Visibility.Hidden:Visibility.Visible;Status.Text=Session==null?$"InerConfig {UpdateService.Current}":$"{Session.Format}   ·   {Session.EncodingName}{(Session.Bom.Length>0?" BOM":"")}   ·   {(Session.Text.Contains("\r\n")?"CRLF":"LF")}   ·   {document?.All.Count()??0}개 항목   ·   {(Session.Dirty?"변경됨":"변경 없음")}";}
    private async void SourceChanged(object sender,TextChangedEventArgs e){if(switching||Session==null)return;if(Source.IsReadOnly)return;Session.Change(Source.Text);UpdateStatus();document=null;revision++;parseTimer.Stop();parseTimer.Start();await Task.CompletedTask;}
    private void SourceView(object sender,RoutedEventArgs e){sourceMode=true;SetView();Source.Focus();}
    private async void GridView(object sender,RoutedEventArgs e)
    {
        if(Session==null)return;
        var session=Session;
        if(document==null)await Parse();
        if(session!=Session)return;
        if(document==null)
        {
            if(syntaxError==null)return;
            var dialog=new SyntaxErrorDialog(this,session.FilePath??session.Title,session.Text,syntaxError);
            if(dialog.ShowDialog()==true)
            {
                sourceMode=true;SetView();Source.Focus();
                Source.Select(dialog.ErrorOffset,dialog.ErrorOffset<Source.Text.Length?1:0);
                Source.ScrollToLine(Source.GetLineIndexFromCharacterIndex(dialog.ErrorOffset));
            }
            return;
        }
        if(Session.StructureEditing&&Session.RawEditing){try{Session.CompleteStructure();}catch(Exception ex){Error(ex);return;}}
        sourceMode=false;SetView();
    }
    private void OpenSearch() { SearchPanel.Visibility=Visibility.Visible;Search.Focus();Search.SelectAll(); }
    private void CloseSearch(object sender,RoutedEventArgs e)
    {
        Search.Clear();if(Session!=null)Session.SearchText="";
        SearchPanel.Visibility=Visibility.Collapsed;workspacePending=true;
        if(sourceMode)Source.Focus();else if(Session?.Format=="CSV")Csv.Focus();else if(editorView==EditorView.Tree)Tree.Focus();else if(editorView==EditorView.Stages)Stage.Focus();else Groups.Focus();
    }
    private void SearchChanged(object sender,TextChangedEventArgs e)
    {
        if(switching)return;
        if(Search.Text.Length>0)SearchPanel.Visibility=Visibility.Visible;
        Populate();
    }
    private async void CsvOptionChanged(object sender,RoutedEventArgs e){if(switching||Session==null)return;Session.CsvHeader=CsvHeader.IsChecked==true;Session.Separator=CsvSeparator.SelectedIndex==1?'\t':CsvSeparator.SelectedIndex==2?';':',';await Parse();}
    private void ToggleNode(object sender,RoutedEventArgs e)
    {
        if((sender as FrameworkElement)?.DataContext is not ConfigNode n||Session==null||!n.Group)return;
        ToggleBranch(n,(Keyboard.Modifiers&ModifierKeys.Alt)!=0);
    }
    private void ToggleBranch(ConfigNode node,bool recursive)
    {
        if(Session==null||!node.Group)return;
        node=document?.All.FirstOrDefault(item=>item.Path==node.Path)??node;
        bool open=!node.Open;
        foreach(var item in (recursive?ConfigDocument.Walk([node]):new[]{node}).Where(n=>n.Group))
        {
            item.Open=open;
            if(open)Session.Collapsed.Remove(item.Path);else Session.Collapsed.Add(item.Path);
        }
        Populate();
    }
    private void SelectionChanged(object sender,SelectionChangedEventArgs e){if(Tree.SelectedItem is ConfigNode n)SelectNode(n,false);}
    private async Task Apply(string text)
    {if(Session==null)return;string? path=(Tree.SelectedItem as ConfigNode)?.Path;Session.Change(text);switching=true;Source.Text=text;switching=false;await Parse(true);if(path!=null&&document!=null){Tree.SelectedItem=document.All.FirstOrDefault(n=>n.Path==path);if(Tree.SelectedItem is ConfigNode selected)SelectNode(selected,false);}}
    private void EditDoubleClick(object sender,MouseButtonEventArgs e){if(inlineEnabled)return;if(Tree.SelectedItem is ConfigNode {Kind:"Add"} add){OpenAddPalette(add.Parent!,Tree);return;}if(Find<Button>(e.OriginalSource as DependencyObject)!=null||Find<CheckBox>(e.OriginalSource as DependencyObject)!=null||Find<ComboBox>(e.OriginalSource as DependencyObject)!=null)return;if(Tree.SelectedItem is ConfigNode n)Edit(n);}
    private void EditCsv(object sender,MouseButtonEventArgs e){Csv.BeginEdit();}
    private ConfigNode? CsvNode(){int row=Csv.Items.IndexOf(Csv.CurrentCell.Item),column=Csv.CurrentCell.Column?.DisplayIndex??-1;return row>=0&&row<csvRows.Count&&column>=0&&column<csvRows[row].Children.Count?csvRows[row].Children[column]:null;}
    private async void Edit(ConfigNode n,bool typeChange=false,bool forcePopup=false)
    {
        if(inlineEnabled&&!typeChange&&!forcePopup&&Session?.Format!="CSV"){BeginInline(n);return;}
        if(ReferenceEditor!=null||Session?.RawEditing==true)return;
        if(document==null||!n.Editable){GoSource(n);return;}var originalDocument=document;var originalSession=Session;var originalText=Session?.Text;var d=new EditDialog(this,typeChange?"자료형 변경":n.Kind=="Comment"?"주석 수정":"값 수정",n.Path,n.Value,n.DisplayKind,typeChange&&Session?.Format is "JSON" or "YAML"?new[]{"String","Number","Boolean","Null"}:null);
        if(Session?.Input is {} pending&&pending.Path==n.Path&&pending.Baseline==Session.Text)d.ValueBox.Text=pending.Text;
        var inputSession=Session!;
        void TrackInput(object sender,TextChangedEventArgs e){inputSession.Input=new(n.Path,d.ValueBox.Text,originalText!,d.Kind);inputSession.Notify();}
        d.ValueBox.TextChanged+=TrackInput;
        bool accepted=await ShowReferenceEdit(d);d.ValueBox.TextChanged-=TrackInput;
        inputSession.Input=null;inputSession.Notify();SyncStructure();
        if(!accepted)return;
        if(Session!=originalSession||Session?.Text!=originalText){Message.Text="편집 중 문서가 변경되어 적용하지 않았습니다.";return;}
        try{await Apply(originalDocument.Change(n,d.Value,typeChange?d.Kind:null));}catch(Exception ex){inputSession.Input=new(n.Path,d.Value,originalText!,d.Kind);inputSession.Notify();SyncStructure();Error(ex);}
    }
    private async void ToggleBool(object sender,RoutedEventArgs e){if((sender as FrameworkElement)?.DataContext is not ConfigNode n||document==null)return;n=document.All.FirstOrDefault(item=>item.Path==n.Path)??n;try{SelectNode(n,false);await Apply(document.Change(n,n.Value=="true"?"false":"true"));}catch(Exception ex){Error(ex);}}
    private static T? Find<T>(DependencyObject? child) where T:DependencyObject {while(child!=null){if(child is T found)return found;child=child is Visual?VisualTreeHelper.GetParent(child):null;}return null;}
    private void SelectContextRow(object sender,MouseButtonEventArgs e){var row=Find<DataGridRow>(e.OriginalSource as DependencyObject);if(row!=null)Tree.SelectedItem=row.Item;}
    private void SelectCsvCell(object sender,MouseButtonEventArgs e){var cell=Find<DataGridCell>(e.OriginalSource as DependencyObject);if(cell!=null)Csv.CurrentCell=new DataGridCellInfo(cell);}
    private void NodeMenu(object sender,RoutedEventArgs e){if((sender as FrameworkElement)?.DataContext is ConfigNode {Kind:"Add"} add){OpenAddPalette(add.Parent!,sender as UIElement??Tree);return;}if((sender as FrameworkElement)?.DataContext is ConfigNode n){Tree.SelectedItem=n;var menu=MakeMenu(n);menu.PlacementTarget=(UIElement)sender;menu.IsOpen=true;}}
    private void TreeMenu(object sender,ContextMenuEventArgs e){if(Tree.SelectedItem is ConfigNode n)Tree.ContextMenu=MakeMenu(n);else e.Handled=true;}
    private void CsvMenu(object sender,ContextMenuEventArgs e){if(CsvNode() is ConfigNode n)Csv.ContextMenu=MakeMenu(n);else e.Handled=true;}
    private ContextMenu MakeMenu(ConfigNode n)
    {
        n=document?.All.FirstOrDefault(item=>item.Path==n.Path)??n;
        var menu=new ContextMenu();void Item(string title,Action action,bool enabled=true){var i=new MenuItem{Header=title,IsEnabled=enabled && (Session?.StructureEditing==true || !(title.Contains("추가")||title.Contains("삭제")||title.Contains("이름 변경")))};i.Click+=(_,_)=>action();menu.Items.Add(i);}
        if(Session?.Format=="CSV"){Item("선택 범위 복사",()=>CopyCsv(false));Item("헤더 포함 복사",()=>CopyCsv(true));Item("붙여넣기",async()=>await PasteCsv(false));Item("내용 지우기",async()=>await PasteCsv(true));menu.Items.Add(new Separator());}
        if(!n.Group){Item(n.Kind=="Comment"?"주석 수정":"값 수정",()=>Edit(n),n.Editable);if(inlineEnabled)Item("팝업에서 수정…",()=>Edit(n,false,true),n.Editable);Item("값 복사",()=>Copy(n.Value));}
        Item("경로 복사",()=>Copy(n.Path));Item("원문 위치로 이동",()=>GoSource(n));
        if(n.Editable&&n.Kind!="Null"){menu.Items.Add(new Separator());if(ScriptEnabled)Item("스크립트로 변환…",()=>OpenScript(n));Item("암호화…",()=>Transform(n,"암호화"));Item("복호화…",()=>Transform(n,"복호화"));Item("해시 계산…",()=>Transform(n,"SHA-256"));}
        menu.Items.Add(new Separator());
        if(Session?.StructureEditing!=true){Item("구조 편집 시작",()=>StructureClick(this,new()));return menu;}
        Item("이름 변경…",()=>Rename(n),n.KeyStart>=0&&!n.ReadOnly);
        Item(Session?.Format=="CSV"?"행 추가…":"하위 항목 추가…",()=>Insert(n.Group||n.Kind=="Element"?n:n.Parent),n.Group||n.Kind=="Element"||n.Parent!=null);
        Item(n.PendingDelete?"삭제 복구":Session?.Format=="CSV"?"행 삭제…":"삭제…",()=>Delete(n),!n.ReadOnly);
        if(Session?.Format=="INI")Item("새 섹션 추가…",AddSection);
        if(Session?.Format=="CSV"){Item("헤더 이름 변경…",()=>RenameCsvHeader(n),Session.CsvHeader);Item("마지막에 열 추가…",()=>CsvColumn(n,false));Item("이 열 삭제…",()=>CsvColumn(n,true));}
        return menu;
    }

    private void Copy(string text){try{Clipboard.SetText(text);}catch(Exception ex){Error(ex);}}
    private void GoSource(ConfigNode n){sourceMode=true;SetView();Source.Focus();Source.Select(Math.Min(n.Start,Source.Text.Length),Math.Max(0,Math.Min(n.End-n.Start,Source.Text.Length-n.Start)));Source.ScrollToLine(Source.GetLineIndexFromCharacterIndex(Math.Min(n.Start,Source.Text.Length)));}
    private async void Rename(ConfigNode n){if(document==null||!RequireStructure())return;var d=new EditDialog(this,"이름 변경",n.Path,n.Name.Trim('[',']').TrimStart('@'),"String");if(d.ShowDialog()!=true)return;try{await Apply(document.Rename(n,d.Value));}catch(Exception ex){Error(ex);}}
    private void Insert(ConfigNode? n)
    {
        if(n==null||document==null||!RequireStructure())return;
        if(Session?.Format=="CSV"){AddCsvRow();return;}
        OpenAddPalette(n,Tree);
    }
    private async void Delete(ConfigNode n)
    {
        if(document==null||Session?.StructureEditing!=true||Session.Draft!=null||Session.RawEditing)return;
        string path=Session.Format=="CSV"?(n.Parent??n).Path:n.Path;
        if(!Session.PendingDeletes.Contains(path)){try{_=document.Delete(n);}catch(Exception ex){Error(ex);return;}}
        Session.ToggleDeletion(path);await Parse();Message.Text="삭제 예정 · 같은 메뉴에서 복구 · 완료 시 반영";
    }
    private async void AddSection(){if(document==null||!RequireStructure())return;var d=new EditDialog(this,"섹션 추가","새 섹션 이름","section","String");if(d.ShowDialog()!=true)return;try{await Apply(document.AddSection(d.Value));}catch(Exception ex){Error(ex);}}
    private async void CsvColumn(ConfigNode n,bool delete)
    {
        if(document==null||n.Parent==null||Session==null||!RequireStructure())return;
        if(delete&&MessageBox.Show(this,"모든 행에서 이 열을 삭제할까요?","열 삭제",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        string name="";if(!delete){var d=new EditDialog(this,"열 추가","마지막 열에 추가합니다.","newColumn","String");if(d.ShowDialog()!=true)return;name=d.Value;}
        try{await Apply(document.CsvColumn(n.Parent.Children.IndexOf(n),delete,name,Session.CsvHeader));}catch(Exception ex){Error(ex);}
    }
    public void ShowCrypto(string text)=>new CryptoWindow(this,text,"복호화").ShowDialog();
    private void OpenTools(object sender,RoutedEventArgs e)=>new CryptoWindow(this,"").ShowDialog();
    private async void Transform(ConfigNode n,string action)
    {
        if(document==null||Session==null)return;var s=Session;var before=s.Text;var doc=document;var window=new CryptoWindow(this,n.Value,action,true);if(window.ShowDialog()!=true||window.Result==null)return;
        if(s!=Session||s.Text!=before){Message.Text="대상 문서가 바뀌었습니다. 변환을 다시 실행하세요.";return;}
        string? kind=null;if(n.Kind is "Number" or "Boolean"){if(MessageBox.Show(this,"결과를 적용하면 자료형이 String으로 바뀝니다. 적용할까요?","자료형 변경",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;kind="String";}
        try{await Apply(doc.Change(n,window.Result,kind));}catch(Exception ex){Error(ex);}
    }
    private async void Undo(object sender,RoutedEventArgs e){if(Session==null)return;if(Session.Draft!=null){Message.Text="추가 초안을 확정하거나 취소하세요.";return;}Session.Undo();await ShowSessionText();}
    private async void Redo(object sender,RoutedEventArgs e){if(Session==null)return;if(Session.Draft!=null){Message.Text="추가 초안을 확정하거나 취소하세요.";return;}Session.Redo();await ShowSessionText();}
    private async Task ShowSessionText(){switching=true;Source.Text=Session?.Text??"";switching=false;await Parse();}
    private void SaveFile(object sender,RoutedEventArgs e)=>Save(Session,false);
    private void SaveAs(object sender,RoutedEventArgs e)=>Save(Session,true);
    private bool Save(FileSession? s,bool saveAs)
    {
        if(s==null)return false;if(inlineBox!=null&&!CommitInline())return false;if(s.Input!=null&&FindFocusedCsv()==null){Message.Text="미적용 입력을 복원하거나 취소한 뒤 저장하세요.";return false;}if(s.Draft!=null){try{if(s!=Session)return false;var added=BuildAddDraft();if(added==null)return false;s.Change(added);s.Draft=null;switching=true;Source.Text=added;switching=false;document=new ConfigDocument(s.Text,s.Format,s.Separator);}catch(Exception ex){Error(ex);return false;}}if(s==Session&&!Csv.CommitEdit(DataGridEditingUnit.Cell,true))return false;string? path=s.FilePath;
        if(path==null||saveAs){var d=new SaveFileDialog{FileName=path==null?"configuration."+s.Format.ToLowerInvariant():Path.GetFileName(path),Filter=$"{s.Format}|*.{s.Format.ToLowerInvariant()}",OverwritePrompt=true};if(d.ShowDialog(this)!=true)return false;path=d.FileName;}
        try{if(sessions.Any(other=>other!=s&&string.Equals(other.FilePath,Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)))throw new IOException("다른 탭에서 열려 있는 파일입니다. 그 탭을 닫거나 다른 경로를 선택하세요.");s.Save(path);s.CompleteStructure();SyncStructure();Populate();RememberFile(path);UpdateStatus();Message.Text="저장했습니다. 숨김 백업은 앱 정상 종료 시 정리됩니다.";return true;}catch(Exception ex){Error(ex);return false;}
    }
    private bool MayClose(FileSession s){if(!s.Dirty)return true;var result=MessageBox.Show(this,s.Title+"\n변경 내용을 저장할까요?","저장 확인",MessageBoxButton.YesNoCancel);return result==MessageBoxResult.No||result==MessageBoxResult.Yes&&Save(s,false);}
    private void CloseTab(object sender,RoutedEventArgs e){e.Handled=true;if((sender as FrameworkElement)?.DataContext is FileSession s&&MayClose(s)){sessions.Remove(s);if(Files.SelectedItem==null&&sessions.Count>0)Files.SelectedIndex=0;try{Checkpoint();}catch(Exception ex){Error(ex);}}}
    private void ClosingWindow(object? sender,CancelEventArgs e){if(ReferenceEditor!=null){e.Cancel=true;ReferenceEditor.Activate();return;}if(closingForUpdate)return;if(Workspace!=null){try{Checkpoint();return;}catch(Exception ex){e.Cancel=true;Error(ex);return;}}foreach(var s in sessions)if(!MayClose(s)){e.Cancel=true;return;}}
    private void ChangeEncoding(object sender,RoutedEventArgs e){if(Session==null)return;var menu=new ContextMenu();foreach(var name in new[]{"UTF-8","UTF-8 BOM","UTF-16 LE","CP949"}){var i=new MenuItem{Header=name};i.Click+=(_,_)=>{Session?.SetEncoding(name);UpdateStatus();Message.Text="선택한 인코딩은 다음 저장에 적용됩니다.";};menu.Items.Add(i);}menu.PlacementTarget=(UIElement)sender;menu.IsOpen=true;}
    private int zoomPercent = 100;
    private void SetZoom(int percent)
    {
        zoomPercent = Math.Clamp(percent, 50, 200);
        EditorScale.ScaleX = EditorScale.ScaleY = zoomPercent / 100d;
        ZoomReset.Content = $"{zoomPercent}%";
    }
    private void EditorMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if(ReferenceEditor!=null){if((Keyboard.Modifiers & ModifierKeys.Control)!=0)e.Handled=true;return;}
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.Delta == 0) return;
        SetZoom(zoomPercent + Math.Sign(e.Delta) * 10);
        e.Handled = true;
    }
    private void ResetZoom(object sender, RoutedEventArgs e) => SetZoom(100);
    private void Keys(object sender,KeyEventArgs e)
    {
        if(ReferenceEditor!=null){e.Handled=true;return;}
        if(InlineKeys(e)){e.Handled=true;return;}
        if(inlineBox!=null)return;
        if(Csv.IsKeyboardFocusWithin&&Find<TextBox>(e.OriginalSource as DependencyObject)!=null&&e.Key is Key.Z or Key.Y)return;
        if(Keyboard.Modifiers==ModifierKeys.Control)
        {if(e.Key is Key.D0 or Key.NumPad0){SetZoom(100);e.Handled=true;}else if(e.Key is Key.OemPlus or Key.Add){SetZoom(zoomPercent+10);e.Handled=true;}else if(e.Key is Key.OemMinus or Key.Subtract){SetZoom(zoomPercent-10);e.Handled=true;}else if(e.Key==Key.O){OpenFile(this,new());e.Handled=true;}else if(e.Key==Key.S){Save(Session,false);e.Handled=true;}else if(e.Key==Key.F){OpenSearch();e.Handled=true;}else if(e.Key==Key.Z&&!Source.IsKeyboardFocusWithin){Undo(this,new());e.Handled=true;}else if(e.Key==Key.Y&&!Source.IsKeyboardFocusWithin){Redo(this,new());e.Handled=true;}}
        else if(e.Key==Key.Escape&&SearchPanel.Visibility==Visibility.Visible){CloseSearch(this,new());e.Handled=true;}
        else if(e.Key==Key.F2&&!Csv.IsKeyboardFocusWithin&&Tree.SelectedItem is ConfigNode n){Edit(n);e.Handled=true;}
    }
    private void LightTheme(object sender,RoutedEventArgs e)=>SetTheme(false);
    private void DarkTheme(object sender,RoutedEventArgs e)=>SetTheme(true);
    public void SetTheme(bool dark,bool persist=true)
    {
        var colors=dark?new[]{"#252F3E","#232B37","#E0E7F1","#ADBED5","#3C4B60","#202835","#304964","#459CF5"}:new[]{"#F7F9FC","#FFFFFF","#27374D","#77869B","#DEE5EF","#F1F5FA","#DCEBFD","#087AF5"};
        var keys=new[]{"Canvas","Surface","Text","Muted","Line","Property","Selected","Blue"};for(int i=0;i<keys.Length;i++)Application.Current.Resources[keys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        var depthText=dark?new[]{"#E2EDFF","#B6D4FF","#94BAED","#82A4D0","#7899BF"}:new[]{"#173D75","#23559A","#386CA4","#4D739B","#5C7793"};
        var depthBackground=dark?new[]{"#344C6E","#2D405A","#293749","#263140","#242E3B"}:new[]{"#CBDDF5","#DCE8F8","#E8EFF9","#F0F4FA","#F6F8FC"};
        for(int i=0;i<5;i++){
            Application.Current.Resources["DepthText"+i]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(depthText[i]));
            Application.Current.Resources["DepthBackground"+i]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(depthBackground[i]));
        }
        if(persist)try{Directory.CreateDirectory(Path.GetDirectoryName(themeFile)!);File.WriteAllText(themeFile,dark?"dark":"light");}catch{ }
    }
    private void Error(Exception ex)=>MessageBox.Show(this,ex.Message,"InerConfig",MessageBoxButton.OK,MessageBoxImage.Warning);
    private void CloseApp(object sender,RoutedEventArgs e)=>Close();
    private void Minimize(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void Maximize(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    public async Task RunUiTest(string output)
    {
        closingForUpdate=true;inlineEnabled=false;Directory.CreateDirectory(output);
        await TestNewFiles();
        await TestScripts(output);
        await TestInline(output);
        await TestStructureUi(output);
        var examples=new Dictionary<string,string>{{"JSON","{\n  \"server\": {\"host\": \"localhost\", \"port\": 8080, \"enabled\": true},\n  \"security\": {\"token\": \"sample-key\"},\n  \"devices\": [{\"name\": \"Receiver-A\"}, {\"name\": \"Receiver-B\"}]\n}"},{"XML","<configuration>\n  <!-- Example Config -->\n  <server host='localhost' port='8080'><token>sample-key</token></server>\n</configuration>"},{"CSV","name,host,port\r\nReceiver-A,localhost,08080\r\nReceiver-B,localhost,08081\r\n"},{"YAML","# comment\nserver:\n  host: localhost\n  port: 8080\n"},{"INI","; comment\n[server]\nhost=localhost\nport=8080\n[security]\ntoken=sample-key\n"}};
        foreach(var pair in examples){var s=new FileSession(pair.Key,pair.Value);sessions.Add(s);Files.SelectedItem=s;await Parse();if(document==null)throw new Exception("UI parse failed: "+pair.Key);}
        foreach(var session in sessions.Where(s=>s.Format!="CSV").ToArray())
        {
            Files.SelectedItem=session;await Parse();
            foreach(var mode in Enum.GetValues<EditorView>())
            {
                ChangeView(mode,false);var node=document!.All.First(n=>n.Editable&&!n.Group&&n.Value=="localhost");
                var before=session.Text;SelectNode(node,false);await Apply(document.Change(node,"ui-edited"));
                var edited=session.Text;var path=session.SelectedPath;
                foreach(var next in Enum.GetValues<EditorView>())
                {
                    ChangeView(next,false);
                    if(session.Text!=edited||!session.CanUndo||session.SelectedPath!=path)throw new Exception("Shared edit/history: "+session.Format+" "+next);
                }
                session.Undo();await ShowSessionText();if(session.Text!=before)throw new Exception("View undo: "+session.Format);
                if(session.Format is "JSON" or "YAML" or "XML")
                {
                    ChangeView(mode,false);
                    var typed=document!.All.First(n=>n.Editable&&(session.Format=="XML"?n.XmlValue!=null:n.Kind=="String"));
                    session.BeginStructure();var picker=(ComboBox)CreateTypePicker(typed);
                    if(session.Format!="XML")
                    {
                        picker.SelectedItem="Number";
                        if(session.Text!=before||picker.SelectedItem as string!="String")throw new Exception("Invalid type rollback");
                    }
                    picker.SelectedItem=session.Format=="XML"?"CDATA":"Null";
                    for(int wait=0;wait<100&&(document==null||document.All.First(n=>n.Path==typed.Path).DisplayKind==typed.DisplayKind);wait++)await Task.Delay(20);
                    if(document==null||document.All.First(n=>n.Path==typed.Path).DisplayKind!=(session.Format=="XML"?"CDATA":"Null"))throw new Exception("Dropdown conversion: "+mode+" "+session.Format);
                    session.Undo();await ShowSessionText();if(session.Text!=before)throw new Exception("Dropdown undo");session.CompleteStructure();
                }
            }
        }
        Files.SelectedIndex=0;await Parse();
        var host=document!.All.First(n=>n.Name=="host");await Apply(document.Change(host,"changed"));if(!Session!.Dirty)throw new Exception("UI dirty state");Session.Undo();await ShowSessionText();if(document!.All.First(n=>n.Name=="host").Value!="localhost")throw new Exception("UI undo");
        CloseSearch(this,new());if(SearchPanel.Visibility!=Visibility.Collapsed)throw new Exception("Search initially hidden");
        OpenSearch();if(SearchPanel.Visibility!=Visibility.Visible||!Search.IsKeyboardFocused)throw new Exception("Open search focus");
        Search.Text="Receiver-A";if(Tree.Items.Count==0)throw new Exception("UI search");
        CloseSearch(this,new());if(Search.Text!=""||Session!.SearchText!=""||SearchPanel.Visibility!=Visibility.Collapsed||!Tree.Items.Cast<ConfigNode>().Any(n=>n.Name=="host"))throw new Exception("Close search clears filter");
        Session!.BeginStructure(true);sourceMode=true;SetView();Source.Text="{";await Parse();if(document!=null||!sourceMode)throw new Exception("UI invalid source");Session.Undo();await ShowSessionText();Session.CompleteStructure();sourceMode=false;SetView();
        async Task Capture(string name,FrameworkElement? target=null)
        {
            target??=this;target.UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)target.ActualWidth,(int)target.ActualHeight,96,96,PixelFormats.Pbgra32);if(target is Window)bitmap.Render(target);else{var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(target),null,new Rect(0,0,target.ActualWidth,target.ActualHeight));bitmap.Render(visual);}
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name));encoder.Save(file);
        }
        OpenSearch();Search.Text="Receiver";
        SetTheme(false,false);await Capture("wpf-search-floating-light.png");
        SetTheme(true,false);await Capture("wpf-search-floating-dark.png");
        CloseSearch(this,new());
        Files.SelectedIndex=1;await Parse();
        foreach(var mode in Enum.GetValues<EditorView>())
        {
            ChangeView(mode,false);
            foreach(var dark in new[]{false,true})
            {
                SetTheme(dark,false);await Capture($"wpf-comment-{mode}-{(dark?"dark":"light")}.png");
                var comment=document!.All.First(n=>n.Kind=="Comment");
                if(mode!=EditorView.Groups)
                {
                    var grid=mode==EditorView.Tree?Tree:Stage;
                    var row=(DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(comment);
                    if(row==null||Math.Abs(row.ActualHeight-26)>1)throw new Exception("Comment row height: "+mode);
                    var text=Visuals<TextBlock>(row).First(t=>t.Text==comment.Display);
                    var center=text.TransformToAncestor(row).Transform(new Point(0,text.ActualHeight/2)).Y;
                    if(Math.Abs(center-row.ActualHeight/2)>1.5)throw new Exception("Comment vertical center: "+mode+" "+center+"/"+row.ActualHeight);
                    foreach(var guide in Visuals<TreeGuides>(row))
                    {
                        var drawing=VisualTreeHelper.GetDrawing(guide);
                        if(drawing!=null&&drawing.Children.OfType<GeometryDrawing>().Any(d=>d.Geometry is LineGeometry line&&line.StartPoint.Y==line.EndPoint.Y&&line.StartPoint.X!=line.EndPoint.X))throw new Exception("Comment horizontal branch");
                    }
                }
            }
        }
        SetTheme(false,false);Files.SelectedIndex=0;await Parse();
        foreach(var dark in new[]{false,true})
        {
            SetTheme(dark,false);
            var problem=new SyntaxErrorDialog(this,"sample.xml","<root>\n  <port>8080</wrong>\n</root>",new System.Xml.XmlException("시작 태그 port와 종료 태그 wrong이 일치하지 않습니다.",null,2,15));
            problem.Show();await Capture($"wpf-syntax-error-{(dark?"dark":"light")}.png",problem);problem.Close();
            var accept=new SyntaxErrorDialog(this,"sample.xml","<root>\n  <port>8080</wrong>\n</root>",new System.Xml.XmlException("잘못된 종료 태그",null,2,15));
            _=Dispatcher.BeginInvoke(new Action(()=>accept.LocateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))),DispatcherPriority.ApplicationIdle);
            if(accept.ShowDialog()!=true||accept.ErrorOffset!=21)throw new Exception("Syntax dialog locate action");
        }
        SetTheme(false,false);
        var recoveryApp=(App)Application.Current;var previousStore=recoveryApp.Workspace;
        recoveryApp.Workspace=new WorkspaceStore(Path.Combine(output,"workspace"));
        var restoreWindow=new MainWindow();
        restoreWindow.Show();
        try
        {
            var draft=new FileSession("JSON","{unfinished") { SourceMode=true,SearchText="draft" };
            var restoredState=new WorkspaceStore.WindowState(1,Guid.NewGuid(),1,[new FileSession("XML","<root>unsaved</root>").Capture(),draft.Capture()]);
            await restoreWindow.RestoreWorkspace(restoredState);
            if(restoreWindow.sessions.Count!=2||restoreWindow.Files.SelectedIndex!=1||restoreWindow.Session!.Text!="{unfinished"||!restoreWindow.Session.Dirty||restoreWindow.Source.Text!="{unfinished"||!restoreWindow.sourceMode)throw new Exception("Restored window tabs, selected draft and source");
            restoreWindow.Checkpoint();
            var durable=recoveryApp.Workspace.LoadWindows([]).Single(w=>w.Id==restoredState.Id);
            if(durable.Selected!=1||durable.Tabs[1].Text!="{unfinished")throw new Exception("Durable update checkpoint");
            restoreWindow.Close();
            if(Editors.Contains(restoreWindow))throw new Exception("Unsaved hot exit");
            var reopened=new MainWindow();reopened.Show();
            try { await reopened.RestoreWorkspace(durable);if(reopened.Session?.Text!="{unfinished"||reopened.sessions.Count!=2)throw new Exception("Hot exit reopen"); }
            finally { reopened.Close(); }
            recoveryApp.Workspace.Remember(Path.Combine(output,"recent-example.json"));
            if(!recoveryApp.Workspace.Recent().Any(p=>p.EndsWith("recent-example.json")))throw new Exception("Recent workspace path");
        }
        finally { if(Editors.Contains(restoreWindow)){restoreWindow.closingForUpdate=true;restoreWindow.Close();}recoveryApp.Workspace=previousStore; }
        var selected=document!.All.First(n=>n.Name=="host");SelectNode(selected,false);
        var originalText=Session!.Text;var originalDirty=Session.Dirty;
        foreach(var mode in Enum.GetValues<EditorView>())
        {
            ChangeView(mode,false);
            if(Session.Text!=originalText||Session.Dirty!=originalDirty||Session.SelectedPath!=selected.Path||Tree.SelectedItem!=selected)throw new Exception("View switch changed document or selection: "+mode);
            Search.Text="Receiver-A";if(Tree.Items.Count==0)throw new Exception("View search: "+mode);Search.Clear();
            var group=document.All.First(n=>n.Name=="server");group.Open=false;Populate();
            if(Tree.Items.Cast<ConfigNode>().Any(n=>n.Parent==group))throw new Exception("View collapse: "+mode);
            await Capture("wpf-"+mode+"-collapsed.png");
            group.Open=true;Populate();
            SetTheme(false,false);await Capture("wpf-"+mode+"-light.png");await Capture("editor-"+mode+"-light.png",EditorArea);SetTheme(true,false);await Capture("wpf-"+mode+"-dark.png");await Capture("editor-"+mode+"-dark.png",EditorArea);
        }
        foreach(var branchView in Enum.GetValues<EditorView>())
        {
            ChangeView(branchView,false);Search.Clear();
            var branch=document!.Roots[0];
            branch.Open=true;ToggleBranch(branch,true);
            if(ConfigDocument.Walk([branch]).Where(n=>n.Group).Any(n=>n.Open||!Session!.Collapsed.Contains(n.Path)))throw new Exception("Recursive collapse");
            ToggleBranch(branch,true);
            if(ConfigDocument.Walk([branch]).Where(n=>n.Group).Any(n=>!n.Open||Session!.Collapsed.Contains(n.Path)))throw new Exception("Recursive expand");
            var child=branch.Children.First(n=>n.Group);ToggleBranch(child,false);ToggleBranch(branch,false);ToggleBranch(branch,false);
            if(child.Open)throw new Exception("Ordinary toggle preserves descendants");
            branch.Open=false;ToggleBranch(branch,true);
        }
        if(ParseView("bad")!=EditorView.Stages||ParseView("Tree")!=EditorView.Tree)throw new Exception("View preference fallback");
        ChangeView(EditorView.Stages,false);
        var zoomText = Session!.Text; var zoomPath = Session.SelectedPath; var zoomUndo = Session.CanUndo;
        SetZoom(999);if(zoomPercent!=200)throw new Exception("Zoom upper limit");
        SetZoom(1);if(zoomPercent!=50)throw new Exception("Zoom lower limit");
        SetZoom(150);
        foreach(var mode in Enum.GetValues<EditorView>())
        {
            ChangeView(mode,false);await Capture("wpf-zoom-"+mode+".png");
            if(EditorScale.ScaleX!=1.5||EditorScale.ScaleY!=1.5||Session.Text!=zoomText||Session.SelectedPath!=zoomPath||Session.CanUndo!=zoomUndo)throw new Exception("Zoom state preservation");
        }
        sourceMode=true;SetView();await Capture("wpf-zoom-source.png");sourceMode=false;SetView();
        ResetZoom(this,new());if(EditorScale.ScaleX!=1||zoomPercent!=100)throw new Exception("Zoom reset");
        var alignmentSession=new FileSession("JSON",System.Text.Json.JsonSerializer.Serialize(new {
            targets=new Dictionary<string,object> { [".NETCoreApp,Version=v8.0"]=new Dictionary<string,object> {
                ["ExampleApp/1.0.0"]=new { dependencies=Enumerable.Range(0,80).ToDictionary(i=>i%2==0?"Example."+i:"Example.Controls."+new string('X',100)+i,i=>"1.0."+i) }
            }}
        }));
        sessions.Add(alignmentSession);Files.SelectedItem=alignmentSession;await Parse();ChangeView(EditorView.Stages,false);SetTheme(false,false);
        static IEnumerable<T> Visuals<T>(DependencyObject root) where T:DependencyObject
        {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            {
                var child=VisualTreeHelper.GetChild(root,i);
                if(child is T match)yield return match;
                foreach(var descendant in Visuals<T>(child))yield return descendant;
            }
        }
        var referenceNode=document!.All.First(n=>n.Name=="Example.0");
        SelectNode(referenceNode,false);Edit(referenceNode);
        var reference=ReferenceEditor as EditDialog??throw new Exception("Reference edit not opened");
        if(!IsEnabled||!reference.IsVisible)throw new Exception("Reference owner disabled");
        reference.ValueBox.Text="edited-reference";
        var lockedClick=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=Mouse.PreviewMouseDownEvent };
        GridButton.RaiseEvent(lockedClick);
        if(!lockedClick.Handled)throw new Exception("Reference background click unlocked");
        Stage.ScrollIntoView(Stage.Items[40]);UpdateLayout();
        var scroll=Visuals<ScrollViewer>(Stage).First();
        var previousOffset=scroll.VerticalOffset;
        scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120) { RoutedEvent=Mouse.MouseWheelEvent });
        UpdateLayout();
        if(scroll.VerticalOffset<=previousOffset||Session!.SelectedPath!=referenceNode.Path)throw new Exception("Reference wheel or selection");
        await Capture("wpf-reference-background.png");
        await Capture("wpf-reference-dialog.png",reference);
        Visuals<Button>(reference).First(b=>b.Content as string=="적용").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(int wait=0;wait<100&&(ReferenceEditor!=null||document==null||!Session.Text.Contains("edited-reference"));wait++)await Task.Delay(20);
        if(ReferenceEditor!=null||document==null||document.All.First(n=>n.Path==referenceNode.Path).Value!="edited-reference")throw new Exception("Reference applies original node");
        Session.Undo();await ShowSessionText();
        var cancelReference=new EditDialog(this,"값 수정","test","original","String");
        var cancelTask=ShowReferenceEdit(cancelReference);cancelReference.ValueBox.Text="discard";
        cancelReference.Close();if(await cancelTask.WaitAsync(TimeSpan.FromSeconds(5))||ReferenceEditor!=null)throw new Exception("Reference cancel cleanup");
        var resizeHeader=(Grid)Stage.Columns[0].Header;
        var resizeGrip=resizeHeader.Children.OfType<System.Windows.Controls.Primitives.Thumb>().ElementAt(1);
        resizeGrip.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(120,0) { RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
        ResizeStage(2,130);
        if(stageWidths[1]!=300||stageWidths[2]!=130||Session!.Text!=alignmentSession.Text)throw new Exception("Stage resize drag");
        foreach(var zoom in new[]{100,125,150})
        {
            SetZoom(zoom);
            foreach(var index in new[]{0,40,75})
            {
                Stage.ScrollIntoView(Stage.Items[index]);await Capture($"wpf-alignment-{zoom}-{index}.png");
                var rows=Visuals<StageRow>(Stage).ToArray();
                foreach(var row in rows)
                {
                    var layout=(Grid)((Border)row.Content).Child;
                    if(Math.Abs(layout.ColumnDefinitions[1].ActualWidth-300)>.1||Math.Abs(layout.ColumnDefinitions[2].ActualWidth-130)>.1)throw new Exception("Resized virtual row width");
                }
                var origins=rows.Select(r=>((Border)r.Content).Child.TransformToAncestor(Stage).Transform(new Point()).X).ToArray();
                if(origins.Length<2||origins.Max()-origins.Min()>.1)throw new Exception("Stage row alignment: "+string.Join(", ",origins));
            }
        }
        ResizeStage(1,1);if(stageWidths[1]!=100)throw new Exception("Stage minimum width");
        ResizeStage(1,20000);if(stageWidths[1]!=10000)throw new Exception("Stage maximum width");
        ResizeStage(1,StageWidth);ResizeStage(2,StageWidth);
        sessions.Remove(alignmentSession);Files.SelectedIndex=0;await Parse();SetZoom(100);
        foreach(var dark in new[]{false,true})
        {
            SetTheme(dark,false);
            var chooser=new ViewChooser(this);
            if(chooser.SelectedView!=EditorView.Stages)throw new Exception("First-run default must be B");
            chooser.Show();
            foreach(var choice in Enum.GetValues<EditorView>()){chooser.SelectView(choice);await Capture($"wpf-chooser-{choice}-{(dark?"dark":"light")}.png",chooser);}
            chooser.Close();
            SetTheme(dark,false);var update=new UpdateDialog(this,new Version(0,1,7),new Version(0,1,8));update.Show();await Capture(dark?"wpf-update-dark.png":"wpf-update-light.png",update);update.Close();
        }
        foreach(var choice in Enum.GetValues<EditorView>())
        {
            var chooser=new ViewChooser(this);chooser.SelectView(choice);
            chooser.Loaded+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>chooser.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            if(chooser.ShowDialog()!=true||chooser.SelectedView!=choice)throw new Exception("View chooser selection");
        }
        var dismissed=new ViewChooser(this);dismissed.SelectView(EditorView.Tree);
        dismissed.Loaded+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>dismissed.LaterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        if(dismissed.ShowDialog()==true)throw new Exception("View chooser dismiss must not apply");
        foreach(var accept in new[]{false,true})
        {
            var update=new UpdateDialog(this,new Version(0,1,7),new Version(0,1,8));
            update.Loaded+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=> (accept?update.InstallButton:update.LaterButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            if((update.ShowDialog()==true)!=accept)throw new Exception("Update dialog decision");
        }
        var transform=new CryptoWindow(this,"sample-key","SHA-256",true);transform.Show();await Capture("wpf-crypto.png",transform);transform.Close();
        var edit=new EditDialog(this,"값 수정","$/server/host","localhost","String");edit.Show();await Capture("wpf-edit.png",edit);edit.Close();
        Files.SelectedIndex=2;await Parse();if(Csv.Columns.Count!=3||csvRows.Count!=3)throw new Exception("CSV grid");
        foreach(var mode in Enum.GetValues<EditorView>()){ChangeView(mode,false);if(Csv.Visibility!=Visibility.Visible||Stage.Visibility==Visibility.Visible||Groups.Visibility==Visibility.Visible)throw new Exception("CSV mode isolation, zoom limits/reset/state and five editor renders");}SetZoom(150);await Capture("wpf-csv.png");SetZoom(100);
        await TestWindowTabs(output);
        File.WriteAllText(Path.Combine(output,"ui-test-result.txt"),"PASS: five formats, tabs, edit, undo, search, invalid source recovery, three views, selection and document preservation, collapse, six theme renders, preference fallback, CSV mode isolation, zoom limits/reset/state, concurrent file open deduplication, tab detach/merge/reorder, cross-window duplicate open, transferred unsaved state/history/search/source mode, empty window close");
        sessions.Clear();
    }
}







