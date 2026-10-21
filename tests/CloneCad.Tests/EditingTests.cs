using System.Text.Json;
using CloneCad.Core;
using CloneCad.Agent;
using Xunit;

namespace CloneCad.Tests;

public sealed class EditingTests
{
    [Fact]
    public void MissingCoordinatesAndNullIdsReturnToolErrors()
    {
        var session = new DrawingSession(); var tools = new CadTools(id => session, () => [session.Id]);
        var incomplete = JsonSerializer.SerializeToElement(new { documentId = session.Id, baseRevision = 0, summary = "bad", operations = new[] { new { action = "create", entity = new { kind = "circle", points = new[] { new { x = 10 } }, radius = 5 } } } });
        Assert.True(tools.Call("cad_propose_edit", incomplete).IsError); Assert.Empty(session.Snapshot().Entities);
        Assert.True(tools.Call("cad_get_entities", JsonSerializer.SerializeToElement(new { documentId = session.Id, ids = (string[]?)null })).IsError);
    }
    [Fact]
    public void MutatingDisplayedPreviewCannotChangeApprovedTransaction()
    {
        var session = new DrawingSession(); var proposal = session.Propose(0, "safe", [new() { Entity = Line(new(0, 0), new(10, 0)) }]);
        session.Preview!.Result.Entities.Clear(); session.Approve(proposal.Id); Assert.Single(session.Snapshot().Entities);
    }
    private static Entity Line(Point2 a, Point2 b) => new() { Kind = EntityKind.Line, Points = [a, b] };
    private static DrawingSession SessionWith(Entity entity) => new(new Drawing { Entities = [entity] });

