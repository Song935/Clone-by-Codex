# Roadmap

## 0.1 — shared editing foundation (implemented preview)

- Numeric 2D model, shared atomic operations and native persistence.
- WPF drafting surface, basic commands and annotations.
- Built-in Anthropic adapter, validated proposal workflow and external MCP stdio bridge.
- Supported-object DXF exchange and vector PDF preview/export.
- Tests, synthetic validation drawing, Windows packaging and contribution examples.

## Before a production 2D release

- Real Anthropic account/model integration tests and external-client usability testing.
- Independent DXF rendering comparisons beyond our own round trip, especially dimensions, blocks, text and hatches.
- Clean Windows 11 install and multi-DPI/multi-device checks.
- Activate the prepared CI workflow when a maintainer's GitHub credential has workflow permission.
- Interactive 10,000-object WPF measurements; renderer-only performance is not sufficient.
- More robust polyline offsets, corner trimming, association topology, command compatibility and rich text handling.
- Larger synthetic/fuzz geometry cases, recovery-failure tests and user walkthroughs.

## After the 2D acceptance gate

- Direct 3D solids: primitives, extrusion, revolution and boolean operations through a separately evaluated kernel.
- Reuse document IDs, transactions, approval and agent contracts with explicit 3D capability/version additions.
- Broader file compatibility and compiled/runtime extension design based on actual contributor needs.

No delivery dates are committed; each release should state its tested behavior and limitations.
