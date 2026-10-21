namespace CloneCad.Core;

public sealed record EditOperation
{
    public string Action { get; init; } = "create";
    public Entity? Entity { get; init; }
    public List<Guid> Ids { get; init; } = [];
    public Point2 Vector { get; init; }
    public Point2 Origin { get; init; }
    public Point2 End { get; init; }
    public double Value { get; init; }
    public int Count { get; init; } = 1;
    public int Rows { get; init; } = 1;
    public Point2 RowVector { get; init; }
    public string? Name { get; init; }
    public Layer? Layer { get; init; }
    public PrintSettings? Print { get; init; }
    public DimensionStyle? DimensionStyle { get; init; }
}
public sealed record ChangeSet(Guid Id, Guid DocumentId, long BaseRevision, string Summary,
    List<EditOperation> Operations, Drawing Result, DateTimeOffset Created);

public static class Editor
{
    public static void Apply(Drawing doc, EditOperation op)
    {
        if (string.IsNullOrWhiteSpace(op.Action)) throw new ArgumentException("편집 연산 이름이 필요합니다.");
        var action = op.Action.ToLowerInvariant();
        if (OperationExtensions.TryApply(doc, op)) return;
        if (action == "print_settings")
        { doc.Print = op.Print ?? throw new ArgumentException("출력 설정이 필요합니다."); return; }
        if (action == "dimstyle")
        {
            var style = op.DimensionStyle ?? throw new ArgumentException("치수 스타일이 필요합니다.");
            if (!double.IsFinite(style.TextHeight) || style.TextHeight <= 0 || !double.IsFinite(style.ArrowSize) || style.ArrowSize <= 0 || style.Precision is < 0 or > 8) throw new ArgumentException("치수 스타일 값이 유효하지 않습니다.");
            doc.DimStyle = style;
            doc.Entities = doc.Entities.Select(e => e.Kind == EntityKind.Dimension ? e with { TextHeight = style.TextHeight } : e).ToList(); return;
        }
        if (action == "units")
        {
            if (op.Name is not ("mm" or "cm" or "m" or "inch")) throw new ArgumentException("지원하지 않는 단위입니다.");
            doc.Units = op.Name; return;
        }
        if (action == "create")
        {
            if (op.Entity == null) throw new ArgumentException("객체가 필요합니다.");
            Geometry.Validate(op.Entity); var entity = op.Entity.DeepCopy();
            if (doc.Entities.Any(x => x.Id == entity.Id)) throw new ArgumentException("중복 객체 ID입니다.");
            if (doc.GetLayer(entity.Layer).Locked) throw new InvalidOperationException("잠긴 레이어입니다.");
            doc.Entities.Add(entity); return;
        }
        if (action == "layer")
        {
            var layer = op.Layer ?? throw new ArgumentException("레이어가 필요합니다.");
            if (string.IsNullOrWhiteSpace(layer.Name) || !double.IsFinite(layer.LineWeight) || layer.LineWeight < 0) throw new ArgumentException("레이어 값이 유효하지 않습니다.");
            var index = doc.Layers.FindIndex(x => x.Name == layer.Name);
            if (index < 0) doc.Layers.Add(layer); else doc.Layers[index] = layer;
            return;
        }
        var ids = op.Ids.Distinct().ToArray();
        if (ids.Length == 0) throw new ArgumentException("대상 객체를 선택하세요.");
        var entities = ids.Select(id => doc.Entities.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException($"객체가 없습니다: {id}")).ToArray();
        if (entities.Any(e => doc.GetLayer(e.Layer).Locked)) throw new InvalidOperationException("잠긴 레이어 객체는 편집할 수 없습니다.");
        if (!double.IsFinite(op.Value) || !op.Vector.IsFinite || !op.Origin.IsFinite || !op.End.IsFinite) throw new ArgumentException("유효한 수치를 입력하세요.");
        if (action is "trim" or "extend" or "fillet" or "chamfer")
        {
            EditCorner(doc, entities, op with { Action = action }); return;
        }
        if (action == "join")
        {
            var chains = entities.Select(e => e.Kind is EntityKind.Line or EntityKind.Polyline && !e.Closed ? new List<Point2>(e.Points) : throw new NotSupportedException("JOIN은 열린 선·폴리선만 지원합니다.")).ToList();
            var points = chains[0]; chains.RemoveAt(0);
            while (chains.Count > 0)
            {
                var next = chains.FindIndex(c => c[0].Distance(points[^1]) < Geometry.Epsilon || c[^1].Distance(points[^1]) < Geometry.Epsilon);
                if (next < 0) { points.Reverse(); next = chains.FindIndex(c => c[0].Distance(points[^1]) < Geometry.Epsilon || c[^1].Distance(points[^1]) < Geometry.Epsilon); }
                if (next < 0) throw new ArgumentException("선의 끝점이 연결되어 있지 않습니다.");
                var chain = chains[next]; if (chain[^1].Distance(points[^1]) < Geometry.Epsilon) chain.Reverse();
                points.AddRange(chain.Skip(1)); chains.RemoveAt(next);
            }
            var closed = points.Count > 3 && points[0].Distance(points[^1]) < Geometry.Epsilon;
            if (closed) points.RemoveAt(points.Count - 1);
            doc.Entities.RemoveAll(e => ids.Contains(e.Id));
            doc.Entities.Add(new() { Kind = EntityKind.Polyline, Points = points, Closed = closed, Layer = entities[0].Layer }); return;
        }
        if (action == "block")
        {
            if (string.IsNullOrWhiteSpace(op.Name)) throw new ArgumentException("블록 이름이 필요합니다.");
            var definition = doc.Blocks.FirstOrDefault(b => b.Name == op.Name);
            var blockId = definition?.Id ?? Guid.NewGuid();
            doc.Blocks.RemoveAll(b => b.Id == blockId);
            doc.Blocks.Add(new(blockId, op.Name, entities.Select(e => Geometry.Transform(e, p => p - op.Origin)).ToList()));
            doc.Entities.RemoveAll(e => ids.Contains(e.Id));
            doc.Entities.Add(new() { Kind = EntityKind.Block, BlockId = blockId, Points = [op.Origin], Layer = entities[0].Layer }); return;
        }
        foreach (var e in entities)
        {
            var index = doc.Entities.FindIndex(x => x.Id == e.Id);
            Entity next = e;
            switch (action)
            {
                case "delete": doc.Entities.RemoveAt(index); continue;
                case "replace":
                    next = (op.Entity ?? throw new ArgumentException("교체 객체가 필요합니다.")) with { Id = e.Id };
                    if (doc.GetLayer(next.Layer).Locked) throw new InvalidOperationException("대상 레이어가 잠겼습니다.");
                    break;
                case "move": next = Geometry.Transform(e, p => p + op.Vector); break;
                case "stretch":
                    if (e.Kind is not (EntityKind.Line or EntityKind.Polyline)) throw new NotSupportedException("STRETCH는 선·폴리선만 지원합니다.");
                    var region = new Bounds2(Math.Min(op.Origin.X, op.End.X), Math.Min(op.Origin.Y, op.End.Y), Math.Max(op.Origin.X, op.End.X), Math.Max(op.Origin.Y, op.End.Y));
                    next = e with { Points = e.Points.Select(p => region.Contains(p) ? p + op.Vector : p).ToList() }; break;
                case "copy": doc.Entities.Add(Geometry.Transform(e, p => p + op.Vector) with { Id = Guid.NewGuid() }); continue;
                case "rotate": next = Geometry.Transform(e, p => Geometry.Rotate(p, op.Origin, op.Value), rotate: op.Value); break;
                case "scale":
                    if (op.Value <= Geometry.Epsilon) throw new ArgumentException("축척은 0보다 커야 합니다.");
                    next = Geometry.Transform(e, p => op.Origin + (p - op.Origin) * op.Value, op.Value); break;
                case "mirror":
                    if (e.Kind == EntityKind.Block) throw new NotSupportedException("블록 대칭은 분해 후 수행하세요.");
                    var axis = op.End - op.Origin;
                    if (axis.Length < Geometry.Epsilon) throw new ArgumentException("대칭축이 너무 짧습니다.");
                    Point2 Reflect(Point2 point) => op.Origin + axis * (2 * Geometry.Dot(point - op.Origin, axis) / Geometry.Dot(axis, axis)) - (point - op.Origin);
                    next = Geometry.Transform(e, Reflect);
                    if (e.Kind == EntityKind.Arc)
                    {
                        var start = Reflect(Geometry.AtAngle(e, e.EndAngle)) - next.Center; var end = Reflect(Geometry.AtAngle(e, e.StartAngle)) - next.Center;
                        next = next with { StartAngle = Math.Atan2(start.Y, start.X) * 180 / Math.PI, EndAngle = Math.Atan2(end.Y, end.X) * 180 / Math.PI };
                    }
                    if (e.Kind == EntityKind.Text) next = next with { Rotation = 2 * Math.Atan2(axis.Y, axis.X) * 180 / Math.PI - e.Rotation };
                    break;
                case "offset":
                    if (e.Kind == EntityKind.Circle)
                    {
                        next = e with { Id = Guid.NewGuid(), Radius = e.Radius + op.Value }; Geometry.Validate(next); doc.Entities.Add(next); continue;
                    }
                    if (e.Kind == EntityKind.Polyline)
                    {
                        var segments = Geometry.Segments(e).ToArray(); var shifted = segments.Select(s =>
                        { var direction = s.B - s.A; if (direction.Length < Geometry.Epsilon) throw new ArgumentException("중복 꼭짓점을 제거하세요."); var normal = new Point2(-direction.Y, direction.X) * (op.Value / direction.Length); return (A: s.A + normal, B: s.B + normal); }).ToArray();
                        var pts = new List<Point2>();
                        for (var i = 0; i < e.Points.Count; i++)
                        {
                            if (!e.Closed && i == 0) { pts.Add(shifted[0].A); continue; }
                            if (!e.Closed && i == e.Points.Count - 1) { pts.Add(shifted[^1].B); continue; }
                            var previous = shifted[(i - 1 + shifted.Length) % shifted.Length]; var current = shifted[i % shifted.Length];
                            var intersection = Geometry.Intersect(previous.A, previous.B, current.A, current.B, true) ?? current.A;
                            if (intersection.Distance(e.Points[i]) > Math.Max(1, Math.Abs(op.Value)) * 1000) throw new ArgumentException("OFFSET 모서리가 불안정합니다."); pts.Add(intersection);
                        }
                        var offset = e with { Id = Guid.NewGuid(), Points = pts }; Geometry.Validate(offset); doc.Entities.Add(offset); continue;
                    }
                    if (e.Kind != EntityKind.Line) throw new NotSupportedException("OFFSET은 선·폴리선·원을 지원합니다.");
                    var d = e.Points[1] - e.Points[0]; var n = new Point2(-d.Y, d.X) * (op.Value / d.Length);
                    doc.Entities.Add(Geometry.Transform(e, p => p + n) with { Id = Guid.NewGuid() }); continue;
                case "array":
                    if (op.Count is < 1 or > 1000 || op.Rows is < 1 or > 1000 || !op.RowVector.IsFinite) throw new ArgumentException("배열 행·열 개수는 1~1000입니다.");
                    if ((long)doc.Entities.Count + (long)op.Count * op.Rows * entities.Length > 100_000) throw new ArgumentException("배열 결과가 객체 한도를 초과합니다.");
                    for (var row = 0; row < (op.Name == "polar" ? 1 : op.Rows); row++)
                        for (var i = 0; i < op.Count; i++)
                        { if (row == 0 && i == 0) continue; var shift = op.Vector * i + op.RowVector * row; doc.Entities.Add((op.Name == "polar" ? Geometry.Transform(e, p => Geometry.Rotate(p, op.Origin, op.Value * i), rotate: op.Value * i) : Geometry.Transform(e, p => p + shift)) with { Id = Guid.NewGuid() }); }
                    continue;
                case "explode":
                    if (e.Kind == EntityKind.Block)
                    {
                        var block = doc.Blocks.FirstOrDefault(b => b.Id == e.BlockId) ?? throw new ArgumentException("블록 정의가 없습니다.");
                        doc.Entities.RemoveAt(index);
                        foreach (var child in block.Entities) doc.Entities.Add(Geometry.Transform(child, p => Geometry.Rotate(p * e.Scale, default, e.Rotation) + e.Center, e.Scale, e.Rotation) with { Id = Guid.NewGuid() });
                        continue;
                    }
                    if (e.Kind != EntityKind.Polyline) throw new NotSupportedException("현재 EXPLODE는 폴리선만 지원합니다.");
                    doc.Entities.RemoveAt(index);
                    foreach (var (a, b) in Geometry.Segments(e)) doc.Entities.Add(new() { Kind = EntityKind.Line, Points = [a, b], Layer = e.Layer });
                    continue;
                default: throw new NotSupportedException($"아직 지원하지 않는 편집 연산: {action}");
            }
            Geometry.Validate(next); doc.Entities[index] = next.DeepCopy();
        }
    }

