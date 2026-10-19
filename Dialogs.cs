using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace InerConfig;
public sealed class UpdateDialog : Window
{
    internal Button InstallButton { get; } = new() { Content = "업데이트 후 다시 실행", IsDefault = true, Padding = new Thickness(16, 10, 16, 10) };
    internal Button LaterButton { get; } = new() { Content = "나중에", IsCancel = true, Padding = new Thickness(16, 10, 16, 10) };
    public UpdateDialog(Window owner, Version current, Version next)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Owner = owner; Title = "InerConfig 업데이트"; Width = 460; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = System.Windows.Media.Brushes.Transparent; ShowInTaskbar = false;
        var body = new StackPanel { Margin = new Thickness(26) };
        var frame = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Child = body };
        frame.SetResourceReference(Border.BackgroundProperty, "Canvas"); frame.SetResourceReference(Border.BorderBrushProperty, "Line"); Content = frame;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        var close = new Button { Content = "×", Padding = new Thickness(5, 0, 5, 0), Background = System.Windows.Media.Brushes.Transparent, ToolTip = "닫기" };
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var icon = new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Icon/InerConfig-64.png")), Width = 26, Height = 26, Margin = new Thickness(0, 0, 10, 0) };
        header.Children.Add(icon);
        var title = new TextBlock { Text = "InerConfig 업데이트", FontWeight = FontWeights.SemiBold };
        title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
        header.Children.Add(title); body.Children.Add(header);
        body.Children.Add(new TextBlock { Text = "새 버전이 준비되었습니다", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var version = new TextBlock { Text = $"{current}  →  {next}", FontSize = 15, Margin = new Thickness(0, 0, 0, 16) };
        version.SetResourceReference(TextBlock.ForegroundProperty, "Blue"); body.Children.Add(version);
        var note = new TextBlock { Text = "업데이트를 설치한 뒤 InerConfig를 다시 실행합니다.\n열려 있는 탭과 미저장 내용은 로컬에 보관하여 다시 엽니다.", TextWrapping = TextWrapping.Wrap, LineHeight = 23 };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); body.Children.Add(note);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        InstallButton.SetResourceReference(Button.BackgroundProperty, "Blue"); InstallButton.Content = new TextBlock { Text = "업데이트 후 다시 실행", Foreground = System.Windows.Media.Brushes.White };
        InstallButton.Click += (_, _) => DialogResult = true; LaterButton.Click += (_, _) => Close();
        buttons.Children.Add(LaterButton); buttons.Children.Add(InstallButton); body.Children.Add(buttons);
    }
}
public sealed class EditDialog : Window
{
    private TaskCompletionSource<bool>? referenceResult;
    public Task<bool> ShowForReferenceAsync()
    {
        referenceResult=new(TaskCreationOptions.RunContinuationsAsynchronously);
        Closed+=(_,_)=>referenceResult.TrySetResult(false);
        PreviewKeyDown+=(_,e)=> { if(e.Key==System.Windows.Input.Key.Escape){Close();e.Handled=true;} };
        Show();return referenceResult.Task;
    }
    public TextBox NameBox { get; } = new();
    public TextBox ValueBox { get; } = new() { AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=130 };
    public ComboBox KindBox { get; } = new();
    public string Value => ValueBox.Text;
    public string ItemName => NameBox.Text;
    public string Kind => KindBox.SelectedItem as string ?? "String";
    public EditDialog(Window owner,string title,string path,string value,string kind,IEnumerable<string>? kinds=null,string? name=null)
    {
        SetResourceReference(StyleProperty,typeof(Window));
        Owner=owner;Title=title;Width=490;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(22)};Content=panel;
        panel.Children.Add(new TextBlock{Text=path,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
        if(name!=null){panel.Children.Add(new TextBlock{Text="이름"});NameBox.Text=name;NameBox.Margin=new Thickness(0,6,0,12);panel.Children.Add(NameBox);}
        if(kinds!=null){panel.Children.Add(new TextBlock{Text="자료형"});KindBox.ItemsSource=kinds.ToList();KindBox.SelectedItem=kind;if(KindBox.SelectedIndex<0)KindBox.SelectedIndex=0;panel.Children.Add(KindBox);}
        panel.Children.Add(new TextBlock{Text="값",Margin=new Thickness(0,12,0,6)});ValueBox.Text=value;panel.Children.Add(ValueBox);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
        var cancel=new Button{Content="취소",IsCancel=true};cancel.Click+=(_,_)=>Close();var apply=new Button{Content="적용",IsDefault=true};apply.Click+=(_,_)=> { if(referenceResult==null)DialogResult=true;else { referenceResult.TrySetResult(true);Close(); } };buttons.Children.Add(cancel);buttons.Children.Add(apply);panel.Children.Add(buttons);
        Loaded+=(_,_)=>{ValueBox.Focus();ValueBox.SelectAll();};
    }
}
public sealed class CryptoWindow : Window
{
    private readonly ComboBox mode=new(),encoding=new();
    private readonly PasswordBox password=new(){Padding=new Thickness(8)};
    private readonly TextBox input=new(){AcceptsReturn=true,Height=110,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBox result=new(){AcceptsReturn=true,Height=110,IsReadOnly=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBlock note=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)},error=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Button apply=new(){Content="선택 값에 적용"},copy=new(){Content="결과 복사"},calculate=new(){Content="계산"};
    private readonly TextBlock passwordLabel=new(){Text="비밀번호",Margin=new Thickness(0,8,0,5)};
    public string? Result { get; private set; }
    private int version;
    public CryptoWindow(Window owner,string text,string action="암호화",bool canApply=false)
    {
        SetResourceReference(StyleProperty,typeof(Window));
        Owner=owner;Title="암호화 · 복호화 · 해시";Width=540;Height=665;MinHeight=550;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(22)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        mode.ItemsSource=new[]{"암호화","복호화","SHA-256"};mode.SelectedItem=action;
        encoding.ItemsSource=new[]{"Hex","Base64"};encoding.SelectedIndex=0;
        panel.Children.Add(mode);panel.Children.Add(note);panel.Children.Add(passwordLabel);panel.Children.Add(password);panel.Children.Add(encoding);
        panel.Children.Add(new TextBlock{Text="입력 · UTF-8 (공백·개행 유지)",Margin=new Thickness(0,12,0,6)});input.Text=text;panel.Children.Add(input);panel.Children.Add(calculate);
        panel.Children.Add(new TextBlock{Text="결과",Margin=new Thickness(0,8,0,6)});panel.Children.Add(result);panel.Children.Add(error);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        buttons.Children.Add(copy);if(canApply)buttons.Children.Add(apply);buttons.Children.Add(new Button{Content="닫기",IsCancel=true});panel.Children.Add(buttons);
        void Invalidate(){version++;result.Clear();copy.IsEnabled=apply.IsEnabled=false;error.Text="";}
        void Mode(){bool hash=(string)mode.SelectedItem=="SHA-256";password.Visibility=passwordLabel.Visibility=hash?Visibility.Collapsed:Visibility.Visible;encoding.Visibility=hash?Visibility.Visible:Visibility.Collapsed;note.Text=hash?"SHA-256 해시 · 원문 복원 불가":"InerConfig CE1 · AES-256-GCM / PBKDF2-SHA256. 기존 타 프로그램의 암호문과 호환되지 않습니다.";Invalidate();}
        mode.SelectionChanged+=(_,_)=>Mode();encoding.SelectionChanged+=(_,_)=>Invalidate();input.TextChanged+=(_,_)=>Invalidate();password.PasswordChanged+=(_,_)=>Invalidate();
        calculate.Click+=async(_,_)=>
        {
            Invalidate();int rev=version;string value=input.Text,key=password.Password,operation=(string)mode.SelectedItem,representation=(string)encoding.SelectedItem;calculate.IsEnabled=false;
            try
            {
                string transformed=await Task.Run(()=>operation=="SHA-256"?Hash(value,representation):Crypto.Transform(value,key,operation=="복호화"));
                if(version==rev){result.Text=transformed;copy.IsEnabled=apply.IsEnabled=true;}
            }
            catch(CryptographicException){error.Text="복호화 실패: 비밀번호가 다르거나 암호문이 변경되었습니다.";}
            catch(Exception ex){error.Text=ex.Message;}
            finally{calculate.IsEnabled=true;}
        };
        copy.Click+=(_,_)=>{try{Clipboard.SetText(result.Text);error.Text="복사했습니다.";}catch(Exception ex){error.Text=ex.Message;}};
        apply.Click+=(_,_)=>{Result=result.Text;DialogResult=true;};Closed+=(_,_)=>{version++;password.Clear();input.Clear();result.Clear();};Mode();
    }
    public static string Hash(string text,string representation){var bytes=SHA256.HashData(Encoding.UTF8.GetBytes(text));return representation=="Base64"?Convert.ToBase64String(bytes):Convert.ToHexString(bytes).ToLowerInvariant();}
}



public sealed class AboutDialog : Window
{
    public AboutDialog(Window owner)
    {
        Owner=owner;Title="InerConfig 앱 정보";Width=400;SizeToContent=SizeToContent.Height;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=System.Windows.Media.Brushes.Transparent;ShowInTaskbar=false;
        var body=new StackPanel{Margin=new Thickness(28)};
        var frame=new Border{CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Child=body};
        frame.SetResourceReference(Border.BackgroundProperty,"Canvas");frame.SetResourceReference(Border.BorderBrushProperty,"Line");Content=frame;
        body.Children.Add(new Image{Source=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Icon/InerConfig-64.png")),Width=56,Height=56,Margin=new Thickness(0,0,0,12)});
        body.Children.Add(new TextBlock{Text="InerConfig",FontSize=24,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center});
        var version=new TextBlock{Text=$"버전 {UpdateService.Current}",HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,6,0,20)};
        version.SetResourceReference(TextBlock.ForegroundProperty,"Muted");body.Children.Add(version);
        body.Children.Add(new TextBlock{Text="설정 파일을 편리하게 읽고 수정하는 편집기",TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap});
        var formats=new TextBlock{Text="JSON · XML · YAML · INI · CSV",TextAlignment=TextAlignment.Center,Margin=new Thickness(0,10,0,0)};
        formats.SetResourceReference(TextBlock.ForegroundProperty,"Muted");body.Children.Add(formats);
        var guide=new TextBlock{Text="간단한 사용법\n일반 모드: 값·주석 수정\n구조 편집: 추가·삭제·이름·자료형 변경\n\nCtrl+F  검색     Ctrl+휠  확대·축소\nAlt+클릭  모든 하위 항목 펼침·접힘".Replace("\n",Environment.NewLine),TextWrapping=TextWrapping.Wrap,LineHeight=23};
        var card=new Border{Child=guide,Padding=new Thickness(14),Margin=new Thickness(0,20,0,0),CornerRadius=new CornerRadius(10)};card.SetResourceReference(Border.BackgroundProperty,"Surface");body.Children.Add(card);
        body.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource is TextBlock && e.ButtonState==System.Windows.Input.MouseButtonState.Pressed)DragMove();};
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,24,0,0)};
        var update=new Button{Content="업데이트 확인"};update.Click+=(_,_)=>DialogResult=true;
        var close=new Button{Content="닫기",IsCancel=true,IsDefault=true};close.Click+=(_,_)=>Close();
        buttons.Children.Add(update);buttons.Children.Add(close);body.Children.Add(buttons);
    }
}
