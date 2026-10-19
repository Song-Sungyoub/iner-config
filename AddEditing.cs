using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace InerConfig;

public sealed class AddDraft
{
    public string ParentPath {get;set;}="";
    public string Name {get;set;}="";
    public string Kind {get;set;}="String";
    public string Value {get;set;}="";
    public bool Entered {get;set;}
    public List<AddDraft> Children {get;set;}=[];
    public string Example {get;set;}="";
}
public sealed partial class FileSession
{
    public AddDraft? Draft {get;set;}
}
public sealed class AddItemButton : ContentControl
{
    public AddItemButton(){Loaded+=(_,_)=>Build();DataContextChanged+=(_,_)=>Build();}
    private void Build(){if(DataContext is ConfigNode {Kind:"Add",Parent: {} parent}&&Window.GetWindow(this) is MainWindow w)Content=w.AddButton(parent,0);}
}
public partial class MainWindow
{
    private Popup? addPopup;
    internal FrameworkElement AddButton(ConfigNode parent,double indent)
    {
        var b=new Button{Content="＋ 항목 추가",Height=26,FontSize=12,Margin=new Thickness(indent,0,0,0),Padding=new Thickness(8,0,8,0),HorizontalAlignment=HorizontalAlignment.Left,Background=Brushes.Transparent,ToolTip=parent.Path+"에 추가"};
        b.MouseEnter+=(_,_)=>b.SetResourceReference(Button.BackgroundProperty,"Selected");b.MouseLeave+=(_,_)=>b.Background=Brushes.Transparent;
        b.SetResourceReference(ForegroundProperty,"Muted");b.Click+=(_,_)=>OpenAddPalette(parent,b);return b;
    }
    private void OpenAddPalette(ConfigNode parent,UIElement target)
    {
        if(Session?.Draft!=null){ShowAddDraft(target);return;}
        if(!RequireStructure()||Session==null||Search.Text.Length>0)return;
        addPopup?.SetCurrentValue(Popup.IsOpenProperty,false);
        var palette=new WrapPanel{MaxWidth=650};
        string[] kinds=Session.Format=="XML"?["Element","Attribute","Comment"]:Session.Format=="INI"?["String"]:["String","Number","Boolean","Null","Object","Array"];
        void Option(string label,string kind,Action action)
        {
            var b=new Button{Content=new TextBlock{Text=label,Foreground=TypeColor(kind)},Foreground=TypeColor(kind),Padding=new Thickness(9,6,9,6)};
            b.Click+=(_,_)=>{addPopup!.IsOpen=false;action();};palette.Children.Add(b);
        }
        var sample=parent.Children.LastOrDefault();
        if(parent.Kind is "Array" or "Sequence"&&sample!=null&&!ConfigDocument.Walk([sample]).Any(n=>n.ReadOnly))
        {
            foreach(bool copy in new[]{false,true})Option(copy?"값까지 복제":"이전 구조로 추가",sample.Kind,()=> {
                AddDraft Clone(ConfigNode n)=>new(){Name=n.Name,Kind=n.Kind switch{"Mapping"=>"Object","Sequence"=>"Array",_=>n.DisplayKind},Example=n.Value,Value=copy?n.Value:"",Entered=copy||n.Group,Children=n.Children.Select(Clone).ToList()};
                Session.Draft=Clone(sample);Session.Draft.ParentPath=parent.Path;Session.Notify();ShowAddDraft(target);
            });
        }
        foreach(var kind in kinds)
        {
            var label=kind switch{"String"=>"문자열","Number"=>"숫자","Boolean"=>"참/거짓","Null"=>"null","Object"=>"그룹","Array"=>"리스트","Element"=>"태그","Attribute"=>"속성","Comment"=>"주석",_=>kind};
            Option(label,kind,()=>{Session.Draft=new(){ParentPath=parent.Path,Kind=kind,Entered=kind is "Null" or "Object" or "Array"};Session.Notify();ShowAddDraft(target);});
        }
        ShowAddPopup(target,palette);
    }
    private void ShowAddPopup(UIElement target,UIElement content)
    {
        addPopup=new Popup{PlacementTarget=target,Placement=PlacementMode.Bottom,StaysOpen=false,AllowsTransparency=true,
            Child=new Border{Child=content,Padding=new Thickness(10),CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1)}};
        var frame=(Border)addPopup.Child;frame.SetResourceReference(Border.BackgroundProperty,"Surface");frame.SetResourceReference(Border.BorderBrushProperty,"Line");addPopup.IsOpen=true;
    }
    private void ShowAddDraft(UIElement target)
    {
        if(Session?.Draft is not {} draft)return;
        var session=Session;
        var parent=document?.All.FirstOrDefault(n=>n.Path==draft.ParentPath);
        if(parent==null){Message.Text="추가 대상이 없어졌습니다. 편집 전체 취소로 초안을 정리하세요.";return;}
        var panel=new StackPanel{Width=400};
        panel.Children.Add(new TextBlock{Text=parent.Path+" · 추가 중",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        void Changed(){session.Notify();}
        bool needsName=parent.Kind is not ("Array" or "Sequence");
        if(needsName)
        {
            panel.Children.Add(new TextBlock{Text="이름"});
            var name=new TextBox{Text=draft.Name};name.TextChanged+=(_,_)=>{draft.Name=name.Text;Changed();};panel.Children.Add(name);
        }
        void Field(AddDraft field,int depth)
        {
            panel.Children.Add(new TextBlock{Text=new string(' ',depth*2)+field.Name+" · "+field.Kind,Foreground=TypeColor(field.Kind)});
            if(field.Kind is "Object" or "Array"){foreach(var child in field.Children.ToArray()){Field(child,depth+1);var remove=new Button{Content=child.Name+" 제외",FontSize=11};remove.Click+=(_,_)=>{field.Children.Remove(child);Changed();addPopup!.IsOpen=false;ShowAddDraft(target);};panel.Children.Add(remove);}return;}
            if(field.Kind=="Boolean")
            {
                var pick=new ComboBox{ItemsSource=new[]{"true","false"},SelectedIndex=field.Entered?(field.Value=="true"?0:1):-1};
                pick.SelectionChanged+=(_,_)=>{field.Value=pick.SelectedItem?.ToString()??"";field.Entered=pick.SelectedIndex>=0;Changed();};panel.Children.Add(pick);
            }
            else if(field.Kind=="Null")
            {
                var check=new CheckBox{Content="null 지정",IsChecked=field.Entered};check.Click+=(_,_)=>{field.Entered=check.IsChecked==true;Changed();};panel.Children.Add(check);
            }
            else
            {
                var grid=new Grid();var box=new TextBox{Text=field.Value,MinHeight=30};
                var hint=new TextBlock{Text=field.Example,Opacity=.5,Margin=new Thickness(10,0,0,0),IsHitTestVisible=false};
                grid.Children.Add(box);grid.Children.Add(hint);panel.Children.Add(grid);
                void Hint()=>hint.Visibility=!box.IsKeyboardFocusWithin&&!field.Entered?Visibility.Visible:Visibility.Collapsed;
                box.TextChanged+=(_,_)=>{field.Value=box.Text;field.Entered=box.Text.Length>0;Hint();Changed();};
                box.GotKeyboardFocus+=(_,_)=>Hint();box.LostKeyboardFocus+=(_,_)=>Hint();Hint();
                var previous=new TextBlock{Text=field.Example.Length>0?"이전 값: "+field.Example:"",FontSize=11,Opacity=.6,TextWrapping=TextWrapping.Wrap};panel.Children.Add(previous);
                var empty=new CheckBox{Content="빈 값 지정",IsChecked=field.Entered&&field.Value.Length==0};
                empty.Click+=(_,_)=>{if(empty.IsChecked==true)box.Text="";field.Entered=empty.IsChecked==true||box.Text.Length>0;Hint();Changed();};panel.Children.Add(empty);
            }
        }
        Field(draft,0);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="추가 취소"};cancel.Click+=(_,_)=>{session.Draft=null;session.Notify();addPopup!.IsOpen=false;};
        var apply=new Button{Content="항목 추가"};apply.Click+=async(_,_)=>{try{await CommitAddDraft();addPopup!.IsOpen=false;}catch(Exception ex){Message.Text=ex.Message;}};
        actions.Children.Add(cancel);actions.Children.Add(apply);panel.Children.Add(actions);
        panel.PreviewKeyDown+=async(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter&&(System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control||draft.Children.Count==0)){e.Handled=true;try{await CommitAddDraft();addPopup!.IsOpen=false;}catch(Exception ex){Message.Text=ex.Message;}}};
        panel.PreviewKeyDown+=async(_,e)=>
        {
            if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;addPopup!.IsOpen=false;}
            if(e.Key==System.Windows.Input.Key.S&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control)
            {e.Handled=true;try{await CommitAddDraft();addPopup!.IsOpen=false;Save(session,false);}catch(Exception ex){Message.Text=ex.Message;}}
        };
        ShowAddPopup(target,new ScrollViewer{MaxHeight=430,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=panel});
    }
    private string DraftRaw(AddDraft d)
    {
        if(d.Kind=="Object")return "{"+string.Join(",",d.Children.Select(c=>ConfigDocument.Scalar(c.Name,"String")+":"+DraftRaw(c)))+"}";
        if(d.Kind=="Array")return "["+string.Join(",",d.Children.Select(DraftRaw))+"]";
        if(!d.Entered)throw new InvalidOperationException(d.Name+" 값을 입력하세요. 예시는 저장되지 않습니다.");
        return ConfigDocument.Scalar(d.Value,d.Kind);
    }
    private async Task CommitAddDraft()
    {
        var text=BuildAddDraft();if(text==null)return;Session!.Draft=null;await Apply(text);
    }
    private string? BuildAddDraft()
    {
        if(Session?.Draft is not {} draft||document==null)return null;
        var parent=document.All.FirstOrDefault(n=>n.Path==draft.ParentPath)??throw new InvalidOperationException("추가 대상이 변경되었습니다.");
        if(parent.Kind is not ("Array" or "Sequence")&&draft.Kind!="Comment"&&string.IsNullOrWhiteSpace(draft.Name))throw new InvalidOperationException("이름을 입력하세요.");
        string text;
        if(Session.Format is "JSON" or "YAML")text=document.InsertRaw(parent,draft.Name,DraftRaw(draft));
        else
        {
            if(!draft.Entered)throw new InvalidOperationException("값 또는 빈 값 지정을 입력하세요.");
            text=document.Insert(parent,draft.Name,draft.Value,draft.Kind);
        }
        return text;
    }
}
public sealed partial class ConfigDocument
{
    public string InsertRaw(ConfigNode parent,string name,string raw)
    {
        _=System.Text.Json.JsonDocument.Parse(raw);
        if(Format=="JSON")
        {
            if(parent.Kind=="Object"&&parent.Children.Any(c=>c.Name==name))throw new FormatException("중복 이름");
            string nl=Text.Contains("\r\n")?"\r\n":"\n";
            return Checked(Patch(parent.End-1,parent.End-1,(parent.Children.Count>0?",":"")+nl+(parent.Kind=="Object"?Scalar(name,"String")+":":"")+raw+nl));
        }
        if(Format=="YAML")
        {
            if(IsFlow(parent))
            {
                bool mapping=parent.Kind=="Mapping";
                if(mapping&&parent.Children.Any(n=>n.Name==name))throw new FormatException("중복 이름");
                int close=parent.End-1;
                if(close<parent.Start||close>=Text.Length||Text[close]!=(mapping?'}':']'))
                    throw new InvalidOperationException($"YAML collection span {parent.Start}..{parent.End}: {Text}");
                return Checked(Patch(close,close,(parent.Children.Count>0?", ":"")+(mapping?Scalar(name,"String")+": ":"")+raw));
            }
            // JSON flow syntax is valid YAML; only patch this new subtree.
            string placeholder=Insert(parent,name,"","String");
            var parsed=new ConfigDocument(placeholder,Format,Separator);
            var p=parsed.All.First(n=>n.Path==parent.Path);
            var added=p.Children.Last();
            return parsed.Checked(parsed.Patch(added.Start,added.End,raw));
        }
        throw new InvalidOperationException();
    }
}
