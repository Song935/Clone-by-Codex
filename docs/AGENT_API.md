# CAD tool interface v1

`tools-v1.json` is the generated authoritative JSON Schema contract shared by Anthropic client tools and MCP. Tool schemas are obtained from the same `CadTools.Definitions`; the bridge does not maintain duplicate business logic.

## Connection

Launch the packaged `CloneCad.Mcp.exe --pipe CloneCad-<instance>` using your MCP client. Copy the exact current pipe name/configuration from the desktop connection panel. The official MCP C# SDK handles stdio initialization, capabilities and tool calls. The bridge connects to a same-user-only Windows named pipe; no HTTP port is opened.

## Tools

| Tool | Input / behavior |
|---|---|
| `cad_get_document` | Omit documentId to list documents; otherwise read revision, units, layer/block metadata, selection and capabilities. |
| `cad_list_entities` | documentId, optional kind/layer/bounds, offset and limit (max 200). |
| `cad_get_entities` | documentId and up to 200 object IDs; exact geometry and properties. |
| `cad_measure` | documentId, points a/b; distance and angle in document units/degrees. |
| `cad_propose_edit` | documentId, baseRevision, summary, operations; validate isolated result and return a pending proposal ID. |
| `cad_get_proposal` | documentId/proposalId; pending/stale/not-pending status and operations. |
| `cad_get_preview` | documentId, optional proposalId; PNG plus structured metadata. |
| `cad_discard_proposal` | documentId/proposalId; discard without modifying drawing. |

There is deliberately **no approval, shell, arbitrary file access or code execution tool**. Approval occurs in the CAD UI. MCP clients cannot bypass it.

## Editing sequence

1. Discover document ID and units. Query revision and actual target geometry.
2. Resolve missing dimensions or ambiguous targets with the user.
3. Submit all related operations in one `cad_propose_edit` call using that revision.
4. Inspect the returned proposal and, if useful, request a rendered preview.
5. Tell the user to approve/reject it in CAD. Do not claim it was applied.
6. Re-query before the next edit. A changed revision invalidates an old proposal. Applying the same proposal twice is rejected.

All positions are `{ "x": number, "y": number }` in drawing units; angles are degrees. `create` allocates an object UUID when omitted. Use `replace` to preserve an existing ID while changing properties. A whole invalid batch fails without editing the live document or undo history.

Example operations for a 600×900 rectangle and central diameter-100 circle:

```json
[
  { "action": "create", "entity": { "kind": "polyline", "closed": true, "points": [{"x":0,"y":0},{"x":600,"y":0},{"x":600,"y":900},{"x":0,"y":900}] } },
  { "action": "create", "entity": { "kind": "circle", "points": [{"x":300,"y":450}], "radius": 50 } }
]
```

Supported actions are advertised by `cad_get_document`. Basic corner edits use two line IDs in target/boundary order. `trim`/`extend` use `origin` near the endpoint to replace. `fillet`/`chamfer` use `value` for radius/equal setback. `stretch` uses `origin`/`end` as the vertex region and `vector` as displacement. Rectangular array uses `count`, `vector`, `rows`, `rowVector`; polar array uses `name: "polar"`, `origin`, `count` and angular step `value`.

Dimension entity types are `aligned`, `linear`, `angular`, `radius`, `diameter`. Aligned/linear points are two measured points and a dimension position; linear rotation chooses horizontal/vertical. Radius/diameter points are center and circumference. Angular points are vertex, first ray, second ray and optional arc location. Link source object IDs with `references`; native storage preserves them.

Built-in agent defaults: 30 tool calls and 120 seconds per request, manual restart after a limit, one in-flight request, user-supplied API key, no automatic file export. Drawings stay usable without the cloud. Drawing annotations and tool output are untrusted data.

Contract changes must preserve `v1` behavior or introduce a documented new version with compatibility tests. Provider adapters implement `IAgentProvider`; provider-specific messages must not leak into Core or the CAD operation contract.
