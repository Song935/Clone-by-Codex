# (26.10.21) Clone by Codex

이 폴더에서 작업할 때만 적용하는 프로젝트 규칙이다. 상위 폴더의 AGENTS.md와 현재 Codex 세션 지침도 따른다.

## 목표

- 사람과 AI 에이전트가 같은 도면 엔진을 사용하는 Windows CAD 개발.
- 전체 코드 MIT 오픈소스 공개 및 외부 코드 기여 수용.
- 범용 2D 검증 후 직접 솔리드 3D 모델링으로 확장.

## 기록

- 프로젝트 노트: Brain `1 Projects\(26.10.21) Clone by Codex.md`
- 만든 날: 2026-10-21

## 진행 방식

- C# / .NET 10 / WPF, SkiaSharp, ACadSharp, Anthropic API, MCP C# SDK 사용.
- 기본 작도판부터 내장 AI와 외부 MCP 연결을 함께 검증한다.
- 모든 편집은 공통 도면 엔진을 사용한다. 에이전트 변경은 별도 변경안으로 검증하고 사람이 CAD에서 승인한다.
- 기준 검증 도면은 토목·구조 상세도다. DXF 왕복·치수·한글·해치·축척 PDF를 확인한다.
- 개발: scripts/build.ps1, scripts/run.ps1. 전체 검증: scripts/verify.ps1 -PdfCheck (별도 PyMuPDF 필요).
- .NET 10 SDK가 PATH에 없으면 D:\Song\.tools\dotnet10\dotnet.exe 사용 가능.
- 외부 기여는 CONTRIBUTING.md와 docs/AGENT_API.md를 따른다. v1 도구 계약 변경 시 tools-v1.json을 갱신한다.
- 라이브 Anthropic 검증과 별도 Windows 11 설치 검증을 자동 테스트 결과로 대체해 주장하지 않는다.

## 금지·주의

- API 키·토큰·실제 고객 도면은 저장소에 넣지 않는다.
- 에이전트 도구로 승인·셸·임의 코드 실행·임의 파일 접근을 제공하지 않는다.
- Autodesk 코드·아이콘·폰트·기타 자산을 복제하지 않는다.
- 외부 DXF 전체 호환이나 실무 대체 완성을 검증 없이 주장하지 않는다.