    private static void EditCorner(Drawing doc, Entity[] entities, EditOperation op)
    {
        if (entities.Length != 2 || entities.Any(e => e.Kind != EntityKind.Line)) throw new ArgumentException("대상 선과 경계 선, 두 개의 직선을 순서대로 선택하세요.");
        var a = entities[0]; var b = entities[1];
        var cross = Geometry.Intersect(a.Points[0], a.Points[1], b.Points[0], b.Points[1], true) ?? throw new ArgumentException("평행선은 처리할 수 없습니다.");
        var ai = doc.Entities.FindIndex(e => e.Id == a.Id); var bi = doc.Entities.FindIndex(e => e.Id == b.Id);
        if (op.Action is "trim" or "extend")
        {
            if (Geometry.Closest(cross, b.Points[0], b.Points[1]).Distance(cross) > Geometry.Epsilon) throw new ArgumentException("경계 선분과 만나지 않습니다.");
            if (op.Action == "trim" && Geometry.Closest(cross, a.Points[0], a.Points[1]).Distance(cross) > Geometry.Epsilon) throw new ArgumentException("잘라낼 교차점이 없습니다.");
            var points = new List<Point2>(a.Points); var endpoint = a.Points[0].Distance(op.Origin) <= a.Points[1].Distance(op.Origin) ? 0 : 1;
            points[endpoint] = cross; var updated = a with { Points = points }; Geometry.Validate(updated); doc.Entities[ai] = updated; return;
        }
        if (op.Value < 0) throw new ArgumentException("거리·반지름은 음수일 수 없습니다.");
        var aFar = a.Points.OrderByDescending(p => p.Distance(cross)).First(); var bFar = b.Points.OrderByDescending(p => p.Distance(cross)).First();
        var u = (aFar - cross) * (1 / aFar.Distance(cross)); var v = (bFar - cross) * (1 / bFar.Distance(cross));
        var angle = Math.Acos(Math.Clamp(Geometry.Dot(u, v), -1, 1));
        if (angle < Geometry.Epsilon || Math.PI - angle < Geometry.Epsilon) throw new ArgumentException("모서리 각도가 유효하지 않습니다.");
        var distance = op.Action == "fillet" ? op.Value / Math.Tan(angle / 2) : op.Value;
        if (distance >= Math.Min(aFar.Distance(cross), bFar.Distance(cross))) throw new ArgumentException("선 길이보다 큰 모서리 값입니다.");
        var t1 = cross + u * distance; var t2 = cross + v * distance;
        doc.Entities[ai] = a with { Points = [aFar, t1] }; doc.Entities[bi] = b with { Points = [bFar, t2] };
        if (distance < Geometry.Epsilon) return;
        if (op.Action == "chamfer") doc.Entities.Add(new() { Kind = EntityKind.Line, Points = [t1, t2], Layer = a.Layer });
        else
        {
            var bisector = u + v; var center = cross + bisector * (op.Value / Math.Sin(angle / 2) / bisector.Length);
            var start = Math.Atan2(t1.Y - center.Y, t1.X - center.X) * 180 / Math.PI; var end = Math.Atan2(t2.Y - center.Y, t2.X - center.X) * 180 / Math.PI;
            if ((end - start + 360) % 360 > 180) (start, end) = (end, start);
            doc.Entities.Add(new() { Kind = EntityKind.Arc, Points = [center], Radius = op.Value, StartAngle = start, EndAngle = end, Layer = a.Layer });
        }
    }

