using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CloneCad.Core;

namespace CloneCad.Agent;

public sealed record PipeRequest(string Tool, JsonElement Arguments);
public static class PipeJson
{
    public static readonly JsonSerializerOptions Options = new(CadJson.Options) { WriteIndented = false };
}

public sealed class CadPipeServer(string pipeName, CadTools tools) : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private Task? task;
    public void Start() => task = Task.Run(RunAsync);
    private async Task RunAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                while (pipe.IsConnected && !stop.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(stop.Token); if (line == null) break;
                    ToolReply reply;
                    if (line.Length > 4_000_000) reply = new(true, "요청이 너무 큽니다.");
                    else
                    {
                        try { var request = JsonSerializer.Deserialize<PipeRequest>(line, CadJson.Options)!; reply = tools.Call(request.Tool, request.Arguments); }
                        catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException) { reply = new(true, "잘못된 도구 요청입니다."); }
                    }
                    await writer.WriteLineAsync(JsonSerializer.Serialize(reply, PipeJson.Options));
                }
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { }
        }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); if (task != null) await task.ConfigureAwait(false); stop.Dispose();
    }
}

public sealed class CadPipeClient(string pipeName)
{
    public async Task<ToolReply> CallAsync(string tool, JsonElement args, CancellationToken ct = default)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(5000, ct);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(new PipeRequest(tool, args), PipeJson.Options));
        return JsonSerializer.Deserialize<ToolReply>(await reader.ReadLineAsync(ct) ?? throw new IOException("CAD 연결이 종료됐습니다."), CadJson.Options)!;
    }
}