    [Fact]
    public void ProposalDoesNotChangeDrawingUntilHumanApprovalAndUndoIsAtomic()
    {
        var s = new DrawingSession();
        var proposal = s.Propose(0, "two lines", [new EditOperation() { Entity = Line(new(0, 0), new(10, 0)) }, new EditOperation() { Entity = Line(new(0, 0), new(0, 10)) }]);
        Assert.Empty(s.Snapshot().Entities); Assert.False(s.IsDirty);
        s.Approve(proposal.Id); Assert.Equal(2, s.Snapshot().Entities.Count);
        Assert.True(s.Undo()); Assert.Empty(s.Snapshot().Entities);
        Assert.True(s.Redo()); Assert.Equal(2, s.Snapshot().Entities.Count);
        Assert.Throws<InvalidOperationException>(() => s.Approve(proposal.Id));
    }
    [Fact]
    public void RejectionAndStaleRevisionPreserveLiveDrawing()
    {
        var s = new DrawingSession(); var p = s.Propose(0, "preview", [new EditOperation() { Entity = Line(new(0, 0), new(10, 0)) }]);
        s.Execute("human", new EditOperation() { Entity = Line(new(0, 0), new(0, 10)) });
        Assert.Throws<InvalidOperationException>(() => s.Approve(p.Id)); Assert.Single(s.Snapshot().Entities);
        s.Reject(p.Id); Assert.Null(s.GetProposal(p.Id));
        Assert.Throws<InvalidOperationException>(() => s.Propose(0, "stale", [new EditOperation() { Entity = Line(new(0, 0), new(1, 0)) }]));
    }
    [Fact]
    public void FailedBatchNeverPartiallyChangesDrawingOrUndoHistory()
    {
        var s = new DrawingSession();
        Assert.Throws<ArgumentException>(() => s.Execute("bad batch", new EditOperation() { Entity = Line(new(0, 0), new(1, 0)) }, new EditOperation() { Entity = Line(new(0, 0), new(0, 0)) }));
        Assert.Empty(s.Snapshot().Entities); Assert.False(s.CanUndo); Assert.Equal(0, s.Revision);
    }
    [Fact]
    public void SnapshotAndProposalAreIsolatedFromCallers()
    {
        var s = new DrawingSession(); var p = s.Propose(0, "one", [new EditOperation() { Entity = Line(new(0, 0), new(1, 0)) }]);
        p.Result.Entities.Clear(); s.Approve(p.Id); Assert.Single(s.Snapshot().Entities);
        var snapshot = s.Snapshot(); snapshot.Entities.Clear(); Assert.Single(s.Snapshot().Entities);
    }
    [Fact]
    public void UndoRevisionNeverReusesAnOldRevision()
    {
        var s = new DrawingSession(); s.Execute("line", new EditOperation() { Entity = Line(new(0, 0), new(1, 0)) });
        var p = s.Propose(1, "preview", [new EditOperation() { Entity = Line(new(0, 1), new(1, 1)) }]);
        s.Undo(); s.Redo(); Assert.Equal(3, s.Revision); Assert.Throws<InvalidOperationException>(() => s.Approve(p.Id));
    }
    [Fact]
    public void LockedLayerCannotBeEditedEvenThroughAgent()
    {
        var line = Line(new(0, 0), new(10, 0)); var s = SessionWith(line);
        s.Execute("lock", new EditOperation() { Action = "layer", Layer = new("0", Locked: true) });
        Assert.Throws<InvalidOperationException>(() => s.Execute("move", new EditOperation() { Action = "move", Ids = [line.Id], Vector = new(10, 10) }));
        Assert.Equal(new Point2(0, 0), s.Snapshot().Entities[0].Points[0]);
    }
    [Theory]
    [InlineData("move", 15, 20)]
    [InlineData("copy", 15, 20)]
    public void MoveAndCopyUseTheSameOperationEngine(string action, double x, double y)
    {
        var line = Line(new(5, 0), new(10, 0)); var s = SessionWith(line);
        s.Execute(action, new EditOperation() { Action = action, Ids = [line.Id], Vector = new(10, 20) });
        Assert.Equal(new Point2(x, y), s.Snapshot().Entities[^1].Points[0]);
    }
    [Fact]
    public void FilletMakesTangentArcAndTrimsBothLines()
    {
        var a = Line(new(0, 0), new(100, 0)); var b = Line(new(0, 0), new(0, 100)); var s = new DrawingSession(new() { Entities = [a, b] });
        s.Execute("fillet", new EditOperation() { Action = "fillet", Ids = [a.Id, b.Id], Value = 10 });
        var arc = s.Snapshot().Entities.Single(e => e.Kind == EntityKind.Arc);
        Assert.Equal(10, arc.Radius); Assert.Equal(10, arc.Center.X, 7); Assert.Equal(10, arc.Center.Y, 7);
    }
    [Fact]
    public void InvalidFilletRollsBackBothLines()
    {
        var a = Line(new(0, 0), new(10, 0)); var b = Line(new(0, 0), new(0, 10)); var s = new DrawingSession(new() { Entities = [a, b] });
        Assert.Throws<ArgumentException>(() => s.Execute("fillet", new EditOperation() { Action = "fillet", Ids = [a.Id, b.Id], Value = 50 }));
        Assert.Equal(2, s.Snapshot().Entities.Count); Assert.Equal(new Point2(0, 0), s.Snapshot().Entities[0].Points[0]);
    }
    [Fact]
    public void JoinConnectsReversedLinesAndExplodePreservesSegments()
    {
        var a = Line(new(0, 0), new(10, 0)); var b = Line(new(20, 0), new(10, 0)); var s = new DrawingSession(new() { Entities = [a, b] });
        s.Execute("join", new EditOperation() { Action = "join", Ids = [a.Id, b.Id] });
        var joined = Assert.Single(s.Snapshot().Entities); Assert.Equal(3, joined.Points.Count);
        s.Execute("explode", new EditOperation() { Action = "explode", Ids = [joined.Id] }); Assert.Equal(2, s.Snapshot().Entities.Count);
    }
    [Fact]
    public void DimensionAndHatchUpdateWithSourceAndBecomeInvalidAfterDeletion()
    {
        var d = Examples.StructuralDetail(); var source = d.Entities.First(e => e.Kind == EntityKind.Line); var boundary = d.Entities.First(e => e.Kind == EntityKind.Polyline); var s = new DrawingSession(d);
        s.Execute("move", new EditOperation() { Action = "move", Ids = [source.Id, boundary.Id], Vector = new(100, 20) });
        var dim = s.Snapshot().Entities.First(e => e.Kind == EntityKind.Dimension); Assert.Equal(new Point2(100, 20), dim.Points[0]);
        var hatch = s.Snapshot().Entities.First(e => e.Kind == EntityKind.Hatch); Assert.Equal(new Point2(100, 20), hatch.Points[0]);
        s.Execute("delete", new EditOperation() { Action = "delete", Ids = [source.Id, boundary.Id] });
        Assert.All(s.Snapshot().Entities.Where(e => e.References.Count > 0), e => Assert.True(e.Invalid));
    }
    [Fact]
    public void AgentAndHumanProduceEquivalentGeometry()
    {
        var op = new EditOperation { Entity = Line(new(0, 0), new(100, 0)) }; var human = new DrawingSession(); var agent = new DrawingSession();
        human.Execute("line", op);
        var tools = new CadTools(id => id == agent.Id ? agent : null, () => [agent.Id]);
        var reply = tools.Call("cad_propose_edit", JsonSerializer.SerializeToElement(new { documentId = agent.Id, baseRevision = 0, summary = "line", operations = new[] { op } }, CadJson.Options));
        Assert.False(reply.IsError, reply.Text); Assert.Empty(agent.Snapshot().Entities); agent.Approve(agent.Preview!.Id);
        Assert.Equal(JsonSerializer.Serialize(human.Snapshot().Entities, CadJson.Options), JsonSerializer.Serialize(agent.Snapshot().Entities, CadJson.Options));
        Assert.DoesNotContain(CadTools.Definitions, t => t.Name.Contains("approve"));
    }
    [Fact]
    public void InvalidAgentArgumentsReturnErrorInsteadOfChangingDocument()
    {
        var s = new DrawingSession(); var tools = new CadTools(id => s, () => [s.Id]);
        var result = tools.Call("cad_propose_edit", JsonSerializer.SerializeToElement(new { documentId = s.Id, baseRevision = 0, summary = "bad", operations = new[] { new EditOperation { Entity = new Entity { Kind = EntityKind.Circle, Points = [new(0, 0)], Radius = -10 } } } }, CadJson.Options));
        Assert.True(result.IsError); Assert.Empty(s.Snapshot().Entities); Assert.Null(s.Preview);
    }
    [Fact]
    public void SpatialIndexQueriesOnlyMatchingBounds()
    {
        var s = new DrawingSession(new() { Entities = Enumerable.Range(0, 10000).Select(i => Line(new(i * 10, 0), new(i * 10 + 5, 5))).ToList() });
        Assert.Equal(2, s.Query(new(0, -1, 15, 6)).Length);
        s.Execute("create", new EditOperation() { Entity = Line(new(1, 1), new(2, 2)) }); Assert.Equal(3, s.Query(new(0, -1, 15, 6)).Length);
    }
    [Theory]
    [InlineData("@10,20", 15, 25)]
    [InlineData("@10<90", 5, 15)]
    [InlineData("10,20", 10, 20)]
    public void CoordinateParsingRespectsReference(string input, double x, double y)
    { var p = CoordinateParser.Parse(input, new(5, 5)); Assert.Equal(x, p.X, 7); Assert.Equal(y, p.Y, 7); }
}
