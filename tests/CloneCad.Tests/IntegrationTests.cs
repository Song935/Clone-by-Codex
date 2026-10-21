using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CloneCad.Core;
using CloneCad.Agent;
using CloneCad.Formats;
using Xunit;

namespace CloneCad.Tests;

public sealed class IntegrationTests
{
    [Fact]
    public void NativeFileRoundTripPreservesAssociationsAndKoreanPaths()
    {
        var folder = Path.Combine(Path.GetTempPath(), "캐드-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "상세도.cbcad"); var original = Examples.StructuralDetail(); DrawingFile.Save(original, path);
            var restored = DrawingFile.Load(path); Assert.Equal(JsonSerializer.Serialize(original, CadJson.Options), JsonSerializer.Serialize(restored, CadJson.Options));
            DrawingFile.Save(restored, path); Assert.Single(Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact]
    public void StructuralDxfRoundTripPreservesSupportedEntityGeometry()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dxf");
        try
        {
            var d = Examples.StructuralDetail(); DxfExchange.Export(d, path); var report = DxfExchange.Import(path);
            Assert.Empty(report.Unsupported); Assert.Equal(d.Entities.Count, report.Drawing.Entities.Count);
            foreach (var kind in new[] { EntityKind.Circle, EntityKind.Polyline, EntityKind.Text, EntityKind.Dimension, EntityKind.Hatch })
                Assert.Equal(d.Entities.Count(e => e.Kind == kind), report.Drawing.Entities.Count(e => e.Kind == kind));
            Assert.Contains(report.Drawing.Entities, e => e.Text.Contains("콘크리트"));
            Assert.Contains(report.Drawing.Entities, e => e.Kind == EntityKind.Circle && e.Radius == 16 && e.Center == new Point2(120, 120));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void PdfAndPreviewAreRealArtifacts()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        try
        {
            var d = Examples.StructuralDetail(); DrawingRenderer.ExportPdf(d, path, new("A3", true, 10));
            var bytes = File.ReadAllBytes(path); Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4)); Assert.True(bytes.Length > 1000);
            var png = DrawingRenderer.PreviewPng(d); Assert.Equal((byte)137, png[0]); Assert.True(png.Length > 1000);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task NamedPipeUsesTheSameToolsAndNeverAutoApproves()
    {
        var s = new DrawingSession(); var tools = new CadTools(id => id == s.Id ? s : null, () => [s.Id]); var pipe = "CloneCad-test-" + Guid.NewGuid();
        await using var server = new CadPipeServer(pipe, tools); server.Start(); var client = new CadPipeClient(pipe);
        var list = await client.CallAsync("cad_get_document", JsonSerializer.SerializeToElement(new { })); Assert.False(list.IsError, list.Text);
        var proposal = await client.CallAsync("cad_propose_edit", JsonSerializer.SerializeToElement(new { documentId = s.Id, baseRevision = 0, summary = "테스트", operations = new[] { new EditOperation { Entity = new Entity { Kind = EntityKind.Circle, Points = [new(0, 0)], Radius = 10 } } } }, CadJson.Options));
        Assert.False(proposal.IsError, proposal.Text); Assert.Empty(s.Snapshot().Entities); Assert.NotNull(s.Preview);
    }
    private sealed class ScriptedProvider(params JsonObject[] responses) : IAgentProvider
    {
        private int index;
        public Task<JsonObject> CompleteAsync(JsonArray messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(responses[index++]); }
    }
    [Fact]
    public async Task AgentToolLoopReturnsResultsAndLeavesProposalPending()
    {
        var s = new DrawingSession(); var tools = new CadTools(id => s, () => [s.Id]);
        var arguments = JsonSerializer.SerializeToNode(new { documentId = s.Id, baseRevision = 0, summary = "선", operations = new[] { new EditOperation { Entity = new Entity { Kind = EntityKind.Line, Points = [new(0, 0), new(10, 0)] } } } }, CadJson.Options);
        var provider = new ScriptedProvider(new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_use", ["id"] = "call_1", ["name"] = "cad_propose_edit", ["input"] = arguments }) }, new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "변경안을 확인하세요." }) });
        var text = await new AgentRunner(provider, tools).RunAsync("선을 그려줘", CancellationToken.None);
        Assert.Contains("변경안", text); Assert.Empty(s.Snapshot().Entities); Assert.NotNull(s.Preview);
    }
    [Fact]
    public async Task CancellationDoesNotExecuteTools()
    {
        var s = new DrawingSession(); var tools = new CadTools(id => s, () => [s.Id]); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AgentRunner(new ScriptedProvider(), tools).RunAsync("cancel", cancel.Token)); Assert.Empty(s.Snapshot().Entities);
    }
}
