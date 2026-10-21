using System.Collections.Concurrent;
namespace CloneCad.Core;

/// <summary>Compiled-in extensions run inside the same isolated drawing transaction as built-in operations.</summary>
public interface ICadOperation
{
    string Action { get; }
    string Description { get; }
    void Apply(Drawing drawing, EditOperation operation);
}

public static class OperationExtensions
{
    private static readonly ConcurrentDictionary<string, ICadOperation> handlers = new(StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyList<string> BuiltInActions { get; } = ["create", "move", "copy", "rotate", "scale", "mirror", "delete", "replace", "layer", "units", "print_settings", "dimstyle", "offset", "array", "explode", "trim", "extend", "fillet", "chamfer", "stretch", "join", "block"];
    public static void Register(ICadOperation handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (string.IsNullOrWhiteSpace(handler.Action) || BuiltInActions.Contains(handler.Action, StringComparer.OrdinalIgnoreCase) || !handlers.TryAdd(handler.Action, handler))
            throw new ArgumentException("중복되거나 예약된 확장 연산 이름입니다.");
    }
    public static IEnumerable<object> Describe() => handlers.Values.Select(h => (object)new { action = h.Action, description = h.Description });
    public static IEnumerable<string> SupportedActions => BuiltInActions.Concat(handlers.Keys);
    internal static bool TryApply(Drawing drawing, EditOperation operation)
    {
        if (!handlers.TryGetValue(operation.Action, out var handler)) return false;
        handler.Apply(drawing, operation); return true;
    }
}
