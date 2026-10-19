using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace InerConfig.Models;

// Resource/lifetime boundary only; this does not restrict filesystem/network rights.
internal sealed class PythonJob : IDisposable
{
    private readonly SafeFileHandle handle;
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimit
    {public long UserTime,JobTime;public uint Flags;public UIntPtr Minimum,Maximum;public uint Active;public UIntPtr Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimit {public BasicLimit Basic;public IoCounters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
    [StructLayout(LayoutKind.Sequential)] private struct Accounting {public long User,Kernel,PeriodUser,PeriodKernel;public uint Faults,Total,Active,Terminated;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetInformationJobObject(SafeFileHandle job,int info,ref ExtendedLimit limit,int size);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool TerminateJobObject(SafeFileHandle job,uint code);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool QueryInformationJobObject(SafeFileHandle job,int info,out Accounting accounting,int size,IntPtr returned);
    public PythonJob()
    {
        handle=CreateJobObject(IntPtr.Zero,null);
        if(handle.IsInvalid)throw new Win32Exception();
        var limits=new ExtendedLimit{Basic=new BasicLimit{Flags=0x2000|0x200},JobMemory=(UIntPtr)(256UL*1024*1024)};
        if(!SetInformationJobObject(handle,9,ref limits,Marshal.SizeOf<ExtendedLimit>())){handle.Dispose();throw new Win32Exception();}
    }
    public void Attach(Process process){if(!AssignProcessToJobObject(handle,process.Handle))throw new Win32Exception();}
    public async Task StopAsync()
    {
        if(!TerminateJobObject(handle,1))throw new Win32Exception();
        var clock=Stopwatch.StartNew();
        do
        {
            if(!QueryInformationJobObject(handle,1,out var info,Marshal.SizeOf<Accounting>(),IntPtr.Zero))throw new Win32Exception();
            if(info.Active==0)return;
            await Task.Delay(20).ConfigureAwait(false);
        }while(clock.ElapsedMilliseconds<2000);
        throw new IOException("Python 프로세스 종료를 확인하지 못했습니다. 창을 닫고 다시 시도해주세요.");
    }
    public void Dispose()=>handle.Dispose();
}
