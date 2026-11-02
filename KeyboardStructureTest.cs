namespace InerConfig;

internal static class KeyboardStructureTest
{
    internal static int Count;
    internal static void Run()
    {
        static ConfigNode Node(ConfigDocument d,string name)=>d.All.First(n=>n.Name==name);
        static void Check(bool ok,string message){Count++;if(!ok)throw new Exception("Keyboard structure: "+message);}
        static void Reject(Action action){try{action();}catch(InvalidOperationException){Count++;return;}throw new Exception("Expected rejected keyboard move");}
        var json=new ConfigDocument("{\"group\":{\"keep\":1},\"move\":{\"child\":[1,2]},\"last\":3}","JSON");
        var moved=json.ChangeLevel(Node(json,"move"),false);
        var parsed=new ConfigDocument(moved.Text,"JSON");
        Check(Node(parsed,"move").Parent?.Name=="group"&&Node(parsed,"child").Children.Select(n=>n.Value).SequenceEqual(new[]{"1","2"}),"JSON subtree indent");
        var restored=parsed.ChangeLevel(Node(parsed,"move"),true);
        parsed=new ConfigDocument(restored.Text,"JSON");
        Check(parsed.Roots[0].Children.Select(n=>n.Name).SequenceEqual(new[]{"group","move","last"}),"outdent inserted after parent");
        Check(Node(parsed,"child").Children.Count==2&&Node(parsed,"last").Value=="3","subtree values preserved");
        var add=json.InsertSibling(Node(json,"group"),"newKey","","String");
        parsed=new ConfigDocument(add.Text,"JSON");
        Check(parsed.Roots[0].Children[1].Name=="newKey"&&parsed.All.Any(n=>n.Path==add.Path),"sibling inserted immediately after anchor");
        var array=new ConfigDocument("[{\"keep\":1},2,3]","JSON");
        moved=array.ChangeLevel(array.Roots[0].Children[1],false);
        parsed=new ConfigDocument(moved.Text,"JSON");
        Check(Node(parsed,"item").Value=="2"&&parsed.All.Any(n=>n.Path==moved.Path),"array to object generates key");
        restored=parsed.ChangeLevel(Node(parsed,"item"),true);parsed=new ConfigDocument(restored.Text,"JSON");
        Check(parsed.Roots[0].Children[1].Value=="2"&&parsed.Roots[0].Children[2].Value=="3","object to array outdent");
        var duplicate=new ConfigDocument("{\"g\":{\"x\":1},\"x\":2}","JSON");
        Reject(()=>duplicate.ChangeLevel(duplicate.Roots[0].Children[1],false));
        Reject(()=>json.ChangeLevel(json.Roots[0].Children[0],false));
        Reject(()=>json.ChangeLevel(json.Roots[0].Children[0],true));
        var scalars=new ConfigDocument("{\"a\":1,\"b\":2}","JSON");
        Reject(()=>scalars.ChangeLevel(Node(scalars,"b"),false));
        var xml=new ConfigDocument("<root><group><keep>1</keep></group><move a=\"x\"><child>2</child></move><last/></root>","XML");
        moved=xml.ChangeLevel(Node(xml,"move"),false);parsed=new ConfigDocument(moved.Text,"XML");
        Check(Node(parsed,"move").Parent?.Name=="group"&&Node(parsed,"@a").Value=="x","XML subtree/attribute preserved");
        restored=parsed.ChangeLevel(Node(parsed,"move"),true);parsed=new ConfigDocument(restored.Text,"XML");
        Check(parsed.Roots[0].Children.Select(n=>n.Name).SequenceEqual(new[]{"group","move","last"}),"XML outdent");
        Reject(()=>parsed.ChangeLevel(Node(parsed,"@a"),true));
        var ini=new ConfigDocument("[s]\na=1\nb=2\n","INI");
        add=ini.InsertSibling(Node(ini,"a"),"newKey","","String");parsed=new ConfigDocument(add.Text,"INI");
        Check(parsed.Roots[0].Children.Select(n=>n.Name).SequenceEqual(new[]{"a","newKey","b"}),"INI sibling add");
        Reject(()=>ini.ChangeLevel(Node(ini,"a"),true));
        var yaml=new ConfigDocument("group:\n  keep: 1\nmove:\n  child: 2\nlast: 3\n","YAML");
        moved=yaml.ChangeLevel(Node(yaml,"move"),false);parsed=new ConfigDocument(moved.Text,"YAML");
        Check(Node(parsed,"move").Parent?.Name=="group"&&Node(parsed,"child").Value=="2","YAML block indent");
        restored=parsed.ChangeLevel(Node(parsed,"move"),true);parsed=new ConfigDocument(restored.Text,"YAML");
        Check(parsed.Roots[0].Children.Select(n=>n.Name).SequenceEqual(new[]{"group","move","last"}),"YAML outdent");
        add=yaml.InsertSibling(Node(yaml,"group"),"newKey","","String");parsed=new ConfigDocument(add.Text,"YAML");
        Check(parsed.Roots[0].Children[1].Name=="newKey","YAML sibling insertion");
        var sequences=new ConfigDocument("- - 1\n- - 2\n- - 3\n","YAML");
        moved=sequences.ChangeLevel(sequences.Roots[0].Children[1],false);parsed=new ConfigDocument(moved.Text,"YAML");
        Check(parsed.Roots[0].Children.Count==2&&parsed.Roots[0].Children[0].Children.Count==2,"YAML sequence indent");
        var session=new FileSession("JSON",json.Text);session.BeginStructure();session.Change(json.ChangeLevel(Node(json,"move"),false).Text);
        session.Undo();Check(session.Text==json.Text,"structure undo restores hierarchy");session.Redo();Check(session.Text!=json.Text,"structure redo restores hierarchy");
    }
}
