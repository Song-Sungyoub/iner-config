using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace InerConfig;
public sealed class ConfigNode
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Kind { get; set; } = "String";
    public string Note { get; set; } = "";
    public ConfigNode? Parent;
    public List<ConfigNode> Children { get; } = [];
    public int Start, End, KeyStart = -1, KeyEnd;
    public bool PendingDelete {get;set;}
    public bool ReadOnly, Open = true;
    public object? Tag;
    public (int Start, int End, bool SelfClosing, bool CData)? XmlValue;
    public string DisplayKind => XmlValue is { } leaf ? leaf.CData ? "CDATA" : "String" : Kind is "Attribute" or "Text" ? "String" : Kind;
    public bool Group => XmlValue == null && Kind is ("Object" or "Array" or "Mapping" or "Sequence" or "Section" or "Element" or "Row");
    public bool Editable => !Group && !ReadOnly && !PendingDelete && Kind!="Add";
    public string Path => Parent == null ? Name : Parent.Path + "/" + Name.Replace("~", "~0").Replace("/", "~1");
    public int Depth => Parent == null ? 0 : Parent.Depth + 1;
    public int DepthBand => Math.Min(Depth, 4);
    public bool LastVisibleSibling { get; set; }
    public bool HasVisibleChildren { get; set; }
    public bool TopGroup => Group && (Parent == null || Parent.Parent == null && Parent.Name == "$");
    public string ChildCount => Children.Count.ToString();
    public System.Windows.Thickness Indent => new(Depth * 20, 0, 0, 0);
    public string Display => Kind=="Add" ? "" : Group ? "" : Value.Length == 0 ? "(빈 문자열)" : Value.Replace("\r", "").Replace("\n", " ↵ ");
    public string State { get; set; } = "";
}
public sealed partial class ConfigDocument
{
    public string Text { get; }
    public string Format { get; }
    public char Separator { get; }
    public List<ConfigNode> Roots { get; } = [];
    public IEnumerable<ConfigNode> All => Walk(Roots);
    public static IEnumerable<ConfigNode> Walk(IEnumerable<ConfigNode> nodes) { foreach (var n in nodes) { yield return n; foreach (var c in Walk(n.Children)) yield return c; } }
    private ConfigNode Add(ConfigNode? p,string name,string kind,int start,int end,string value="")
    { var n=new ConfigNode{Parent=p,Name=name,Kind=kind,Start=start,End=end,Value=value};(p?.Children??Roots).Add(n);return n; }
    public ConfigDocument(string text,string format,char separator=',')
    {
        Text=text;Format=format;Separator=separator;
        switch(format){case "JSON":Json();break;case "XML":Xml();break;case "YAML":Yaml();break;case "INI":Ini();break;case "CSV":Csv();break;default:throw new FormatException("지원하지 않는 형식입니다.");}
    }
    private void Json()
    {
        using var check=JsonDocument.Parse(Text);
        var bytes=Encoding.UTF8.GetBytes(Text);var r=new Utf8JsonReader(bytes);var stack=new Stack<ConfigNode>();
        string key="";int ks=-1,ke=0;var offsets=new int[bytes.Length+1];int bp=0,cp=0;
        foreach(var rune in Text.EnumerateRunes()){for(int i=0;i<rune.Utf8SequenceLength;i++)offsets[bp++]=cp;cp+=rune.Utf16SequenceLength;}offsets[bp]=cp;
        while(r.Read())
        {
            int s=offsets[(int)r.TokenStartIndex],e=offsets[(int)r.BytesConsumed];
            if(r.TokenType==JsonTokenType.PropertyName){key=r.GetString()!;ks=s;ke=s+Encoding.UTF8.GetCharCount(r.ValueSpan)+2;continue;}
            if(r.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject){stack.Pop().End=e;continue;}
            var p=stack.Count>0?stack.Peek():null;var name=p==null?"$":p.Kind=="Array"?$"[{p.Children.Count}]":key;
            if(p?.Kind=="Object"&&p.Children.Any(n=>n.Name==name))throw new FormatException("중복 JSON 키: "+name);
            var kind=r.TokenType switch{JsonTokenType.StartObject=>"Object",JsonTokenType.StartArray=>"Array",JsonTokenType.String=>"String",JsonTokenType.Number=>"Number",JsonTokenType.True or JsonTokenType.False=>"Boolean",_=>"Null"};
            var n=Add(p,name,kind,s,e,r.TokenType==JsonTokenType.String?r.GetString()!:Text[s..e]);
            if(p?.Kind=="Object"){n.KeyStart=ks;n.KeyEnd=ke;}ks=-1;if(n.Group)stack.Push(n);
        }
    }
    private static string Decode(string s)=>Regex.Replace(s,@"&(#x[0-9a-fA-F]+|#\d+|amp|lt|gt|quot|apos);",m=>m.Groups[1].Value switch{"amp"=>"&","lt"=>"<","gt"=>">","quot"=>"\"","apos"=>"'",var n when n.StartsWith("#x")=>char.ConvertFromUtf32(Convert.ToInt32(n[2..],16)),var n=>char.ConvertFromUtf32(int.Parse(n[1..]))});
    private void Xml()
    {
        using var xr=XmlReader.Create(new StringReader(Text),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=50_000_000});_=XDocument.Load(xr);
        var stack=new Stack<ConfigNode>();
        foreach(Match t in Regex.Matches(Text,"<!--[\\s\\S]*?-->|<!\\[CDATA\\[[\\s\\S]*?\\]\\]>|<\\?[\\s\\S]*?\\?>|</[^>]+>|<(?:\"[^\"]*\"|'[^']*'|[^'\">])*>|[^<]+"))
        {
            var s=t.Value;var p=stack.Count>0?stack.Peek():null;
            if(s.StartsWith("<?"))continue;
            if(s.StartsWith("<!--")){Add(p,"#comment","Comment",t.Index,t.Index+s.Length,s[4..^3]);continue;}
            if(s.StartsWith("<![CDATA[")){Add(p,"#cdata","CDATA",t.Index+9,t.Index+s.Length-3,s[9..^3].Replace("\r\n","\n").Replace('\r','\n'));continue;}
            if(s.StartsWith("</")){stack.Pop().End=t.Index+s.Length;continue;}
            if(s.StartsWith('<'))
            {
                var name=Regex.Match(s,@"^<([^\s/>]+)").Groups[1];var n=Add(p,name.Value,"Element",t.Index,t.Index+s.Length);n.KeyStart=t.Index+1;n.KeyEnd=n.KeyStart+name.Length;
                foreach(Match a in Regex.Matches(s,"([^\\s=<>/]+)\\s*=\\s*([\"'])(.*?)\\2",RegexOptions.Singleline))
                {var v=a.Groups[3];var at=Add(n,"@"+a.Groups[1].Value,"Attribute",t.Index+v.Index,t.Index+v.Index+v.Length,Decode(v.Value.Replace("\r\n","\n").Replace('\r','\n').Replace('\n',' ').Replace('\t',' ')));at.KeyStart=t.Index+a.Groups[1].Index;at.KeyEnd=at.KeyStart+a.Groups[1].Length;at.Tag=(t.Index+a.Index,t.Index+a.Index+a.Length);if(at.Name=="@xmlns"||at.Name.StartsWith("@xmlns:")){at.ReadOnly=true;at.Note="네임스페이스는 원문에서 수정하세요.";}}
                if(!s.EndsWith("/>"))stack.Push(n);
            }
            else if(p!=null&&!string.IsNullOrWhiteSpace(s))Add(p,"#text","Text",t.Index,t.Index+s.Length,Decode(s.Replace("\r\n","\n").Replace('\r','\n')));
        }
        foreach(var element in All.Where(n=>n.Kind=="Element").ToList())
        {
            if(element.Children.Count==0 || element.Children.Count==1 && element.Children[0].Kind is "Text" or "CDATA")
            {
                var child=element.Children.FirstOrDefault();
                var selfClosing=Text[(element.End-2)..element.End]=="/>";
                var start=selfClosing?element.End-2:FindTagEnd(element.Start)+1;
                var end=selfClosing?element.End:Text.LastIndexOf("</",element.End-1,StringComparison.Ordinal);
                element.XmlValue=(start,end,selfClosing,child?.Kind=="CDATA");
                element.Value=child?.Value??Decode(Text[start..(selfClosing?start:end)]);
                element.Children.Clear();
            }
        }
        foreach(var p in All.Where(n=>n.Group).ToList())foreach(var g in p.Children.GroupBy(n=>n.Name).Where(g=>g.Count()>1)){int i=0;foreach(var n in g)n.Name+=$"[{++i}]";}
    }
    private void Yaml()
    {
        var stream=new YamlStream();stream.Load(new StringReader(Text));var seen=new HashSet<YamlNode>(ReferenceEqualityComparer.Instance);
        void Visit(YamlNode src,ConfigNode? p,string name,YamlNode? key,int depth)
        {
            if(depth>100)throw new FormatException("YAML 최대 중첩 깊이는 100입니다.");
            if(!seen.Add(src)){var a=Add(p,name,"Alias",0,0,"→ &"+src.Anchor);a.ReadOnly=true;a.Note="별칭은 원문에서 수정하세요.";return;}
            var n=Add(p,name,src is YamlMappingNode?"Mapping":src is YamlSequenceNode?"Sequence":"String",(int)src.Start.Index,(int)src.End.Index);n.Tag=src;
            if(key is YamlScalarNode){n.KeyStart=(int)key.Start.Index;n.KeyEnd=(int)key.End.Index;}
            n.ReadOnly=(p?.ReadOnly??false)||!src.Anchor.IsEmpty||!src.Tag.IsEmpty||key!=null&&key is not YamlScalarNode;
            if(n.ReadOnly)n.Note="앵커·태그·복합 키는 원문에서 수정하세요.";
            if(src is YamlScalarNode s){n.Value=s.Value??"";if(s.Style==ScalarStyle.Plain)n.Kind=n.Value is "true" or "false"?"Boolean":n.Value is "null" or "~" or ""?"Null":Regex.IsMatch(n.Value,@"^-?(0|[1-9]\d*)(\.\d+)?([eE][+-]?\d+)?$")?"Number":"String";if(s.Style is ScalarStyle.Literal or ScalarStyle.Folded){n.ReadOnly=true;n.Note="블록 문자열은 원문에서 편집합니다.";}}
            else if(src is YamlMappingNode map)foreach(var kv in map.Children)Visit(kv.Value,n,kv.Key is YamlScalarNode k?k.Value??"":"[복합 키]",kv.Key,depth+1);
            else if(src is YamlSequenceNode seq)for(int i=0;i<seq.Children.Count;i++)Visit(seq.Children[i],n,$"[{i}]",null,depth+1);
        }
        for(int i=0;i<stream.Documents.Count;i++)Visit(stream.Documents[i].RootNode,null,$"문서 {i+1}",null,0);
        // Representation nodes do not reliably retain collection end marks; parser events do.
        var parser=new Parser(new StringReader(Text));var containers=new Stack<ConfigNode?>();
        while(parser.MoveNext())
        {
            if(parser.Current is MappingStart or SequenceStart)
            {
                string kind=parser.Current is MappingStart?"Mapping":"Sequence";
                containers.Push(All.FirstOrDefault(n=>n.Kind==kind&&n.Start==(int)parser.Current.Start.Index));
            }
            else if(parser.Current is MappingEnd or SequenceEnd)
            {
                if(containers.Count>0&&containers.Pop() is {} collection)
                {
                    collection.End=(int)parser.Current.End.Index;
                    if(IsFlow(collection)&&collection.End<Text.Length&&Text[collection.End]==(collection.Kind=="Mapping"?'}':']'))collection.End++;
                }
            }
        }

    }
    private void Ini()
    {
        ConfigNode? p=null;
        foreach(Match line in Regex.Matches(Text,@"[^\r\n]+"))
        {
            var s=line.Value.Trim();if(s.Length==0||s.StartsWith(';')||s.StartsWith('#'))continue;
            if(s.StartsWith('[')){var m=Regex.Match(line.Value,@"^\s*\[([^\]]+)\]\s*(?:[;#].*)?$");if(!m.Success)throw new FormatException("INI 섹션 오류");if(p!=null)p.End=line.Index;p=Add(null,"["+m.Groups[1].Value+"]","Section",line.Index,Text.Length);p.KeyStart=line.Index+m.Groups[1].Index;p.KeyEnd=p.KeyStart+m.Groups[1].Length;continue;}
            var k=Regex.Match(line.Value,@"^(\s*)([^=:#;][^=:]*?)(\s*[=:][ \t]*)(.*)$");if(!k.Success)throw new FormatException($"문자 {line.Index+1}: key=value 또는 key:value가 필요합니다.");
            p??=Add(null,"[전역]","Section",0,Text.Length);var v=k.Groups[4];var n=Add(p,k.Groups[2].Value.Trim(),"String",line.Index+v.Index,line.Index+v.Index+v.Length,v.Value);n.KeyStart=line.Index+k.Groups[2].Index;n.KeyEnd=n.KeyStart+k.Groups[2].Length;n.Note="전체 줄 주석만 해석하며 인라인 ;/#는 값에 포함됩니다.";
        }
    }
    private void Csv()
    {
        int pos=0;ConfigNode? row=null;
        while(pos<Text.Length)
        {
            row??=Add(null,$"행 {Roots.Count+1}","Row",pos,0);int start=pos;var value=new StringBuilder();
            if(Text[pos]=='"'){pos++;bool closed=false;while(pos<Text.Length){if(Text[pos]!='"'){value.Append(Text[pos++]);continue;}pos++;if(pos<Text.Length&&Text[pos]=='"'){value.Append('"');pos++;}else{closed=true;break;}}if(!closed||pos<Text.Length&&Text[pos]!=Separator&&Text[pos] is not ('\r' or '\n'))throw new FormatException("CSV 인용부호 또는 구분자 오류");}
            else while(pos<Text.Length&&Text[pos]!=Separator&&Text[pos] is not ('\r' or '\n')){if(Text[pos]=='"')throw new FormatException("CSV 인용부호 오류");value.Append(Text[pos++]);}
            Add(row,$"열 {row.Children.Count+1}","String",start,pos,value.ToString());row.End=pos;if(pos==Text.Length)break;
            if(Text[pos++]==Separator){if(pos==Text.Length){Add(row,$"열 {row.Children.Count+1}","String",pos,pos);row.End=pos;}}
            else{if(Text[pos-1]=='\r'&&pos<Text.Length&&Text[pos]=='\n')pos++;row.End=pos;row=null;}
        }
    }
    public string Patch(int start,int end,string replacement)=>Text[..start]+replacement+Text[end..];
    public string Checked(string text){_=new ConfigDocument(text,Format,Separator);return text;}
    public static string Scalar(string value,string kind)
    {
        if(kind=="String")return JsonSerializer.Serialize(value,new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
        if(kind=="Null")return "null";if(kind=="Boolean"&&value is "true" or "false")return value;
        if(kind=="Number"&&Regex.IsMatch(value,@"^-?(0|[1-9]\d*)(\.\d+)?([eE][+-]?\d+)?$"))return value;
        if(kind is "Object" or "Mapping")return "{}";if(kind is "Array" or "Sequence")return "[]";
        throw new FormatException(kind+" 형식으로 입력하세요.");
    }
    public string Change(ConfigNode n,string value,string? kind=null)
    {
        if(!n.Editable)throw new InvalidOperationException(n.Note.Length>0?n.Note:"값 행을 선택하세요.");
        if(Format=="XML" && n.Kind=="Comment")
        {
            if(value.Contains("--")||value.EndsWith('-'))throw new FormatException("XML 주석에는 --를 넣거나 마지막에 -를 사용할 수 없습니다.");
            return Checked(Patch(n.Start,n.End,"<!--"+value+"-->"));
        }
        if(Format=="XML" && n.XmlValue is { } leaf)
        {
            var target=kind??n.DisplayKind;
            if(target is not ("String" or "CDATA"))throw new FormatException("XML 값은 String 또는 CDATA로 저장합니다.");
            if(target=="CDATA" && value.Contains("]]>"))throw new FormatException("CDATA에 ]]>를 넣을 수 없습니다.");
            var content=target=="CDATA"?"<![CDATA["+value+"]]>":(System.Security.SecurityElement.Escape(value)??"").Replace("\r","&#13;");
            if(leaf.SelfClosing)content=">"+content+"</"+Text[n.KeyStart..n.KeyEnd]+">";
            return Checked(Patch(leaf.Start,leaf.End,content));
        }
        string raw=value;
        if(Format is "JSON" or "YAML")raw=Scalar(value,kind??n.Kind);
        else if(Format=="XML")
        {if(n.Kind=="CDATA"){if(value.Contains("]]>"))throw new FormatException("CDATA에 ]]>를 넣을 수 없습니다.");}else{raw=value.Replace("&","&amp;").Replace("<","&lt;").Replace("]]>","]]&gt;").Replace("\r","&#13;");if(n.Kind=="Attribute")raw=raw.Replace("\"","&quot;").Replace("'","&apos;").Replace("\n","&#10;").Replace("\t","&#9;");}}
        else if(Format=="CSV")raw=QuoteCsv(value);
        else if(value.IndexOfAny(['\r','\n'])>=0)throw new FormatException("INI 값은 한 줄이어야 합니다.");
        return Checked(Patch(n.Start,n.End,raw));
    }
    private string QuoteCsv(string v)=>v.IndexOfAny([Separator,'"','\r','\n'])>=0?"\""+v.Replace("\"","\"\"")+"\"":v;
    public string Rename(ConfigNode n,string name)
    {
        if(n.ReadOnly||n.KeyStart<0)throw new InvalidOperationException("이름은 원문에서 수정하세요.");
        if(string.IsNullOrWhiteSpace(name))throw new FormatException("이름을 입력하세요.");
        if((n.Parent?.Children??Roots).Any(c=>c!=n&&c.Name==name))throw new FormatException("중복 이름입니다.");
        if(Format is "JSON" or "YAML")return Checked(Patch(n.KeyStart,n.KeyEnd,Scalar(name,"String")));
        if(Format=="INI"){if(name.IndexOfAny(['[',']','=',':','\r','\n'])>=0)throw new FormatException("이름에 구분자를 사용할 수 없습니다.");return Checked(Patch(n.KeyStart,n.KeyEnd,name.Trim()));}
        if(Format=="XML")
        {
            if(Text[n.KeyStart..n.KeyEnd].Contains(':'))throw new InvalidOperationException("네임스페이스가 있는 이름은 원문에서 변경하세요.");
            XmlConvert.VerifyNCName(name);string result=Text;
            if(n.Kind=="Element"&&!Text[(n.End-2)..n.End].Equals("/>")){int p=Text.LastIndexOf("</",n.End-1,StringComparison.Ordinal),e=Text.IndexOfAny([' ','\t','\r','\n','>'],p+2);result=result[..(p+2)]+name+result[e..];}
            return Checked(result[..n.KeyStart]+name+result[n.KeyEnd..]);
        }
        throw new InvalidOperationException("CSV 헤더 셀을 수정하세요.");
    }
    public string Delete(ConfigNode n)
    {
        if(n.ReadOnly)throw new InvalidOperationException("원문에서 삭제하세요.");
        if(Format=="JSON"){if(n.Parent==null)throw new InvalidOperationException("루트는 삭제할 수 없습니다.");var ss=n.Parent.Children;int i=ss.IndexOf(n),s=n.KeyStart>=0?n.KeyStart:n.Start,e=n.End;if(i<ss.Count-1)e=Text.IndexOf(',',e)+1;else if(i>0)s=Text.LastIndexOf(',',s-1);return Checked(Patch(s,e,""));}
        if(Format=="INI")return Checked(Patch(LineStart(n.KeyStart>=0?n.KeyStart:n.Start),n.Group?n.End:LineEnd(n.End),""));
        if(Format=="XML"){if(n.Parent==null&&n.Kind=="Element")throw new InvalidOperationException("루트 요소는 삭제할 수 없습니다.");if(n.Kind=="Attribute"&&n.Tag is ValueTuple<int,int> a)return Checked(Patch(a.Item1,a.Item2,""));return Checked(Patch(n.Kind=="CDATA"?n.Start-9:n.Start,n.Kind=="CDATA"?n.End+3:n.End,""));}
        if(Format=="CSV")return Checked(Patch((n.Parent??n).Start,(n.Parent??n).End,""));
        if(Format=="YAML"){if(n.Parent==null||n.KeyStart<0||IsFlow(n.Parent)||IsFlow(n)||Walk([n]).Any(x=>x.ReadOnly))throw new InvalidOperationException("이 YAML 구조는 원문에서 삭제하세요.");return Checked(Patch(LineStart(n.KeyStart),BlockEnd(n),""));}
        throw new InvalidOperationException();
    }
    private bool IsFlow(ConfigNode n)=>n.Tag is YamlMappingNode m&&m.Style==MappingStyle.Flow||n.Tag is YamlSequenceNode s&&s.Style==SequenceStyle.Flow;
    public string Insert(ConfigNode p,string name,string value,string kind)
    {
        if((!p.Group&&!(Format=="XML"&&p.Kind=="Element"))||p.ReadOnly)throw new InvalidOperationException("추가할 부모 그룹을 선택하세요.");var nl=Text.Contains("\r\n")?"\r\n":"\n";
        if(Format=="JSON"){if(p.Kind=="Object"&&p.Children.Any(c=>c.Name==name))throw new FormatException("중복 키입니다.");return Checked(Patch(p.End-1,p.End-1,(p.Children.Count>0?",":"")+nl+new string(' ',(p.Depth+1)*2)+(p.Kind=="Object"?Scalar(name,"String")+": ":"")+Scalar(value,kind)+nl));}
        if(Format=="INI"){if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(['[',']','=',':','\r','\n'])>=0||value.IndexOfAny(['\r','\n'])>=0)throw new FormatException("한 줄 키·값을 입력하세요.");if(p.Children.Any(c=>c.Name==name))throw new FormatException("중복 키입니다.");return Checked(Patch(p.End,p.End,(p.End>0&&Text[p.End-1]!='\n'?nl:"")+name+"="+value+nl));}
        if(Format=="XML")
        {
            if(kind is not ("Text" or "Comment"))XmlConvert.VerifyNCName(name);string escaped=System.Security.SecurityElement.Escape(value)??"";
            if(kind=="Attribute"){int at=FindTagEnd(p.Start);if(Text[at-1]=='/')at--;return Checked(Patch(at,at," "+name+"=\""+escaped+"\""));}
            if(kind=="Comment"&&(value.Contains("--")||value.EndsWith('-')))throw new FormatException("잘못된 XML 주석");
            string content=kind=="Comment"?"<!--"+value+"-->":kind=="Text"?escaped:"<"+name+">"+escaped+"</"+name+">";
            if(Text[(p.End-2)..p.End]=="/>")return Checked(Patch(p.End-2,p.End,">"+content+"</"+Text[p.KeyStart..p.KeyEnd]+">"));
            int end=Text.LastIndexOf("</",p.End-1,StringComparison.Ordinal);return Checked(Patch(end,end,content));
        }
        if(Format=="CSV"){int count=Roots.Select(n=>n.Children.Count).DefaultIfEmpty(1).Max();return Checked(Text+(Text.Length>0&&Text[^1]!='\n'?nl:"")+string.Join(Separator,Enumerable.Repeat("",count))+nl);}
        if(Format=="YAML")
        {
            if(IsFlow(p)||p.Children.Count==0||p.Children.Any(n=>n.ReadOnly))throw new InvalidOperationException("빈 그룹·흐름 스타일·참조가 있는 그룹의 추가는 원문에서 수행하세요.");if(p.Kind=="Mapping"&&p.Children.Any(c=>c.Name==name))throw new FormatException("중복 키입니다.");
            var first=p.Children[0];int anchor=first.KeyStart>=0?first.KeyStart:first.Start;int indent=anchor-LineStart(anchor);if(p.Kind=="Sequence")indent=Math.Max(0,indent-2);int pos=BlockEnd(p.Children[^1]);return Checked(Patch(pos,pos,(pos>0&&Text[pos-1]!='\n'?nl:"")+new string(' ',indent)+(p.Kind=="Mapping"?Scalar(name,"String")+": ":"- ")+Scalar(value,kind)+nl));
        }
        throw new InvalidOperationException();
    }
    private int FindTagEnd(int s){char quote='\0';for(int i=s;i<Text.Length;i++){char c=Text[i];if(quote!='\0'){if(c==quote)quote='\0';}else if(c is '\'' or '"')quote=c;else if(c=='>')return i;}throw new FormatException("XML 태그 오류");}
    public string AddSection(string name)
    {
        if(Format!="INI"||string.IsNullOrWhiteSpace(name)||name.IndexOfAny(['[',']','\r','\n'])>=0)throw new FormatException("올바른 섹션 이름을 입력하세요.");
        if(Roots.Any(n=>n.Name=="["+name+"]"))throw new FormatException("중복 섹션입니다.");var nl=Text.Contains("\r\n")?"\r\n":"\n";return Checked(Text+nl+"["+name+"]"+nl);
    }
    public string CsvColumn(int index,bool delete,string header,bool hasHeader)
    {
        if(Format!="CSV")throw new InvalidOperationException();string result=Text;
        foreach(var row in Roots.AsEnumerable().Reverse())
        {
            if(delete)
            {
                if(index>=row.Children.Count)continue;if(row.Children.Count==1)throw new InvalidOperationException("마지막 열은 삭제할 수 없습니다.");
                var n=row.Children[index];int start=n.Start,end=n.End;if(index<row.Children.Count-1)end++;else start--;result=result[..start]+result[end..];
            }
            else{int pos=row.Children[^1].End;result=result[..pos]+Separator+(hasHeader&&row==Roots[0]?QuoteCsv(header):"")+result[pos..];}
        }
        return Checked(result);
    }
    public int LineStart(int p)=>p<=0?0:Text.LastIndexOf('\n',Math.Min(p-1,Text.Length-1))+1;
    private int LineEnd(int p){int i=Text.IndexOf('\n',Math.Min(p,Text.Length));return i<0?Text.Length:i+1;}
    private int BlockEnd(ConfigNode n)=>n.Group&&n.End>0&&string.IsNullOrWhiteSpace(Text[LineStart(n.End)..n.End])?LineStart(n.End):LineEnd(n.End);
}
