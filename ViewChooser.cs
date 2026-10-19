using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InerConfig;

public sealed class ViewChooser : Window
{
    internal EditorView SelectedView { get; private set; }
    internal readonly Button ApplyButton = new() { Content = new TextBlock { Text = "이 보기로 시작", Foreground = Brushes.White }, IsDefault = true, Padding = new Thickness(16, 10, 16, 10) };
    internal readonly Button LaterButton = new() { Content = "단계 보기로 시작", IsCancel = true };
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly RadioButton[] choices = new RadioButton[3];
    private readonly bool dark;
    public ViewChooser(Window owner, EditorView initial = EditorView.Stages, bool firstRun = true)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Owner = owner; Title = "InerConfig 보기 선택"; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = Math.Min(980, SystemParameters.WorkArea.Width - 40); Height = Math.Min(660, SystemParameters.WorkArea.Height - 40);
        MinWidth = 580; MinHeight = 420; ShowInTaskbar = false;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        dark = ((SolidColorBrush)FindResource("Canvas")).Color.R < 128;
        var grid = new Grid { Margin = new Thickness(24) };
        foreach(var size in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto}) grid.RowDefinitions.Add(new RowDefinition { Height = size });
        var frame = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Child = grid };
        frame.SetResourceReference(Border.BackgroundProperty, "Canvas"); frame.SetResourceReference(Border.BorderBrushProperty, "Line"); Content = frame;
        var heading = new DockPanel();
        var close = new Button { Content = "×", ToolTip = "닫기", Padding = new Thickness(8,0,8,0) }; close.Click += (_,_)=>Close();
        DockPanel.SetDock(close,owner.FindName("WindowButtons") is FrameworkElement chromeButtons&&chromeButtons.HorizontalAlignment==HorizontalAlignment.Left?Dock.Left:Dock.Right);heading.Children.Add(close);
        heading.Children.Add(new TextBlock { Text = "편한 보기 방식을 선택하세요", FontSize = 23, FontWeight = FontWeights.SemiBold }); grid.Children.Add(heading);
        var note = new TextBlock { Text = "같은 설정 파일을 표시한 실제 화면입니다. 선택하지 않으면 단계 보기로 시작하며, 보기 메뉴에서 언제든 바꿀 수 있습니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,18) };
        note.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetRow(note,1);grid.Children.Add(note);
        var options = new Grid { Margin = new Thickness(0,0,0,16) }; Grid.SetRow(options,2);grid.Children.Add(options);
        var labels = new[]{("트리 보기","속성과 값을 고정 열로"),("단계 보기 · 기본·추천","값을 속성의 다음 단계에"),("그룹 보기","부모별 카드로 묶어서")};
        for(int i=0;i<3;i++)
        {
            options.ColumnDefinitions.Add(new ColumnDefinition());
            var text = new StackPanel(); text.Children.Add(new TextBlock { Text = labels[i].Item1, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = labels[i].Item2, FontSize = 12, Margin = new Thickness(0,6,0,0), TextWrapping = TextWrapping.Wrap });
            var mode = (EditorView)i;
            text.Children.Add(new Image{Source=new BitmapImage(new Uri($"pack://application:,,,/Assets/ViewExamples/editor-{mode}-{(dark?"dark":"light")}.png")),Height=90,Stretch=Stretch.Uniform,Margin=new Thickness(0,10,0,0)});
            var radio = new RadioButton { Content = text, GroupName = "EditorView", Margin = new Thickness(10), VerticalContentAlignment = VerticalAlignment.Center };
            var outline=new FrameworkElementFactory(typeof(Border));outline.Name="Outline";
            outline.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));outline.SetValue(Border.BorderThicknessProperty,new Thickness(2));outline.SetValue(Border.PaddingProperty,new Thickness(10));
            outline.SetResourceReference(Border.BorderBrushProperty,"Line");outline.SetResourceReference(Border.BackgroundProperty,"Surface");outline.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            var template=new ControlTemplate(typeof(RadioButton)){VisualTree=outline};
            var selected=new Trigger{Property=RadioButton.IsCheckedProperty,Value=true};selected.Setters.Add(new Setter(Border.BorderBrushProperty,new DynamicResourceExtension("Blue"),"Outline"));template.Triggers.Add(selected);
            var focused=new Trigger{Property=RadioButton.IsKeyboardFocusedProperty,Value=true};focused.Setters.Add(new Setter(Border.BorderBrushProperty,new DynamicResourceExtension("Blue"),"Outline"));template.Triggers.Add(focused);radio.Template=template;
            radio.SetResourceReference(Control.ForegroundProperty,"Text"); radio.Checked += (_,_) => SelectView(mode);
            choices[i]=radio;Grid.SetColumn(radio,i);options.Children.Add(radio);
        }
        var previewFrame = new Border { Child = preview, BorderThickness = new Thickness(1), Padding = new Thickness(8) };
        previewFrame.SetResourceReference(Border.BackgroundProperty,"Surface");previewFrame.SetResourceReference(Border.BorderBrushProperty,"Line");Grid.SetRow(previewFrame,3);grid.Children.Add(previewFrame);
        var footer = new DockPanel { Margin = new Thickness(0,18,0,0) }; Grid.SetRow(footer,4);grid.Children.Add(footer);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };DockPanel.SetDock(buttons,Dock.Right);footer.Children.Add(buttons);
        if(!firstRun) { LaterButton.Content="취소";ApplyButton.Content=new TextBlock{Text="적용",Foreground=Brushes.White};note.Text="보기 이름을 선택해 실제 화면을 비교하세요. 편집 내용은 그대로 유지됩니다."; }
        LaterButton.Click += (_,_)=>Close();ApplyButton.SetResourceReference(Control.BackgroundProperty,"Blue");ApplyButton.Click += (_,_)=>DialogResult=true;
        buttons.Children.Add(LaterButton);buttons.Children.Add(ApplyButton);
        var csvNote=new TextBlock{Text="CSV는 모든 보기에서 표로 표시됩니다.",FontSize=12,TextWrapping=TextWrapping.Wrap};csvNote.SetResourceReference(TextBlock.ForegroundProperty,"Muted");footer.Children.Add(csvNote);
        choices[(int)initial].IsChecked=true;
    }
    internal void SelectView(EditorView mode)
    {
        SelectedView=mode;
        preview.Source=new BitmapImage(new Uri($"pack://application:,,,/Assets/ViewExamples/editor-{mode}-{(dark?"dark":"light")}.png"));
        if(choices[(int)mode] is {} choice && choice.IsChecked!=true)choice.IsChecked=true;
    }
}
