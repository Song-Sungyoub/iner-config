using System.IO;
using System.Text;
using System.Text.Json;

namespace InerConfig.Models;

public sealed record ScriptDefinition(int SchemaVersion, string Id, string Name, string Description, string Code, int Revision, DateTime CreatedAt, DateTime UpdatedAt)
{
    public string Language {get;init;}="python";
    public bool IsExample => Id.StartsWith("example-");
    public string Label => IsExample ? "기본 예제 · 사본으로 저장" : UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

public sealed class ScriptLibrary(string root)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented=true, PropertyNamingPolicy=JsonNamingPolicy.CamelCase };
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"InerConfig","Scripts");
    public static readonly ScriptDefinition[] Examples = [
        new(1,"example-ip","IP 각 자리 −44","각 숫자에서 44를 빼서 주소 해석","parts = value.split(\".\")\nresult = \".\".join(str(int(part) - 44) for part in parts)",0,default,default),
        new(1,"example-trim","앞뒤 공백 제거","문자열 앞뒤 공백 정리","result = value.strip()",0,default,default),
        new(1,"example-upper","대문자로 변환","영문을 대문자로 통일","result = value.upper()",0,default,default)];
    private string FilePath(string id) => Guid.TryParseExact(id,"N",out _) ? Path.Combine(root,id+".json") : throw new IOException("잘못된 스크립트 ID입니다.");
    private FileStream Lock()
    {
        Directory.CreateDirectory(root);
        try { return new FileStream(Path.Combine(root,"library.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
        catch(IOException) { throw new IOException("다른 창에서 보관함을 저장 중입니다. 다시 시도해주세요."); }
    }
    private ScriptDefinition Read(string path)
    {
        if(new FileInfo(path).Length>512*1024)throw new IOException("스크립트 파일 크기 초과");
        var item=JsonSerializer.Deserialize<ScriptDefinition>(File.ReadAllText(path),Json)??throw new IOException("빈 스크립트 파일");
        if(item.SchemaVersion!=1||!Guid.TryParseExact(item.Id,"N",out _)||Path.GetFileNameWithoutExtension(path)!=item.Id||item.Revision<1)throw new IOException("지원하지 않는 스크립트 형식");
        Validate(item);return item;
    }
    public (List<ScriptDefinition> Items,string Errors) Load()
    {
        var items=new List<ScriptDefinition>();var errors=new List<string>();
        if(Directory.Exists(root))foreach(var path in Directory.EnumerateFiles(root,"*.json"))
        {try{items.Add(Read(path));}catch(Exception e){errors.Add(Path.GetFileName(path)+": "+e.Message);}}
        return (Examples.Concat(items.OrderByDescending(s=>s.UpdatedAt)).ToList(),string.Join("\n",errors));
    }
    public static void Validate(ScriptDefinition item)
    {
        if(item.Language!="python")throw new IOException("Python 스크립트만 지원합니다.");
        if(string.IsNullOrWhiteSpace(item.Name)||item.Name.Length>80)throw new IOException("이름은 1~80자로 입력해주세요.");
        if(item.Description==null||item.Description.Length>500)throw new IOException("설명은 500자 이하여야 합니다.");
        if(item.Code==null||new UTF8Encoding(false,true).GetByteCount(item.Code)>65536)throw new IOException("코드는 UTF-8 64 KiB 이하여야 합니다.");
    }
    public ScriptDefinition Save(ScriptDefinition edited,ScriptDefinition? expected,bool copy=false)
    {
        Validate(edited);using var gate=Lock();
        bool fresh=copy||expected==null||expected.IsExample;
        var now=DateTime.UtcNow;
        var item=edited with { Id=fresh?Guid.NewGuid().ToString("N"):expected!.Id,SchemaVersion=1,Revision=fresh?1:expected!.Revision+1,CreatedAt=fresh?now:expected!.CreatedAt,UpdatedAt=now };
        var path=FilePath(item.Id);
        if(!fresh&&(!File.Exists(path)||Read(path)!=expected))throw new IOException("다른 창에서 변경되었습니다. 다른 이름으로 저장하거나 저장본을 다시 읽어주세요.");
        var tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {JsonSerializer.Serialize(stream,item,Json);stream.Flush(true);}
            File.Move(tmp,path,!fresh);return item;
        }
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    public void Delete(ScriptDefinition expected)
    {
        if(expected.IsExample)throw new IOException("기본 예제는 삭제할 수 없습니다.");
        using var gate=Lock();var path=FilePath(expected.Id);
        if(!File.Exists(path)||Read(path)!=expected)throw new IOException("다른 창에서 변경되었습니다. 다시 읽은 뒤 삭제해주세요.");
        File.Delete(path);
    }
}
