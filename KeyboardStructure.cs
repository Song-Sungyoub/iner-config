using System.Windows;
using System.Windows.Input;
namespace InerConfig;

public sealed partial class ConfigDocument
{
    public string UniqueKey(ConfigNode parent,string basis="item")
    {
        var name=basis;int i=2;
        while(parent.Children.Any(n=>n.Name==name))name=basis+i++;
        return name;
    }
    public (string Text,string Path) InsertSibling(ConfigNode node,string name,string value,string kind)
    {
        var parent=node.Parent??throw new InvalidOperationException("루트 아래 그룹을 선택하세요.");
        if(node.ReadOnly||node.PendingDelete)throw new InvalidOperationException("읽기 전용·삭제 예정 항목 옆에는 추가할 수 없습니다.");
        var text=Insert(parent,name,value,kind);
        var parsed=new ConfigDocument(text,Format,Separator);
        var p=parsed.All.First(n=>n.Path==parent.Path);
        var added=p.Children.Last();
        var anchor=p.Children[parent.Children.IndexOf(node)];
        text=Format=="YAML"?parsed.MoveYamlBefore(added,anchor,true):parsed.MoveBefore(added,anchor,true);
        parsed=new ConfigDocument(text,Format,Separator);
        p=parsed.All.First(n=>n.Path==parent.Path);
        return(text,p.Children[parent.Children.IndexOf(node)+1].Path);
    }
    public (string Text,string Path) ChangeLevel(ConfigNode node,bool outdent)
    {
        var parent=node.Parent??throw new InvalidOperationException("루트의 단계는 변경할 수 없습니다.");
        if(node.PendingDelete||Walk([node]).Any(n=>n.ReadOnly||n.PendingDelete))throw new InvalidOperationException("읽기 전용·삭제 예정 항목은 이동할 수 없습니다.");
        ConfigNode target;
        if(outdent)target=parent.Parent??throw new InvalidOperationException("이미 최상위 단계입니다.");
        else
        {
            int i=parent.Children.IndexOf(node);
            if(i<=0)throw new InvalidOperationException("앞 형제 그룹이 없어 자식으로 이동할 수 없습니다.");
            target=parent.Children[i-1];
            if(!target.Group)throw new InvalidOperationException("바로 앞 항목이 그룹이나 리스트여야 합니다.");
        }
        if(target.ReadOnly||target.PendingDelete)throw new InvalidOperationException("이동할 그룹을 수정할 수 없습니다.");
        if(Format is "CSV" or "INI")throw new InvalidOperationException(Format+"는 중첩 단계 이동을 지원하지 않습니다.");
        if(Format=="XML"&&(node.Kind!="Element"||target.Kind!="Element"||parent.Children.Any(n=>n.Kind is "Text" or "CDATA")||target.Children.Any(n=>n.Kind is "Text" or "CDATA")))
            throw new InvalidOperationException("XML 속성·주석·혼합 내용의 단계는 원문에서 수정하세요.");
        var raw=Text[node.Start..node.End];
        string name=node.Name;
        if(Format=="JSON"&&target.Kind=="Object")
        {
            if(parent.Kind=="Array")name=UniqueKey(target);
            else if(target.Children.Any(n=>n!=node&&n.Name==name))throw new InvalidOperationException("이동할 그룹에 같은 Key가 있습니다.");
        }
        if(Format=="YAML")
        {
            ValidateYamlMove(node,target);
            raw=Text[LineStart(node.KeyStart>=0?node.KeyStart:node.Start)..BlockEnd(node)];
        }
        var removed=new ConfigDocument(Format=="YAML"?Checked(Patch(LineStart(node.KeyStart>=0?node.KeyStart:node.Start),BlockEnd(node),"")):Delete(node),Format,Separator);
        var destination=removed.All.First(n=>n.Path==target.Path);
        string inserted;
        if(Format=="JSON")inserted=removed.InsertRaw(destination,name,raw);
        else if(Format=="XML")
        {
            int pos=removed.Text.LastIndexOf("</",destination.End-1,StringComparison.Ordinal);
            inserted=removed.Checked(removed.Patch(pos,pos,raw));
        }
        else if(Format=="YAML")
        {
            var sample=destination.Children.FirstOrDefault()??throw new InvalidOperationException("빈 YAML 그룹은 원문에서 편집하세요.");
            int indent=removed.YamlIndent(sample),oldIndent=YamlIndent(node);
            raw=ReindentYaml(raw,oldIndent,indent);
            int pos=removed.BlockEnd(destination.Children.Last());
            string nl=Text.Contains("\r\n")?"\r\n":"\n";
            inserted=removed.Checked(removed.Patch(pos,pos,(pos>0&&removed.Text[pos-1]!='\n'?nl:"")+raw+(raw.EndsWith('\n')?"":nl)));
        }
        else throw new InvalidOperationException("지원하지 않는 단계 이동입니다.");
        var parsed=new ConfigDocument(inserted,Format,Separator);
        var dest=parsed.All.First(n=>n.Path==target.Path);
        var added=dest.Children.Last();
        int index=dest.Children.Count-1;
        if(outdent)
        {
            var anchor=dest.Children.First(n=>n.Path==parent.Path);
            index=dest.Children.IndexOf(anchor)+1;
            inserted=Format=="YAML"?parsed.MoveYamlBefore(added,anchor,true):parsed.MoveBefore(added,anchor,true);
        }
        parsed=new ConfigDocument(inserted,Format,Separator);
        return(inserted,parsed.All.First(n=>n.Path==target.Path).Children[index].Path);
    }
    private int YamlIndent(ConfigNode n)
    {
        int anchor=n.KeyStart>=0?n.KeyStart:n.Start,start=LineStart(anchor);
        if(n.KeyStart>=0)return anchor-start;
        string prefix=Text[start..anchor];int dash=prefix.LastIndexOf('-');
        return n.Parent?.Kind=="Sequence"&&dash>=0?dash:prefix.TakeWhile(c=>c==' ').Count();
    }
    private void ValidateYamlMove(ConfigNode node,ConfigNode target)
    {
        if(node.Parent==null||IsFlow(node.Parent)||IsFlow(target)||IsFlow(node)||target.Children.Count==0||node.Parent.Kind!=target.Kind||Walk([node,target]).Any(n=>n.ReadOnly))
            throw new InvalidOperationException("YAML 단계 이동은 같은 종류의 블록 그룹 사이에서 지원합니다. 흐름·참조·빈 그룹은 원문에서 편집하세요.");
        if(target.Kind=="Mapping"&&target.Children.Any(n=>n!=node&&n.Name==node.Name))throw new InvalidOperationException("이동할 그룹에 같은 Key가 있습니다.");
    }
    private static string ReindentYaml(string raw,int from,int to)=>System.Text.RegularExpressions.Regex.Replace(raw,@"(?m)^ *",m=>new string(' ',Math.Max(0,m.Length-from+to)));
    private string MoveYamlBefore(ConfigNode node,ConfigNode target,bool after)
    {
        if(node==target)return Text;
        if(node.Parent!=target.Parent||node.Parent==null||IsFlow(node.Parent)||Walk([node,target]).Any(n=>n.ReadOnly))throw new InvalidOperationException("YAML 블록 그룹 안에서 이동하세요.");
        int start=LineStart(node.KeyStart>=0?node.KeyStart:node.Start),end=BlockEnd(node);
        string raw=Text[start..end];
        int pos=after?BlockEnd(target):LineStart(target.KeyStart>=0?target.KeyStart:target.Start);
        if(pos>=start&&pos<=end)return Text;
        var without=Patch(start,end,"");if(pos>end)pos-=end-start;
        string nl=Text.Contains("\r\n")?"\r\n":"\n";
        if(!raw.EndsWith('\n')&&pos<without.Length)raw+=nl;
        if(pos>0&&without[pos-1]!='\n')raw=nl+raw;
        return Checked(without[..pos]+raw+without[pos..]);
    }
}

