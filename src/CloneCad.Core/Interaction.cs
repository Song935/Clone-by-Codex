namespace CloneCad.Core;

public enum SnapKind { Endpoint, Midpoint, Center, Intersection, Perpendicular, Tangent, Nearest }
public sealed record SnapResult(Point2 Point, SnapKind Kind);

public static class ObjectSnap
{
    public static SnapResult? Find(IEnumerable<Entity> source, Point2 cursor, double tolerance, Point2? anchor = null)
    {
        if (!cursor.IsFinite || !double.IsFinite(tolerance) || tolerance <= 0) return null;
        var candidates = source.Where(e => e.Kind is EntityKind.Line or EntityKind.Polyline or EntityKind.Circle or EntityKind.Arc).ToArray();
        var features = new List<SnapResult>();
        var nearest = new List<SnapResult>();
        foreach (var entity in candidates)
        {
            if (entity.Kind is EntityKind.Line or EntityKind.Polyline)
            {
                features.AddRange(entity.Points.Select(p => new SnapResult(p, SnapKind.Endpoint)));
                foreach (var (a, b) in Geometry.Segments(entity))
                {
                    features.Add(new((a + b) * .5, SnapKind.Midpoint));
                    nearest.Add(new(Geometry.Closest(cursor, a, b), SnapKind.Nearest));
                    if (anchor is { } from)
                    {
                        var perpendicular = Geometry.Closest(from, a, b);
                        if (Math.Abs(Geometry.Dot(from - perpendicular, b - a)) < Geometry.Epsilon)
                            features.Add(new(perpendicular, SnapKind.Perpendicular));
                    }
                }
            }
            else
            {
                features.Add(new(entity.Center, SnapKind.Center));
                if (entity.Kind == EntityKind.Arc)
                {
                    features.Add(new(Geometry.AtAngle(entity, entity.StartAngle), SnapKind.Endpoint));
                    features.Add(new(Geometry.AtAngle(entity, entity.EndAngle), SnapKind.Endpoint));
                }
                var delta = cursor - entity.Center;
                if (delta.Length > Geometry.Epsilon && Geometry.OnArc(entity, cursor))
                    nearest.Add(new(entity.Center + delta * (entity.Radius / delta.Length), SnapKind.Nearest));
                if (anchor is { } from)
                    features.AddRange(Geometry.Tangents(entity, from).Select(p => new SnapResult(p, SnapKind.Tangent)));
            }
        }
        var segments = candidates.SelectMany(Geometry.Segments).Take(100).ToArray();
        var circles = candidates.Where(e => e.Kind is EntityKind.Circle or EntityKind.Arc).Take(50).ToArray();
        for (var i = 0; i < segments.Length; i++)
            for (var j = i + 1; j < segments.Length; j++)
                if (Geometry.Intersect(segments[i].A, segments[i].B, segments[j].A, segments[j].B) is { } cross)
                    features.Add(new(cross, SnapKind.Intersection));
        foreach (var circle in circles)
            foreach (var segment in segments)
                features.AddRange(Geometry.LineCircle(segment.A, segment.B, circle).Select(p => new SnapResult(p, SnapKind.Intersection)));
        for (var i = 0; i < circles.Length; i++)
            for (var j = i + 1; j < circles.Length; j++)
                features.AddRange(Geometry.CircleCircle(circles[i], circles[j]).Select(p => new SnapResult(p, SnapKind.Intersection)));
        return Closest(features) ?? Closest(nearest);

        SnapResult? Closest(IEnumerable<SnapResult> values) => values.Where(s => s.Point.Distance(cursor) <= tolerance)
            .OrderBy(s => s.Point.Distance(cursor)).ThenBy(s => s.Kind).FirstOrDefault();
    }
}

public static class SelectionGeometry
{
    public static bool Crosses(Entity entity, Bounds2 region, Drawing drawing, int depth = 0)
    {
        if (!Geometry.Bounds(entity, drawing).Intersects(region)) return false;
        if (entity.Kind == EntityKind.Block)
        {
            if (depth >= 16) return false;
            var block = drawing.Blocks.FirstOrDefault(b => b.Id == entity.BlockId);
            return block?.Entities.Any(child => Crosses(Geometry.Transform(child,
                p => Geometry.Rotate(p * entity.Scale, default, entity.Rotation) + entity.Center,
                entity.Scale, entity.Rotation), region, drawing, depth + 1)) == true;
        }
        var corners = new[] { new Point2(region.MinX, region.MinY), new Point2(region.MaxX, region.MinY), new Point2(region.MaxX, region.MaxY), new Point2(region.MinX, region.MaxY) };
        var edges = corners.Select((p, i) => (A: p, B: corners[(i + 1) % 4])).ToArray();
        if (entity.Kind is EntityKind.Circle or EntityKind.Arc)
        {
            if (entity.Kind == EntityKind.Arc && (region.Contains(Geometry.AtAngle(entity, entity.StartAngle)) || region.Contains(Geometry.AtAngle(entity, entity.EndAngle)))) return true;
            if (entity.Kind == EntityKind.Circle && region.Contains(Geometry.AtAngle(entity, 0))) return true;
            return edges.Any(edge => Geometry.LineCircle(edge.A, edge.B, entity).Any());
        }
        if (entity.Kind == EntityKind.Text) return true; // Approximate text envelope, as used for display culling.
        if (entity.Kind == EntityKind.Dimension)
        {
            var layout = Dimensions.Layout(entity, drawing.DimStyle);
            if (layout.Lines.Any(s => SegmentCrosses(s.A, s.B))) return true;
            return layout.ArcCenter is { } center && Crosses(new Entity
            { Kind = EntityKind.Arc, Points = [center], Radius = layout.ArcRadius, StartAngle = layout.ArcStart, EndAngle = layout.ArcStart + layout.ArcSweep }, region, drawing);
        }
        if (Geometry.Segments(entity).Any(s => SegmentCrosses(s.A, s.B))) return true;
        return entity.Kind == EntityKind.Hatch && ContainsPolygon(entity.Points, corners[0]);

        bool SegmentCrosses(Point2 a, Point2 b) => region.Contains(a) || region.Contains(b) || edges.Any(edge => Geometry.Intersect(a, b, edge.A, edge.B) != null);
    }
    public static bool ContainsPolygon(IReadOnlyList<Point2> polygon, Point2 point)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
            if (Geometry.Closest(point, a, b).Distance(point) < Geometry.Epsilon) return true;
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}
