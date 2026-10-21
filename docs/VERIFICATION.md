# Verification record — 2026-10-21

This records observed results, not a claim of production readiness.

| Check | Result |
|---|---|
| .NET 10 Release solution build | Passed, zero warnings/errors |
| Automated tests | 47 passed, zero failed/skipped |
| Hidden real WPF app smoke | Passed, app exited successfully |
| GitHub CI | Template provided under docs/ci; activation pending workflow permission |
| Command smoke | Lines, rectangles, circle, arc, Korean text, rectangular array, radius/angular dimensions, hatch, block definition/insertion |
| AI preview / approve / atomic undo / redo | Passed through shared session/tool layer |
| Real MCP subprocess | stdio initialize, tools/list and tools/call passed through named pipe |
| Native round trip | Korean path, annotations, object IDs and association metadata preserved |
| DXF round trip | Tested supported geometry, five dimension types, shared blocks, layers, colors, weights, units and hatch spacing |
| Unsupported DXF | Bulged polyline reported instead of flattened |
| Vector PDF scale | Independent PyMuPDF checks at 1:10, 1:50, 1:100 measured 99.99999mm for a 100mm expected line |
| PDF visuals | Synthetic structural section rasterized and visually inspected; Korean text, hatch and dimensions visible |
| 10,000-object benchmark | About 41–44fps Skia render, 0.02–0.03ms indexed query on this PC; engine-only, not interactive WPF FPS |

Development machine: Intel64 Family 6 Model 165 Stepping 5, Windows 10 22H2 build 19045, .NET SDK 10.0.402 / runtime 10.0.13. Target deployment remains Windows 11 x64.

Not yet verified:

- A live Anthropic API key/model and real-world token/cost behavior.
- Clean Windows 11 installation, different machines and 150%/200% DPI walkthroughs.
- Human interaction timing across the full WPF selection/snap/render pipeline.
- Independent DXF reader visual comparison of a broad external drawing corpus.
- Full-disk/access-denied/abrupt-power-loss recovery scenarios and long editing sessions.
- General concave polyline offset topology, all text-formatting variants and all DXF style representations.

Run `scripts/verify.ps1 -PdfCheck` (PyMuPDF installed) to regenerate test results, desktop/drawing/PDF previews and scale evidence under `artifacts/verification/`. No private drawing or live API credential is used.

## Follow-up validation

- Fixed missing DXF dimension picture blocks. Independent ezdxf 1.4.4 audit preserves all 21 synthetic entities and both dimensions, with zero errors or repairs; measured definition-point lengths are 600 and 1200. Korean text and layers survive.
- Added regression tests for feature snap priority, crossing selection, arc bounds, dimension/hatch picking, copy/array reference remapping and proposal lifecycle.
- Same-machine offscreen WPF pan + pointer/snap + render/composite benchmark (10,000 lines, 15 frames): mean 65.42ms before and 48.31ms after; p95 89.35ms before and 59.74ms after. This excludes human input delivery, display vsync and other devices, and is not a live display FPS guarantee.
- Run `scripts/verify.ps1 -PdfCheck -DxfCheck`; Python dependencies: PyMuPDF and ezdxf==1.4.4.

Repeated optimized run: mean 35.41ms and p95 44.75ms. These short samples vary with machine load; the earlier paired sample is retained above.

## Startup and installer follow-up

- Install.cmd now launches the CAD after installation and retains the console on failure. Tested the installer against the default per-user installation directory; the launched main window remained responsive.
- Startup exceptions now show an error dialog with a local log path. Logging failure does not hide the original error. Invalid/missing model settings are ignored.
- Release build, 47 tests, WPF smoke and independent PDF/DXF checks passed again for 0.1.2-preview.
