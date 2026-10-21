using System.Text.Json;
using CloneCad.Agent;
using CloneCad.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

if (args.Length == 2 && args[0] == "--export-tools")
{
    File.WriteAllText(args[1], JsonSerializer.Serialize(new { version = "v1", tools = CadTools.Definitions }, CadJson.Options));
    return 0;
}
if (args.Length != 2 || args[0] != "--pipe" || !args[1].StartsWith("CloneCad-", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Usage: CloneCad.Mcp --pipe CloneCad-<instance-id> (copy from CAD connection panel)");
    return 2;
}
var client = new CadPipeClient(args[1]);
var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders(); builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer().WithStdioServerTransport()
    .WithListToolsHandler((request, cancellationToken) => ValueTask.FromResult(new ListToolsResult
    {
        Tools = CadTools.Definitions.Select(t => new Tool { Name = t.Name, Description = t.Description, InputSchema = t.InputSchema }).ToList()
    }))
    .WithCallToolHandler(async (request, cancellationToken) =>
    {
        try
        {
            var parameters = request.Params ?? throw new ArgumentException("Missing parameters");
            var arguments = JsonSerializer.SerializeToElement(parameters.Arguments ?? new Dictionary<string, JsonElement>(), CadJson.Options);
            var reply = await client.CallAsync(parameters.Name, arguments, cancellationToken);
            var content = new List<ContentBlock> { new TextContentBlock { Text = reply.Text } };
            if (reply.ImageBase64 != null) content.Add(ImageContentBlock.FromBytes(Convert.FromBase64String(reply.ImageBase64), "image/png"));
            return new CallToolResult { IsError = reply.IsError, Content = content };
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or ArgumentException)
        { return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "CAD connection failed: " + ex.Message }] }; }
    });
await builder.Build().RunAsync();
return 0;
