using System.IO;
using System.Text;
using System.Text.Json;

namespace InerConfig;

internal sealed class WorkspaceStore
{
    internal sealed record WindowState(int Version,Guid Id,int Selected,List<FileSession.Snapshot> Tabs);
    private readonly string root;
    public WorkspaceStore(string root) { this.root=root;Directory.CreateDirectory(root); }
    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"InerConfig","Workspace",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes((Environment.ProcessPath??"").ToUpperInvariant())))[..12]);
    private static void Write(string path,string text)
    {
        var temp=path+".tmp";
        try
        {
            using(var file=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) { var bytes=Encoding.UTF8.GetBytes(text);file.Write(bytes);file.Flush(true); }
            if(File.Exists(path))File.Replace(temp,path,path+".bak",true);else File.Move(temp,path);
        }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
    public void SaveWindow(WindowState state)
    {
        var path=Path.Combine(root,state.Id+".json");
        if(state.Tabs.Count==0)
        {
            if(File.Exists(path+".bak"))File.Delete(path+".bak");
            if(File.Exists(path))File.Delete(path);
            return;
        }
        var text=JsonSerializer.Serialize(state);
        if(File.Exists(path)&&File.ReadAllText(path)==text)return;
        Write(path,text);
    }
    public List<WindowState> LoadWindows(List<string> warnings)
    {
        var result=new List<WindowState>();
        foreach(var path in Directory.EnumerateFiles(root,"*.json").Where(p=>Guid.TryParse(Path.GetFileNameWithoutExtension(p),out _)))
        {
            WindowState? state=null;
            foreach(var candidate in new[]{path,path+".bak"})
            {
                try { state=JsonSerializer.Deserialize<WindowState>(File.ReadAllText(candidate));if(state?.Version is not (1 or 2))throw new IOException("지원하지 않는 복원 데이터 버전");break; }
                catch(Exception ex) { warnings.Add(Path.GetFileName(candidate)+": "+ex.Message);state=null; }
            }
            if(state!=null)result.Add(state);
        }
        return result;
    }
    public List<string> Recent()
    {
        var path=Path.Combine(root,"recent.json");
        if(!File.Exists(path))return [];
        return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path))??[];
    }
    public void Remember(string path)
    {
        var recent=Recent();var full=Path.GetFullPath(path);
        recent.RemoveAll(p=>string.Equals(p,full,StringComparison.OrdinalIgnoreCase));recent.Insert(0,full);
        Write(Path.Combine(root,"recent.json"),JsonSerializer.Serialize(recent.Take(20)));
    }
    public void ClearRecent()=>Write(Path.Combine(root,"recent.json"),"[]");
}