    public static void UpdateAssociations(Drawing doc)
    {
        for (var i = 0; i < doc.Entities.Count; i++)
        {
            var e = doc.Entities[i]; if (e.References.Count == 0) continue;
            var sources = e.References.Select(id => doc.Entities.FirstOrDefault(x => x.Id == id)).ToArray();
            if (sources.Any(x => x == null)) { doc.Entities[i] = e with { Invalid = true }; continue; }
            var source = sources[0]!;
            if (e.Kind == EntityKind.Hatch)
                doc.Entities[i] = e with { Points = [.. source.Points], Invalid = !source.Closed || source.Points.Count < 3 };
            if (e.Kind == EntityKind.Dimension && e.DimensionType is "radius" or "diameter" && source.Kind is EntityKind.Circle or EntityKind.Arc)
            {
                var ray = e.Points[1] - e.Points[0]; var unit = ray.Length > Geometry.Epsilon ? ray * (1 / ray.Length) : new Point2(1, 0);
                doc.Entities[i] = e with { Points = [source.Center, source.Center + unit * source.Radius], Invalid = false }; continue;
            }
            if (e.Kind == EntityKind.Dimension && source.Points.Count >= 2)
            {
                var oldDelta = e.Points[1] - e.Points[0]; var newDelta = source.Points[1] - source.Points[0];
                var oldNormal = oldDelta.Length > Geometry.Epsilon ? new Point2(-oldDelta.Y, oldDelta.X) * (1 / oldDelta.Length) : new Point2(0, 1);
                var newNormal = newDelta.Length > Geometry.Epsilon ? new Point2(-newDelta.Y, newDelta.X) * (1 / newDelta.Length) : new Point2(0, 1);
                var offset = e.Points.Count > 2 ? Geometry.Dot(e.Points[2] - e.Points[1], oldNormal) : 10;
                doc.Entities[i] = e with { Points = [source.Points[0], source.Points[1], source.Points[1] + newNormal * offset], Invalid = false };
            }
        }
    }
}

