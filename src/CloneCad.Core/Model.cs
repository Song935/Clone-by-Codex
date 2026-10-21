using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloneCad.Core;

public readonly record struct Point2([property: JsonRequired] double X, [property: JsonRequired] double Y)
{
    public static Point2 operator +(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Point2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Point2 operator *(Point2 p, double s) => new(p.X * s, p.Y * s);
    [JsonIgnore] public double Length => Math.Sqrt(X * X + Y * Y);
    public double Distance(Point2 other) => (this - other).Length;
    [JsonIgnore] public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

public enum EntityKind { Line, Polyline, Circle, Arc, Text, Dimension, Hatch, Block }

public sealed record Entity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    [JsonRequired] public EntityKind Kind { get; init; }
    public string Layer { get; init; } = "0";
    [JsonRequired] public List<Point2> Points { get; init; } = [];
    public bool Closed { get; init; }
    public double Radius { get; init; }
    public double StartAngle { get; init; }
    public double EndAngle { get; init; } = 360;
    public string Text { get; init; } = "";
    public string FontFamily { get; init; } = "Malgun Gothic";
    public double TextHeight { get; init; } = 3.5;
    public string DimensionType { get; init; } = "aligned";
    public List<Guid> References { get; init; } = [];
    public Guid? BlockId { get; init; }
    public double Rotation { get; init; }
    public double Scale { get; init; } = 1;
    public string? Color { get; init; }
    public string? LineType { get; init; }
    public double? LineWeight { get; init; }
    public bool Invalid { get; init; }
    public string HatchPattern { get; init; } = "ANSI31";
    public double HatchSpacing { get; init; } = 10;
    public Entity DeepCopy() => this with { Points = [.. Points], References = [.. References] };
    public bool ContentEquals(Entity other) => Points.SequenceEqual(other.Points) && References.SequenceEqual(other.References) &&
        (this with { Points = other.Points, References = other.References }) == other;
    [JsonIgnore] public Point2 Center => Points.Count > 0 ? Points[0] : default;
}

public sealed record Layer(string Name, string Color = "#D6E4ED", bool Visible = true,
    bool Locked = false, string LineType = "Continuous", double LineWeight = 0.25);
public sealed record BlockDefinition(Guid Id, string Name, List<Entity> Entities);
public sealed record PrintSettings(string Paper = "A3", bool Landscape = true, double Scale = 50,
    bool Monochrome = true, double Margin = 10);
public sealed record DimensionStyle(double TextHeight = 3.5, double ArrowSize = 3, int Precision = 2);

public sealed record Drawing
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "새 도면";
    public string Units { get; set; } = "mm";
    public long Revision { get; set; }
    public List<Entity> Entities { get; set; } = [];
    public List<Layer> Layers { get; set; } = [new("0")];
    public List<BlockDefinition> Blocks { get; set; } = [];
    public PrintSettings Print { get; set; } = new();
    public DimensionStyle DimStyle { get; set; } = new();
    public Drawing DeepCopy() => this with
    {
        Entities = Entities.Select(x => x.DeepCopy()).ToList(),
        Layers = [.. Layers],
        Blocks = Blocks.Select(b => b with { Entities = b.Entities.Select(x => x.DeepCopy()).ToList() }).ToList()
    };
    public Layer GetLayer(string name) => Layers.FirstOrDefault(x => x.Name == name)
        ?? throw new InvalidOperationException($"레이어가 없습니다: {name}");
}

public static class CadJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}
