using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InerConfig;

internal sealed class InstanceService : IDisposable
{
    private readonly Mutex mutex;
    private readonly CancellationTokenSource stop = new();
    private readonly string name;
    internal bool IsPrimary { get; }
    internal InstanceService(string? identity = null)
    {
        var key = identity ?? $"{Environment.UserName}|{Environment.ProcessPath?.ToUpperInvariant()}|{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
        name = "InerConfig-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24];
        mutex = new Mutex(false, "Local\\" + name);
        try { IsPrimary = mutex.WaitOne(0); } catch (AbandonedMutexException) { IsPrimary = true; }
    }
    internal async Task Forward(string[] args)
    {
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(5000);
        using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(args));
        // The receiving window acknowledges after accepting the request; file dialogs may take longer.
        if (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) != "OK") throw new IOException("기존 창이 파일 열기 요청을 받지 못했습니다.");
    }
    internal async Task Listen(Action<string[]> receive)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(server, leaveOpen: true);
                using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(stop.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), stop.Token);
                var args = JsonSerializer.Deserialize<string[]>(line ?? "") ?? throw new IOException("잘못된 파일 열기 요청");
                receive(args);
                await writer.WriteLineAsync("OK");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or JsonException or TimeoutException) { }
        }
    }
    public void Dispose()
    {
        stop.Cancel();
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
