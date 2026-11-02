using System.IO;
namespace InerConfig;
internal static class ExternalChangesTest
{
    internal static void Run()
    {
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        void Assert(bool ok){if(!ok)throw new Exception("External refresh regression");}
        try
        {
            File.WriteAllText(path,"{\"v\":1}");var s=FileSession.Load(path);
            File.WriteAllText(path,"{\"v\":2}");Assert(!s.MatchesDisk(FileSession.ReadDiskHash(path)));
            s.BeginStructure();s.ReloadExternal(FileSession.Load(path));s.CancelStructure();
            Assert(s.Text.Contains(":2")&&!s.Dirty&&!s.CanUndo);
            s.Change("{\"v\":3}");File.WriteAllText(path,"{\"v\":20}");bool refused=false;try{s.Save(path);}catch(IOException){refused=true;}Assert(refused&&s.Dirty);
            var approved=FileSession.ReadDiskHash(path);File.WriteAllText(path,"{\"v\":4}");
            refused=false;try{s.Save(path,approved);}catch(IOException){refused=true;}Assert(refused&&File.ReadAllText(path).Contains(":4"));
            s.Save(path,FileSession.ReadDiskHash(path));Assert(!s.Dirty&&File.ReadAllText(path).Contains(":3"));
            File.Delete(path);Assert(!s.MatchesDisk(FileSession.ReadDiskHash(path)));
            s.Change("{\"v\":5}");s.Save(path,FileSession.ReadDiskHash(path));Assert(File.Exists(path)&&!s.Dirty);
        }
        finally{File.Delete(path);}
    }
}
