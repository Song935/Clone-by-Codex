# Third-party components

The application is MIT licensed. Dependencies keep their own licenses and notices.
The packaging script includes the original NuGet package metadata and available license/notice files under `licenses/`, including transitive dependencies and native components. Review these files before redistribution.

| Component | Purpose | Upstream |
|---|---|---|
| .NET / WPF | Runtime and Windows UI | https://github.com/dotnet/runtime · https://github.com/dotnet/wpf |
| SkiaSharp and native Skia | Drawing, text and vector PDF | https://github.com/mono/SkiaSharp |
| ACadSharp, CSMath and CSUtilities | DXF parsing and serialization | https://github.com/DomCR/ACadSharp |
| MCP C# SDK and Microsoft.Extensions | MCP bridge and hosting | https://github.com/modelcontextprotocol/csharp-sdk |
| xUnit and Microsoft.NET.Test.Sdk | Development tests, not application features | https://github.com/xunit/xunit |

Fonts are resolved from the user's Windows installation. No Autodesk code, fonts, icons, or other Autodesk assets are bundled. The application does not redistribute Windows font files as standalone assets. PDF generation may embed font data subject to the chosen font's embedding permissions; verify custom font permissions before sharing output.
