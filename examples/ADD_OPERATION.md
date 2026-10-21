# Add a CAD operation usable by humans and agents

This is a compiled contribution example, not a runtime plugin. Put the class into Core (or a referenced contribution project), register it at application startup, and add tests. Never edit `DrawingSession` snapshots directly.

```csharp
public sealed class CenteredRectangle : ICadOperation
{
    public string Action => "centered_rectangle";
    public string Description => "Create a rectangle at origin; vector is width/height.";

    public void Apply(Drawing drawing, EditOperation operation)
    {
        if (operation.Vector.X <= 0 || operation.Vector.Y <= 0)
            throw new ArgumentException("Width and height must be positive.");
        var c = operation.Origin;
        var half = operation.Vector * 0.5;
        Editor.Apply(drawing, new EditOperation
        {
            Action = "create",
            Entity = new Entity
            {
                Kind = EntityKind.Polyline,
                Closed = true,
                Points = [c - half, new(c.X + half.X, c.Y - half.Y),
                          c + half, new(c.X - half.X, c.Y + half.Y)]
            }
        });
    }
}
```

Register with `OperationExtensions.Register(new CenteredRectangle())`. The action and description are then advertised through `cad_get_document`. Existing `cad_propose_edit` calls can use:

```json
{ "action": "centered_rectangle", "origin": {"x":300,"y":450}, "vector": {"x":600,"y":900} }
```

For a human command, add its alias and prompted parameter collection in `CommandMachine`, then call `session.Execute` with this exact operation. Both routes receive the same final document validation, undo and proposal behavior. Add tests for valid/invalid dimensions, preview isolation, undo and save/restore. Avoid introducing provider-specific logic into the operation.
