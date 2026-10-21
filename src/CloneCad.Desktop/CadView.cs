using System.Windows;
using System.Windows.Input;
using CloneCad.Core;
using CloneCad.Formats;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace CloneCad.Desktop;

public sealed class CadView : SKElement
{
    public DrawingSession Session { get; }
    public HashSet<Guid> Selection { get; } = [];
    public bool SnapEnabled { get; set; } = true;
    public bool OrthoEnabled { get; set; }
    public bool PolarEnabled { get; set; }
    public bool Picking { get; set; }
    public Point2? Anchor { get; set; }
    public event Action<Point2>? PointPicked;
    public event Action? SelectionChanged;
    public event Action<Point2>? CoordinateChanged;
    private double zoom = .8;
    private Point2 center = new(300, 200);
    private Point2 mouseWorld;
    private Point mousePixel;
    private Point? panStart, selectionStart;
    private Point2 panCenter;
    private Drawing? cachedDrawing;
    private Drawing Drawing => cachedDrawing?.Revision == Session.Revision ? cachedDrawing : cachedDrawing = Session.Snapshot();
    private double PixelRatio => ActualWidth > 0 ? CanvasSize.Width / ActualWidth : 1;
    public CadView(DrawingSession session)
    {
        Session = session; Focusable = true; Cursor = Cursors.Cross;
        PaintSurface += Paint;
        session.Changed += () => Dispatcher.BeginInvoke(() => { var ids = session.Snapshot().Entities.Select(e => e.Id).ToHashSet(); Selection.RemoveWhere(id => !ids.Contains(id)); InvalidateVisual(); });
        MouseDown += MouseDownHandler; MouseUp += MouseUpHandler; MouseMove += MouseMoveHandler; MouseWheel += Wheel;
        SizeChanged += (_, _) => InvalidateVisual();
    }
    public Point2 World(Point p) => new(center.X + (p.X - ActualWidth / 2) / zoom, center.Y - (p.Y - ActualHeight / 2) / zoom);
    public void Fit()
    {
        var d = Session.Snapshot(); if (d.Entities.Count == 0) { center = new(300, 200); zoom = .8; }
        else
        {
            var b = DrawingRenderer.Extents(d); center = new((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2);
            zoom = Math.Clamp(Math.Min((ActualWidth - 80) / Math.Max(1, b.MaxX - b.MinX), (ActualHeight - 80) / Math.Max(1, b.MaxY - b.MinY)), .00001, 10000);
        }
        InvalidateVisual();
    }
    public void SetPreviewFit()
    {
        var d = Session.Preview?.Result; if (d == null || d.Entities.Count == 0) return;
        var b = DrawingRenderer.Extents(d); center = new((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2);
        zoom = Math.Clamp(Math.Min((ActualWidth - 80) / Math.Max(1, b.MaxX - b.MinX), (ActualHeight - 80) / Math.Max(1, b.MaxY - b.MinY)), .00001, 10000); InvalidateVisual();
    }
    private void Paint(object? sender, SKPaintSurfaceEventArgs args)
    {
        var c = args.Surface.Canvas; c.Clear(new SKColor(15, 24, 35));
        var ratio = PixelRatio; c.Scale((float)ratio);
        c.Save(); c.Translate((float)(ActualWidth / 2), (float)(ActualHeight / 2)); c.Scale((float)zoom, (float)-zoom); c.Translate((float)-center.X, (float)-center.Y);
        var min = World(new(0, ActualHeight)); var max = World(new(ActualWidth, 0));
        var step = Math.Pow(10, Math.Ceiling(Math.Log10(35 / zoom)));
        using var grid = new SKPaint { Color = new SKColor(32, 47, 62), StrokeWidth = (float)(.6 / zoom) };
        for (var x = Math.Floor(min.X / step) * step; x <= max.X; x += step) c.DrawLine((float)x, (float)min.Y, (float)x, (float)max.Y, grid);
        for (var y = Math.Floor(min.Y / step) * step; y <= max.Y; y += step) c.DrawLine((float)min.X, (float)y, (float)max.X, (float)y, grid);
        using var axis = new SKPaint { Color = new SKColor(54, 85, 100), StrokeWidth = (float)(1 / zoom) };
        c.DrawLine((float)min.X, 0, (float)max.X, 0, axis); c.DrawLine(0, (float)min.Y, 0, (float)max.Y, axis);
        var doc = Drawing.DeepCopy();
        // Cull off-screen objects with the revision-indexed bounding-volume hierarchy.
        doc.Entities = Session.Query(new(min.X, min.Y, max.X, max.Y)).ToList();
        DrawingRenderer.Draw(c, doc, zoom, Selection);
        if (Session.Preview is { } preview)
        {
            var originals = Drawing.Entities.ToDictionary(e => e.Id);
            var changes = preview.Result.DeepCopy();
            changes.Entities = changes.Entities.Where(e => !originals.TryGetValue(e.Id, out var old) || !e.ContentEquals(old)).ToList();
            DrawingRenderer.Draw(c, changes, zoom, overrideColor: new SKColor(100, 230, 200, 200));
            var resultingIds = preview.Result.Entities.Select(e => e.Id).ToHashSet();
            var deleted = Drawing.DeepCopy(); deleted.Entities = deleted.Entities.Where(e => !resultingIds.Contains(e.Id)).ToList();
            DrawingRenderer.Draw(c, deleted, zoom, overrideColor: new SKColor(255, 100, 100, 200));
        }
        if (Picking && Anchor is { } anchor)
        {
            using var ghost = new SKPaint { Color = SKColors.Yellow.WithAlpha(160), StrokeWidth = (float)(1 / zoom), PathEffect = SKPathEffect.CreateDash([(float)(5 / zoom), (float)(5 / zoom)], 0) };
            c.DrawLine(DrawingRenderer.P(anchor), DrawingRenderer.P(mouseWorld), ghost);
        }
        c.Restore();
        using var cursor = new SKPaint { Color = new SKColor(137, 203, 220), StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        c.DrawLine((float)mousePixel.X - 13, (float)mousePixel.Y, (float)mousePixel.X + 13, (float)mousePixel.Y, cursor);
        c.DrawLine((float)mousePixel.X, (float)mousePixel.Y - 13, (float)mousePixel.X, (float)mousePixel.Y + 13, cursor);
        if (selectionStart is { } start)
        {
            using var rect = new SKPaint { Color = mousePixel.X >= start.X ? SKColors.DodgerBlue.WithAlpha(60) : SKColors.SpringGreen.WithAlpha(60), Style = SKPaintStyle.Fill };
            c.DrawRect(SKRect.Create((float)Math.Min(start.X, mousePixel.X), (float)Math.Min(start.Y, mousePixel.Y), (float)Math.Abs(start.X - mousePixel.X), (float)Math.Abs(start.Y - mousePixel.Y)), rect);
        }
    }
    private Point2 Snap(Point2 point)
    {
        if (OrthoEnabled && Anchor is { } a) point = Math.Abs(point.X - a.X) > Math.Abs(point.Y - a.Y) ? new(point.X, a.Y) : new(a.X, point.Y);
        if (!OrthoEnabled && PolarEnabled && Anchor is { } origin)
        {
            var d = point - origin; var angle = Math.Atan2(d.Y, d.X); var tracked = Math.Round(angle / (Math.PI / 12)) * (Math.PI / 12);
            if (Math.Abs(angle - tracked) < Math.PI / 60) point = origin + new Point2(Math.Cos(tracked), Math.Sin(tracked)) * d.Length;
        }
        if (!SnapEnabled) return point;
        var tol = 10 / zoom; var candidates = Session.Query(new Bounds2(point.X, point.Y, point.X, point.Y).Expand(tol));
        var points = new List<Point2>();
        foreach (var e in candidates)
        {
            if (e.Kind is EntityKind.Text or EntityKind.Dimension or EntityKind.Hatch) continue;
            points.AddRange(e.Points);
            foreach (var (p, q) in Geometry.Segments(e)) { points.Add((p + q) * .5); points.Add(Geometry.Closest(point, p, q)); if (Anchor is { } anchor) points.Add(Geometry.Closest(anchor, p, q)); }
            if (e.Kind is EntityKind.Circle or EntityKind.Arc)
            {
                points.Add(e.Center); var delta = point - e.Center;
                if (e.Kind == EntityKind.Arc) { points.Add(Geometry.AtAngle(e, e.StartAngle)); points.Add(Geometry.AtAngle(e, e.EndAngle)); }
                if (delta.Length > Geometry.Epsilon && Geometry.OnArc(e, point)) points.Add(e.Center + delta * (e.Radius / delta.Length));
                if (Anchor is { } tangentFrom) points.AddRange(Geometry.Tangents(e, tangentFrom));
            }
        }
        var segments = candidates.SelectMany(Geometry.Segments).Take(100).ToArray();
        for (var i = 0; i < segments.Length; i++) for (var j = i + 1; j < segments.Length; j++)
            if (Geometry.Intersect(segments[i].A, segments[i].B, segments[j].A, segments[j].B) is { } cross) points.Add(cross);
        var circles = candidates.Where(e => e.Kind is EntityKind.Circle or EntityKind.Arc).Take(50).ToArray();
        foreach (var circle in circles) foreach (var segment in segments) points.AddRange(Geometry.LineCircle(segment.A, segment.B, circle));
        for (var i = 0; i < circles.Length; i++) for (var j = i + 1; j < circles.Length; j++) points.AddRange(Geometry.CircleCircle(circles[i], circles[j]));
        return points.Where(p => p.Distance(point) <= tol).OrderBy(p => p.Distance(point)).FirstOrDefault(point);
    }
    private void MouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        Focus(); var p = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Middle) { panStart = p; panCenter = center; CaptureMouse(); return; }
        if (e.ChangedButton == MouseButton.Right) { PointPicked?.Invoke(new(double.NaN, double.NaN)); return; }
        if (e.ChangedButton != MouseButton.Left) return;
        if (Picking) PointPicked?.Invoke(Snap(World(p)));
        else { selectionStart = p; CaptureMouse(); }
    }
    private void MouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle) { panStart = null; ReleaseMouseCapture(); return; }
        if (e.ChangedButton != MouseButton.Left || selectionStart is not { } start) return;
        var end = e.GetPosition(this); var a = World(start); var b = World(end);
        var bounds = new Bounds2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if ((end - start).Length < 4)
        {
            var hit = Session.Query(new Bounds2(b.X, b.Y, b.X, b.Y).Expand(8 / zoom)).Where(x => !Drawing.GetLayer(x.Layer).Locked).OrderBy(x => Geometry.HitDistance(x, b, Drawing)).FirstOrDefault();
            if (hit != null && Geometry.HitDistance(hit, b, Drawing) < 10 / zoom) { if (shift) Selection.Remove(hit.Id); else Selection.Add(hit.Id); }
            else if (!shift) Selection.Clear();
        }
        else foreach (var entity in Session.Query(bounds))
            if (!Drawing.GetLayer(entity.Layer).Locked && (end.X < start.X || bounds.Contains(Geometry.Bounds(entity, Drawing)))) { if (shift) Selection.Remove(entity.Id); else Selection.Add(entity.Id); }
        selectionStart = null; ReleaseMouseCapture(); SelectionChanged?.Invoke(); InvalidateVisual();
    }
    private void MouseMoveHandler(object sender, MouseEventArgs e)
    {
        mousePixel = e.GetPosition(this);
        if (panStart is { } start) center = panCenter + new Point2(-(mousePixel.X - start.X) / zoom, (mousePixel.Y - start.Y) / zoom);
        mouseWorld = Picking ? Snap(World(mousePixel)) : World(mousePixel); CoordinateChanged?.Invoke(mouseWorld); InvalidateVisual();
    }
    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        var p = e.GetPosition(this); var before = World(p); zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), .00001, 100000);
        var after = World(p); center += before - after; InvalidateVisual();
    }
}
