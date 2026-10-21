using CloneCad.Core;
using SkiaSharp;

namespace CloneCad.Formats;

public static class DrawingRenderer
{
    public static void Draw(SKCanvas canvas, Drawing drawing, double pixelScale, HashSet<Guid>? selected = null,
        SKColor? overrideColor = null, bool paper = false)
    {
        var paints = new Dictionary<(SKColor Color, float Width, string Type), SKPaint>();
        try
        {
            foreach (var e in drawing.Entities)
            {
                var layer = drawing.GetLayer(e.Layer); if (!layer.Visible) continue;
                var color = overrideColor ?? (selected?.Contains(e.Id) == true ? SKColors.DeepSkyBlue :
                    SKColor.TryParse(e.Color ?? layer.Color, out var c) ? c : SKColors.White);
                if (e.Invalid) color = SKColors.OrangeRed;
                var lineType = e.LineType ?? layer.LineType;
                var width = (float)(paper ? Math.Max(0.05, e.LineWeight ?? layer.LineWeight) : 1.2 / pixelScale);
                var key = (color, width, lineType);
                if (!paints.TryGetValue(key, out var paint))
                {
                    paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width }; paints.Add(key, paint);
                    if (!lineType.Equals("Continuous", StringComparison.OrdinalIgnoreCase))
                    { using var dash = SKPathEffect.CreateDash(lineType.Contains("Center", StringComparison.OrdinalIgnoreCase) ? [8, 2, 2, 2] : [6, 3], 0); paint.PathEffect = dash; }
                }
                DrawEntity(canvas, e, paint, pixelScale, drawing);
            }
        }
        finally { foreach (var paint in paints.Values) paint.Dispose(); }
    }
    private static void DrawEntity(SKCanvas c, Entity e, SKPaint p, double zoom, Drawing doc, int depth = 0)
    {
        if (depth > 16) return;
        switch (e.Kind)
        {
            case EntityKind.Line: c.DrawLine(P(e.Points[0]), P(e.Points[1]), p); break;
            case EntityKind.Polyline:
                using (var path = Path(e.Points, e.Closed)) c.DrawPath(path, p); break;
            case EntityKind.Circle: c.DrawCircle(P(e.Center), (float)e.Radius, p); break;
            case EntityKind.Arc:
                var r = (float)e.Radius; var x = (float)e.Center.X; var y = (float)e.Center.Y;
                var sweep = (e.EndAngle - e.StartAngle + 360) % 360;
                c.DrawArc(new SKRect(x - r, y - r, x + r, y + r), (float)e.StartAngle, (float)sweep, false, p); break;
            case EntityKind.Text:
                DrawText(c, e.Text, e.Center, e.TextHeight, e.Rotation, e.FontFamily, p.Color); break;
            case EntityKind.Dimension:
                if (e.Points.Count < 2) break;
                var layout = Dimensions.Layout(e, doc.DimStyle);
                foreach (var (start, end) in layout.Lines) c.DrawLine(P(start), P(end), p);
                if (layout.ArcCenter is { } ac)
                { var radius = (float)layout.ArcRadius; c.DrawArc(new((float)ac.X - radius, (float)ac.Y - radius, (float)ac.X + radius, (float)ac.Y + radius), (float)layout.ArcStart, (float)layout.ArcSweep, false, p); }
                DrawText(c, layout.Label, layout.LabelPosition, e.TextHeight, layout.LabelAngle, e.FontFamily, p.Color); break;
            case EntityKind.Hatch:
                if (e.Invalid || e.Points.Count < 3) break;
                using (var path = Path(e.Points, true))
                {
                    c.Save(); c.ClipPath(path);
                    if (e.HatchPattern == "SOLID")
                    { using var fill = new SKPaint { Color = p.Color.WithAlpha(100), Style = SKPaintStyle.Fill }; c.DrawPath(path, fill); }
                    else
                    {
                        var bounds = Geometry.Bounds(e); var span = bounds.MaxY - bounds.MinY; var step = Math.Max(e.HatchSpacing, (bounds.MaxX - bounds.MinX + span) / 2000);
                        for (double start = bounds.MinX - span; start <= bounds.MaxX; start += step)
                            c.DrawLine((float)start, (float)bounds.MinY, (float)(start + span), (float)bounds.MaxY, p);
                    }
                    c.Restore();
                }
                break;
            case EntityKind.Block:
                var block = doc.Blocks.FirstOrDefault(b => b.Id == e.BlockId); if (block == null) break;
                c.Save(); c.Translate(P(e.Center)); c.RotateDegrees((float)e.Rotation); c.Scale((float)e.Scale);
                foreach (var child in block.Entities) DrawEntity(c, child, p, zoom, doc, depth + 1);
                c.Restore(); break;
        }
    }
    public static SKPoint P(Point2 p) => new((float)p.X, (float)p.Y);
    public static SKPath Path(List<Point2> points, bool closed)
    {
        var path = new SKPath(); if (points.Count == 0) return path;
        path.MoveTo(P(points[0])); foreach (var p in points.Skip(1)) path.LineTo(P(p)); if (closed) path.Close(); return path;
    }
    private static void DrawText(SKCanvas canvas, string text, Point2 at, double height, double rotation, string family, SKColor color)
    {
        using var face = SKTypeface.FromFamilyName(family); using var font = new SKFont(face, (float)height);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.Save(); canvas.Translate(P(at)); canvas.RotateDegrees((float)rotation); canvas.Scale(1, -1);
        var lines = text.Replace("\\P", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++) canvas.DrawText(lines[i], 0, (float)(i * height * 1.4), font, paint);
        canvas.Restore();
    }
    public static Bounds2 Extents(Drawing d) => Bounds2.From(d.Entities.SelectMany(e => { var b = Geometry.Bounds(e, d); return new[] { new Point2(b.MinX, b.MinY), new Point2(b.MaxX, b.MaxY) }; }));
    public static byte[] PreviewPng(Drawing d, int width = 1200, int height = 800)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height)); var c = surface.Canvas; c.Clear(new SKColor(18, 27, 38));
        var b = Extents(d); var scale = Math.Min((width - 80) / Math.Max(1, b.MaxX - b.MinX), (height - 80) / Math.Max(1, b.MaxY - b.MinY));
        c.Translate(width / 2f, height / 2f); c.Scale((float)scale, (float)-scale); c.Translate((float)(-(b.MinX + b.MaxX) / 2), (float)(-(b.MinY + b.MaxY) / 2));
        Draw(c, d, scale); using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
    public static void ExportPdf(Drawing drawing, string path, PrintSettings settings, Bounds2? region = null)
    {
        var (w, h) = PaperSize(settings); const double ptsPerMm = 72 / 25.4;
        var temp = System.IO.Path.GetFullPath(path) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var pdf = SKDocument.CreatePdf(temp))
            {
                var canvas = pdf.BeginPage((float)(w * ptsPerMm), (float)(h * ptsPerMm)); DrawPaper(canvas, drawing, settings, region);
                pdf.EndPage(); pdf.Close();
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static (double Width, double Height) PaperSize(PrintSettings settings)
    {
        if (!double.IsFinite(settings.Scale) || settings.Scale <= 0) throw new ArgumentException("출력 축척은 0보다 커야 합니다.");
        var size = settings.Paper switch { "A4" => (210d, 297d), "A3" => (297d, 420d), "A2" => (420d, 594d), "A1" => (594d, 841d), "A0" => (841d, 1189d), _ => throw new ArgumentException("지원하지 않는 용지입니다.") };
        var w = settings.Landscape ? size.Item2 : size.Item1; var h = settings.Landscape ? size.Item1 : size.Item2;
        if (!double.IsFinite(settings.Margin) || settings.Margin < 0 || settings.Margin * 2 >= Math.Min(w, h)) throw new ArgumentException("출력 여백이 유효하지 않습니다.");
        return (w, h);
    }
    public static byte[] PrintPreview(Drawing drawing, PrintSettings settings, Bounds2? region = null)
    {
        var (w, h) = PaperSize(settings); const double dpi = 96; var width = (int)(w / 25.4 * dpi); var height = (int)(h / 25.4 * dpi);
        using var surface = SKSurface.Create(new SKImageInfo(width, height)); surface.Canvas.Scale((float)(dpi / 72)); DrawPaper(surface.Canvas, drawing, settings, region);
        using var image = surface.Snapshot(); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100); return encoded.ToArray();
    }
    private static void DrawPaper(SKCanvas canvas, Drawing drawing, PrintSettings settings, Bounds2? region)
    {
        var (w, h) = PaperSize(settings);
        var mmPerUnit = drawing.Units switch { "m" => 1000d, "cm" => 10, "inch" => 25.4, _ => 1 };
        const double ptsPerMm = 72 / 25.4; var scale = ptsPerMm * mmPerUnit / settings.Scale; var margin = settings.Margin * ptsPerMm;
        canvas.Clear(SKColors.White);
        canvas.ClipRect(new SKRect((float)margin, (float)margin, (float)(w * ptsPerMm - margin), (float)(h * ptsPerMm - margin)));
        var b = region ?? Extents(drawing); canvas.Translate((float)(w * ptsPerMm / 2), (float)(h * ptsPerMm / 2));
        canvas.Scale((float)scale, (float)-scale); canvas.Translate((float)(-(b.MinX + b.MaxX) / 2), (float)(-(b.MinY + b.MaxY) / 2));
        if (region != null) canvas.ClipRect(new((float)b.MinX, (float)b.MinY, (float)b.MaxX, (float)b.MaxY));
        // CAD lineweights are paper millimetres; compensate for model-space scaling.
        var printDoc = drawing.DeepCopy();
        printDoc.Layers = printDoc.Layers.Select(l => l with { LineWeight = l.LineWeight * settings.Scale / mmPerUnit }).ToList();
        printDoc.Entities = printDoc.Entities.Select(e => e with { LineWeight = e.LineWeight * settings.Scale / mmPerUnit }).ToList();
        Draw(canvas, printDoc, scale, overrideColor: settings.Monochrome ? SKColors.Black : null, paper: true);
    }
}
