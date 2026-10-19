using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace InerConfig.Models;

public sealed record PythonResult(bool Success,string Text,string Kind,string Error,int Line,int Column,string Log);
public sealed class PythonRunner
{
    private static readonly string RunRoot=Path.Combine(Path.GetTempPath(),"InerConfig","ScriptRuns");
    private readonly string folder;
    private bool blocked;
    public PythonRunner(string? folder=null){this.folder=folder??Path.Combine(AppContext.BaseDirectory,"Python");}
    public bool Available=>File.Exists(Path.Combine(folder,"python.exe"))&&File.Exists(Path.Combine(folder,"runner.py"))&&!blocked;
    public async Task<PythonResult> RunAsync(string code,string value,CancellationToken cancel)
    {
        if(!Available)throw new IOException("Python 실행 환경이 없거나 종료 확인이 필요합니다. 앱 설치를 복구해주세요.");
        var utf8=new UTF8Encoding(false,true);
        if(utf8.GetByteCount(code)>65536||utf8.GetByteCount(value)>1048576)throw new IOException("코드 64 KiB / 입력 1 MiB 제한을 초과했습니다.");
        var id=Guid.NewGuid().ToString("N");
        await Task.Run(CleanAbandoned,cancel).ConfigureAwait(false);
        var work=Path.Combine(RunRoot,id);Directory.CreateDirectory(work);
        using var lease=new FileStream(Path.Combine(work,".inerconfig-run"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel);timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var job=new PythonJob();
        using var process=new Process{StartInfo=new ProcessStartInfo(Path.Combine(folder,"python.exe"))
        {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=work,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=utf8}};
        process.StartInfo.ArgumentList.Add("-I");process.StartInfo.ArgumentList.Add("-u");process.StartInfo.ArgumentList.Add(Path.Combine(folder,"runner.py"));
        int outputBytes=0;var log=new StringBuilder();bool started=false,attached=false;
        async Task Drain(Stream stream)
        {
            using var reader=new StreamReader(stream,Encoding.UTF8,false,4096,true);
            var chars=new char[4096];int n;
            while((n=await reader.ReadAsync(chars.AsMemory(),timeout.Token).ConfigureAwait(false))>0)
            {
                if(Interlocked.Add(ref outputBytes,Encoding.UTF8.GetByteCount(chars,0,n))>65536){timeout.Cancel();throw new IOException("진단 출력이 64 KiB를 초과했습니다.");}
                lock(log)log.Append(chars,0,n);
            }
        }
        Task? streams=null;
        try
        {
            cancel.ThrowIfCancellationRequested();process.Start();started=true;
            // Trusted runner blocks in json.load(stdin). Attach before sending code.
            job.Attach(process);attached=true;
            streams=Task.WhenAll(Drain(process.StandardOutput.BaseStream),Drain(process.StandardError.BaseStream));
            var request=JsonSerializer.Serialize(new{runId=id,code,value});
            await process.StandardInput.WriteAsync(request.AsMemory(),timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            // A completed parent may have left children holding the redirected pipes.
            await job.StopAsync().ConfigureAwait(false);
            await streams.WaitAsync(timeout.Token).ConfigureAwait(false);
            cancel.ThrowIfCancellationRequested();
            if(process.ExitCode!=0)throw new IOException("Python이 비정상 종료했습니다 (메모리 제한 또는 강제 종료). 코드: "+process.ExitCode);
            var response=Path.Combine(work,"result.json");
            if(!File.Exists(response)||new FileInfo(response).Length>7*1048576)throw new IOException("실행 결과가 없거나 응답 크기를 초과했습니다.");
            using var json=JsonDocument.Parse(await File.ReadAllTextAsync(response,utf8,cancel).ConfigureAwait(false));var r=json.RootElement;
            if(r.GetProperty("protocolVersion").GetInt32()!=1||r.GetProperty("runId").GetString()!=id)throw new IOException("잘못된 실행 응답입니다.");
            if(r.GetProperty("success").GetBoolean())
            {
                var text=r.GetProperty("resultText").GetString()??throw new IOException("결과 누락");var kind=r.GetProperty("resultKind").GetString()??"";
                if(utf8.GetByteCount(text)>1048576||kind is not ("String" or "Number" or "Boolean"))throw new IOException("결과 형식 또는 크기 오류");
                return new(true,text,kind,"",0,0,log.ToString());
            }
            return new(false,"","",r.GetProperty("errorKind").GetString()+": "+r.GetProperty("message").GetString(),r.GetProperty("line").GetInt32(),r.GetProperty("column").GetInt32(),log.ToString());
        }
        catch(OperationCanceledException)
        {throw new IOException(outputBytes>65536?"진단 출력이 64 KiB를 초과했습니다.":cancel.IsCancellationRequested?"실행을 중지했습니다.":"실행 제한 5초를 초과했습니다.");}
        finally
        {
            timeout.Cancel();
            try
            {
                if(attached)await job.StopAsync().ConfigureAwait(false);
                else if(started&&!process.HasExited){process.Kill(true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);}
            }
            catch{blocked=true;throw;}
            finally
            {
                if(streams!=null)try{await streams.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);}catch{}
                // Only this invocation's generated directory; never follow reparse points.
                lease.Dispose();DeleteRun(work);
            }
        }
    }
    private static void DeleteRun(string path)
    {
        try
        {
            if(Path.GetDirectoryName(path)!=RunRoot||!Guid.TryParseExact(Path.GetFileName(path),"N",out _)||new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))return;
            Directory.Delete(path,true);
        }catch{}
    }
    private static void CleanAbandoned()
    {
        try
        {
            if(!Directory.Exists(RunRoot)||new DirectoryInfo(RunRoot).Attributes.HasFlag(FileAttributes.ReparsePoint))return;
            foreach(var path in Directory.EnumerateDirectories(RunRoot).Take(100))
            {
                var marker=Path.Combine(path,".inerconfig-run");
                if(!File.Exists(marker)||File.GetLastWriteTimeUtc(marker)>DateTime.UtcNow.AddDays(-1))continue;
                try{using(var lease=new FileStream(marker,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}DeleteRun(path);}catch(IOException){}
            }
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
