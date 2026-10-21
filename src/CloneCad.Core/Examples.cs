namespace CloneCad.Core;

public static class Examples
{
    public static Drawing StructuralDetail()
    {
        var outline = new Entity
        {
            Kind = EntityKind.Polyline,
            Layer = "콘크리트",
            Closed = true,
            Points = [new(0, 0), new(1200, 0), new(1200, 600), new(0, 600)]
        };
        var dimensionBase = new Entity { Kind = EntityKind.Line, Layer = "외곽선", Points = [new(0, 0), new(1200, 0)] };
        var doc = new Drawing
        {
            Name = "검증 · 콘크리트 기초 단면",
            Print = new("A3", true, 10),
            Layers = [new("0"), new("콘크리트", "#A5BDC9"), new("철근", "#F5BA68", LineWeight: .5), new("치수", "#7DD3C8"), new("문자", "#E1E9EE"), new("외곽선", "#D7E5EF")],
            Entities = [outline, dimensionBase,
                new() { Kind = EntityKind.Hatch, Layer = "콘크리트", Points = [.. outline.Points], Closed = true, References = [outline.Id], HatchSpacing = 35 },
                new() { Kind = EntityKind.Polyline, Layer = "철근", Closed = true, Points = [new(60, 60), new(1140, 60), new(1140, 540), new(60, 540)] },
                new() { Kind = EntityKind.Dimension, Layer = "치수", Points = [new(0, 0), new(1200, 0), new(1200, -100)], References = [dimensionBase.Id], TextHeight = 22 },
                new() { Kind = EntityKind.Dimension, Layer = "치수", Points = [new(1200, 0), new(1200, 600), new(1320, 600)], TextHeight = 22 },
                new() { Kind = EntityKind.Text, Layer = "문자", Points = [new(0, 730)], Text = "콘크리트 기초 단면 상세도", TextHeight = 38 },
                new() { Kind = EntityKind.Text, Layer = "문자", Points = [new(0, -210)], Text = "단위: mm  /  피복 60  /  검증용 합성 도면", TextHeight = 20 }]
        };
        foreach (var x in new[] { 120d, 360, 600, 840, 1080 })
        {
            doc.Entities.Add(new() { Kind = EntityKind.Circle, Layer = "철근", Points = [new(x, 120)], Radius = 16 });
            doc.Entities.Add(new() { Kind = EntityKind.Circle, Layer = "철근", Points = [new(x, 480)], Radius = 16 });
        }
        return doc;
    }
}
