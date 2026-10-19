using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace InerConfig;
public partial class ScriptTransformWindow : Window
{
    internal ScriptTransformViewModel Model {get;}
    private bool allowClose,closing;
    public ScriptTransformWindow(Window owner,ScriptTransformViewModel model,bool right)
    {
        InitializeComponent();SetResourceReference(StyleProperty,typeof(Window));Owner=owner;FontFamily=owner.FontFamily;Model=model;DataContext=model;
        CloseButton.HorizontalAlignment=right?HorizontalAlignment.Right:HorizontalAlignment.Left;
        Width=Math.Min(1100,SystemParameters.WorkArea.Width-30);Height=Math.Min(780,SystemParameters.WorkArea.Height-30);
        MinWidth=Math.Min(820,Width);MinHeight=Math.Min(620,Height);
        model.Confirm=(title,text,buttons)=>Task.FromResult(Ask(this,title,text,buttons));
        model.CopyText=Clipboard.SetText;
        model.CloseRequested+=()=>{allowClose=true;Close();};
        model.ErrorPositionRequested+=(line,column)=>{if(line<1||line>CodeBox.LineCount)return;CodeBox.Focus();CodeBox.CaretIndex=CodeBox.GetCharacterIndexFromLineIndex(line-1)+Math.Max(0,Math.Min(column-1,CodeBox.GetLineLength(line-1)));CodeBox.ScrollToLine(line-1);};
        bool initialized=false;
        Loaded+=async(_,_)=>{await model.InitializeAsync();initialized=true;};
        Activated+=async(_,_)=>{if(initialized)await model.RefreshLibraryAsync();};
        Closing+=ClosingScript;
        PreviewKeyDown+=(_,e)=>
        {
            ICommand? command=Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.Enter?model.RunCommand:Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.S?model.SaveCommand:e.Key==Key.Escape?model.StopCommand:null;
            if(command!=null){if(command.CanExecute(null))command.Execute(null);e.Handled=true;}
        };
    }
    private async void ClosingScript(object? sender,CancelEventArgs e)
    {
        if(allowClose)return;e.Cancel=true;if(closing)return;closing=true;
        try{await Model.CloseAsync();}finally{closing=false;}
    }
    private void HeaderDrag(object sender,MouseButtonEventArgs e)
    {if(e.OriginalSource is not Button && MainWindow.FindButtonForScript(e.OriginalSource as DependencyObject)==null)DragMove();}
    private void CodeKeys(object sender,KeyEventArgs e)
    {
        if(e.Key!=Key.Tab)return;e.Handled=true;
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){CodeBox.MoveFocus(new TraversalRequest(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?FocusNavigationDirection.Previous:FocusNavigationDirection.Next));return;}
        CodeBox.SelectedText="    ";CodeBox.CaretIndex=CodeBox.SelectionStart+4;
    }
    private void CodeScrolled(object sender,ScrollChangedEventArgs e)=>LineScroll.ScrollToVerticalOffset(e.VerticalOffset);
    internal static int Ask(Window owner,string title,string text,string[] labels)
    {
        int answer=-1;
        var window=new Window{Owner=owner,Title=title,Width=480,SizeToContent=SizeToContent.Height,MaxHeight=Math.Max(200,SystemParameters.WorkArea.Height-50),WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,ResizeMode=ResizeMode.NoResize,FontFamily=owner.FontFamily,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=System.Windows.Media.Brushes.Transparent};
        window.SetResourceReference(StyleProperty,typeof(Window));
        var panel=new StackPanel{Margin=new Thickness(22)};
        panel.Children.Add(new TextBlock{Text=title,FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,14)});
        panel.Children.Add(new ScrollViewer{MaxHeight=350,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap}});
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};
        for(int i=0;i<labels.Length;i++){int index=i;var b=new Button{Content=labels[i],Margin=new Thickness(4),Padding=new Thickness(12,8,12,8)};b.Click+=(_,_)=>{answer=index;window.Close();};buttons.Children.Add(b);}
        panel.Children.Add(buttons);
        var frame=new Border{CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Child=panel};frame.SetResourceReference(Border.BackgroundProperty,"Canvas");frame.SetResourceReference(Border.BorderBrushProperty,"Line");window.Content=frame;
        panel.Children[0].MouseLeftButtonDown+=(_,_)=>window.DragMove();window.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;window.Close();}};
        window.ShowDialog();return answer;
    }
}
