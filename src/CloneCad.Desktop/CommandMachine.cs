using System.Globalization;
using CloneCad.Core;

namespace CloneCad.Desktop;

public sealed class CommandMachine(DrawingSession session, CadView view, Func<string> layer)
{
    public event Action<string>? Output;
    public event Action<string>? Prompt;
    public string Active { get; private set; } = "";
    private string previous = "LINE";
    private readonly List<Point2> points = [];
    private List<Guid> targets = [];
    private bool awaitingSelection;
    private double scalar = 3.5;
    private string blockName = "";
    private int stage;
    private int arrayColumns, arrayRows;
    public static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["L"] = "LINE",
        ["PL"] = "PLINE",
        ["REC"] = "RECTANG",
        ["C"] = "CIRCLE",
        ["A"] = "ARC",
        ["M"] = "MOVE",
        ["CO"] = "COPY",
        ["CP"] = "COPY",
        ["RO"] = "ROTATE",
        ["SC"] = "SCALE",
        ["MI"] = "MIRROR",
        ["E"] = "ERASE",
        ["O"] = "OFFSET",
        ["TR"] = "TRIM",
        ["EX"] = "EXTEND",
        ["F"] = "FILLET",
        ["CHA"] = "CHAMFER",
        ["S"] = "STRETCH",
        ["AR"] = "ARRAY",
        ["AP"] = "ARRAYPOLAR",
        ["X"] = "EXPLODE",
        ["J"] = "JOIN",
        ["T"] = "TEXT",
        ["MT"] = "TEXT",
        ["D"] = "DIMALIGNED",
        ["DAL"] = "DIMALIGNED",
        ["DLI"] = "DIMLINEAR",
        ["DAN"] = "DIMANGULAR",
        ["DRA"] = "DIMRADIUS",
        ["DDI"] = "DIMDIAMETER",
        ["MTEXT"] = "TEXT",
        ["H"] = "HATCH",
        ["B"] = "BLOCK",
        ["I"] = "INSERT",
        ["U"] = "UNDO"
    };
    public void Cancel()
    {
        Active = ""; points.Clear(); targets.Clear(); awaitingSelection = false; stage = 0;
        view.Picking = false; view.Anchor = null; view.InvalidateVisual(); Prompt?.Invoke("명령:");
    }
    public void Begin(string command)
    {
        Cancel(); command = command.ToUpperInvariant(); Active = Aliases.GetValueOrDefault(command, command);
        if (Active == "UNDO") { session.Undo(); Cancel(); return; }
        if (Active == "REDO") { session.Redo(); Cancel(); return; }
        if (Active is not ("LINE" or "PLINE" or "RECTANG" or "CIRCLE" or "ARC" or "MOVE" or "COPY" or "ROTATE" or "SCALE" or "MIRROR" or "ERASE" or "OFFSET" or "TRIM" or "EXTEND" or "FILLET" or "CHAMFER" or "STRETCH" or "ARRAY" or "ARRAYPOLAR" or "EXPLODE" or "JOIN" or "TEXT" or "DIMALIGNED" or "DIMLINEAR" or "DIMANGULAR" or "DIMRADIUS" or "DIMDIAMETER" or "HATCH" or "BLOCK" or "INSERT" or "DIST"))
        { var unknown = Active; Cancel(); throw new ArgumentException($"지원하지 않는 명령: {unknown}"); }
        previous = Active; Output?.Invoke(Active);
        if (NeedsSelection())
        {
            if (view.Selection.Count == 0) { awaitingSelection = true; UpdatePrompt(); return; }
            CaptureSelection();
        }
        if (Active is "DIMALIGNED" or "DIMLINEAR" && view.Selection.Count == 1)
        {
            var source = session.Snapshot().Entities.FirstOrDefault(e => e.Id == view.Selection.First());
            if (source?.Kind == EntityKind.Line) { points.AddRange(source.Points); view.Anchor = points[^1]; }
        }
        UpdatePrompt();
    }
    private bool NeedsSelection() => Active is "MOVE" or "COPY" or "ROTATE" or "SCALE" or "MIRROR" or "ERASE" or "OFFSET" or "TRIM" or "EXTEND" or "FILLET" or "CHAMFER" or "STRETCH" or "ARRAY" or "ARRAYPOLAR" or "EXPLODE" or "JOIN" or "HATCH" or "BLOCK" or "DIMRADIUS" or "DIMDIAMETER";
    private void CaptureSelection()
    {
        targets = view.Selection.ToList(); awaitingSelection = false;
        if (Active is "ERASE" or "EXPLODE" or "JOIN") Apply(new() { Action = Active == "ERASE" ? "delete" : Active.ToLowerInvariant(), Ids = targets });
        else if (Active == "HATCH")
        {
            var selected = session.Snapshot().Entities.Where(e => targets.Contains(e.Id)).ToArray();
            if (selected.Any(e => e.Kind != EntityKind.Polyline || !e.Closed)) throw new ArgumentException("닫힌 폴리선을 선택하세요.");
            var ops = selected.Select(e => new EditOperation { Action = "create", Entity = new() { Kind = EntityKind.Hatch, Points = [.. e.Points], References = [e.Id], Closed = true, Layer = layer() } }).ToArray();
            session.Execute("해치", ops); Cancel();
        }
    }
    public void Feed(string input)
    {
        input = input.Trim();
        if (Active == "")
        {
            if (input == "") { Begin(previous); return; }
            var split = input.Split(' ', StringSplitOptions.RemoveEmptyEntries); Begin(split[0]);
            for (var i = 1; i < split.Length; i++)
            { if (Active == "TEXT" && points.Count == 1 && stage == 1) { Feed(string.Join(' ', split.Skip(i))); break; } Feed(split[i]); }
            return;
        }
        if (awaitingSelection)
        {
            if (view.Selection.Count == 0) throw new ArgumentException("객체를 선택한 후 Enter를 누르세요.");
            CaptureSelection(); UpdatePrompt(); return;
        }
        if (input == "" || (Active == "PLINE" && input.Equals("C", StringComparison.OrdinalIgnoreCase)))
        {
            if (Active == "PLINE" && points.Count >= 2)
            { Create(new() { Kind = EntityKind.Polyline, Points = [.. points], Closed = input != "" }); return; }
            Cancel(); return;
        }
        if ((Active == "BLOCK" || Active == "INSERT") && stage == 0)
        { blockName = input; stage++; UpdatePrompt(); return; }
        if (Active == "TEXT" && points.Count == 1)
        {
            if (stage == 0) { scalar = CoordinateParser.Number(input); if (scalar <= 0) throw new ArgumentException("문자 높이는 0보다 커야 합니다."); stage++; UpdatePrompt(); return; }
            Create(new() { Kind = EntityKind.Text, Points = [points[0]], Text = input, TextHeight = scalar }); return;
        }
        if (ExpectsNumber())
        {
            var number = CoordinateParser.Number(input);
            switch (Active)
            {
                case "CIRCLE": Create(new() { Kind = EntityKind.Circle, Points = [points[0]], Radius = number }); break;
                case "ROTATE": case "SCALE": Apply(new() { Action = Active.ToLowerInvariant(), Ids = targets, Origin = points[0], Value = number }); break;
                case "OFFSET": case "FILLET": case "CHAMFER": Apply(new() { Action = Active.ToLowerInvariant(), Ids = targets, Value = number }); break;
                case "ARRAY":
                    if (number != Math.Truncate(number) || number < 1 || number > 1000) throw new ArgumentException("행·열 수는 1~1000 정수입니다.");
                    if (stage == 0) { arrayColumns = (int)number; stage = 1; UpdatePrompt(); }
                    else { arrayRows = (int)number; stage = 2; if (arrayRows == 1) Apply(new() { Action = "array", Ids = targets, Vector = points[1] - points[0], Count = arrayColumns }); else UpdatePrompt(); }
                    break;
                case "ARRAYPOLAR":
                    if (stage == 0) { if (number != Math.Truncate(number) || number < 2 || number > 1000) throw new ArgumentException("개수는 2~1000 정수입니다."); arrayColumns = (int)number; stage = 1; UpdatePrompt(); }
                    else Apply(new() { Action = "array", Name = "polar", Ids = targets, Origin = points[0], Count = arrayColumns, Value = number }); break;
            }
            return;
        }
        Pick(CoordinateParser.Parse(input, points.LastOrDefault()));
    }
    private bool ExpectsNumber() => Active == "CIRCLE" && points.Count == 1 || Active is "ROTATE" or "SCALE" && points.Count == 1 ||
        Active is "OFFSET" or "FILLET" or "CHAMFER" || Active == "ARRAY" && points.Count == 2 && stage < 2 || Active == "ARRAYPOLAR" && points.Count == 1;
    public void Pick(Point2 point)
    {
        if (!point.IsFinite) { Feed(""); return; }
        if (Active == "" || awaitingSelection) return;
        if (Active == "CIRCLE" && points.Count == 1) { Feed(point.Distance(points[0]).ToString(CultureInfo.InvariantCulture)); return; }
        if (ExpectsNumber() || Active == "TEXT" && points.Count == 1 || Active is "BLOCK" or "INSERT" && stage == 0) return;
        points.Add(point); view.Anchor = point;
        switch (Active)
        {
            case "LINE" when points.Count == 2: Create(new() { Kind = EntityKind.Line, Points = [.. points] }); return;
            case "RECTANG" when points.Count == 2:
                var a = points[0]; var b = points[1];
                if (Math.Abs(a.X - b.X) < Geometry.Epsilon || Math.Abs(a.Y - b.Y) < Geometry.Epsilon) throw new ArgumentException("사각형의 폭·높이가 필요합니다.");
                Create(new() { Kind = EntityKind.Polyline, Points = [a, new(b.X, a.Y), b, new(a.X, b.Y)], Closed = true }); return;
            case "ARC" when points.Count == 3:
                var center = points[0]; Create(new() { Kind = EntityKind.Arc, Points = [center], Radius = points[1].Distance(center), StartAngle = Angle(points[1] - center), EndAngle = Angle(points[2] - center) }); return;
            case "MOVE" or "COPY" when points.Count == 2: Apply(new() { Action = Active.ToLowerInvariant(), Ids = targets, Vector = points[1] - points[0] }); return;
            case "MIRROR" when points.Count == 2: Apply(new() { Action = "mirror", Ids = targets, Origin = points[0], End = points[1] }); return;
            case "TRIM" or "EXTEND" when points.Count == 1: Apply(new() { Action = Active.ToLowerInvariant(), Ids = targets, Origin = points[0] }); return;
            case "STRETCH" when points.Count == 4: Apply(new() { Action = "stretch", Ids = targets, Origin = points[0], End = points[1], Vector = points[3] - points[2] }); return;
            case "ARRAY" when points.Count == 3: Apply(new() { Action = "array", Ids = targets, Vector = points[1] - points[0], Count = arrayColumns, Rows = arrayRows, RowVector = points[2] - points[0] }); return;
            case "DIMALIGNED" or "DIMLINEAR" when points.Count == 3:
                Create(new() { Kind = EntityKind.Dimension, DimensionType = Active == "DIMLINEAR" ? "linear" : "aligned", Points = [.. points], Rotation = Math.Abs(points[1].X - points[0].X) < Geometry.Epsilon ? 90 : 0, TextHeight = session.Snapshot().DimStyle.TextHeight }); return;
            case "DIMANGULAR" when points.Count == 4: Create(new() { Kind = EntityKind.Dimension, DimensionType = "angular", Points = [.. points], TextHeight = session.Snapshot().DimStyle.TextHeight }); return;
            case "DIMRADIUS" or "DIMDIAMETER" when points.Count == 1:
                var circle = session.Snapshot().Entities.FirstOrDefault(e => targets.Contains(e.Id) && e.Kind is EntityKind.Circle or EntityKind.Arc) ?? throw new ArgumentException("원·호 하나를 선택하세요.");
                var ray = point - circle.Center; if (ray.Length < Geometry.Epsilon) throw new ArgumentException("중심에서 떨어진 방향점을 지정하세요.");
                Create(new() { Kind = EntityKind.Dimension, DimensionType = Active == "DIMRADIUS" ? "radius" : "diameter", Points = [circle.Center, circle.Center + ray * (circle.Radius / ray.Length)], References = [circle.Id], TextHeight = session.Snapshot().DimStyle.TextHeight }); return;
            case "DIST" when points.Count == 2: Output?.Invoke($"거리: {points[0].Distance(points[1]):0.########}  각도: {Angle(points[1] - points[0]):0.####}°"); Cancel(); return;
            case "BLOCK" when stage == 1: Apply(new() { Action = "block", Ids = targets, Name = blockName, Origin = point }); return;
            case "INSERT" when stage == 1:
                var block = session.Snapshot().Blocks.FirstOrDefault(b => b.Name == blockName) ?? throw new ArgumentException("블록 이름이 없습니다.");
                Create(new() { Kind = EntityKind.Block, Points = [point], BlockId = block.Id }); return;
        }
        UpdatePrompt();
    }
    private static double Angle(Point2 p) => Math.Atan2(p.Y, p.X) * 180 / Math.PI;
    private void Create(Entity e)
    {
        if (e.Kind == EntityKind.Dimension && view.Selection.FirstOrDefault() is var selected && selected != Guid.Empty)
        {
            var source = session.Snapshot().Entities.FirstOrDefault(x => x.Id == selected);
            if (source?.Kind == EntityKind.Line && e.Points[0].Distance(source.Points[0]) < Geometry.Epsilon && e.Points[1].Distance(source.Points[1]) < Geometry.Epsilon) e = e with { References = [selected] };
        }
        Apply(new() { Action = "create", Entity = e with { Layer = layer() } });
    }
    private void Apply(EditOperation op) { session.Execute(Active, op); Output?.Invoke($"{Active} 완료"); Cancel(); }
    private void UpdatePrompt()
    {
        if (Active == "") return;
        view.Picking = !awaitingSelection;
        var p = awaitingSelection ? "객체 선택 후 Enter" : Active switch
        {
            "LINE" => points.Count == 0 ? "첫 점" : "끝 점",
            "PLINE" => points.Count == 0 ? "첫 점" : "다음 점 / C 닫기 / Enter 완료",
            "RECTANG" => points.Count == 0 ? "첫 모서리" : "반대 모서리",
            "CIRCLE" => points.Count == 0 ? "중심점" : "반지름 또는 원 위의 점",
            "ARC" => points.Count == 0 ? "중심점" : points.Count == 1 ? "시작점" : "끝 각도의 점",
            "TEXT" => points.Count == 0 ? "삽입점" : stage == 0 ? "문자 높이" : "문자 내용",
            "ROTATE" or "SCALE" => points.Count == 0 ? "기준점" : Active == "ROTATE" ? "회전각(도)" : "축척",
            "OFFSET" => "부호 있는 간격 (선의 왼쪽 +)",
            "FILLET" => "반지름",
            "CHAMFER" => "동일 모따기 거리",
            "TRIM" or "EXTEND" => "처리할 대상 선의 끝 쪽 점 (선택 순서: 대상→경계)",
            "ARRAY" => points.Count == 0 ? "배열 기준점" : points.Count == 1 ? "다음 열 위치" : stage == 0 ? "열 개수" : stage == 1 ? "행 개수" : "다음 행 위치 (기준점 기준)",
            "ARRAYPOLAR" => points.Count == 0 ? "배열 중심점" : stage == 0 ? "전체 개수" : "복사 간격 각도(도)",
            "STRETCH" => points.Count < 2 ? "늘릴 꼭짓점 영역의 모서리 " + (points.Count + 1) : points.Count == 2 ? "변위 기준점" : "변위 끝점",
            "DIMALIGNED" or "DIMLINEAR" => points.Count < 2 ? "측정점 " + (points.Count + 1) : "치수선 위치",
            "DIMANGULAR" => points.Count == 0 ? "각도 꼭짓점" : points.Count < 3 ? "각도 방향점 " + points.Count : "치수 호 위치",
            "DIMRADIUS" or "DIMDIAMETER" => "치수 방향점",
            "BLOCK" or "INSERT" => stage == 0 ? "블록 이름" : "기준·삽입점",
            _ => points.Count == 0 ? "기준점" : "끝점"
        };
        Prompt?.Invoke($"{Active} · {p}:"); view.InvalidateVisual();
    }
}
