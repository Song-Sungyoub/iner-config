using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace InerConfig;

public enum EditorView { Tree, Stages, Groups }

public partial class MainWindow
{
    private EditorView editorView = EditorView.Stages;
    private readonly string viewFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InerConfig", "view.txt");
    private readonly Dictionary<string, Border> groupSelections = [];
    internal int StageCount { get; private set; } = 2;
    internal const double StageWidth = 180;
    private readonly List<double> stageWidths = [];
    private double StageTotalWidth => stageWidths.Take(StageCount).Sum() + 140;
    private void ResizeStage(int index, double width)
    {
        stageWidths[index] = Math.Clamp(width, 100, 10000);
        if(Stage.Columns[0].Header is Grid header) { header.ColumnDefinitions[index].Width=new GridLength(stageWidths[index]);header.Width=StageTotalWidth; }
        Stage.Columns[0].Width=StageTotalWidth;
        void UpdateRows(DependencyObject parent)
        {
            if(parent is StageRow row && row.Content is Border { Child: Grid grid })
            {
                grid.Width=StageTotalWidth;grid.ColumnDefinitions[index].Width=new GridLength(stageWidths[index]);
                foreach(var guide in grid.Children.OfType<TreeGuides>())guide.InvalidateVisual();
                return;
            }
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)UpdateRows(VisualTreeHelper.GetChild(parent,i));
        }
        UpdateRows(Stage);
        if(stickyLayer!=null&&index<stickyLayer.ColumnDefinitions.Count)stickyLayer.ColumnDefinitions[index].Width=new GridLength(stageWidths[index]);
    }
    private bool restoringSelection;

    private void LoadView()
    {
        try { editorView = ParseView(File.ReadAllText(viewFile)); } catch { editorView = EditorView.Stages; }
        try{stickyEnabled=File.Exists(stickyFile)&&File.ReadAllText(stickyFile)=="true";}catch{}
        Stage.LayoutUpdated+=(_,_)=>UpdateSticky();
    }
    internal static EditorView ParseView(string value) => Enum.TryParse<EditorView>(value, out var mode) && Enum.IsDefined(mode) ? mode : EditorView.Stages;
    public void ChooseInitialView()
    {
        if (File.Exists(viewFile)) return;
        var chooser = new ViewChooser(this);
        ChangeView(chooser.ShowDialog() == true ? chooser.SelectedView : EditorView.Stages);
    }
    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        CommandItem(menu,"환경 설정…",(_,_)=>OpenPreferences());
        menu.Items.Add(new Separator());
        var update = new MenuItem { Header = "업데이트 확인…" }; update.Click += UpdateClick; menu.Items.Add(new Separator()); menu.Items.Add(update);
        var about = new MenuItem { Header = "InerConfig 정보…" }; about.Click += (_,_) => { if(new AboutDialog(this).ShowDialog()==true) UpdateClick(this,new RoutedEventArgs()); }; menu.Items.Add(about);
        menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true;
    }
    private void ChangeView(EditorView mode, bool persist = true)
    {
        editorView = mode;
        if (persist) try { Directory.CreateDirectory(Path.GetDirectoryName(viewFile)!); File.WriteAllText(viewFile, mode.ToString()); } catch (Exception ex) { Message.Text = "보기 설정을 저장하지 못했습니다: " + ex.Message; }
        Populate(); SetView(); RevealSelection();
    }
    private void RenderViews(List<ConfigNode> nodes)
    {
        StageCount = Math.Max(2, nodes.Select(n => n.Depth + (n.Group || n.Kind=="Add" ? 1 : 2)).DefaultIfEmpty(2).Max());
        while(stageWidths.Count<StageCount)stageWidths.Add(StageWidth);
        Stage.ItemsSource = null;
        if (editorView == EditorView.Stages)
        {
            Stage.ColumnHeaderHeight=stickyEnabled?61:35;
            var header = new Grid { Width = StageTotalWidth, Height = stickyEnabled?61:35 };
            header.RowDefinitions.Add(new RowDefinition{Height=new GridLength(35)});header.RowDefinitions.Add(new RowDefinition{Height=new GridLength(stickyEnabled?26:0)});
            for (int i = 0; i < StageCount; i++)
            {
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(stageWidths[i]) });
                var label = new TextBlock { Text = $"{i + 1}단계", Margin = new Thickness(12, 0, 0, 0), FontSize = 12 };
                label.SetResourceReference(ForegroundProperty, "Muted"); Grid.SetColumn(label, i); header.Children.Add(label);
                var index=i;
                var grip=new Thumb { Width=8, HorizontalAlignment=HorizontalAlignment.Right, Cursor=Cursors.SizeWE, Template=(ControlTemplate)FindResource("StageResizeGrip"), ToolTip="드래그: 열 너비 조절 · 더블클릭: 내용에 맞춤" };
                grip.DragDelta+=(_,e)=> { ResizeStage(index,stageWidths[index]+e.HorizontalChange);e.Handled=true; };
                grip.MouseDoubleClick+=(_,e)=> { AutoFitStage(index);e.Handled=true; };
                var columnMenu=new ContextMenu();var fit=new MenuItem{Header="내용에 맞춤"};fit.Click+=(_,_)=>AutoFitStage(index);columnMenu.Items.Add(fit);var reset=new MenuItem{Header="기본 너비"};reset.Click+=(_,_)=>ResizeStage(index,StageWidth);columnMenu.Items.Add(reset);grip.ContextMenu=columnMenu;label.ContextMenu=columnMenu;
                Grid.SetColumn(grip,i);header.Children.Add(grip);
            }
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            var metadata = new TextBlock { Text = "자료형 · 상태", FontSize = 12 }; Grid.SetColumn(metadata, StageCount); header.Children.Add(metadata);
            stickyLayer=new Grid{IsHitTestVisible=false,ClipToBounds=true};
            stickyLayer.SetResourceReference(BackgroundProperty,"Property");
            for(int i=0;i<StageCount;i++)stickyLayer.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(stageWidths[i])});
            Grid.SetRow(stickyLayer,1);Grid.SetColumnSpan(stickyLayer,StageCount+1);header.Children.Add(stickyLayer);stickySignature="";
            Stage.Columns[0].Header = header; Stage.Columns[0].Width = StageTotalWidth;
            Stage.ItemsSource = nodes;
        }
        GroupContent.Children.Clear(); groupSelections.Clear();
        if (editorView == EditorView.Groups)
        {
            var visible = nodes.ToHashSet();
            foreach (var node in nodes.Where(n => n.Parent == null)) GroupContent.Children.Add(BuildGroup(node, visible));
        }
        SelectNode(nodes.FirstOrDefault(n => n.Path == Session?.SelectedPath), false);
    }
    private UIElement BuildGroup(ConfigNode node, HashSet<ConfigNode> visible)
    {
        if (!node.Group) return CreateNodeRow(node, false);
        var content = new StackPanel();
        content.Children.Add(CreateNodeRow(node, false));
        if (node.HasVisibleChildren || node.Open&&Session?.StructureEditing==true)
        {
            var children = new StackPanel { Margin = new Thickness(14, 0, 12, 10) };
            foreach (var child in node.Children.Where(visible.Contains)) children.Children.Add(BuildGroup(child, visible));
            if(Session?.StructureEditing==true&&!Session.RawEditing&&Search.Text.Length==0&&!node.ReadOnly&&!node.PendingDelete)children.Children.Add(AddButton(node,12));
            content.Children.Add(children);
        }
        var card = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Margin = new Thickness(0, 8, 0, 0), Child = content };
        card.SetResourceReference(BorderBrushProperty, "Line"); card.SetResourceReference(BackgroundProperty, "Surface");
        return card;
    }
    internal FrameworkElement CreateNodeRow(ConfigNode node, bool stages)
    {
        if(node.Kind=="Add")return AddButton(node.Parent!,stages?stageWidths.Take(node.Depth).Sum():12);
        var comment = node.Kind == "Comment";
        var layout = new Grid { MinHeight = comment ? 24 : 38, DataContext = node };
        var border = new Border { BorderThickness = new Thickness(stages ? 0 : 1), BorderBrush = Brushes.Transparent, Child = layout, DataContext = node, Focusable = true, ToolTip = node.Path };
        if (stages)
        {
            // Keep every row on the header's coordinate system, regardless of content width.
            layout.Width = StageTotalWidth;
            layout.HorizontalAlignment = HorizontalAlignment.Left;
            layout.ClipToBounds = true;
            for (int i = 0; i < StageCount; i++) layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(stageWidths[i]) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            var guides = new TreeGuides { Node = node, ColumnWidths = stageWidths, Opacity = .55, IsHitTestVisible = false };
            guides.SetResourceReference(TreeGuides.StrokeProperty, "Muted"); Grid.SetColumnSpan(guides, StageCount); layout.Children.Add(guides);
        }
        else
        {
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            groupSelections[node.Path] = border;
        }
        var name = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 0, 8, 0) };
        if (node.Group)
        {
            var toggle = new Button { Content = node, ContentTemplate = (DataTemplate)FindResource("NodeArrow"), ToolTip = "클릭: 펼치기/접기 · Alt+클릭: 모든 하위 항목 포함", Width = 22, Padding = new Thickness(0), Background = Brushes.Transparent, DataContext = node };
            toggle.Click += ToggleNode; name.Children.Add(toggle);
            var count = new TextBlock { Text = node.ChildCount, FontSize = 10, Margin = new Thickness(7, 0, 0, 0), ToolTip = "전체 하위 항목 수" };
            count.SetResourceReference(ForegroundProperty, "Muted"); DockPanel.SetDock(count, Dock.Right); name.Children.Add(count);
        }
        if(Session?.StructureEditing==true){var grip=new TextBlock{Text="⠿",Width=18,ToolTip="드래그: 같은 부모 안에서 순서 변경",Cursor=System.Windows.Input.Cursors.SizeAll};name.Children.Add(grip);}
        var keyText = new TextBlock { Text = node.Name, FontWeight = node.Group ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = node.Name };
        if(node.PendingDelete){keyText.TextDecorations=TextDecorations.Strikethrough;keyText.Opacity=.5;}
        if (comment) { keyText.FontSize = 12; keyText.SetResourceReference(ForegroundProperty, "Muted"); }
        name.Children.Add(keyText);
        var keyCell = new Border { Tag="inline-key",DataContext=node,Child = name, MinHeight = comment ? 24 : 38 };
        keyCell.SetResourceReference(BackgroundProperty, "DepthBackground" + node.DepthBand);
        Grid.SetColumn(keyCell, stages ? node.Depth : 0);
        Grid.SetColumnSpan(keyCell, node.Group ? (stages ? StageCount - node.Depth : 2) : 1); layout.Children.Add(keyCell);
        if (!node.Group)
        {
            FrameworkElement value;
            if (node.Kind == "Boolean")
            {
                var check = new CheckBox { Content = node.Value, IsChecked = node.Value == "true", IsEnabled = node.Editable, DataContext = node, Margin = new Thickness(12, 0, 8, 0) };
                check.Click += ToggleBool; value = check;
            }
            else value = new TextBlock { Text = node.Display, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = node.Value, Margin = new Thickness(12, 0, 8, 0) };
            if (comment && value is TextBlock commentText) { commentText.FontSize = 12; commentText.SetResourceReference(ForegroundProperty, "Muted"); }
            var valueCell = new Border { Tag="inline-value",DataContext=node,Child = value, BorderThickness = new Thickness(1, 0, 0, 0) };
            valueCell.SetResourceReference(BorderBrushProperty, "Line"); valueCell.SetResourceReference(BackgroundProperty, "Surface");
            Grid.SetColumn(valueCell, stages ? node.Depth + 1 : 1); Grid.SetColumnSpan(valueCell, stages ? StageCount - node.Depth - 1 : 1); layout.Children.Add(valueCell);
        }
        var info = new DockPanel { Margin = new Thickness(8, 0, 0, 0) };
        var more = new Button { Content = "⋯", Padding = new Thickness(5, comment ? 0 : 5, 5, comment ? 0 : 5), Width = 28, Background = Brushes.Transparent, DataContext = node };
        more.Click += NodeMenu; DockPanel.SetDock(more, Dock.Right); info.Children.Add(more);
        var detail = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        detail.Children.Add(CreateTypePicker(node));
        if(comment)detail.ToolTip=node.State;
        if(node.State.Length>0&&!comment) { var state=new TextBlock { Text=node.State, FontSize=10, ToolTip=node.State };state.SetResourceReference(ForegroundProperty,"Muted");detail.Children.Add(state); }
        info.Children.Add(detail); Grid.SetColumn(info, stages ? StageCount : 2); layout.Children.Add(info);
        border.MouseLeftButtonDown += (_, e) =>
        {
            if (Find<Button>(e.OriginalSource as DependencyObject) != null || Find<CheckBox>(e.OriginalSource as DependencyObject) != null || Find<ComboBox>(e.OriginalSource as DependencyObject) != null) return;
            SelectNode(node, false);
            if (e.ClickCount == 2 && !inlineEnabled) { Edit(node); e.Handled = true; }
        };
        border.PreviewMouseRightButtonDown += (_, _) => SelectNode(node, false);
        border.ContextMenu = new ContextMenu(); border.ContextMenuOpening += (_, _) => border.ContextMenu = MakeMenu(node);
        border.GotKeyboardFocus += (_, _) => SelectNode(node, false);
        RowHover.Attach(border);AttachMove(border,node);return border;
    }
    internal FrameworkElement CreateTypePicker(ConfigNode node)
    {
        if(node.Kind=="Add")return new TextBlock();
        string[] choices = !node.Editable || Session?.RawEditing==true ? [] : Session?.Format is "JSON" or "YAML" ? ["String","Number","Boolean","Null"] : node.XmlValue!=null ? ["String","CDATA"] : [];
        if(choices.Length==0)
        {
            var label=new TextBlock { Text=node.DisplayKind, FontSize=11, TextTrimming=TextTrimming.CharacterEllipsis, ToolTip=node.Group?"구조 자료형": "이 항목의 자료형은 고정입니다." };
            label.Foreground=TypeColor(node.DisplayKind);return label;
        }
        var picker=new ComboBox { Height=22, Template=(ControlTemplate)FindResource("CompactTypePicker"), ItemsSource=choices, SelectedItem=node.DisplayKind, FontSize=11, Margin=new Thickness(0), Padding=new Thickness(4,0,18,0), ToolTip="자료형 변경" };
        picker.Foreground=TypeColor(node.DisplayKind);
        var typeText=new FrameworkElementFactory(typeof(TextBlock));
        typeText.SetBinding(TextBlock.TextProperty,new Binding("."));
        typeText.SetValue(TextBlock.ForegroundProperty,TypeColor(node.DisplayKind));
        picker.ItemTemplate=new DataTemplate{VisualTree=typeText};
        picker.SelectionChanged+=async (_,_)=>
        {
            if(Session==null||Session.RawEditing||picker.SelectedItem is not string kind||kind==node.DisplayKind||document==null||!document.All.Contains(node))return;
            picker.SelectedItem=node.DisplayKind;
            try { SelectNode(node,false);await Apply(document.Change(node,kind=="Null"?"null":node.Value,kind)); }
            catch(Exception ex) { Message.Text="자료형을 변경하지 않았습니다: "+ex.Message; }
        };
        return picker;
    }
    private void SelectNode(ConfigNode? node, bool reveal)
    {
        if (restoringSelection) return;
        restoringSelection = true;
        try
        {
            if (node != null && Session != null) Session.SelectedPath = node.Path;
            Tree.SelectedItem = node; Stage.SelectedItem = node;
            if (node != null) { NodePath.Text = node.Path; if (node.Note.Length > 0) Message.Text = node.Note; }
            foreach (var (path, border) in groupSelections)
                if (path == node?.Path) border.SetResourceReference(BorderBrushProperty, "Blue"); else border.BorderBrush = Brushes.Transparent;
        }
        finally { restoringSelection = false; }
        if (reveal) RevealSelection();
    }
    private void RevealSelection()
    {
        if (Tree.SelectedItem is not ConfigNode node) return;
        if (editorView == EditorView.Tree) Tree.ScrollIntoView(node);
        else if (editorView == EditorView.Stages) Stage.ScrollIntoView(node);
        else if (groupSelections.TryGetValue(node.Path, out var border)) border.BringIntoView();
    }
    private void StageSelectionChanged(object sender, SelectionChangedEventArgs e) { if (Stage.SelectedItem is ConfigNode n) SelectNode(n, false); }
}

public sealed class StageRow : ContentControl
{
    public StageRow()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        HorizontalAlignment = HorizontalAlignment.Left;
        Loaded += (_, _) => Rebuild(); DataContextChanged += (_, _) => Rebuild();
    }
    private void Rebuild() { if (DataContext is ConfigNode node && Window.GetWindow(this) is MainWindow window) Content = window.CreateNodeRow(node, true); }
}

public sealed class TypePicker : ContentControl
{
    public TypePicker() { Loaded+=(_,_)=>Rebuild();DataContextChanged+=(_,_)=>Rebuild(); }
    private void Rebuild() { if(DataContext is ConfigNode node && Window.GetWindow(this) is MainWindow window)Content=window.CreateTypePicker(node); }
}
