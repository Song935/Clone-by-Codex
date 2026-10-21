using ACadSharp;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using ACadSharp.Types.Units;
using CloneCad.Core;
using CadEntity = ACadSharp.Entities.Entity;
using ModelEntity = CloneCad.Core.Entity;
using E = ACadSharp.Entities;

namespace CloneCad.Formats;

public sealed record ImportReport(Drawing Drawing, Dictionary<string, int> Unsupported, List<string> Warnings);

public static class DxfExchange
{
    private static XYZ V(Point2 p) => new(p.X, p.Y, 0);
    private static Point2 P(XYZ p) => new(p.X, p.Y);
    public static ImportReport Import(string path)
    {
        var warnings = new List<string>();
        var source = DxfReader.Read(path);
        var drawing = new Drawing { Name = Path.GetFileNameWithoutExtension(path), Layers = [] };
        drawing.Units = (int)source.Header.InsUnits switch { 1 => "inch", 4 => "mm", 5 => "cm", 6 => "m", _ => "mm" };
        if ((int)source.Header.InsUnits is not (1 or 4 or 5 or 6)) warnings.Add("원본 단위를 지원하지 않거나 단위가 없습니다. mm로 해석했습니다. 확인하세요.");
        var substitutedFonts = source.TextStyles.Select(t => t.Filename).Where(f => !string.IsNullOrEmpty(f) && !new[] { "malgun.ttf", "arial.ttf", "arialuni.ttf" }.Contains(Path.GetFileName(f).ToLowerInvariant())).Distinct().ToArray();
        if (substitutedFonts.Length > 0) warnings.Add("글꼴 대체 (Malgun Gothic): " + string.Join(", ", substitutedFonts));
        foreach (var l in source.Layers)
            drawing.Layers.Add(new(l.Name, $"#{l.Color.R:X2}{l.Color.G:X2}{l.Color.B:X2}", l.IsOn, l.Flags.HasFlag(LayerFlags.Locked), l.LineType.Name, (short)l.LineWeight >= 0 ? (short)l.LineWeight / 100d : .25));
        foreach (var lineType in source.LineTypes.Where(l => !new[] { "Continuous", "ByLayer", "ByBlock", "Dashed", "Center" }.Contains(l.Name, StringComparer.OrdinalIgnoreCase)))
            warnings.Add($"선종류 '{lineType.Name}'은 기본 대시 표현으로 대체합니다.");
        if (drawing.Layers.All(l => l.Name != "0")) drawing.Layers.Add(new("0"));
        var skipped = new Dictionary<string, int>();
        foreach (var entity in source.Entities)
        {
            try
            {
                var result = FromCad(entity, drawing);
                if (result == null) { Count(entity.ObjectName); continue; }
                Geometry.Validate(result); drawing.Entities.Add(result);
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
            { Count(entity.ObjectName); warnings.Add($"{entity.ObjectName}: {ex.Message}"); }
        }
        return new(drawing, skipped, warnings);
        void Count(string name) => skipped[name] = skipped.GetValueOrDefault(name) + 1;
    }
    private static ModelEntity? FromCad(CadEntity entity, Drawing drawing, int depth = 0)
    {
        if (depth > 12) throw new NotSupportedException("블록 중첩 한도를 초과했습니다.");
        ModelEntity? result = entity switch
        {
            E.Line l when Math.Abs(l.StartPoint.Z) < Geometry.Epsilon && Math.Abs(l.EndPoint.Z) < Geometry.Epsilon => new() { Kind = EntityKind.Line, Points = [P(l.StartPoint), P(l.EndPoint)] },
            E.Arc a when Math.Abs(a.Center.Z) < Geometry.Epsilon && a.Normal.Z > .999999 => new() { Kind = EntityKind.Arc, Points = [P(a.Center)], Radius = a.Radius, StartAngle = a.StartAngle * 180 / Math.PI, EndAngle = a.EndAngle * 180 / Math.PI },
            E.Circle c when Math.Abs(c.Center.Z) < Geometry.Epsilon && c.Normal.Z > .999999 => new() { Kind = EntityKind.Circle, Points = [P(c.Center)], Radius = c.Radius },
            E.LwPolyline p when p.Vertices.All(v => Math.Abs(v.Bulge) < Geometry.Epsilon) && Math.Abs(p.Elevation) < Geometry.Epsilon && p.Normal.Z > .999999 => new() { Kind = EntityKind.Polyline, Points = p.Vertices.Select(v => new Point2(v.Location.X, v.Location.Y)).ToList(), Closed = p.IsClosed },
            E.TextEntity t => new() { Kind = EntityKind.Text, Points = [P(t.InsertPoint)], Text = t.Value, TextHeight = t.Height, Rotation = t.Rotation * 180 / Math.PI },
            E.MText t => new() { Kind = EntityKind.Text, Points = [P(t.InsertPoint)], Text = t.Value.Replace("\\P", "\n"), TextHeight = t.Height, Rotation = t.Rotation * 180 / Math.PI },
            E.DimensionLinear d => new() { Kind = EntityKind.Dimension, DimensionType = "linear", Points = [P(d.FirstPoint), P(d.SecondPoint), P(d.DefinitionPoint)], Rotation = d.Rotation * 180 / Math.PI, TextHeight = d.Style.TextHeight },
            E.DimensionAligned d => new() { Kind = EntityKind.Dimension, Points = [P(d.FirstPoint), P(d.SecondPoint), P(d.DefinitionPoint)], TextHeight = d.Style.TextHeight },
            E.DimensionRadius d => new() { Kind = EntityKind.Dimension, DimensionType = "radius", Points = [P(d.DefinitionPoint), P(d.AngleVertex)], TextHeight = d.Style.TextHeight },
            E.DimensionDiameter d => new() { Kind = EntityKind.Dimension, DimensionType = "diameter", Points = [P(d.Center), P(d.AngleVertex)], TextHeight = d.Style.TextHeight },
            E.DimensionAngular3Pt d => new() { Kind = EntityKind.Dimension, DimensionType = "angular", Points = [P(d.AngleVertex), P(d.FirstPoint), P(d.SecondPoint), P(d.DefinitionPoint)], TextHeight = d.Style.TextHeight },
            _ => null
        };
        if (entity is E.Hatch hatch)
        {
            if (hatch.Paths.Count != 1) throw new NotSupportedException("해치의 다중 경계는 아직 지원하지 않습니다.");
            var edges = hatch.Paths[0].Edges;
            List<Point2> points;
            if (edges.Count == 1 && edges[0] is E.Hatch.BoundaryPath.Polyline poly && !poly.HasBulge)
                points = poly.Vertices.Select(v => new Point2(v.X, v.Y)).ToList();
            else if (edges.All(e => e is E.Hatch.BoundaryPath.Line))
                points = edges.Cast<E.Hatch.BoundaryPath.Line>().Select(e => new Point2(e.Start.X, e.Start.Y)).ToList();
            else throw new NotSupportedException("곡선 해치 경계는 아직 지원하지 않습니다.");
            if (!hatch.IsSolid && hatch.Pattern.Name != "ANSI31") throw new NotSupportedException("현재 해치 패턴은 SOLID·ANSI31만 지원합니다.");
            var spacing = hatch.Pattern.Lines.Count > 0 ? Math.Sqrt(2) * Math.Abs(hatch.Pattern.Lines[0].LineOffset) : 10;
            result = new() { Kind = EntityKind.Hatch, Points = points, Closed = true, HatchPattern = hatch.IsSolid ? "SOLID" : "ANSI31", HatchSpacing = spacing > Geometry.Epsilon ? spacing : 10 };
        }
        if (entity is E.Insert insert)
        {
            if (insert.HasAttributes || insert.Block.Name.StartsWith("*U", StringComparison.OrdinalIgnoreCase) || Math.Abs(insert.XScale - insert.YScale) > Geometry.Epsilon || insert.RowCount > 1 || insert.ColumnCount > 1)
                throw new NotSupportedException("단순·균일 축척 블록만 지원합니다.");
            var name = insert.Block.Name; var block = drawing.Blocks.FirstOrDefault(b => b.Name == name);
            if (block == null)
            {
                var contents = insert.Block.Entities.Select(e => FromCad(e, drawing, depth + 1) ?? throw new NotSupportedException("블록 내부에 미지원 객체가 있습니다.")).ToList();
                block = new(Guid.NewGuid(), name, contents); drawing.Blocks.Add(block);
            }
            result = new() { Kind = EntityKind.Block, Points = [P(insert.InsertPoint)], BlockId = block.Id, Rotation = insert.Rotation * 180 / Math.PI, Scale = insert.XScale };
        }
        return result == null ? null : result with
        {
            Layer = entity.Layer.Name,
            Color = entity.Color.IsByLayer || entity.Color.IsByBlock ? null : $"#{entity.Color.R:X2}{entity.Color.G:X2}{entity.Color.B:X2}",
            LineType = entity.LineType.Name is "ByLayer" or "ByBlock" ? null : entity.LineType.Name,
            LineWeight = (short)entity.LineWeight >= 0 ? (short)entity.LineWeight / 100d : null
        };
    }
    public static void Export(Drawing drawing, string path)
    {
        var doc = new CadDocument(ACadVersion.AC1027);
        doc.Header.InsUnits = (UnitsType)(drawing.Units switch { "inch" => 1, "mm" => 4, "cm" => 5, "m" => 6, _ => 0 });
        foreach (var name in drawing.Layers.Select(l => l.LineType).Concat(drawing.Entities.Select(e => e.LineType)).Where(n => n != null).Distinct())
            if (!doc.LineTypes.Contains(name!))
            {
                var type = new LineType(name!); foreach (var length in name!.Contains("Center", StringComparison.OrdinalIgnoreCase) ? new[] { 8d, -2, 2, -2 } : new[] { 6d, -3 }) type.AddSegment(new LineType.Segment { Length = length }); doc.LineTypes.Add(type);
            }
        var blockCache = new Dictionary<Guid, BlockRecord>();
        var dimensionPictureNumber = 0;
        foreach (var layer in drawing.Layers)
        {
            var l = doc.Layers.TryGetValue(layer.Name, out var existing) ? existing : new ACadSharp.Tables.Layer(layer.Name);
            l.IsOn = layer.Visible; l.Flags = layer.Locked ? LayerFlags.Locked : LayerFlags.None;
            l.Color = ToColor(layer.Color); l.LineType = doc.LineTypes[layer.LineType]; l.LineWeight = (LineWeightType)(short)Math.Round(layer.LineWeight * 100);
            if (!doc.Layers.Contains(layer.Name)) doc.Layers.Add(l);
        }
        foreach (var e in drawing.Entities)
        {
            if (e.Invalid) throw new InvalidOperationException("유효하지 않은 치수·해치를 먼저 수정하세요.");
            var converted = Convert(e);
            converted.Layer = doc.Layers[e.Layer]; doc.Entities.Add(converted);
        }
        var temp = Path.GetFullPath(path) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { DxfWriter.Write(temp, doc, false); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        CadEntity Convert(ModelEntity entity)
        {
            CadEntity converted;
            if (entity.Kind == EntityKind.Block)
            {
                var definition = drawing.Blocks.First(b => b.Id == entity.BlockId);
                if (!blockCache.TryGetValue(definition.Id, out var block))
                {
                    block = new BlockRecord(definition.Name); blockCache.Add(definition.Id, block);
                    foreach (var child in definition.Entities) block.Entities.Add(Convert(child));
                }
                converted = new E.Insert(block) { InsertPoint = V(entity.Center), XScale = entity.Scale, YScale = entity.Scale, ZScale = entity.Scale, Rotation = entity.Rotation * Math.PI / 180 };
            }
            else converted = ToCad(entity, drawing);
            converted.Layer = doc.Layers[entity.Layer];
            if (entity.Color != null) converted.Color = ToColor(entity.Color);
            if (entity.LineType != null) converted.LineType = doc.LineTypes[entity.LineType];
            if (entity.LineWeight != null) converted.LineWeight = (LineWeightType)(short)Math.Round(entity.LineWeight.Value * 100);
            if (converted is E.MText text)
            {
                var name = "CloneCad_" + entity.FontFamily.Replace(' ', '_');
                if (!doc.TextStyles.TryGetValue(name, out var style)) { style = new TextStyle(name) { Filename = entity.FontFamily.Equals("Arial", StringComparison.OrdinalIgnoreCase) ? "arial.ttf" : "malgun.ttf" }; doc.TextStyles.Add(style); }
                text.Style = style;
            }
            if (converted is E.Dimension dimension)
            {
                var style = new ACadSharp.Tables.DimensionStyle("CloneCad_" + entity.TextHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)) { TextHeight = entity.TextHeight, ArrowSize = drawing.DimStyle.ArrowSize, DecimalPlaces = (short)drawing.DimStyle.Precision };
                if (!doc.DimensionStyles.TryGetValue(style.Name, out var existingStyle)) { doc.DimensionStyles.Add(style); existingStyle = style; }
                dimension.Style = existingStyle;
                // DXF DIMENSION requires a referenced picture block. A numeric dimension
                // without this block is removed by independent readers during audit.
                var layout = Dimensions.Layout(entity, drawing.DimStyle);
                var picture = new BlockRecord("*D" + ++dimensionPictureNumber);
                foreach (var (a, b) in layout.Lines)
                    picture.Entities.Add(Convert(new ModelEntity { Kind = EntityKind.Line, Layer = entity.Layer, Points = [a, b] }));
                picture.Entities.Add(Convert(new ModelEntity
                {
                    Kind = EntityKind.Text,
                    Layer = entity.Layer,
                    Points = [layout.LabelPosition],
                    Text = layout.Label,
                    TextHeight = entity.TextHeight,
                    FontFamily = entity.FontFamily,
                    Rotation = layout.LabelAngle
                }));
                if (layout.ArcCenter is { } center)
                    picture.Entities.Add(Convert(new ModelEntity
                    {
                        Kind = EntityKind.Arc,
                        Layer = entity.Layer,
                        Points = [center],
                        Radius = layout.ArcRadius,
                        StartAngle = layout.ArcStart,
                        EndAngle = layout.ArcStart + layout.ArcSweep
                    }));
                dimension.TextMiddlePoint = V(layout.LabelPosition);
                dimension.Block = picture;
            }
            return converted;
        }
    }
    private static ACadSharp.Color ToColor(string hex)
    { var color = SkiaSharp.SKColor.Parse(hex); return new(color.Red, color.Green, color.Blue); }
    private static CadEntity ToCad(ModelEntity e, Drawing drawing)
    {
        switch (e.Kind)
        {
            case EntityKind.Line: return new E.Line { StartPoint = V(e.Points[0]), EndPoint = V(e.Points[1]) };
            case EntityKind.Circle: return new E.Circle { Center = V(e.Center), Radius = e.Radius };
            case EntityKind.Arc: return new E.Arc { Center = V(e.Center), Radius = e.Radius, StartAngle = e.StartAngle * Math.PI / 180, EndAngle = e.EndAngle * Math.PI / 180 };
            case EntityKind.Polyline:
                var poly = new E.LwPolyline { IsClosed = e.Closed };
                foreach (var p in e.Points) poly.Vertices.Add(new E.LwPolyline.Vertex { Location = new XY(p.X, p.Y) }); return poly;
            case EntityKind.Text:
                return new E.MText { InsertPoint = V(e.Center), Value = e.Text.Replace("\n", "\\P"), Height = e.TextHeight, AlignmentPoint = new XYZ(Math.Cos(e.Rotation * Math.PI / 180), Math.Sin(e.Rotation * Math.PI / 180), 0) };
            case EntityKind.Dimension:
                if (e.DimensionType == "linear") return new E.DimensionLinear { FirstPoint = V(e.Points[0]), SecondPoint = V(e.Points[1]), DefinitionPoint = V(e.Points[2]), Rotation = e.Rotation * Math.PI / 180 };
                if (e.DimensionType == "radius") return new E.DimensionRadius { DefinitionPoint = V(e.Points[0]), AngleVertex = V(e.Points[1]) };
                if (e.DimensionType == "diameter") return new E.DimensionDiameter { DefinitionPoint = V(e.Points[0] * 2 - e.Points[1]), AngleVertex = V(e.Points[1]) };
                if (e.DimensionType == "angular") return new E.DimensionAngular3Pt { AngleVertex = V(e.Points[0]), FirstPoint = V(e.Points[1]), SecondPoint = V(e.Points[2]), DefinitionPoint = V(e.Points.Count > 3 ? e.Points[3] : e.Points[2]) };
                return new E.DimensionAligned(V(e.Points[0]), V(e.Points[1])) { DefinitionPoint = V(e.Points.Count > 2 ? e.Points[2] : e.Points[1] + new Point2(0, 10)) };
            case EntityKind.Hatch:
                var hatch = new E.Hatch { IsSolid = e.HatchPattern == "SOLID", PatternScale = 1 };
                hatch.Pattern = new E.HatchPattern(e.HatchPattern);
                if (!hatch.IsSolid) hatch.Pattern.Lines.Add(new E.HatchPattern.Line { Angle = Math.PI / 4, Offset = new XY(-e.HatchSpacing / 2, e.HatchSpacing / 2) });
                var boundary = new E.Hatch.BoundaryPath();
                foreach (var (a, b) in Geometry.Segments(e with { Closed = true })) boundary.Edges.Add(new E.Hatch.BoundaryPath.Line { Start = new XY(a.X, a.Y), End = new XY(b.X, b.Y) });
                hatch.Paths.Add(boundary); return hatch;
            case EntityKind.Block:
                var definition = drawing.Blocks.FirstOrDefault(b => b.Id == e.BlockId) ?? throw new InvalidOperationException("블록 정의가 없습니다.");
                var block = new BlockRecord(definition.Name);
                foreach (var child in definition.Entities) block.Entities.Add(ToCad(child, drawing));
                return new E.Insert(block) { InsertPoint = V(e.Center), XScale = e.Scale, YScale = e.Scale, ZScale = e.Scale, Rotation = e.Rotation * Math.PI / 180 };
            default: throw new NotSupportedException($"DXF 내보내기 미지원 객체: {e.Kind}");
        }
    }
}
