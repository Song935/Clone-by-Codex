# Contributing

The goal is a CAD engine that humans and agents can use accurately through the same operations.
Small, tested improvements are welcome. You can also contribute reproducible bugs, DXF compatibility cases, Korean/English documentation, or interface feedback.

## Development

1. Use Windows and the .NET 10 SDK. Clone this repository; no credentials are required.
2. Run `./scripts/build.ps1`, then `./scripts/verify.ps1`.
3. Run `./scripts/run.ps1`; open the synthetic validation drawing from the toolbar.
4. Keep commits focused, describe the concrete before/after behavior, and include relevant tests and screenshots.

`CloneCad.Core` owns document data and validated atomic edits. `CloneCad.Agent` owns shared tool schemas, cloud-provider adapters and local pipe transport. `CloneCad.Formats` owns DXF, Skia and PDF. `CloneCad.Desktop` owns interaction and approval. `CloneCad.Mcp` only translates standard MCP calls to the same tools.

## Rules for changes

- Every new editing capability must work through the shared engine, including proposal validation and undo; do not add an AI-only path that edits the live document.
- Geometry is numeric model data. Screenshots supplement exact queries; they do not replace geometry or establish dimensions.
- Add meaningful tests for geometry edge cases and persistence/transaction behavior. Use fake providers for CI, never a real API key.
- Preserve tool names and field meaning in `v1`. Breaking contracts require a documented version transition. Update `tools-v1.json` with `CloneCad.Mcp --export-tools` after schema changes.
- Keep drawing text untrusted and keep provider secrets outside drawings, diagnostics, screenshots and Git history.
- Add dependency license notices when introducing a package. Do not copy proprietary application code, assets or private customer drawings.
- Runtime plugins and a 3D kernel are later milestones; discuss changes to those boundaries in an issue before a large PR.

See [ADD_OPERATION](examples/ADD_OPERATION.md) for a small compiled extension using the existing tool contract.

## Reporting bugs

Include app version, Windows version, steps, expected/actual behavior, and a minimal synthetic drawing if possible. State the command sequence and whether it came from UI, built-in AI or MCP. Remove keys and proprietary drawing content from logs. Do not post security-sensitive details publicly.

The maintainer reviews and merges contributions; there is no automatic merge or plugin marketplace.
