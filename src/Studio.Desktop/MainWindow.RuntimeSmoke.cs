using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void CompleteRuntimeUiSmoke(string output, List<string> checks, Action<bool, string> check) => await TryRuntime(async () =>
    {
        var source = File.ReadAllText(selectedEntity!.SourceFile);
        var doc = new BehaviorDocument(source, adapter); var root = doc.Entities.Single(e => e.EntityId == selectedEntity.Id).Root;
        const string expression = "new State(new State(\"native preview\", StudioPreviewFan(), new Follow(StudioPreviewSpeed, range: 1)))";
        const string helpers = """

        namespace TKR.WorldServer.logic { public partial class BehaviorDb {
            const double StudioPreviewSpeed = .3;
            static TKR.WorldServer.logic.behaviors.Shoot StudioPreviewFan() => new TKR.WorldServer.logic.behaviors.Shoot(8, count: 4, coolDown: 200);
        } }
        """;
        document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, expression) + helpers, adapter);
        Changed(); var preparing = StartSessionAsync();
        check(runtimeStarting != null && !PlayButton.IsEnabled, "Compiled preparation exposes its pending state in the real playback control");
        var dispatched = false;
        await Dispatcher.InvokeAsync(() => dispatched = true, DispatcherPriority.Background);
        check(dispatched, "Desktop dispatcher remains available while compiled source is prepared");
        check(await preparing && session is CompiledRuntime && ReferenceEquals(session, Viewport.Session), "Source-context encounter starts in the real viewport through the compiled runtime path");
        var native = (CompiledRuntime)session!;
        running = false; Playback.SelectedIndex = 2;
        var positions = new List<Point>();
        for (var i = 0; i < 12; i++)
        {
            await AdvanceCompiledPlayback(1000d / 60, new(Y: 1));
            positions.Add(EncounterViewport.EntityPosition(session!.Entities.Single(), session.Ms + Viewport.FrameRemainderMs));
        }
        check(native.Ms is >= 195 and <= 200 && native.Shots.Any(s => s.Count == 4), "Compiled preview uses native helper attacks with the shared playback sample clock");
        check(positions.Distinct().Count() == 12 && native.PlayerY > 64.5, "Compiled preview retains smooth enemy presentation and player movement on every sampled frame");
        check(Events.Items.Count > 0 && Coverage.Text.Contains("Compiled encounter") && TimerInfo.Text.Length > 0, "Console and timer inspector consume the authoritative process snapshot");
        var projectPath = Path.Combine(output, "compiled-preview.rbs"); BehaviorDocument.Save(projectPath, Snapshot());
        check(BehaviorDocument.Load(projectPath).Source == document.Source && document.Identities.All(i => BehaviorDocument.Load(projectPath).Identities.Any(saved => saved.Id == i.Id)), "Compiled preview keeps the same saved source document and editor identities");
        native.ExportTrace(Path.Combine(output, "compiled-preview-trace.json"));
        var pid = native.ProcessId; UpdateLayout(); CaptureView(this, Path.Combine(output, "compiled-preview.png"));
        Stop(this, new());
        bool processRunning;
        try { using var observed = System.Diagnostics.Process.GetProcessById(pid); processRunning = !observed.HasExited; } catch (ArgumentException) { processRunning = false; }
        check(session == null && Viewport.Session == null && !processRunning, "Stop clears the viewport and closes the compiled native process");
        var beforeTool = document.Source; var toolArgs = JsonSerializer.SerializeToElement(new { durationMs = 1000, previewProposal = false });
        ShareContext.IsChecked = true;
        var toolResult = JsonSerializer.SerializeToElement(await NativeTool("run_simulation", toolArgs));
        ShareContext.IsChecked = false;
        check(toolResult.GetProperty("Coverage").GetString()!.Contains("Compiled original encounter") && toolResult.GetProperty("events").EnumerateArray().Any(e => e.GetProperty("Kind").GetString() == "attack"), "Bounded application simulation tool can run the same source-context encounter without live AI");
        check(document.Source == beforeTool && session == null, "Application tool preview leaves the shared source unchanged and closes its native process");
        var cancelled = StartSessionAsync(); Stop(this, new());
        try { await cancelled; } catch (OperationCanceledException) { }
        check(session == null && runtimeStarting == null && PlayButton.IsEnabled, "Stopped preparation cannot install a stale compiled session");
        document = doc; Changed();
        check(await StartSessionAsync() && session is EncounterSession, "Ordinary native literal playback remains available after compiled preview cancellation");
        running = false;
        File.WriteAllText(Path.Combine(output, "ui-checks.json"), JsonSerializer.Serialize(new { checks, spriteSheets = sprites.SheetCount, loadedFrames = sprites.LoadedFrames, fallbackNames = sprites.Missing.Order().ToArray() }, BehaviorDocument.JsonOptions));
        Application.Current.Shutdown();
    });
}