public sealed class DrawingSession
{
    private readonly object gate = new();
    private readonly Stack<Drawing> undo = new(), redo = new();
    private readonly Dictionary<Guid, ChangeSet> pending = [];
    private SpatialIndex? index;
    private long indexedRevision = -1;
    private Drawing drawing;
    public event Action? Changed;
    public DrawingSession(Drawing? document = null)
    { drawing = document?.DeepCopy() ?? new(); DrawingValidation.Validate(drawing); }
    public Drawing Snapshot() { lock (gate) return drawing.DeepCopy(); }
    public Guid Id { get { lock (gate) return drawing.Id; } }
    public long Revision { get { lock (gate) return drawing.Revision; } }
    public ChangeSet? Preview { get; private set; }
    public string? FilePath { get; set; }
    public long SavedRevision { get; set; }
    public bool IsDirty => Revision != SavedRevision;
    public bool CanUndo { get { lock (gate) return undo.Count > 0; } }
    public bool CanRedo { get { lock (gate) return redo.Count > 0; } }

    public void Execute(string summary, params EditOperation[] operations)
    {
        lock (gate)
        {
            var copy = drawing.DeepCopy();
            foreach (var op in operations) Editor.Apply(copy, op);
            Editor.UpdateAssociations(copy);
            DrawingValidation.Validate(copy);
            Commit(copy);
        }
        Changed?.Invoke();
    }
    private void Commit(Drawing next)
    {
        undo.Push(drawing); redo.Clear(); next.Revision = drawing.Revision + 1; drawing = next;
        index = null; Preview = null;
    }
    public ChangeSet Propose(long baseRevision, string summary, List<EditOperation> operations)
    {
        ChangeSet change;
        lock (gate)
        {
            if (baseRevision != drawing.Revision) throw new InvalidOperationException("도면이 변경됐습니다. 최신 도면을 조회하고 다시 제안하세요.");
            if (operations.Count is < 1 or > 1000) throw new ArgumentException("변경 연산은 1~1000개여야 합니다.");
            var copy = drawing.DeepCopy();
            foreach (var op in operations) Editor.Apply(copy, op);
            Editor.UpdateAssociations(copy);
            DrawingValidation.Validate(copy);
            if (pending.Count >= 20) throw new InvalidOperationException("보류 중인 변경안이 너무 많습니다. 먼저 적용·폐기하세요.");
            change = new(Guid.NewGuid(), drawing.Id, baseRevision, summary, CadJson.Clone(operations), copy, DateTimeOffset.UtcNow);
            pending.Add(change.Id, change); Preview = CadJson.Clone(change);
        }
        Changed?.Invoke(); return CadJson.Clone(change);
    }
    public ChangeSet? GetProposal(Guid id) { lock (gate) return pending.TryGetValue(id, out var c) ? CadJson.Clone(c) : null; }
    public void Approve(Guid id)
    {
        lock (gate)
        {
            if (!pending.TryGetValue(id, out var c)) throw new InvalidOperationException("이미 처리했거나 없는 변경안입니다.");
            if (c.BaseRevision != drawing.Revision) throw new InvalidOperationException("오래된 변경안입니다. 다시 생성하세요.");
            Commit(c.Result.DeepCopy()); pending.Clear();
        }
        Changed?.Invoke();
    }
    public void Reject(Guid id)
    {
        lock (gate) { pending.Remove(id); if (Preview?.Id == id) Preview = null; }
        Changed?.Invoke();
    }
    public void DiscardAllProposals()
    {
        lock (gate) { pending.Clear(); Preview = null; }
        Changed?.Invoke();
    }
    public bool Undo() => Travel(undo, redo);
    public bool Redo() => Travel(redo, undo);
    private bool Travel(Stack<Drawing> from, Stack<Drawing> to)
    {
        lock (gate)
        {
            if (from.Count == 0) return false;
            to.Push(drawing); var next = from.Pop().DeepCopy(); next.Revision = drawing.Revision + 1;
            drawing = next; index = null; Preview = null;
        }
        Changed?.Invoke(); return true;
    }
    public Entity[] Query(Bounds2 bounds)
    {
        lock (gate)
        {
            if (index == null || indexedRevision != drawing.Revision) { index = new(drawing.Entities, e => Geometry.Bounds(e, drawing)); indexedRevision = drawing.Revision; }
            return index.Query(bounds).Where(e => drawing.GetLayer(e.Layer).Visible).Select(e => e.DeepCopy()).ToArray();
        }
    }
}
