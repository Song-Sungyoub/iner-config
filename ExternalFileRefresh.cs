using System.IO;
using System.Windows;
using System.Windows.Threading;
namespace InerConfig;

public partial class MainWindow
{
    private readonly DispatcherTimer externalTimer=new(){Interval=TimeSpan.FromSeconds(3)};
    private bool externalChecking,externalSaving;
    private readonly Dictionary<FileSession,(long Length,DateTime Time)> externalStamps=[];
    private void InitializeExternalChanges()
    {
        externalTimer.Tick+=async(_,_)=>await CheckExternalChanges();
        Activated+=async(_,_)=>await CheckExternalChanges(true);
        Loaded+=(_,_)=>externalTimer.Start();Closed+=(_,_)=>externalTimer.Stop();
    }
    private async Task CheckExternalChanges(bool force=false)
    {
        if(externalSaving||externalChecking||!IsLoaded||activeTabDrags>0||tabContentPending||ReferenceEditor!=null)return;
        externalChecking=true;
        try
        {
            foreach(var gone in externalStamps.Keys.Where(s=>!sessions.Contains(s)).ToArray())externalStamps.Remove(gone);
            foreach(var s in sessions.ToArray())
            {
                if(s.FilePath is not string path)continue;
                try
                {
                    var stamp=await Task.Run(()=>{var f=new FileInfo(path);return f.Exists?(f.Length,f.LastWriteTimeUtc):(-1L,DateTime.MinValue);});
                    if(!force&&externalStamps.TryGetValue(s,out var prior)&&prior==stamp)continue;
                    var hash=await Task.Run(()=>FileSession.ReadDiskHash(path));
                    if(!sessions.Contains(s)||s.FilePath!=path)continue;
                    if(s.MatchesDisk(hash)){externalStamps[s]=stamp;continue;}
                    bool editing=s==Session&&(inlineBox!=null||FindFocusedCsv()!=null);
                    if(s.Dirty||editing||ReferenceEditor!=null||activeTabDrags>0||tabContentPending)
                    {
                        if(s==Session)Message.Text="외부에서 파일이 변경되었습니다. 현재 편집은 유지되며 저장할 때 사용할 내용을 선택합니다.";
                        continue;
                    }
                    var baseline=s.Capture();
                    var loaded=await Task.Run(()=>FileSession.Load(path,s.Encoding));
                    _=await Task.Run(()=>new ConfigDocument(loaded.Text,loaded.Format,s.Separator));
                    if(!IsLoaded||!sessions.Contains(s)||s.FilePath!=path||s.Dirty||ReferenceEditor!=null||activeTabDrags>0||tabContentPending||s==Session&&(inlineBox!=null||FindFocusedCsv()!=null))continue;
                    var current=s.Capture();
                    if(current.Text!=baseline.Text||!(current.DiskHash??[]).SequenceEqual(baseline.DiskHash??[]))continue;
                    s.ReloadExternal(loaded);externalStamps[s]=stamp;
                    if(s==Session){switching=true;Source.Text=s.Text;switching=false;await Parse(true);Message.Text="외부 변경 내용을 갱신했습니다.";}
                }
                catch(Exception ex)
                {if(s==Session)Message.Text="외부 파일을 갱신하지 못했습니다. 현재 내용은 유지됩니다: "+ex.Message;}
            }
        }
        finally{externalChecking=false;}
    }
    private bool SaveWithExternalCheck(FileSession s,string path)
    {
        externalSaving=true;
        try
        {
        byte[]? expected=null;
        if(string.Equals(Path.GetFullPath(path),s.FilePath,StringComparison.OrdinalIgnoreCase))
        {
            expected=FileSession.ReadDiskHash(path);
            if(!s.MatchesDisk(expected))
            {
                var choice=MessageBox.Show(this,"외부 파일과 현재 편집 내용이 다릅니다.\n\n예: 현재 편집 내용으로 덮어쓰기\n아니요: 현재 편집을 버리고 외부 파일 불러오기\n취소: 편집 유지 (다른 이름으로 저장 가능)","외부 변경 충돌 · "+Path.GetFileName(path),MessageBoxButton.YesNoCancel,MessageBoxImage.Question,MessageBoxResult.Cancel);
                if(choice==MessageBoxResult.Cancel)return false;
                if(choice==MessageBoxResult.No)
                {
                    var loaded=FileSession.Load(path,s.Encoding);
                    _=new ConfigDocument(loaded.Text,loaded.Format,s.Separator);
                    s.ReloadExternal(loaded);if(s==Session)_=ShowSessionText();return false;
                }
            }
        }
        s.Save(path,expected);return true;
        }
        finally{externalSaving=false;}
    }
}
