using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace InerConfig;
public sealed partial class FileSession : INotifyPropertyChanged
{
    internal const long MaxFileBytes=50L*1024*1024;
    internal static void ValidateFileSize(long bytes)
    {
        if(bytes>MaxFileBytes)throw new IOException($"파일이 너무 큽니다 ({bytes/(1024d*1024):N1} MiB). InerConfig는 50 MiB 이하 파일을 지원합니다. 파일을 분할하거나 대용량 파일 전용 도구를 사용해주세요.");
    }
    public string? FilePath { get; private set; }
    public string Format { get; set; } = "JSON";
    public string Text { get; private set; } = "";
    public string SavedText { get; private set; } = "";
    public Encoding Encoding { get; set; } = new UTF8Encoding(false,true);
    public byte[] Bom { get; private set; } = [];
    private byte[]? diskHash;
    private bool encodingChanged;
    public bool Dirty => Text != SavedText || encodingChanged || Draft!=null || Input!=null || PendingDeletes.Count>0;
    public string Title => (FilePath == null ? "새 파일." + Format.ToLowerInvariant() : Path.GetFileName(FilePath)) + (Dirty ? " •" : "");
    public string EncodingName => Encoding.CodePage == 65001 ? "UTF-8" : Encoding.CodePage == 949 ? "CP949" : Encoding.WebName.ToUpperInvariant();
    public char Separator { get; set; } = ',';
    public bool CsvHeader { get; set; }
    public HashSet<string> Collapsed { get; } = [];
    public string? SelectedPath { get; set; }
    public string SearchText { get; set; } = "";
    public bool SourceMode { get; set; }
    private readonly Stack<string> undo = [], redo = [];
    public bool CanUndo => StructureEditing ? editUndo.Count > 0 : undo.Count > 0;
    public bool CanRedo => StructureEditing ? editRedo.Count > 0 : redo.Count > 0;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Notify() { PropertyChanged?.Invoke(this,new(nameof(Title))); PropertyChanged?.Invoke(this,new(nameof(Dirty))); }
    public FileSession(string format,string text="") { Format=format;Text=text;SavedText=""; }
    internal sealed record Snapshot(string? FilePath,string Format,string Text,string SavedText,int CodePage,byte[] Bom,byte[]? DiskHash,bool EncodingChanged,char Separator,bool CsvHeader,string? SelectedPath,string SearchText,bool SourceMode,string[] Collapsed, string? StructureBaseline=null, bool RawEditing=false, AddDraft? Draft=null, string[]? PendingDeletes=null, PendingInput? Input=null);
    internal Snapshot Capture() => new(FilePath,Format,Text,SavedText,Encoding.CodePage,Bom,diskHash,encodingChanged,Separator,CsvHeader,SelectedPath,SearchText,SourceMode,Collapsed.ToArray(),StructureBaseline,RawEditing,Draft,PendingDeletes.ToArray(),Input);
    internal static FileSession Restore(Snapshot saved)
    {
        var encoding=System.Text.Encoding.GetEncoding(saved.CodePage,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);
        var session=new FileSession(saved.Format,saved.Text) { FilePath=saved.FilePath,SavedText=saved.SavedText,Encoding=encoding,Bom=saved.Bom,diskHash=saved.DiskHash,encodingChanged=saved.EncodingChanged };
        // Clean files follow disk changes; unsaved work retains its original disk fingerprint.
        if(saved.Input==null && saved.StructureBaseline==null && !session.Dirty && saved.FilePath!=null && File.Exists(saved.FilePath))
        {
            try { session=Load(saved.FilePath,encoding); } catch { /* Keep the recovery copy if the original cannot be read. */ }
        }
        session.Separator=saved.Separator;session.CsvHeader=saved.CsvHeader;session.SelectedPath=saved.SelectedPath;session.SearchText=saved.SearchText;session.SourceMode=saved.SourceMode;
        session.Input=saved.Input;session.PendingDeletes.UnionWith(saved.PendingDeletes??[]);session.Draft=saved.Draft;session.StructureBaseline=saved.StructureBaseline;session.RawEditing=saved.RawEditing;session.Collapsed.UnionWith(saved.Collapsed);return session;
    }
    public static string GetFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch { ".json"=>"JSON", ".xml" or ".config"=>"XML", ".yaml" or ".yml"=>"YAML", ".ini" or ".cfg"=>"INI", ".csv" or ".tsv"=>"CSV", _=>throw new FormatException("JSON, XML, CSV, YAML, INI 형식의 파일을 선택하세요.") };
    public static FileSession Load(string path,Encoding? fallback=null)
    {
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        ValidateFileSize(input.Length);
        byte[] bytes=new byte[checked((int)input.Length)];input.ReadExactly(bytes);var s=new FileSession(GetFormat(path)){FilePath=Path.GetFullPath(path)};
        if(bytes.AsSpan().StartsWith(new byte[]{0xff,0xfe,0,0})){s.Encoding=new UTF32Encoding(false,true,true);s.Bom=[0xff,0xfe,0,0];}
        else if(bytes.AsSpan().StartsWith(new byte[]{0,0,0xfe,0xff})){s.Encoding=new UTF32Encoding(true,true,true);s.Bom=[0,0,0xfe,0xff];}
        else if(bytes.AsSpan().StartsWith(new byte[]{0xff,0xfe})){s.Encoding=new UnicodeEncoding(false,true,true);s.Bom=[0xff,0xfe];}
        else if(bytes.AsSpan().StartsWith(new byte[]{0xfe,0xff})){s.Encoding=new UnicodeEncoding(true,true,true);s.Bom=[0xfe,0xff];}
        else if(bytes.AsSpan().StartsWith(new byte[]{0xef,0xbb,0xbf})){s.Bom=[0xef,0xbb,0xbf];}
        else if(fallback!=null)s.Encoding=fallback;
        s.Text=s.Encoding.GetString(bytes,s.Bom.Length,bytes.Length-s.Bom.Length);s.SavedText=s.Text;s.diskHash=SHA256.HashData(bytes);
        if(Path.GetExtension(path).Equals(".tsv",StringComparison.OrdinalIgnoreCase))s.Separator='\t';
        return s;
    }
    public void Change(string text)
    {
        if(Text==text)return;
        if(StructureEditing){PushEditUndo();editRedo.Clear();Text=text;Notify();return;}
        // ponytail: full-text history, capped at 30 snapshots; switch to text deltas for very large files.
        if(undo.Count>=30){var keep=undo.Take(29).Reverse().ToList();undo.Clear();foreach(var x in keep)undo.Push(x);}
        undo.Push(Text);redo.Clear();Text=text;Notify();
    }
    public void Undo(){if(StructureEditing){if(editUndo.Count>0){editRedo.Push(EditSnapshot());RestoreEdit(editUndo.Pop());}return;}if(undo.Count==0)return;redo.Push(Text);Text=undo.Pop();Notify();}
    public void Redo(){if(StructureEditing){if(editRedo.Count>0){PushEditUndo();RestoreEdit(editRedo.Pop());}return;}if(redo.Count==0)return;undo.Push(Text);Text=redo.Pop();Notify();}
    public void SetEncoding(string name)
    {
        Encoding=name switch{"CP949"=>System.Text.Encoding.GetEncoding(949,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback),"UTF-16 LE"=>new UnicodeEncoding(false,true,true),"UTF-8 BOM"=>new UTF8Encoding(true,true),_=>new UTF8Encoding(false,true)};
        Bom=name is "UTF-16 LE" or "UTF-8 BOM"?Encoding.GetPreamble():[];
        encodingChanged=true;Notify();
    }
    public void Save(string path)
    {
        if(Input!=null)throw new InvalidOperationException("미적용 입력을 먼저 복원하거나 취소하세요.");
        if(Draft!=null)throw new InvalidOperationException("추가 초안을 먼저 확정하세요.");
        string before=Text;
        try{if(PendingDeletes.Count>0)Text=WithPendingDeletes();SaveCore(path);PendingDeletes.Clear();}
        catch{SessionBackups.Preserve(path);Text=before;throw;}
    }
    private void SaveCore(string path)
    {
        _=new ConfigDocument(Text,Format,Separator);
        if(Format=="XML")
        {
            var declaration=System.Xml.Linq.XDocument.Parse(Text).Declaration?.Encoding;
            if(!string.IsNullOrWhiteSpace(declaration))
            {
                int declared=System.Text.Encoding.GetEncoding(declaration).CodePage;
                if(declared!=Encoding.CodePage&&!(declared==1200&&Encoding.CodePage==1201))throw new IOException("XML 선언의 encoding과 저장 인코딩이 다릅니다. 원문 선언 또는 저장 인코딩을 맞춰 주세요.");
            }
        }
        string full=Path.GetFullPath(path);bool same=string.Equals(full,FilePath,StringComparison.OrdinalIgnoreCase);
        if(same&&diskHash!=null&&(!File.Exists(full)||!SHA256.HashData(File.ReadAllBytes(full)).SequenceEqual(diskHash)))throw new IOException("외부에서 파일이 변경되거나 삭제되었습니다. 다른 이름으로 저장하거나 파일을 다시 여세요.");
        byte[] payload=Encoding.GetBytes(Text),bytes=[..Bom,..payload];
        if(same&&diskHash!=null&&SHA256.HashData(bytes).SequenceEqual(diskHash)){SavedText=Text;encodingChanged=false;Notify();return;}
        string temp=Path.Combine(Path.GetDirectoryName(full)!,"."+Path.GetFileName(full)+"."+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(bytes);f.Flush(true);}
            if(File.Exists(full))SessionBackups.Replace(temp,full);else File.Move(temp,full);
            FilePath=full;diskHash=SHA256.HashData(bytes);SavedText=Text;encodingChanged=false;Notify();
        }
        catch{SessionBackups.Preserve(full);throw;}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
