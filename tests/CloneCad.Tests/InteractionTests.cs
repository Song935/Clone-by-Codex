using CloneCad.Core;
using Xunit;

namespace CloneCad.Tests;

public sealed class InteractionTests
{
    private static Entity Line(Point2 a, Point2 b) => new() { Kind = EntityKind.Line, Points = [a, b] };
    [Fact]
    public void EndpointAndMidpointBeatACloserNearestPoint()
    {
        var line = Line(new(0, 0), new(100, 0));
        var endpoint = ObjectSnap.Find([line], new(2, 1), 5)!; Assert.Equal(SnapKind.Endpoint, endpoint.Kind); Assert.Equal(new Point2(0, 0), endpoint.Point);
        var midpoint = ObjectSnap.Find([line], new(52, 1), 5)!; Assert.Equal(SnapKind.Midpoint, midpoint.Kind); Assert.Equal(new Point2(50, 0), midpoint.Point);
        Assert.Equal(SnapKind.Nearest, ObjectSnap.Find([line], new(25, 1), 5)!.Kind);
    }
    [Fact]
    public void IntersectionAndPerpendicularAreAvailableWithoutBoundingBoxFalseHits()
    {
        var horizontal = Line(new(-100, 0), new(100, 0)); var vertical = Line(new(0, -100), new(0, 100));
        Assert.Equal(new Point2(0, 0), ObjectSnap.Find([horizontal, vertical], new(2, 1), 5)!.Point);
        var result = ObjectSnap.Find([horizontal], new(20, 2), 5, new(20, 10))!; Assert.Equal(SnapKind.Perpendicular, result.Kind); Assert.Equal(new Point2(20, 0), result.Point);
    }
    [Fact]
    public void CrossingSelectionDoesNotSelectEmptyPartOfLineOrCircleEnvelope()
    {
        var doc = new Drawing(); var diagonal = Line(new(0, 0), new(100, 100));
        Assert.False(SelectionGeometry.Crosses(diagonal, new(0, 80, 10, 90), doc));
        Assert.True(SelectionGeometry.Crosses(diagonal, new(40, 40, 60, 60), doc));
        var circle = new Entity { Kind = EntityKind.Circle, Points = [new(0, 0)], Radius = 100 };
        Assert.False(SelectionGeometry.Crosses(circle, new(-5, -5, 5, 5), doc));
        Assert.True(SelectionGeometry.Crosses(circle, new(95, -5, 105, 5), doc));
    }
    [Fact]
    public void ArcBoundsAndSelectionFollowActualSweepAfterManyRotations()
    {
        var arc = new Entity { Kind = EntityKind.Arc, Points = [new(0, 0)], Radius = 10, StartAngle = 1080, EndAngle = 1170 };
        Assert.True(Geometry.OnArc(arc, new(5, 5))); Assert.False(Geometry.OnArc(arc, new(-5, -5)));
        var bounds = Geometry.Bounds(arc); Assert.True(bounds.MinX >= -Geometry.Epsilon); Assert.True(bounds.MinY >= -Geometry.Epsilon);
        Assert.False(SelectionGeometry.Crosses(arc, new(-11, -11, -1, -1), new()));
    }
    [Fact]
    public void DimensionPickUsesRenderedDimensionLineAndHatchUsesFilledArea()
    {
        var dim = new Entity { Kind = EntityKind.Dimension, Points = [new(0, 0), new(100, 0), new(100, 30)] };
        Assert.Equal(0, Geometry.HitDistance(dim, new(25, 30)), 7);
        Assert.True(SelectionGeometry.Crosses(dim, new(20, 29, 30, 31), new()));
        var hatch = new Entity { Kind = EntityKind.Hatch, Closed = true, Points = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)] };
        Assert.Equal(0, Geometry.HitDistance(hatch, new(50, 50))); Assert.True(SelectionGeometry.Crosses(hatch, new(45, 45, 55, 55), new()));
    }
    [Theory]
    [InlineData("copy")]
    [InlineData("array")]
    public void CopyAndArrayRemapDimensionAndHatchReferencesToEachNewSource(string action)
    {
        var drawing = Examples.StructuralDetail(); var s = new DrawingSession(drawing); var originals = drawing.Entities.Select(e => e.Id).ToHashSet();
        s.Execute(action, new EditOperation { Action = action, Ids = originals.ToList(), Vector = new(2000, 0), Count = 2 });
        var result = s.Snapshot(); var copies = result.Entities.Where(e => !originals.Contains(e.Id)).ToArray();
        Assert.Equal(drawing.Entities.Count, copies.Length);
        foreach (var linked in copies.Where(e => e.References.Count > 0))
        { Assert.All(linked.References, id => Assert.DoesNotContain(id, originals)); Assert.True(linked.Points[0].X >= 2000); Assert.False(linked.Invalid); }
        s.Undo(); Assert.Equal(drawing.Entities.Count, s.Snapshot().Entities.Count);
    }
    [Fact]
    public void RejectingLatestProposalRevealsEarlierPendingProposal()
    {
        var s = new DrawingSession(); var first = s.Propose(0, "first", [new() { Entity = Line(new(0, 0), new(1, 0)) }]);
        var second = s.Propose(0, "second", [new() { Entity = Line(new(0, 0), new(0, 1)) }]);
        s.Reject(second.Id); Assert.Equal(first.Id, s.Preview!.Id); s.Approve(first.Id); Assert.Single(s.Snapshot().Entities);
    }
    [Fact]
    public void StaleProposalsDoNotPermanentlyConsumePendingQuota()
    {
        var s = new DrawingSession();
        for (var i = 0; i < 25; i++)
        { s.Propose(s.Revision, "preview", [new() { Entity = Line(new(0, 0), new(1, 0)) }]); s.Execute("human", new EditOperation { Entity = Line(new(i, 0), new(i, 1)) }); }
        Assert.NotNull(s.Propose(s.Revision, "last", [new() { Entity = Line(new(0, 0), new(1, 0)) }]));
    }
}