public partial class MainWindow
{
    private bool keyboardStructureBusy;
    private bool KeyboardStructureKeys(KeyEventArgs e,ModifierKeys modifiers)
    {
        if(ReferenceEditor!=null||sourceMode||Session==null||inlineComposing||e.Key==Key.ImeProcessed)return false;
        bool add=e.Key==Key.Enter&&modifiers==ModifierKeys.Control;
        bool rename=e.Key==Key.F2&&modifiers==ModifierKeys.None;
        bool level=e.Key==Key.Tab&&(modifiers==ModifierKeys.None||modifiers==ModifierKeys.Shift)&&Session.StructureEditing;
        if(!add&&!rename&&!level)return false;
        if(keyboardStructureBusy)return true;
        if(Session.Format=="CSV")
        {
            if(add){if(FindFocusedCsv()!=null&&!Csv.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell,true))return true;AddCsvRow();return true;}
            if(rename){Message.Text="CSV의 Key는 열 제목입니다. 헤더 이름 변경 메뉴를 사용하세요.";return true;}
            return false;
        }
        if(e.OriginalSource is DependencyObject origin&&(Find<System.Windows.Controls.ComboBox>(origin)!=null||Find<System.Windows.Controls.TextBox>(origin) is {} box&&box!=inlineBox))return false;
        if(!RequireStructure())return true;
        if(Search.Text.Length>0){Message.Text="검색을 해제한 뒤 구조를 변경하세요.";return true;}
        if(!CommitInline())return true;
        var node=InlineNode()??Tree.SelectedItem as ConfigNode;
        if(node==null||node.Kind=="Add")return true;
        if(rename){BeginInlineKey(node);return true;}
        _=KeyboardStructureChange(node,add,modifiers.HasFlag(ModifierKeys.Shift));return true;
    }
    private async Task KeyboardStructureChange(ConfigNode node,bool add,bool outdent)
    {
        if(document==null||Session==null)return;
        var session=Session;
        keyboardStructureBusy=true;
        try
        {
            (string Text,string Path) result;
            if(add)
            {
                var parent=node.Parent??node;
                var kind=Session.Format=="XML"?"Element":"String";
                string name=document.UniqueKey(parent,Session.Format=="XML"?"item":"newKey");
                if(node.Parent==null)
                {
                    string text=document.Insert(parent,name,"",kind);
                    var doc=new ConfigDocument(text,Session.Format,Session.Separator);
                    result=(text,doc.All.First(n=>n.Path==parent.Path).Children.Last().Path);
                }
                else result=document.InsertSibling(node,name,"",kind);
            }
            else result=document.ChangeLevel(node,outdent);
            inlinePath=result.Path;Session.SelectedPath=result.Path;
            for(var p=new ConfigDocument(result.Text,Session.Format,Session.Separator).All.First(n=>n.Path==result.Path).Parent;p!=null;p=p.Parent)Session.Collapsed.Remove(p.Path);
            await Apply(result.Text);
            if(Session!=session)return;
            var target=document?.All.FirstOrDefault(n=>n.Path==result.Path);
            if(target!=null){SelectInline(target,!add);if(add){if(target.KeyStart>=0)BeginInlineKey(target);else BeginInline(target);}}
            Message.Text=add?"항목 추가 · F2 Key · Enter 다음 Key · Tab 자식 · Shift+Tab 상위 단계 · Ctrl+Z 취소":"단계 변경 · Ctrl+Z 취소";
        }
        catch(Exception ex){Message.Text=ex.Message;}
        finally{keyboardStructureBusy=false;}
    }
    private void BeginInlineKey(ConfigNode node)
    {
        if(node.KeyStart<0||node.ReadOnly||node.PendingDelete){Message.Text="이 항목은 Key를 변경할 수 없습니다.";return;}
        BeginInline(node,null,true);
    }
}
