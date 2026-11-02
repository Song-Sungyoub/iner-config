using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace InerConfig;

public partial class MainWindow
{
    private sealed record TypeChoice(FileSession Session,string Path,string Baseline,string[] Choices);
    private TypeChoice? typeChoice;
    private int typeChoiceIndex;
    private Popup? typeChoicePopup;
    private bool typeChoiceBusy;

    private static Key ActualKey(KeyEventArgs e)=>e.Key==Key.System?e.SystemKey:e.Key;
    private bool KeyboardTypeKeys(KeyEventArgs e,ModifierKeys modifiers)
    {
        if(inlineComposing||e.Key==Key.ImeProcessed)return false;
        var key=ActualKey(e);
        bool step=modifiers==ModifierKeys.Alt&&key is Key.Up or Key.Down;
        if(typeChoiceBusy)return step||key is Key.Enter or Key.Escape;
        if(typeChoice!=null)
        {
            if(Session!=typeChoice.Session||Session.Text!=typeChoice.Baseline||sourceMode){CancelTypeChoice();return false;}
            if(key==Key.Escape){CancelTypeChoice();Message.Text="자료형 선택 취소 · 기존 입력을 유지합니다.";return true;}
            if(key==Key.Enter&&modifiers==ModifierKeys.None){_=ApplyTypeChoice();return true;}
            if(!step)
            {
                if(key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift)return false;
                CancelTypeChoice();return false;
            }
        }
        if(!step||ReferenceEditor!=null||sourceMode||Session==null)return false;
        if(typeChoice==null&&e.OriginalSource is DependencyObject origin&&Find<ComboBox>(origin)!=null)return false;
        if(!RequireStructure())return true;
        if(inlineKeyEditing){Message.Text="Key 입력을 Enter로 확정한 뒤 자료형을 선택하세요.";return true;}
        var node=InlineNode()??Tree.SelectedItem as ConfigNode;
        if(node==null||document==null)return true;
        string[] choices=TypeChoices(node);
        if(choices.Length==0){Message.Text="이 항목의 자료형은 고정입니다.";return true;}
        if(Session.Input is {} input&&(input.Path!=node.Path||input.Baseline!=Session.Text||input.KeyEditing))
        {Message.Text="미적용 입력을 먼저 복원하거나 취소하세요.";return true;}
        if(typeChoice==null)
        {
            if(inlineBox==null)SelectInline(node);
            typeChoice=new(Session,node.Path,Session.Text,choices);
            typeChoiceIndex=Math.Max(0,Array.IndexOf(choices,node.DisplayKind));
        }
        typeChoiceIndex=(typeChoiceIndex+(key==Key.Up?-1:1)+choices.Length)%choices.Length;
        ShowTypeChoice();return true;
    }
    private string[] TypeChoices(ConfigNode node)=>!node.Editable||Session?.RawEditing==true?[]:
        Session?.Format is "JSON" or "YAML"?["String","Number","Boolean","Null"]:
        node.XmlValue!=null?["String","CDATA"]:[];
    private void ShowTypeChoice()
    {
        if(typeChoice==null)return;
        typeChoicePopup?.SetCurrentValue(Popup.IsOpenProperty,false);
        var panel=new StackPanel();
        for(int i=0;i<typeChoice.Choices.Length;i++)
        {
            var text=new TextBlock{Text=(i==typeChoiceIndex?"› ":"  ")+typeChoice.Choices[i],Foreground=TypeColor(typeChoice.Choices[i]),Padding=new Thickness(9,5,12,5)};
            if(i==typeChoiceIndex)text.SetResourceReference(TextBlock.BackgroundProperty,"Selected");
            panel.Children.Add(text);
        }
        var hint=new TextBlock{Text="Alt+↑↓ 선택 · Enter 적용 · Esc 취소",FontSize=11,Margin=new Thickness(9,6,9,5)};
        hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");panel.Children.Add(hint);
        var frame=new Border{Child=panel,Padding=new Thickness(4),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7)};
        frame.SetResourceReference(Border.BackgroundProperty,"Surface");frame.SetResourceReference(Border.BorderBrushProperty,"Line");
        typeChoicePopup=new Popup{PlacementTarget=InlineAnchor()??(UIElement)EditorArea,Placement=PlacementMode.Bottom,StaysOpen=true,AllowsTransparency=true,Focusable=false,IsHitTestVisible=false,Child=frame};
        typeChoicePopup.IsOpen=true;
        Message.Text="선택 자료형: "+typeChoice.Choices[typeChoiceIndex]+" · Enter 적용 · Esc 취소";
    }
    private void CancelTypeChoice()
    {
        if(typeChoicePopup!=null)typeChoicePopup.IsOpen=false;
        typeChoicePopup=null;typeChoice=null;
    }
    private async Task ApplyTypeChoice()
    {
        if(typeChoice is not {} choice||document==null||typeChoiceBusy)return;
        typeChoiceBusy=true;
        try
        {
            if(Session!=choice.Session||Session.Text!=choice.Baseline||document.Text!=Session.Text||!Session.StructureEditing||Session.RawEditing)
                throw new InvalidOperationException("편집 대상이 변경되었습니다. 자료형을 다시 선택하세요.");
            var node=document.All.First(n=>n.Path==choice.Path);
            string kind=choice.Choices[typeChoiceIndex];
            if(!TypeChoices(node).Contains(kind))throw new InvalidOperationException("이 항목은 자료형을 변경할 수 없습니다.");
            if(inlineBox!=null&&(inlineSession!=Session||inlineBaseline!=Session.Text))throw new InvalidOperationException("현재 입력의 기준 문서가 변경되었습니다.");
            string value=kind=="Null"?"null":inlineBox?.Text??Session.Input?.Text??node.Value;
            string text=document.Change(node,value,kind);
            // Validate before ending the editor so a failed conversion never loses its draft.
            _=new ConfigDocument(text,Session.Format,Session.Separator);
            EndInline(true);CancelTypeChoice();
            if(text!=choice.Session.Text)await Apply(text);
            if(Session!=choice.Session)return;
            var target=document?.All.FirstOrDefault(n=>n.Path==choice.Path);
            if(target!=null){SelectInline(target);BeginInline(target);}
            Message.Text="자료형 적용: "+kind+" · Ctrl+Z 실행 취소";
        }
        catch(Exception ex){Message.Text="자료형을 변경하지 않았습니다: "+ex.Message+" · Alt+↑↓ 다시 선택 · Esc 취소";}
        finally{typeChoiceBusy=false;}
    }
}
