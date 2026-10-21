using System.Globalization;
namespace CloneCad.Core;

public sealed record DimensionLayout(List<(Point2 A, Point2 B)> Lines, string Label, Point2 LabelPosition, double LabelAngle,
    Point2? ArcCenter = null, double ArcRadius = 0, double ArcStart = 0, double ArcSweep = 0);

public static class Dimensions
{
    public static DimensionLayout Layout(Entity e, DimensionStyle style)
    {
        var a = e.Points[0]; var b = e.Points[1]; var delta = b - a;
        string Number(double n) => n.ToString("F" + style.Precision, CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        // F0 has no decimal separator; avoid trimming significant trailing zeroes.
        string Format(double n) => style.Precision == 0 ? n.ToString("F0", CultureInfo.InvariantCulture) : Number(n);
        var lines = new List<(Point2, Point2)>();
        if (e.DimensionType == "angular")
        {
            var first = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI; var second = Math.Atan2(e.Points[2].Y - a.Y, e.Points[2].X - a.X) * 180 / Math.PI;
            var sweep = (second - first + 360) % 360; if (sweep > 180) { first = second; sweep = 360 - sweep; }
            var radius = e.Points.Count > 3 ? a.Distance(e.Points[3]) : Math.Min(a.Distance(b), a.Distance(e.Points[2])) * .6;
            var mid = (first + sweep / 2) * Math.PI / 180; var label = a + new Point2(Math.Cos(mid), Math.Sin(mid)) * (radius + e.TextHeight);
            lines.Add((a, b)); lines.Add((a, e.Points[2]));
            return new(lines, Format(sweep) + "°", label, 0, a, radius, first, sweep);
        }
        if (e.DimensionType is "radius" or "diameter")
        {
            var unit = delta.Length > Geometry.Epsilon ? delta * (1 / delta.Length) : new Point2(1, 0);
            var start = e.DimensionType == "diameter" ? a - delta : a;
            lines.Add((start, b)); AddArrow(lines, b, unit * (-style.ArrowSize), style.ArrowSize);
            return new(lines, (e.DimensionType == "radius" ? "R" : "Ø") + Format(delta.Length * (e.DimensionType == "diameter" ? 2 : 1)), b + unit * e.TextHeight, 0);
        }
        var normal = delta.Length > Geometry.Epsilon ? new Point2(-delta.Y, delta.X) * (1 / delta.Length) : new Point2(0, 1);
        var at = e.Points.Count > 2 ? e.Points[2] : b + normal * 10;
        Point2 da, db; double measure;
        if (e.DimensionType == "linear")
        {
            var vertical = Math.Abs(Math.Sin(e.Rotation * Math.PI / 180)) > .707;
            da = vertical ? new(at.X, a.Y) : new(a.X, at.Y); db = vertical ? new(at.X, b.Y) : new(b.X, at.Y);
            measure = vertical ? Math.Abs(delta.Y) : Math.Abs(delta.X);
        }
        else { var offset = Geometry.Dot(at - b, normal); da = a + normal * offset; db = b + normal * offset; measure = delta.Length; }
        lines.Add((a, da)); lines.Add((b, db)); lines.Add((da, db));
        var dimensionDelta = db - da; var arrow = dimensionDelta.Length > Geometry.Epsilon ? dimensionDelta * (Math.Min(style.ArrowSize, dimensionDelta.Length / 5) / dimensionDelta.Length) : default;
        AddArrow(lines, da, arrow, style.ArrowSize); AddArrow(lines, db, arrow * (-1), style.ArrowSize);
        var labelAngle = Math.Atan2(dimensionDelta.Y, dimensionDelta.X) * 180 / Math.PI; if (labelAngle > 90 || labelAngle < -90) labelAngle += 180;
        return new(lines, Format(measure), (da + db) * .5 + normal * e.TextHeight * .5, labelAngle);
    }
    private static void AddArrow(List<(Point2, Point2)> lines, Point2 tip, Point2 direction, double size)
    { if (direction.Length < Geometry.Epsilon) return; var n = new Point2(-direction.Y, direction.X) * (.3 * size / direction.Length); lines.Add((tip, tip + direction + n)); lines.Add((tip, tip + direction - n)); }
}
