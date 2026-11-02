using System.IO;
using System.Security.Cryptography;
namespace InerConfig;

public sealed partial class FileSession
{
    internal static byte[] ReadDiskHash(string path)
    {
        try { using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);return SHA256.HashData(stream); }
        catch(FileNotFoundException){return [];}
        catch(DirectoryNotFoundException){return [];}
    }
    internal bool MatchesDisk(byte[] hash)=>diskHash!=null&&diskHash.SequenceEqual(hash);
    internal void ReloadExternal(FileSession loaded)
    {
        Text=SavedText=loaded.Text;Encoding=loaded.Encoding;Bom=loaded.Bom;diskHash=loaded.diskHash;
        encodingChanged=false;Draft=null;Input=null;PendingDeletes.Clear();
        undo.Clear();redo.Clear();editUndo.Clear();editRedo.Clear();
        if(StructureEditing)StructureBaseline=Text;
        Notify();
    }
}
