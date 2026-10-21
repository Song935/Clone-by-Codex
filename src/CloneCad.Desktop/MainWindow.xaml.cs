using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CloneCad.Agent;
using CloneCad.Core;
using CloneCad.Formats;
using Microsoft.Win32;

namespace CloneCad.Desktop;

public partial class MainWindow : Window
{
    private sealed record Workspace(DrawingSession Session, CadView View, CommandMachine Commands, TabItem Tab);
    private readonly ConcurrentDictionary<Guid, Workspace> documents = new();
    private readonly ConcurrentDictionary<Guid, Guid[]> selections = new();
    private readonly CadTools tools;
    private readonly CadPipeServer pipe;
    private readonly string pipeName = "CloneCad-" + Guid.NewGuid().ToString("N");
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(110) };
    private readonly Dictionary<Guid, AgentRunner> runners = [];
    private readonly string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloneCad");
    private readonly DispatcherTimer recoveryTimer;
    private CancellationTokenSource? agentCancellation;
    private Workspace? Current => DocumentTabs.SelectedItem is TabItem { Tag: Guid id } ? documents.GetValueOrDefault(id) : null;
    private string CurrentLayer => LayerSelector.SelectedItem as string ?? "0";
    private bool updating;
    private readonly bool isSmoke;
    public MainWindow(bool smoke = false)
    {
        isSmoke = smoke;
        InitializeComponent();
        tools = new(id => id.HasValue && documents.TryGetValue(id.Value, out var w) ? w.Session : null,
            () => documents.Keys.ToArray(), id => selections.GetValueOrDefault(id) ?? [], d => DrawingRenderer.PreviewPng(d));
        pipe = new(pipeName, tools); pipe.Start();
        Directory.CreateDirectory(appData);
        var settings = Path.Combine(appData, "settings.json");
        if (File.Exists(settings))
        {
            try
            {
                using var config = JsonDocument.Parse(File.ReadAllText(settings));
                if (config.RootElement.ValueKind == JsonValueKind.Object && config.RootElement.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
                    ModelInput.Text = model.GetString() ?? "";
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        }
        AddDocument(new());
        recoveryTimer = new(TimeSpan.FromMinutes(5), DispatcherPriority.Background, (_, _) => SaveRecovery(), Dispatcher);
        if (!smoke) recoveryTimer.Start();
        Loaded += (_, _) => { if (!smoke) RestoreRecovery(); };
        PreviewTextInput += (_, e) =>
        {
            if (Keyboard.FocusedElement is TextBox or PasswordBox || string.IsNullOrWhiteSpace(e.Text)) return;
            CommandInput.Focus(); CommandInput.AppendText(e.Text); CommandInput.CaretIndex = CommandInput.Text.Length; e.Handled = true;
        };
    }
    private Guid[] Selected(Guid id) => documents.TryGetValue(id, out var w) ? w.View.Selection.ToArray() : [];
    private void AddDocument(Drawing drawing, string? file = null)
    {
        if (documents.TryGetValue(drawing.Id, out var existing)) { DocumentTabs.SelectedItem = existing.Tab; return; }
        var session = new DrawingSession(drawing) { FilePath = file, SavedRevision = file == null && drawing.Entities.Count > 0 ? -1 : drawing.Revision };
        var view = new CadView(session); var commands = new CommandMachine(session, view, () => CurrentLayer);
        var tab = new TabItem { Header = drawing.Name, Tag = session.Id, Content = view };
        var workspace = new Workspace(session, view, commands, tab); documents[session.Id] = workspace;
        commands.Output += Log; commands.Prompt += p => PromptText.Text = p;
        view.PointPicked += p => Safely(() => commands.Pick(p));
        view.SelectionChanged += () => { selections[session.Id] = view.Selection.ToArray(); UpdatePanels(); };
        view.CoordinateChanged += p => CoordinateStatus.Text = $"{p.X:0.###}, {p.Y:0.###}";
        session.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            tab.Header = session.Snapshot().Name + (session.IsDirty ? " *" : "");
            if (Current?.Session == session) UpdatePanels();
        });
        DocumentTabs.Items.Add(tab); DocumentTabs.SelectedItem = tab; UpdatePanels();
        view.Loaded += (_, _) => view.Fit();
    }
    private void Log(string text) { CommandLog.AppendText(text + "\n"); CommandLog.ScrollToEnd(); StatusText.Text = text; }
    private void Safely(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or NotSupportedException or FormatException or JsonException or OverflowException)
        { Log("오류 · " + ex.Message); Current?.Commands.Cancel(); }
    }
    private void UpdatePanels()
    {
        if (Current is not { } w) return; updating = true;
        var drawing = w.Session.Snapshot(); var selectedLayer = CurrentLayer;
        LayerSelector.ItemsSource = drawing.Layers.Select(l => l.Name).ToArray(); LayerSelector.SelectedItem = drawing.Layers.Any(l => l.Name == selectedLayer) ? selectedLayer : "0";
        UnitSelector.SelectedIndex = Array.IndexOf(new[] { "mm", "cm", "m", "inch" }, drawing.Units);
        var selected = drawing.Entities.Where(e => w.View.Selection.Contains(e.Id)).ToArray();
        selections[w.Session.Id] = selected.Select(e => e.Id).ToArray();
        PropertiesText.Text = selected.Length == 0 ? "선택된 객체가 없습니다.\n\n클릭으로 추가 선택\nShift+클릭으로 해제\n좌→우: 포함 선택\n우→좌: 교차 선택\n휠: 확대·축소\n중간 버튼: 이동\n\n명령: DIST, BLOCK, INSERT, TRIM, FILLET 등" :
            string.Join("\n\n", selected.Take(12).Select(e => $"{e.Kind} · {e.Layer}\n{e.Id}\n점: {e.Points.Count}  R: {e.Radius:0.###}"));
        var proposal = w.Session.Preview;
        ProposalPanel.Visibility = proposal == null ? Visibility.Collapsed : Visibility.Visible;
        if (proposal != null) ProposalText.Text = $"{proposal.Summary}\n{proposal.Operations.Count}개 연산 · 원본 r{proposal.BaseRevision}\n" + (proposal.BaseRevision == drawing.Revision ? "초록색은 미적용 변경안입니다." : "오래된 변경안 · 다시 생성하세요.");
        StatusText.Text = $"{drawing.Name} · {drawing.Entities.Count:N0} 객체 · {selected.Length} 선택 · {drawing.Units} · r{drawing.Revision}";
        w.View.SnapEnabled = SnapToggle.IsChecked == true; w.View.OrthoEnabled = OrthoToggle.IsChecked == true;
        w.View.PolarEnabled = PolarToggle.IsChecked == true;
        updating = false;
    }
    private void New_Click(object sender, RoutedEventArgs e) => AddDocument(new());
    private void CloseDocument_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (Current is not { } w) return; if (agentCancellation != null) throw new InvalidOperationException("AI 작업을 먼저 중단하세요.");
        if (w.Session.IsDirty)
        {
            var result = MessageBox.Show(this, "도면 변경 내용을 저장할까요?", "도면 닫기", MessageBoxButton.YesNoCancel);
            if (result == MessageBoxResult.Cancel || result == MessageBoxResult.Yes && !Save(w)) return;
        }
        documents.TryRemove(w.Session.Id, out _); selections.TryRemove(w.Session.Id, out _); runners.Remove(w.Session.Id);
        DocumentTabs.Items.Remove(w.Tab); var recovery = RecoveryPath(w.Session.Id); if (File.Exists(recovery)) File.Delete(recovery);
        if (documents.IsEmpty) AddDocument(new());
    });
    private void Sample_Click(object sender, RoutedEventArgs e) => AddDocument(Examples.StructuralDetail());
    private void Open_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        var dialog = new OpenFileDialog { Filter = "Clone CAD 도면|*.cbcad" };
        if (dialog.ShowDialog() == true) AddDocument(DrawingFile.Load(dialog.FileName), dialog.FileName);
    });
    private void Save_Click(object sender, RoutedEventArgs e) { if (Current is { } w) Safely(() => Save(w)); }
    private bool Save(Workspace w)
    {
        var path = w.Session.FilePath;
        if (path == null)
        {
            var dialog = new SaveFileDialog { Filter = "Clone CAD 도면|*.cbcad", FileName = w.Session.Snapshot().Name + ".cbcad" };
            if (dialog.ShowDialog() != true) return false; path = dialog.FileName;
        }
        var snapshot = w.Session.Snapshot(); DrawingFile.Save(snapshot, path); w.Session.FilePath = path; w.Session.SavedRevision = snapshot.Revision;
        var recovery = RecoveryPath(w.Session.Id); if (File.Exists(recovery)) File.Delete(recovery);
        w.Tab.Header = snapshot.Name; Log("저장 완료 · " + path); return true;
    }
    private void Import_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        var dialog = new OpenFileDialog { Filter = "DXF 도면|*.dxf" }; if (dialog.ShowDialog() != true) return;
        var report = DxfExchange.Import(dialog.FileName);
        if (report.Unsupported.Count > 0 || report.Warnings.Count > 0)
        {
            var message = "지원하지 않는 객체는 가져오지 않습니다.\n" + string.Join("\n", report.Unsupported.Select(x => $"{x.Key}: {x.Value}개")) + "\n" + string.Join("\n", report.Warnings.Take(6)) + "\n지원 부분만 새 도면으로 가져올까요?";
            if (MessageBox.Show(this, message, "DXF 호환성 확인", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        }
        AddDocument(report.Drawing); Log("DXF를 새 도면으로 가져왔습니다. 단위·글꼴·치수를 확인하세요.");
    });
    private void Export_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (Current is not { } w) return;
        var dialog = new SaveFileDialog { Filter = "DXF R2013 ASCII|*.dxf", FileName = w.Session.Snapshot().Name + ".dxf" };
        if (dialog.ShowDialog() == true) { DxfExchange.Export(w.Session.Snapshot(), dialog.FileName); Log("DXF 내보내기 완료"); }
    });
    private void Print_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (Current is not { } w) return;
        var doc = w.Session.Snapshot(); var f = Dialogs.Form(this, "PDF 출력 설정", ("용지 (A4~A0)", doc.Print.Paper), ("축척 분모 (1:n)", doc.Print.Scale.ToString()), ("가로 방향 (true/false)", doc.Print.Landscape.ToString()), ("흑백 (true/false)", doc.Print.Monochrome.ToString()), ("영역: 전체 또는 minX,minY;maxX,maxY", "전체"));
        if (f == null) return;
        var settings = new PrintSettings(f["용지 (A4~A0)"].ToUpperInvariant(), bool.Parse(f["가로 방향 (true/false)"]), CoordinateParser.Number(f["축척 분모 (1:n)"]), bool.Parse(f["흑백 (true/false)"]));
        Bounds2? region = null; var regionText = f["영역: 전체 또는 minX,minY;maxX,maxY"];
        if (regionText != "전체") { var parts = regionText.Split(';'); if (parts.Length != 2) throw new FormatException("영역 형식을 확인하세요."); var a = CoordinateParser.Parse(parts[0], default); var b = CoordinateParser.Parse(parts[1], default); region = new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)); }
        if (!Dialogs.PrintPreview(this, DrawingRenderer.PrintPreview(doc, settings, region))) return;
        var dialog = new SaveFileDialog { Filter = "PDF 도면|*.pdf", FileName = doc.Name + ".pdf" };
        if (dialog.ShowDialog() != true) return;
        DrawingRenderer.ExportPdf(doc, dialog.FileName, settings, region);
        w.Session.Execute("출력 설정", new EditOperation { Action = "print_settings", Print = settings }); Log("PDF 출력 완료 · " + dialog.FileName);
    });
    private void Fit_Click(object sender, RoutedEventArgs e) { Current?.View.Fit(); }
    private void Command_Click(object sender, RoutedEventArgs e) => Safely(() => { if (Current is { } w && sender is Button { Tag: string command }) { w.Commands.Begin(command); CommandInput.Focus(); } });
    private void CommandInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return; e.Handled = true;
        var input = CommandInput.Text; CommandInput.Clear();
        if (input.Trim().Equals("DIMSTYLE", StringComparison.OrdinalIgnoreCase)) { DimensionStyle_Click(sender, e); return; }
        Safely(() => Current?.Commands.Feed(input));
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Current is not { } w) return;
        if (e.Key == Key.Escape) { w.Commands.Cancel(); w.View.Selection.Clear(); UpdatePanels(); w.View.InvalidateVisual(); e.Handled = true; }
        if (e.Key == Key.F3) { SnapToggle.IsChecked = !SnapToggle.IsChecked; e.Handled = true; }
        if (e.Key == Key.F8) { OrthoToggle.IsChecked = !OrthoToggle.IsChecked; e.Handled = true; }
        if (e.Key == Key.F10) { PolarToggle.IsChecked = !PolarToggle.IsChecked; e.Handled = true; }
        if (Keyboard.Modifiers == ModifierKeys.Control && Keyboard.FocusedElement != ChatInput)
        {
            if (e.Key == Key.S) { Save_Click(sender, e); e.Handled = true; }
            if (e.Key == Key.Z) { w.Commands.Cancel(); w.Session.Undo(); e.Handled = true; }
            if (e.Key == Key.Y) { w.Commands.Cancel(); w.Session.Redo(); e.Handled = true; }
        }
        if (e.Key == Key.Enter && Keyboard.FocusedElement == w.View) { Safely(() => w.Commands.Feed("")); e.Handled = true; }
        if (e.Key == Key.Delete && Keyboard.FocusedElement == w.View) { Safely(() => w.Commands.Begin("ERASE")); e.Handled = true; }
    }
    private void DocumentTabs_Changed(object sender, SelectionChangedEventArgs e) { if (e.Source == DocumentTabs) { foreach (var w in documents.Values) w.Commands.Cancel(); UpdatePanels(); } }
    private void LayerSelector_Changed(object sender, SelectionChangedEventArgs e) { if (!updating) Current?.View.InvalidateVisual(); }
    private void Mode_Changed(object sender, RoutedEventArgs e) { if (IsInitialized) UpdatePanels(); }
    private void AddLayer_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (Current is not { } w) return;
        var existing = sender is Button { Tag: "edit" } ? w.Session.Snapshot().GetLayer(CurrentLayer) : new Layer("새 레이어", "#7DD3C8");
        var form = Dialogs.Form(this, "레이어", ("이름", existing.Name), ("색상 #RRGGBB", existing.Color), ("선종류", existing.LineType), ("선 두께 mm", existing.LineWeight.ToString(System.Globalization.CultureInfo.InvariantCulture))); if (form == null) return;
        if (!SkiaSharp.SKColor.TryParse(form["색상 #RRGGBB"], out _)) throw new ArgumentException("색상 형식이 잘못됐습니다.");
        w.Session.Execute("레이어", new EditOperation { Action = "layer", Layer = new(form["이름"], form["색상 #RRGGBB"], existing.Visible, existing.Locked, LineType: form["선종류"], LineWeight: CoordinateParser.Number(form["선 두께 mm"])) });
    });
    private void DimensionStyle_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (Current is not { } w) return; var style = w.Session.Snapshot().DimStyle;
        var form = Dialogs.Form(this, "공통 치수 스타일", ("문자 높이", style.TextHeight.ToString()), ("화살표 크기", style.ArrowSize.ToString()), ("소수 자릿수", style.Precision.ToString())); if (form == null) return;
        w.Session.Execute("치수 스타일", new EditOperation { Action = "dimstyle", DimensionStyle = new(CoordinateParser.Number(form["문자 높이"]), CoordinateParser.Number(form["화살표 크기"]), int.Parse(form["소수 자릿수"])) });
    });
    private void EditProperties_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (Current is not { } w) return; if (w.View.Selection.Count != 1) throw new ArgumentException("속성을 편집할 객체 하나를 선택하세요.");
        var entity = w.Session.Snapshot().Entities.First(x => x.Id == w.View.Selection.First());
        var form = Dialogs.Form(this, "객체 속성", ("레이어", entity.Layer), ("색상 (빈칸=레이어)", entity.Color ?? ""), ("선종류 (빈칸=레이어)", entity.LineType ?? ""), ("선 두께 (빈칸=레이어)", entity.LineWeight?.ToString() ?? ""),
            ("점 (x,y;...)", string.Join(';', entity.Points.Select(p => FormattableString.Invariant($"{p.X},{p.Y}")))), ("반지름", entity.Radius.ToString()), ("문자", entity.Text.Replace("\n", "\\P")), ("글꼴", entity.FontFamily), ("문자 높이", entity.TextHeight.ToString())); if (form == null) return;
        var color = string.IsNullOrWhiteSpace(form["색상 (빈칸=레이어)"]) ? null : form["색상 (빈칸=레이어)"];
        if (color != null && !SkiaSharp.SKColor.TryParse(color, out _)) throw new ArgumentException("색상이 유효하지 않습니다.");
        w.Session.Execute("속성", new EditOperation
        {
            Action = "replace",
            Ids = [entity.Id],
            Entity = entity with
            {
                Layer = form["레이어"],
                Color = color,
                LineType = string.IsNullOrWhiteSpace(form["선종류 (빈칸=레이어)"]) ? null : form["선종류 (빈칸=레이어)"],
                LineWeight = string.IsNullOrWhiteSpace(form["선 두께 (빈칸=레이어)"]) ? null : CoordinateParser.Number(form["선 두께 (빈칸=레이어)"]),
                Points = form["점 (x,y;...)"].Split(';').Select(x => CoordinateParser.Parse(x, default)).ToList(),
                Radius = CoordinateParser.Number(form["반지름"]),
                Text = form["문자"].Replace("\\P", "\n"),
                FontFamily = form["글꼴"],
                TextHeight = CoordinateParser.Number(form["문자 높이"])
            }
        });
    });
    private void ToggleLayer_Click(object sender, RoutedEventArgs e) => ChangeLayer(l => l with { Visible = !l.Visible });
    private void LockLayer_Click(object sender, RoutedEventArgs e) => ChangeLayer(l => l with { Locked = !l.Locked });
    private void ChangeLayer(Func<Layer, Layer> change) => Safely(() => { if (Current is { } w) w.Session.Execute("레이어", new EditOperation { Action = "layer", Layer = change(w.Session.Snapshot().GetLayer(CurrentLayer)) }); });
    private void Units_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (updating || Current is not { } w || UnitSelector.SelectedItem is not ComboBoxItem { Content: string unit }) return;
        Safely(() => w.Session.Execute("단위 변경", new EditOperation { Action = "units", Name = unit }));
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (!string.IsNullOrWhiteSpace(ApiKeyInput.Password)) { CredentialStore.Save(ApiKeyInput.Password); ApiKeyInput.Clear(); }
        File.WriteAllText(Path.Combine(appData, "settings.json"), JsonSerializer.Serialize(new { model = ModelInput.Text.Trim() }));
        runners.Clear(); Log("AI 연결 설정 저장 완료");
    });
    private void Connection_Click(object sender, RoutedEventArgs e)
    {
        var bridge = Path.Combine(AppContext.BaseDirectory, "mcp", "CloneCad.Mcp.exe");
        Clipboard.SetText(JsonSerializer.Serialize(new { mcpServers = new Dictionary<string, object> { ["clone-cad"] = new { command = bridge, args = new[] { "--pipe", pipeName } } } }, CadJson.Options));
        Log("MCP 설정을 복사했습니다. 실행 중인 이 CAD 인스턴스에 연결합니다.");
    }
    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } w || string.IsNullOrWhiteSpace(ChatInput.Text) || agentCancellation != null) return;
        var prompt = ChatInput.Text.Trim(); var key = CredentialStore.Read();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(ModelInput.Text)) { Log("Anthropic API 키와 모델 ID를 설정하세요."); return; }
        if (w.Session.Preview != null) { Log("먼저 기존 변경안을 적용하거나 거부하세요."); return; }
        ChatInput.Clear(); ChatLog.AppendText("\n\n나: " + prompt + "\n"); SendButton.IsEnabled = false;
        agentCancellation = new(); var token = agentCancellation.Token;
        try
        {
            if (!runners.TryGetValue(w.Session.Id, out var runner)) runners[w.Session.Id] = runner = new(new AnthropicProvider(http, key, ModelInput.Text.Trim()), tools);
            var response = await runner.RunAsync($"Active document ID: {w.Session.Id}; current revision: {w.Session.Revision}. User request: {prompt}", token, name => Dispatcher.BeginInvoke(() => StatusText.Text = "AI 도구 · " + name));
            ChatLog.AppendText("AI: " + response + "\n"); if (w.Session.Preview != null) w.View.SetPreviewFit();
        }
        catch (OperationCanceledException) { ChatLog.AppendText("AI 작업이 중단되었거나 제한 시간에 도달했습니다. 원본 도면은 그대로입니다.\n"); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or JsonException)
        { ChatLog.AppendText("AI 오류: " + ex.Message + "\n검증된 변경안이 있으면 직접 확인하세요.\n"); }
        finally { agentCancellation.Dispose(); agentCancellation = null; SendButton.IsEnabled = true; ChatLog.ScrollToEnd(); UpdatePanels(); }
    }
    private void CancelAgent_Click(object sender, RoutedEventArgs e) => agentCancellation?.Cancel();
    private void Approve_Click(object sender, RoutedEventArgs e) => Safely(() => { if (Current?.Session is { Preview: { } p } s) { s.Approve(p.Id); Log("AI 변경안을 적용했습니다. Ctrl+Z로 한 번에 되돌릴 수 있습니다."); } });
    private void Reject_Click(object sender, RoutedEventArgs e) { if (Current?.Session is { Preview: { } p } s) s.Reject(p.Id); }
    private string RecoveryPath(Guid id) => Path.Combine(appData, "recovery", id + ".cbcad");
    private void SaveRecovery()
    {
        foreach (var w in documents.Values.Where(w => w.Session.IsDirty))
        { try { DrawingFile.Save(w.Session.Snapshot(), RecoveryPath(w.Session.Id)); } catch (IOException) { Log("복구본 저장 실패 · 디스크 상태를 확인하세요."); } }
    }
    private void RestoreRecovery()
    {
        var dir = Path.Combine(appData, "recovery"); if (!Directory.Exists(dir)) return;
        var files = Directory.GetFiles(dir, "*.cbcad"); if (files.Length == 0) return;
        if (MessageBox.Show(this, $"복구 가능한 도면 {files.Length}개가 있습니다. 열까요?", "작업 복구", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            foreach (var file in files) Safely(() => AddDocument(DrawingFile.Load(file)));
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        foreach (var w in documents.Values.Where(w => !isSmoke && w.Session.IsDirty))
        {
            var result = MessageBox.Show(this, $"'{w.Session.Snapshot().Name}' 변경 내용을 저장할까요?", "종료", MessageBoxButton.YesNoCancel);
            if (result == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (result == MessageBoxResult.Yes)
            { try { if (!Save(w)) { e.Cancel = true; return; } } catch (Exception ex) { MessageBox.Show(ex.Message); e.Cancel = true; return; } }
        }
        recoveryTimer.Stop(); agentCancellation?.Cancel();
        if (!isSmoke) foreach (var id in documents.Keys) { var path = RecoveryPath(id); if (File.Exists(path)) File.Delete(path); }
        pipe.DisposeAsync().AsTask().GetAwaiter().GetResult(); http.Dispose();
    }
    public async Task RunSmokeTest(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        RunCommandSmoke();
        AddDocument(Examples.StructuralDetail()); var w = Current!;
        w.Commands.Feed("LINE 1400,0 1600,0");
        if (w.Session.Snapshot().Entities.Count != 19) throw new InvalidOperationException("WPF command LINE failed");
        w.Commands.Feed("RECTANG 1400,100 1600,300");
        if (w.Session.Snapshot().Entities.Count != 20) throw new InvalidOperationException("WPF command RECTANG failed");
        var proposalReply = tools.Call("cad_propose_edit", JsonSerializer.SerializeToElement(new
        {
            documentId = w.Session.Id,
            baseRevision = w.Session.Revision,
            summary = "AI 변경안: 기초 오른쪽에 검토용 원 추가",
            operations = new[] { new EditOperation { Entity = new Entity { Kind = EntityKind.Circle, Points = [new(1500, 450)], Radius = 80, Layer = "철근" } } }
        }, CadJson.Options));
        if (proposalReply.IsError || w.Session.Snapshot().Entities.Count != 20) throw new InvalidOperationException(proposalReply.Text);
        UpdatePanels(); w.View.SetPreviewFit();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(this); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(output, "desktop-preview.png"))) encoder.Save(stream);
        w.Session.Approve(w.Session.Preview!.Id);
        if (w.Session.Snapshot().Entities.Count != 21) throw new InvalidOperationException("Approval failed");
        w.Session.Undo(); if (w.Session.Snapshot().Entities.Count != 20) throw new InvalidOperationException("Atomic undo failed");
        w.Session.Redo();
        var drawing = w.Session.Snapshot();
        DrawingFile.Save(drawing, Path.Combine(output, "structural-detail.cbcad"));
        DxfExchange.Export(drawing, Path.Combine(output, "structural-detail.dxf"));
        DrawingRenderer.ExportPdf(drawing, Path.Combine(output, "structural-detail.pdf"), new("A3", true, 10));
        foreach (var scale in new[] { 10, 50, 100 })
        {
            var scaleDrawing = new Drawing { Entities = [new() { Kind = EntityKind.Line, Points = [new(0, 0), new(100 * scale, 0)] }] };
            DrawingRenderer.ExportPdf(scaleDrawing, Path.Combine(output, $"scale-{scale}.pdf"), new("A3", true, scale));
        }
        File.WriteAllBytes(Path.Combine(output, "drawing-preview.png"), DrawingRenderer.PreviewPng(drawing));
        var performance = MeasurePerformance();
        var wpfPerformance = await MeasureWpfPerformance();
        File.WriteAllText(Path.Combine(output, "smoke-result.json"), JsonSerializer.Serialize(new
        {
            passed = true,
            drawingEntities = drawing.Entities.Count,
            commands = new[] { "LINE", "RECTANG" },
            proposalApprovalUndoRedo = true,
            files = new[] { "cbcad", "dxf", "pdf", "png" },
            os = Environment.OSVersion.ToString(),
            runtime = Environment.Version.ToString(),
            performance,
            wpfPerformance,
            anthropicLiveCall = "not tested: no user API key"
        }, CadJson.Options));
    }
    private async Task<object> MeasureWpfPerformance()
    {
        var doc = new Drawing
        {
            Name = "10k WPF benchmark",
            Entities = Enumerable.Range(0, 10000).Select(i => new Entity
            { Kind = EntityKind.Line, Points = [new(i % 100 * 10, i / 100 * 10), new(i % 100 * 10 + 8, i / 100 * 10 + 8)] }).ToList()
        };
        AddDocument(doc); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var view = Current!.View; view.Fit(); view.Picking = true; view.Anchor = new(0, 0);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var durations = new List<double>(); var framesBefore = view.PaintedFrames;
        var target = new System.Windows.Media.Imaging.RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        for (var i = 0; i < 15; i++)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            view.PanBy(new(.5, 0)); view.UpdatePointer(new(150 + i * 20, 200 + i * 5));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            target.Render(view); durations.Add(timer.Elapsed.TotalMilliseconds);
        }
        var paints = view.PaintedFrames - framesBefore;
        if (paints < 15) throw new InvalidOperationException("WPF benchmark did not repaint each frame.");
        return new { entities = 10000, frames = durations.Count, paints, meanFrameMilliseconds = durations.Average(), p95FrameMilliseconds = durations.Order().ElementAt((int)Math.Ceiling(durations.Count * .95) - 1), fps = 1000 / durations.Average(), scope = "Offscreen WPF pan + pointer/snap + render/composite; excludes human input delivery, display vsync and other devices" };
    }
    private static object MeasurePerformance()
    {
        var doc = new Drawing { Entities = Enumerable.Range(0, 10000).Select(i => new Entity { Kind = EntityKind.Line, Points = [new(i % 100 * 10, i / 100 * 10), new(i % 100 * 10 + 8, i / 100 * 10 + 8)] }).ToList() };
        var session = new DrawingSession(doc); session.Query(new(0, 0, 100, 100)); var timer = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) session.Query(new(100 + i, 100, 120 + i, 120)); var queryMs = timer.Elapsed.TotalMilliseconds / 100;
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(1200, 800)); var canvas = surface.Canvas; canvas.Scale(.7f);
        DrawingRenderer.Draw(canvas, doc, .7); timer.Restart();
        for (var i = 0; i < 20; i++) { canvas.Clear(SkiaSharp.SKColors.Black); DrawingRenderer.Draw(canvas, doc, .7); canvas.Flush(); }
        return new { entities = 10000, renderFps = 20 / timer.Elapsed.TotalSeconds, spatialQueryMilliseconds = queryMs, renderScope = "Skia engine only; interactive WPF frame rate not measured", cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") };
    }
    private static void RunCommandSmoke()
    {
        var session = new DrawingSession(); var view = new CadView(session); var command = new CommandMachine(session, view, () => "0");
        command.Feed("LINE 0,0 100,0"); command.Feed("RECTANG 0,200 100,300"); command.Feed("CIRCLE 200,200 20"); command.Feed("ARC 300,300 320,300 300,320");
        command.Feed("TEXT 0,400 10 한글 검증");
        if (session.Snapshot().Entities.Count != 5 || session.Snapshot().Entities[^1].Text != "한글 검증") throw new InvalidOperationException("Command parsing failed");
        var circle = session.Snapshot().Entities.Single(e => e.Kind == EntityKind.Circle); view.Selection.Add(circle.Id);
        command.Feed("ARRAY 0,0 50,0 2 2 0,50");
        if (session.Snapshot().Entities.Count(e => e.Kind == EntityKind.Circle) != 4) throw new InvalidOperationException("Rectangular ARRAY failed");
        command.Feed("DIMRADIUS 230,230");
        command.Feed("DIMANGULAR 0,0 100,0 0,100 50,50");
        view.Selection.Clear(); var polygon = session.Snapshot().Entities.First(e => e.Kind == EntityKind.Polyline); view.Selection.Add(polygon.Id);
        command.Feed("HATCH"); command.Feed("BLOCK example 0,0"); view.Selection.Clear(); command.Feed("INSERT example 500,0");
        if (session.Snapshot().Entities.Count(e => e.Kind == EntityKind.Block) != 2) throw new InvalidOperationException("BLOCK/INSERT failed");
        DrawingValidation.Validate(session.Snapshot());
    }
}
