using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CloneCad.Agent;
using CloneCad.Core;
using Xunit;

namespace CloneCad.Tests;

public sealed class McpProtocolTests
{
    [Fact]
    public async Task RealMcpBridgeInitializesListsToolsAndCallsCadOverStdio()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CloneCad.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var bridge = Path.Combine(root!.FullName, "src", "CloneCad.Mcp", "bin", configuration, "net10.0", "CloneCad.Mcp.dll");
        Assert.True(File.Exists(bridge), "Build solution (including MCP bridge) before tests.");
        var dotnetRoot = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
        var pipeName = "CloneCad-protocol-" + Guid.NewGuid(); var session = new DrawingSession();
        await using var server = new CadPipeServer(pipeName, new CadTools(id => id == session.Id ? session : null, () => [session.Id])); server.Start();
        var info = new ProcessStartInfo(Path.Combine(dotnetRoot, "dotnet.exe")) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(bridge); info.ArgumentList.Add("--pipe"); info.ArgumentList.Add(pipeName);
        using var process = Process.Start(info)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await Send(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "cad-tests", version = "1.0" } } });
            var initialized = await Receive(); Assert.True(initialized.GetProperty("result").TryGetProperty("serverInfo", out _), initialized.ToString());
            await Send(new { jsonrpc = "2.0", method = "notifications/initialized" });
            await Send(new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } });
            var listing = await Receive(); Assert.Equal(CadTools.Definitions.Count, listing.GetProperty("result").GetProperty("tools").GetArrayLength());
            await Send(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "cad_get_document", arguments = new { } } });
            var result = await Receive(); var text = result.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(); Assert.Contains(session.Id.ToString(), text);
        }
        finally
        {
            process.StandardInput.Close();
            try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { process.Kill(true); }
        }
        async Task Send(object obj) => await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(obj));
        async Task<JsonElement> Receive()
        {
            var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.NotNull(line); return JsonDocument.Parse(line!).RootElement.Clone();
        }
    }
}
