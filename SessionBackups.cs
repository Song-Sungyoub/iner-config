using System.IO;
using System.Security.Cryptography;
namespace InerConfig;

// Ownership lasts only for this process. A later run never deletes old recovery files.
internal static class SessionBackups
{
    private sealed record Backup(string Path, byte[] Hash) { public bool Failed; }
    private static readonly Dictionary<string,Backup> owned=new(StringComparer.OrdinalIgnoreCase);
    internal static void Replace(string temp,string target)
    {
        if(!owned.TryGetValue(target,out var backup))
        {
            string path=target+".bak";
            FileStream output;
            while(true)
            {
                try { output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);break; }
                catch(IOException) when(File.Exists(path)){path=target+"."+Guid.NewGuid().ToString("N")+".bak";}
            }
            using(output)
            using(var input=File.OpenRead(target)){input.CopyTo(output);output.Flush(true);}
            File.SetAttributes(path,File.GetAttributes(path)|FileAttributes.Hidden);
            backup=new Backup(path,SHA256.HashData(File.ReadAllBytes(path)));
            owned.Add(target,backup);
        }
        try { File.Replace(temp,target,null,true); }
        catch { backup.Failed=true;throw; }
    }
    internal static void Preserve(string target)
    {if(owned.TryGetValue(Path.GetFullPath(target),out var backup))backup.Failed=true;}
    internal static void Cleanup()
    {
        foreach(var backup in owned.Values)
        {
            if(backup.Failed)continue;
            try
            {
                if(File.Exists(backup.Path)&&SHA256.HashData(File.ReadAllBytes(backup.Path)).SequenceEqual(backup.Hash))
                    File.Delete(backup.Path);
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        owned.Clear();
    }
}
