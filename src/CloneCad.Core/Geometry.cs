namespace CloneCad.Core;

public readonly record struct Bounds2(double MinX, double MinY, double MaxX, double MaxY)
{
    public bool Intersects(Bounds2 b) => MinX <= b.MaxX && MaxX >= b.MinX && MinY <= b.MaxY && MaxY >= b.MinY;
    public bool Contains(Point2 p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
    public bool Contains(Bounds2 b) => Contains(new Point2(b.MinX, b.MinY)) && Contains(new Point2(b.MaxX, b.MaxY));
    public Bounds2 Expand(double n) => new(MinX - n, MinY - n, MaxX + n, MaxY + n);
    public static Bounds2 From(IEnumerable<Point2> points)
    {
        var a = points.ToArray();
        return a.Length == 0 ? new(0, 0, 0, 0) : new(a.Min(x => x.X), a.Min(x => x.Y), a.Max(x => x.X), a.Max(x => x.Y));
    }
}

public static class Geometry
{
    public const double Epsilon = 1e-8;
    public static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
    public static double Dot(Point2 a, Point2 b) => a.X * b.X + a.Y * b.Y;
    public static Point2 Rotate(Point2 p, Point2 origin, double degrees)
    {
        var a = degrees * Math.PI / 180; var d = p - origin;
        return origin + new Point2(d.X * Math.Cos(a) - d.Y * Math.Sin(a), d.X * Math.Sin(a) + d.Y * Math.Cos(a));
    }
    public static IEnumerable<(Point2 A, Point2 B)> Segments(Entity e)
    {
        for (var i = 1; i < e.Points.Count; i++) yield return (e.Points[i - 1], e.Points[i]);
        if (e.Closed && e.Points.Count > 2) yield return (e.Points[^1], e.Points[0]);
    }
    public static Point2 Closest(Point2 p, Point2 a, Point2 b)
    {
        var d = b - a; var len = Dot(d, d);
        return len < Epsilon * Epsilon ? a : a + d * Math.Clamp(Dot(p - a, d) / len, 0, 1);
    }
    public static Point2? Intersect(Point2 a, Point2 b, Point2 c, Point2 d, bool infinite = false)
    {
        var r = b - a; var s = d - c; var denom = Cross(r, s);
        if (Math.Abs(denom) < Epsilon) return null;
        var t = Cross(c - a, s) / denom; var u = Cross(c - a, r) / denom;
        return infinite || (t >= -Epsilon && t <= 1 + Epsilon && u >= -Epsilon && u <= 1 + Epsilon) ? a + r * t : null;
    }
    public static bool OnArc(Entity e, Point2 point)
    {
        if (e.Kind != EntityKind.Arc) return true;
        var angle = Math.Atan2(point.Y - e.Center.Y, point.X - e.Center.X) * 180 / Math.PI;
        return (angle - e.StartAngle + 720) % 360 <= (e.EndAngle - e.StartAngle + 720) % 360 + Epsilon;
    }
    public static Point2 AtAngle(Entity circle, double degrees) => circle.Center + new Point2(Math.Cos(degrees * Math.PI / 180), Math.Sin(degrees * Math.PI / 180)) * circle.Radius;
    public static IEnumerable<Point2> LineCircle(Point2 a, Point2 b, Entity circle)
    {
        var d = b - a; var f = a - circle.Center; var aa = Dot(d, d); if (aa < Epsilon) yield break;
        var bb = 2 * Dot(f, d); var cc = Dot(f, f) - circle.Radius * circle.Radius; var discriminant = bb * bb - 4 * aa * cc; if (discriminant < -Epsilon) yield break;
        var sqrt = Math.Sqrt(Math.Max(0, discriminant));
        foreach (var t in new[] { (-bb - sqrt) / (2 * aa), (-bb + sqrt) / (2 * aa) }) if (t >= -Epsilon && t <= 1 + Epsilon && OnArc(circle, a + d * t)) yield return a + d * t;
    }
    public static IEnumerable<Point2> CircleCircle(Entity a, Entity b)
    {
        var d = a.Center.Distance(b.Center); if (d < Epsilon || d > a.Radius + b.Radius + Epsilon || d < Math.Abs(a.Radius - b.Radius) - Epsilon) yield break;
        var x = (a.Radius * a.Radius - b.Radius * b.Radius + d * d) / (2 * d); var h = Math.Sqrt(Math.Max(0, a.Radius * a.Radius - x * x));
        var unit = (b.Center - a.Center) * (1 / d); var middle = a.Center + unit * x; var n = new Point2(-unit.Y, unit.X) * h;
        foreach (var point in new[] { middle + n, middle - n }) if (OnArc(a, point) && OnArc(b, point)) yield return point;
    }
    public static IEnumerable<Point2> Tangents(Entity circle, Point2 from)
    {
        var d = from.Distance(circle.Center); if (d <= circle.Radius + Epsilon) yield break;
        var a = Math.Atan2(from.Y - circle.Center.Y, from.X - circle.Center.X); var angle = Math.Acos(circle.Radius / d);
        foreach (var t in new[] { a - angle, a + angle }) { var p = AtAngle(circle, t * 180 / Math.PI); if (OnArc(circle, p)) yield return p; }
    }
    public static Bounds2 Bounds(Entity e, Drawing? drawing = null, int depth = 0)
    {
        if (e.Kind == EntityKind.Block && drawing != null && depth < 16)
        {
            var block = drawing.Blocks.FirstOrDefault(b => b.Id == e.BlockId);
            if (block != null) return Bounds2.From(block.Entities.SelectMany(child =>
            {
                var b = Bounds(child, drawing, depth + 1);
                return new[] { new Point2(b.MinX, b.MinY), new Point2(b.MinX, b.MaxY), new Point2(b.MaxX, b.MinY), new Point2(b.MaxX, b.MaxY) }
                    .Select(p => Rotate(p * e.Scale, default, e.Rotation) + e.Center);
            }));
        }
        if (e.Kind is EntityKind.Circle or EntityKind.Arc) return new(e.Center.X - e.Radius, e.Center.Y - e.Radius, e.Center.X + e.Radius, e.Center.Y + e.Radius);
        if (e.Kind == EntityKind.Text)
        {
            var lines = e.Text.Replace("\\P", "\n").Split('\n'); var width = lines.Max(l => l.Length) * e.TextHeight;
            var height = lines.Length * e.TextHeight * 1.4;
            return Bounds2.From(new[] { e.Center, e.Center + new Point2(width, 0), e.Center + new Point2(width, height), e.Center + new Point2(0, height) }.Select(p => Rotate(p, e.Center, e.Rotation)));
        }
        return Bounds2.From(e.Points);
    }
    public static double HitDistance(Entity e, Point2 p, Drawing? drawing = null, int depth = 0)
    {
        if (e.Kind == EntityKind.Block && drawing != null && depth < 16)
        {
            var block = drawing.Blocks.FirstOrDefault(b => b.Id == e.BlockId);
            var local = Rotate(p - e.Center, default, -e.Rotation) * (1 / e.Scale);
            return block?.Entities.Select(child => HitDistance(child, local, drawing, depth + 1) * Math.Abs(e.Scale)).DefaultIfEmpty(double.MaxValue).Min() ?? double.MaxValue;
        }
        if (e.Kind == EntityKind.Text) return Bounds(e).Contains(p) ? 0 : double.MaxValue;
        if (e.Kind is EntityKind.Circle or EntityKind.Arc)
            return OnArc(e, p) ? Math.Abs(p.Distance(e.Center) - e.Radius) : Math.Min(p.Distance(AtAngle(e, e.StartAngle)), p.Distance(AtAngle(e, e.EndAngle)));
        return Segments(e).Select(s => p.Distance(Closest(p, s.A, s.B))).DefaultIfEmpty(p.Distance(e.Center)).Min();
    }
    public static Entity Transform(Entity e, Func<Point2, Point2> transform, double scale = 1, double rotate = 0) => e with
    {
        Points = e.Points.Select(transform).ToList(),
        Radius = e.Radius * Math.Abs(scale),
        TextHeight = e.TextHeight * Math.Abs(scale),
        Scale = e.Scale * scale,
        Rotation = e.Rotation + rotate,
        StartAngle = e.StartAngle + rotate,
        EndAngle = e.EndAngle + rotate
    };
    public static void Validate(Entity e)
    {
        if (e.Points == null || e.References == null || e.Text == null || string.IsNullOrWhiteSpace(e.Layer)) throw new ArgumentException("객체 필수 속성이 비어 있습니다.");
        if (!Enum.IsDefined(e.Kind) || e.Id == Guid.Empty) throw new ArgumentException("객체 종류·ID가 유효하지 않습니다.");
        if (e.Points.Count == 0 || e.Points.Any(p => !p.IsFinite)) throw new ArgumentException("유효한 좌표가 필요합니다.");
        if (!double.IsFinite(e.Radius) || !double.IsFinite(e.TextHeight) || !double.IsFinite(e.Rotation) ||
            !double.IsFinite(e.Scale) || !double.IsFinite(e.StartAngle) || !double.IsFinite(e.EndAngle))
            throw new ArgumentException("유한한 수치가 필요합니다.");
        if (e.Kind is EntityKind.Circle or EntityKind.Arc && e.Radius <= Epsilon) throw new ArgumentException("반지름은 0보다 커야 합니다.");
        if (e.Kind == EntityKind.Line && (e.Points.Count != 2 || e.Points[0].Distance(e.Points[1]) < Epsilon))
            throw new ArgumentException("선에는 서로 다른 두 점이 필요합니다.");
        if (e.Kind == EntityKind.Polyline && e.Points.Count < (e.Closed ? 3 : 2)) throw new ArgumentException("폴리선 점이 부족합니다.");
        if (e.Kind is EntityKind.Polyline or EntityKind.Hatch)
        {
            if (Segments(e).Any(s => s.A.Distance(s.B) < Epsilon)) throw new ArgumentException("중복된 꼭짓점·영길이 선분입니다.");
            if ((e.Closed || e.Kind == EntityKind.Hatch) && Math.Abs(Segments(e with { Closed = true }).Sum(s => Cross(s.A, s.B))) < Epsilon) throw new ArgumentException("닫힌 경계의 면적이 0입니다.");
        }
        if (e.Kind == EntityKind.Dimension && e.Points.Count < 2) throw new ArgumentException("치수 측정점이 부족합니다.");
        if (e.Kind == EntityKind.Dimension && (e.DimensionType is not ("aligned" or "linear" or "angular" or "radius" or "diameter") || e.DimensionType == "angular" && e.Points.Count < 3)) throw new ArgumentException("지원하지 않는 치수 종류·측정점입니다.");
        if (e.Kind == EntityKind.Hatch && (e.Points.Count < 3 || !double.IsFinite(e.HatchSpacing) || e.HatchSpacing <= 0)) throw new ArgumentException("해치 경계·간격이 유효하지 않습니다.");
        if (e.LineWeight is { } weight && (!double.IsFinite(weight) || weight < 0)) throw new ArgumentException("선 두께가 유효하지 않습니다.");
        if (e.Text.Length > 100_000 || e.Points.Count > 100_000) throw new ArgumentException("객체 데이터가 너무 큽니다.");
        if (e.TextHeight <= 0 || Math.Abs(e.Scale) < Epsilon) throw new ArgumentException("문자 높이·축척이 유효하지 않습니다.");
    }
}

public static class DrawingValidation
{
    public static void Validate(Drawing d)
    {
        if (d.Entities == null || d.Layers == null || d.Blocks == null || d.Print == null || d.DimStyle == null) throw new ArgumentException("도면 필수 데이터가 비어 있습니다.");
        if (d.Units is not ("mm" or "cm" or "m" or "inch")) throw new ArgumentException("지원하지 않는 도면 단위입니다.");
        if (d.Entities.Count > 100_000) throw new ArgumentException("현재 도면당 객체 한도는 100,000개입니다.");
        if (d.Entities.Any(e => e == null) || d.Layers.Any(l => l == null || string.IsNullOrWhiteSpace(l.Name))) throw new ArgumentException("도면에 빈 객체·레이어가 있습니다.");
        if (d.Layers.Count == 0 || d.Layers.Select(l => l.Name).Distinct().Count() != d.Layers.Count) throw new ArgumentException("레이어 이름이 중복됐거나 레이어가 없습니다.");
        if (d.Entities.Select(e => e.Id).Distinct().Count() != d.Entities.Count) throw new ArgumentException("객체 ID가 중복됐습니다.");
        foreach (var e in d.Entities) { Geometry.Validate(e); d.GetLayer(e.Layer); }
        var definitions = d.Blocks.ToDictionary(b => b.Id);
        foreach (var block in d.Blocks) CheckBlock(block.Id, []);
        foreach (var e in d.Entities.Where(e => e.Kind == EntityKind.Block))
            if (e.BlockId == null || !definitions.ContainsKey(e.BlockId.Value)) throw new ArgumentException("블록 정의가 없습니다.");
        void CheckBlock(Guid id, HashSet<Guid> path)
        {
            if (!path.Add(id) || path.Count > 16) throw new ArgumentException("블록 순환 참조·중첩 한도 오류입니다.");
            if (!definitions.TryGetValue(id, out var block)) throw new ArgumentException("블록 정의가 없습니다.");
            foreach (var child in block.Entities)
            {
                Geometry.Validate(child); d.GetLayer(child.Layer);
                if (child.Kind == EntityKind.Block) CheckBlock(child.BlockId ?? Guid.Empty, new(path));
            }
        }
    }
}

/// <summary>Immutable bounding-volume hierarchy. Rebuilt only when document revision changes.</summary>
public sealed class SpatialIndex
{
    private sealed record Node(Bounds2 Bounds, Node? Left, Node? Right, Entity[] Items);
    private readonly Node? root;
    private readonly Func<Entity, Bounds2> boundsOf;
    public SpatialIndex(IEnumerable<Entity> entities, Func<Entity, Bounds2>? bounds = null)
    { boundsOf = bounds ?? (e => Geometry.Bounds(e)); root = Build(entities.ToArray(), 0); }
    private Node? Build(Entity[] items, int depth)
    {
        if (items.Length == 0) return null;
        var b = Bounds2.From(items.SelectMany(e => { var x = boundsOf(e); return new[] { new Point2(x.MinX, x.MinY), new Point2(x.MaxX, x.MaxY) }; }));
        if (items.Length <= 16) return new(b, null, null, items);
        var sorted = items.OrderBy(e => depth % 2 == 0 ? boundsOf(e).MinX : boundsOf(e).MinY).ToArray();
        return new(b, Build(sorted[..(sorted.Length / 2)], depth + 1), Build(sorted[(sorted.Length / 2)..], depth + 1), []);
    }
    public IEnumerable<Entity> Query(Bounds2 bounds) => Query(root, bounds);
    private IEnumerable<Entity> Query(Node? n, Bounds2 b)
    {
        if (n == null || !n.Bounds.Intersects(b)) yield break;
        foreach (var item in n.Items) if (boundsOf(item).Intersects(b)) yield return item;
        foreach (var item in Query(n.Left, b)) yield return item;
        foreach (var item in Query(n.Right, b)) yield return item;
    }
}
