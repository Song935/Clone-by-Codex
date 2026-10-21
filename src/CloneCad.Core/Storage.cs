using System.IO.Compression;
using System.Text.Json;

namespace CloneCad.Core;

public static class DrawingFile
{
    public const int FormatVersion = 1;
    private sealed record Manifest(int Version, string Application);
    public static void Save(Drawing drawing, string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                using (var stream = zip.CreateEntry("manifest.json").Open()) JsonSerializer.Serialize(stream, new Manifest(FormatVersion, "Clone by Codex"), CadJson.Options);
                using (var stream = zip.CreateEntry("drawing.json").Open()) JsonSerializer.Serialize(stream, drawing, CadJson.Options);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static Drawing Load(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var manifest = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("도면 형식 정보가 없습니다.");
        using var ms = manifest.Open();
        var info = JsonSerializer.Deserialize<Manifest>(ms, CadJson.Options);
        if (info?.Version != FormatVersion) throw new InvalidDataException("지원하지 않는 도면 버전입니다.");
        var entry = zip.GetEntry("drawing.json") ?? throw new InvalidDataException("도면 데이터가 없습니다.");
        if (entry.Length > 100_000_000) throw new InvalidDataException("도면 파일이 너무 큽니다.");
        using var ds = entry.Open();
        var drawing = JsonSerializer.Deserialize<Drawing>(ds, CadJson.Options) ?? throw new InvalidDataException("도면이 비어 있습니다.");
        DrawingValidation.Validate(drawing);
        return drawing;
    }
}

public static class CoordinateParser
{
    public static Point2 Parse(string input, Point2 reference)
    {
        var text = input.Trim(); var relative = text.StartsWith('@'); if (relative) text = text[1..];
        var polar = text.Split('<');
        if (polar.Length == 2)
        {
            var r = Number(polar[0]); var a = Number(polar[1]) * Math.PI / 180;
            return (relative ? reference : default) + new Point2(r * Math.Cos(a), r * Math.Sin(a));
        }
        var parts = text.Split(',');
        if (parts.Length != 2) throw new FormatException("좌표 형식: x,y / @dx,dy / @거리<각도");
        return (relative ? reference : default) + new Point2(Number(parts[0]), Number(parts[1]));
    }
    public static double Number(string s)
    {
        if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v)) throw new FormatException("유효한 숫자를 입력하세요.");
        return v;
    }
}
