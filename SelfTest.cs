using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace InerConfig;
public static class SelfTest
{
    public static int Count;
    private static void Check(bool condition,string name){Count++;if(!condition)throw new Exception("FAIL: "+name);}
    private static void Reject(Action action,string name){bool failed=false;try{action();}catch{failed=true;}Check(failed,name);}
    public static void Run(string[]? packages = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        FileSession.ValidateFileSize(FileSession.MaxFileBytes);
        Reject(()=>FileSession.ValidateFileSize(10L*1024*1024*1024),"10 GiB rejected before allocation");
        var oversized=Path.Combine(Path.GetTempPath(),"InerConfig-size-"+Guid.NewGuid()+".json");
        try
        {
            using(var file=File.Create(oversized))file.SetLength(FileSession.MaxFileBytes+1);
            try{FileSession.Load(oversized);throw new Exception("Oversized file accepted");}
            catch(IOException ex){Check(ex.Message.Contains("50 MiB"),"oversized load gives size guidance");}
        }
        finally{File.Delete(oversized);}
        var mode=new FileSession("JSON","{}");mode.Change("{\"x\":1}");
        var prior=mode.Text;mode.BeginStructure();mode.Change("{\"x\":2}");mode.Undo();Check(mode.Text==prior,"structure local undo");mode.Redo();mode.CancelStructure();Check(mode.Text==prior,"structure cancel preserves previous dirty");
        mode.BeginStructure();mode.Change("{\"x\":3}");mode.CompleteStructure();mode.Undo();Check(mode.Text==prior,"structure completion single undo");
        mode.BeginStructure();mode.Change("{\"x\":4}");var recoveredMode=FileSession.Restore(mode.Capture());recoveredMode.CancelStructure();Check(recoveredMode.Text==prior,"structure baseline recovery");
        mode.CancelStructure();mode.BeginStructure();mode.PendingDeletes.Add("$/x");var deleted=FileSession.Restore(mode.Capture());Check(deleted.WithPendingDeletes()=="{}","pending delete recovery");deleted.CancelStructure();Check(deleted.Text==prior,"pending delete cancel");
        mode.Draft=new AddDraft{ParentPath="$",Kind="String",Example="example"};var draftRecovery=FileSession.Restore(mode.Capture());Check(draftRecovery.Draft?.Entered==false&&draftRecovery.Draft.Example=="example","placeholder distinct recovery");Reject(()=>draftRecovery.CompleteStructure(),"unfinished draft prevents completion");mode.CancelStructure();
        mode.BeginStructure();mode.ToggleDeletion("$/x");mode.Change("{\"x\":9}");mode.Undo();Check(mode.Text==prior&&mode.PendingDeletes.Contains("$/x"),"structure undo value before prior deletion");mode.Undo();Check(mode.PendingDeletes.Count==0,"structure undo deletion");mode.Redo();Check(mode.PendingDeletes.Contains("$/x"),"structure redo deletion");mode.CancelStructure();
        var csvEdit=new ConfigDocument("a,b\r\n1,2\r\n3\r\n","CSV");
        var changedCsv=csvEdit.ChangeCsvCells([(1,0,"001"),(1,1,"line\nvalue")]);
        var parsedCsv=new ConfigDocument(changedCsv,"CSV");Check(parsedCsv.Roots[1].Children[0].Value=="001"&&parsedCsv.Roots[1].Children[1].Value=="line\nvalue","CSV atomic multi cell and quoting");
        Reject(()=>csvEdit.ChangeCsvCells([(1,0,"x"),(2,1,"bad")]),"CSV missing cell blocks whole edit");
        var expandedCsv=new ConfigDocument(csvEdit.PasteCsvBlock(2,1,[["tail"]],true),"CSV");
        Check(expandedCsv.Roots[2].Children.Count==2&&expandedCsv.Roots[2].Children[1].Value=="tail"&&expandedCsv.Roots[1].Children[0].Value=="1","CSV short row explicit expansion");
        var missingHeaderCsv=new ConfigDocument("a\r\nx\r\n","CSV");
        Reject(()=>missingHeaderCsv.PasteCsvBlock(1,1,[["v"]],true,true),"CSV header expansion requires names");
        Check(new ConfigDocument(missingHeaderCsv.PasteCsvBlock(1,1,[["v"]],true,true,["new"]),"CSV").Roots[0].Children[1].Value=="new","CSV header expansion explicit");
        var rawList=new ConfigDocument("[]","JSON");Check(new ConfigDocument(rawList.InsertRaw(rawList.Roots[0],"","{\"id\":1}"),"JSON").Roots[0].Children.Count==1,"JSON raw subtree insertion");
        var yamlList=new ConfigDocument("[]","YAML");Check(new ConfigDocument(yamlList.InsertRaw(yamlList.Roots[0],"","{\"id\":1}"),"YAML").Roots[0].Children.Count==1,"YAML empty flow list insertion");
        var moving=new ConfigDocument("{\"a\":1, \"b\":2}","JSON");Check(new ConfigDocument(moving.MoveBefore(moving.Roots[0].Children[1],moving.Roots[0].Children[0]),"JSON").Roots[0].Children[0].Name=="b","JSON sibling reorder");

        Check(SyntaxErrorDialog.Locate(new System.Xml.XmlException("bad",null,2,3),"first\r\nabcdef")==9,"XML error line column mapping");
        Check(SyntaxErrorDialog.Locate(new System.Text.Json.JsonException("bad",null,0,Encoding.UTF8.GetByteCount("{\"한글\":1,")),"{\"한글\":1,!}")==8,"JSON UTF8 error offset mapping");
        Check(SyntaxErrorDialog.Locate(new FormatException("bad"),"text")==-1,"Missing error position explicit");
        Count += UpdateTests.Run(packages ?? []);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"self-test-updater.ps1"),UpdateService.RestartCommand(@"C:\a'b\update.msi",@"C:\a'b\InerConfig.exe",@"C:\a'b\result.txt",12345,new Version(0,1,2)));
        var json="{\r\n  \"한글😀\": \"old\", \"num\": 9007199254740993, \"empty\": {}, \"list\": [1,2]\r\n}";
        var j=new ConfigDocument(json,"JSON");var leaf=j.All.First(n=>n.Name=="한글😀");
        Check(j.Change(leaf,"새 값")==json.Replace("\"old\"","\"새 값\""),"JSON exact source patch Unicode");
        Check(j.All.Any(n=>n.Name=="empty"&&n.Group),"empty group visible");
        Check(j.Change(j.All.First(n=>n.Name=="num"),"9007199254740994").Contains("9007199254740994"),"large number exact");
        Check(new ConfigDocument(j.Rename(leaf,"새이름"),"JSON").All.Any(n=>n.Name=="새이름"),"JSON rename");
        Check(new ConfigDocument(j.Insert(j.Roots[0],"added","true","Boolean"),"JSON").All.Any(n=>n.Name=="added"),"JSON insert");
        Check(!new ConfigDocument(j.Delete(leaf),"JSON").All.Any(n=>n.Name=="한글😀"),"JSON delete first");
        var arr=j.All.First(n=>n.Name=="list");Check(new ConfigDocument(j.Delete(arr.Children[1]),"JSON").All.First(n=>n.Name=="list").Children.Count==1,"array delete last");
        Reject(()=>new ConfigDocument("{\"a\":1,\"a\":2}","JSON"),"duplicate JSON");Reject(()=>j.Change(j.All.First(n=>n.Name=="num"),"oops"),"type reject");
        var xml="<?xml version='1.0' encoding='utf-8'?>\r\n<!--keep--><root a='hello &amp; bye'><x/>text<b>bold</b><![CDATA[test]]></root>";
        var x=new ConfigDocument(xml,"XML");Check(x.Change(x.All.First(n=>n.Name=="@a"),"a&\"b")==xml.Replace("hello &amp; bye","a&amp;&quot;b"),"XML attribute exact preserve quotes/comment");
        Check(x.Change(x.All.First(n=>n.Kind=="CDATA"),"new").Contains("<![CDATA[new]]>"),"CDATA preserve");
        Check(new ConfigDocument("<x a='one\r\ntwo&#10;three'/>","XML").All.First(n=>n.Name=="@a").Value=="one two\nthree","XML logical attribute normalization");
        Check(new ConfigDocument(x.Insert(x.All.First(n=>n.Name=="x"),"child","v","Element"),"XML").All.Any(n=>n.Name=="child"),"XML empty insertion");
        Check(new ConfigDocument(x.Rename(x.All.First(n=>n.Name=="b"),"strong"),"XML").All.Any(n=>n.Name=="strong"),"XML paired rename");
        Reject(()=>new ConfigDocument("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///test'>]><x>&e;</x>","XML"),"DTD blocked");
        var commentDoc=new ConfigDocument("<root><!-- first & <note> -->\r\n<x>v</x><!--last--></root>","XML");
        var commentNode=commentDoc.All.First(n=>n.Kind=="Comment");
        Check(commentNode.Editable,"XML comments editable");
        Check(commentDoc.Change(commentNode," edited & <text>\nline ")=="<root><!-- edited & <text>\nline -->\r\n<x>v</x><!--last--></root>","XML comment body edit preserves surrounding text");
        Reject(()=>commentDoc.Change(commentNode,"invalid--comment"),"XML comment double hyphen rejected");
        Reject(()=>commentDoc.Change(commentNode,"invalid-"),"XML comment trailing hyphen rejected");
        var leafXml=new ConfigDocument("<root><port>08080</port><empty/><c><![CDATA[a&b]]></c><mixed>Hello <b>world</b>!</mixed></root>","XML");
        var port=leafXml.All.First(n=>n.Name=="port");
        Check(!port.Group&&port.Editable&&port.DisplayKind=="String"&&port.Value=="08080"&&port.Children.Count==0,"XML scalar flattened without type inference");
        Check(leafXml.Change(port,"a&b").Contains("<port>a&amp;b</port>"),"XML scalar tag preservation and escaping");
        Check(leafXml.Change(port,"a&b","CDATA").Contains("<port><![CDATA[a&b]]></port>"),"XML text to CDATA");
        var cdataLeaf=leafXml.All.First(n=>n.Name=="c");
        Check(leafXml.Change(cdataLeaf,"a&b","String").Contains("<c>a&amp;b</c>"),"XML CDATA to text");
        Reject(()=>leafXml.Change(cdataLeaf,"]]>","CDATA"),"XML invalid CDATA rejected");
        Check(leafXml.Change(leafXml.All.First(n=>n.Name=="empty"),"v").Contains("<empty>v</empty>"),"XML empty leaf expansion");
        Check(leafXml.Delete(port).Contains("<root><empty/>"),"XML scalar deletes full element");
        Check(leafXml.All.First(n=>n.Name=="mixed").Children.Count==3,"XML mixed content order retained");
        var ini="; keep\r\n[section]\r\n key = old\r\nempty=\r\n";var i=new ConfigDocument(ini,"INI");
        Check(i.Change(i.All.First(n=>n.Name=="key"),"new")==ini.Replace("old","new"),"INI whitespace preserve");
        Check(i.All.Any(n=>n.Name=="empty"&&n.Value==""),"INI empty value");Check(new ConfigDocument(i.Insert(i.Roots[0],"newKey","001","String"),"INI").All.Any(n=>n.Value=="001"),"INI insert");
        var yaml="# keep\r\nserver:\r\n  host: localhost # comment\r\n  port: 8080\r\n";var y=new ConfigDocument(yaml,"YAML");
        Check(y.Change(y.All.First(n=>n.Name=="host"),"new")==yaml.Replace("localhost","\"new\""),"YAML exact patch comment");
        Check(new ConfigDocument(y.Insert(y.All.First(n=>n.Name=="server"),"enabled","true","Boolean"),"YAML").All.Any(n=>n.Name=="enabled"),"YAML insertion");
        var refs=new ConfigDocument("defaults: &base\n  a: 1\ncopy: *base\n","YAML");Check(refs.All.Any(n=>n.Kind=="Alias"&&n.ReadOnly),"YAML alias visible");
        var csv="name,value\r\na,\"b,b\"\r\nc,\"line\nline\"\r\nd,\r\n";var c=new ConfigDocument(csv,"CSV");
        Check(c.Roots.Count==4&&c.Roots[3].Children.Count==2,"CSV empty final cell");
        Check(c.Change(c.Roots[1].Children[1],"quote\"x")==csv.Replace("\"b,b\"","\"quote\"\"x\""),"CSV quoted patch");
        Check(new ConfigDocument("a;b\n1;2","CSV",';').Roots[1].Children[1].Value=="2","CSV delimiter");
        Check(new ConfigDocument("a,","CSV").Roots[0].Children.Count==2,"CSV EOF empty");
        Check(new ConfigDocument("\"a\n\"\"b\",c","CSV").Roots[0].Children[0].Value=="a\n\"b","CSV multiline quote");
        Check(new ConfigDocument(c.CsvColumn(0,false,"extra",true),"CSV").Roots.All(r=>r.Children.Count==3),"CSV add column");
        Check(new ConfigDocument(c.CsvColumn(1,true,"",true),"CSV").Roots.All(r=>r.Children.Count==1),"CSV delete column");
        Check(new ConfigDocument(i.AddSection("new"),"INI").Roots.Count==2,"INI add section");
        Check(new ConfigDocument(y.Delete(y.All.First(n=>n.Name=="host")),"YAML").All.All(n=>n.Name!="host"),"YAML delete leaf");
        var escapedKey=new ConfigDocument("{\"a\\\"b\":1,\"z\":2}","JSON");Check(new ConfigDocument(escapedKey.Rename(escapedKey.Roots[0].Children[0],"key"),"JSON").All.Any(n=>n.Name=="key"),"escaped key rename");
        Reject(()=>refs.Change(refs.All.First(n=>n.Name=="a"),"2"),"anchor descendant protected");
        Check(new ConfigDocument(x.Insert(x.All.First(n=>n.Name=="x"),"","a&b","Text"),"XML").All.Any(n=>n.Value=="a&b"),"XML add text");
        string cipher=Crypto.Transform("비밀😀\nabc","test-password",false);Check(Crypto.Transform(cipher,"test-password",true)=="비밀😀\nabc","AES roundtrip");
        Check(cipher!=Crypto.Transform("비밀😀\nabc","test-password",false),"random salt nonce");Reject(()=>Crypto.Transform(cipher,"wrong",true),"wrong key");
        var bytes=Convert.FromBase64String(cipher[4..]);bytes[^1]^=1;Reject(()=>Crypto.Transform("CE1:"+Convert.ToBase64String(bytes),"test-password",true),"tamper");
        Check(Crypto.Transform(Crypto.Transform("","p",false),"p",true)=="","empty plaintext");
        Check(CryptoWindow.Hash("abc","Hex")=="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad","SHA known vector");
        var folder=Path.Combine(Path.GetTempPath(),"InerConfig-Test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            string backupTarget=Path.Combine(folder,"backup-owned.txt");
            File.WriteAllText(backupTarget,"original");File.WriteAllText(backupTarget+".bak","unrelated");
            string replacement=Path.Combine(folder,"replacement.tmp");File.WriteAllText(replacement,"new");
            SessionBackups.Replace(replacement,backupTarget);
            var generated=Directory.GetFiles(folder,"backup-owned.txt.*.bak").Single();
            Check((File.GetAttributes(generated)&FileAttributes.Hidden)!=0,"session backup hidden");
            Check(File.ReadAllText(generated)=="original","session backup original bytes");
            SessionBackups.Cleanup();
            Check(!File.Exists(generated)&&File.ReadAllText(backupTarget+".bak")=="unrelated","cleanup preserves preexisting backup");
            File.WriteAllText(replacement,"newer");SessionBackups.Replace(replacement,backupTarget);SessionBackups.Preserve(backupTarget);
            generated=Directory.GetFiles(folder,"backup-owned.txt.*.bak").Single();SessionBackups.Cleanup();
            Check(File.Exists(generated),"failed save backup retained");
            string file=Path.Combine(folder,"sample.ini");File.WriteAllText(file,ini,new UTF8Encoding(true));var original=File.ReadAllBytes(file);var s=FileSession.Load(file);s.Save(file);Check(File.ReadAllBytes(file).SequenceEqual(original)&&!File.Exists(file+".bak"),"unchanged save exact");
            s.Change(ini.Replace("old","new"));s.Save(file);Check(File.ReadAllBytes(file+".bak").SequenceEqual(original),"backup exact");
            File.AppendAllText(file,";external");s.Change(s.Text+"\r\n");Reject(()=>s.Save(file),"external change conflict");
            var cp=Encoding.GetEncoding(949,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);string korean=Path.Combine(folder,"korean.ini");File.WriteAllText(korean,"[설정]\n키=값",cp);var ks=FileSession.Load(korean,cp);ks.Change(ks.Text.Replace("값","새값"));ks.Save(korean);Check(File.ReadAllText(korean,cp).Contains("새값"),"CP949 roundtrip");
            ks.SetEncoding("UTF-8 BOM");Check(ks.Dirty,"encoding dirty");ks.Save(korean);Check(File.ReadAllBytes(korean).AsSpan().StartsWith(new byte[]{0xef,0xbb,0xbf}),"encoding conversion BOM");
            var store=new WorkspaceStore(folder);
            var dirty=FileSession.Load(korean);dirty.Change("{invalid recovery text");dirty.SourceMode=true;dirty.SearchText="needle";dirty.SelectedPath="$/key";dirty.Collapsed.Add("$");dirty.SetEncoding("UTF-16 LE");
            var state=new WorkspaceStore.WindowState(1,Guid.NewGuid(),0,[dirty.Capture(),new FileSession("JSON","{unfinished").Capture()]);
            store.SaveWindow(state);var warnings=new List<string>();var recovered=store.LoadWindows(warnings).Single();
            var restored=FileSession.Restore(recovered.Tabs[0]);
            Check(restored.Text==dirty.Text&&restored.Dirty&&restored.FilePath==dirty.FilePath,"Recovery preserves unsaved raw text and path");
            Check(restored.Encoding.CodePage==1200&&restored.Bom.SequenceEqual(dirty.Bom)&&restored.SourceMode&&restored.SearchText=="needle"&&restored.Collapsed.Contains("$"),"Recovery encoding and editor state");
            Check(FileSession.Restore(recovered.Tabs[1]).FilePath==null&&FileSession.Restore(recovered.Tabs[1]).Text=="{unfinished","Untitled invalid document recovery");
            var conflict=FileSession.Load(korean);conflict.Change("[설정]\n키=미저장");
            var conflictCopy=conflict.Capture();File.AppendAllText(korean,"\n;external",Encoding.UTF8);
            Reject(()=>FileSession.Restore(conflictCopy).Save(korean),"Recovery retains external-change protection");
            var clean=FileSession.Load(korean).Capture();File.AppendAllText(korean,"\n;new",Encoding.UTF8);
            Check(FileSession.Restore(clean).Text.EndsWith(";new"),"Clean recovery reads latest disk contents");
            store.SaveWindow(state with { Selected=1 });
            File.WriteAllText(Path.Combine(folder,state.Id+".json"),"{broken");
            Check(store.LoadWindows(warnings).Single().Selected==0&&warnings.Count>0,"Corrupt recovery uses previous atomic backup");
            store.SaveWindow(state with { Tabs=[] });
            Check(store.LoadWindows([]).Count==0,"Closed tabs remove recovery and backup");
            for(int recentIndex=0;recentIndex<22;recentIndex++)store.Remember(Path.Combine(folder,recentIndex+".ini"));
            store.Remember(Path.Combine(folder,"5.ini"));
            Check(store.Recent().Count==20&&store.Recent()[0].EndsWith("5.ini")&&store.Recent().Distinct(StringComparer.OrdinalIgnoreCase).Count()==20,"Recent files ordered unique capped");
            store.ClearRecent();Check(store.Recent().Count==0,"Clear recent files");
            var xmlSession=new FileSession("XML","<?xml version='1.0' encoding='utf-8'?><root/>");xmlSession.SetEncoding("UTF-16 LE");Reject(()=>xmlSession.Save(Path.Combine(folder,"wrong.xml")),"XML encoding mismatch");
        }
        finally{foreach(var file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);}
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"self-test-result.txt"),$"PASS: {Count} checks");
    }
}
