using CloneCad.Core;
using CloneCad.Formats;
using Xunit;

namespace CloneCad.Tests;

public sealed class CompatibilityTests
{
    [Fact]
    public void DxfPreservesUnitsColorsLineweightsAndHatchSpacing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dxf");
        try
        {
            var d = Examples.StructuralDetail(); d.Units = "m"; DxfExchange.Export(d, path); var result = DxfExchange.Import(path);
            Assert.Equal("m", result.Drawing.Units); Assert.Equal("#F5BA68", result.Drawing.GetLayer("철근").Color);
            Assert.Equal(.5, result.Drawing.GetLayer("철근").LineWeight);
            Assert.Equal(35, result.Drawing.Entities.First(e => e.Kind == EntityKind.Hatch).HatchSpacing, 5);
        }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData("aligned", 100)]
    [InlineData("linear", 100)]
    [InlineData("radius", 100)]
    [InlineData("diameter", 200)]
    [InlineData("angular", 90)]
    public void DimensionKindsHaveCorrectMeasurementsAndDxfRoundTrip(string kind, double measurement)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dxf");
        var e = new Entity { Kind = EntityKind.Dimension, DimensionType = kind, Points = [new(0, 0), new(100, 0), new(0, 100), new(50, 50)] };
        try
        {
            var d = new Drawing { Entities = [e] }; var label = Dimensions.Layout(e, d.DimStyle).Label;
            Assert.Contains(measurement.ToString(), label); DxfExchange.Export(d, path); var report = DxfExchange.Import(path);
            Assert.Empty(report.Unsupported); var restored = Assert.Single(report.Drawing.Entities); Assert.Equal(kind, restored.DimensionType);
            Assert.Equal(label, Dimensions.Layout(restored, report.Drawing.DimStyle).Label);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void BlockInstancesShareOneDefinitionAndKeepLayersInDxf()
    {
        var d = new Drawing(); var id = Guid.NewGuid(); d.Layers.Add(new("details", "#FF0000"));
        d.Blocks.Add(new(id, "unit", [new() { Kind = EntityKind.Line, Points = [new(0, 0), new(10, 0)], Layer = "details" }]));
        d.Entities.Add(new() { Kind = EntityKind.Block, BlockId = id, Points = [new(0, 0)] });
        d.Entities.Add(new() { Kind = EntityKind.Block, BlockId = id, Points = [new(100, 0)] });
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dxf");
        try
        {
            DxfExchange.Export(d, path); var report = DxfExchange.Import(path); Assert.Empty(report.Unsupported);
            Assert.Equal(2, report.Drawing.Entities.Count); Assert.Single(report.Drawing.Blocks);
            Assert.Equal("details", report.Drawing.Blocks[0].Entities[0].Layer);
            var session = new DrawingSession(report.Drawing); Assert.Single(session.Query(new(105, -1, 111, 1)));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void UnsupportedBulgeIsReportedInsteadOfSilentlyFlattened()
    {
        var doc = new ACadSharp.CadDocument(ACadSharp.ACadVersion.AC1027); var poly = new ACadSharp.Entities.LwPolyline();
        poly.Vertices.Add(new() { Location = new CSMath.XY(0, 0), Bulge = 1 }); poly.Vertices.Add(new() { Location = new CSMath.XY(10, 0) }); doc.Entities.Add(poly);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dxf");
        try { ACadSharp.IO.DxfWriter.Write(path, doc, false); var report = DxfExchange.Import(path); Assert.Empty(report.Drawing.Entities); Assert.Single(report.Unsupported); }
        finally { File.Delete(path); }
    }
    [Fact]
    public void CircularIntersectionsAndTangentsAreGeometricallyExact()
    {
        var circle = new Entity { Kind = EntityKind.Circle, Points = [new(0, 0)], Radius = 5 };
        var points = Geometry.LineCircle(new(-10, 0), new(10, 0), circle).ToArray(); Assert.Equal(2, points.Length); Assert.Contains(new Point2(5, 0), points);
        var tangent = Geometry.Tangents(circle, new(10, 0)).ToArray(); Assert.Equal(2, tangent.Length);
        foreach (var p in tangent) { Assert.Equal(5, p.Length, 7); Assert.Equal(0, Geometry.Dot(p, new Point2(10, 0) - p), 7); }
        var other = circle with { Id = Guid.NewGuid(), Points = [new(6, 0)] }; Assert.Equal(2, Geometry.CircleCircle(circle, other).Count());
    }
    [Fact]
    public void BlockCyclesAndDegenerateBoundariesAreRejected()
    {
        var id = Guid.NewGuid(); var block = new Entity { Kind = EntityKind.Block, BlockId = id, Points = [new(0, 0)] };
        Assert.Throws<ArgumentException>(() => new DrawingSession(new() { Blocks = [new(id, "cycle", [block])] }));
        Assert.Throws<ArgumentException>(() => Geometry.Validate(new() { Kind = EntityKind.Polyline, Closed = true, Points = [new(0, 0), new(1, 0), new(2, 0)] }));
    }
    [Fact]
    public void RectangularArrayAndPolylineOffsetCreateValidGeometry()
    {
        var line = new Entity { Kind = EntityKind.Line, Points = [new(0, 0), new(5, 0)] }; var s = new DrawingSession(new() { Entities = [line] });
        s.Execute("array", new EditOperation { Action = "array", Ids = [line.Id], Count = 3, Rows = 2, Vector = new(10, 0), RowVector = new(0, 20) });
        Assert.Equal(6, s.Snapshot().Entities.Count); Assert.Contains(s.Snapshot().Entities, e => e.Points[0] == new Point2(20, 20));
        var polygon = new Entity { Kind = EntityKind.Polyline, Closed = true, Points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)] }; var p = new DrawingSession(new() { Entities = [polygon] });
        p.Execute("offset", new EditOperation { Action = "offset", Ids = [polygon.Id], Value = 1 }); Assert.Contains(new Point2(1, 1), p.Snapshot().Entities[^1].Points);
    }
}
