using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void CombatPathSmoke() => await TryRuntime(async () =>
    {
        var output = Path.GetFullPath(".artifacts/combat-path-desktop"); Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool passed, string label) { if (!passed) throw new InvalidOperationException("UI check failed: " + label); checks.Add(label); }
        var source = document.Source; var root = EncounterRoot;
        await RunCombatUiSmoke(source, root, output, Check);
        await RunCombatPathUiSmoke(output, Check);
        File.WriteAllText(Path.Combine(output, "ui-checks.json"), JsonSerializer.Serialize(new { checks }, BehaviorDocument.JsonOptions));
        Application.Current.Shutdown(0);
    });

    async Task RunCombatPathUiSmoke(string output, Action<bool, string> check)
    {
        var previous = Snapshot(); var suppliedFile = selectedEntity!.SourceFile; var supplied = File.ReadAllText(suppliedFile);
        const string source = "using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioMovingDesktop=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"moving preview\",new Shoot(20,count:2,coolDown:100)));}}";
        ApplyProject(previous with { Source = source, Identities = [], SourceDrafts = [], SelectedSourceFile = null, LockedNodes = [], CollapsedNodes = [], Layout = [], AdditionalSources = [], Arena = new(), Player = previous.Player with { Invulnerable = true, Spectator = false, StartX = 68.5, StartY = 64.5 }, Seed = 7 }, null);
        CombatPhase.IsChecked = false; CombatGrid.SelectedIndex = 2; CombatDuration.SelectedIndex = 0;
        CombatInputMode.SelectedIndex = 5; CombatFiring.IsChecked = true; CombatSeedCount.SelectedIndex = 1;
        CombatPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var pending = combatTask;
        var dispatched = false; await Dispatcher.InvokeAsync(() => dispatched = true, DispatcherPriority.Background);
        check(dispatched && Viewport.CombatPreviewOpen && !CombatRerun.IsEnabled, "Moving multi-seed preview uses actual controls and leaves the desktop dispatcher available");
        await pending!; var report = Viewport.CombatReport!;
        check(report.Scenarios.Select(s => s.Seed).SequenceEqual(new[] { 7, 8, 9 }) && report.Scenarios.All(s => s.Points.Length == 1 && s.Reference.PlayerMoved && s.Reference.PlayerProjectiles > 0), "Desktop captures fresh native movement and XML weapon fire for all three seeds");
        check(report.Options.Inputs.Length == 4 && report.Options.Inputs.All(i => i.DurationMs == 750 && i.Input.Attack) && report.Points[0].X == 68.5, "Single-start and four-direction settings retain player coordinates and timed firing intent");
        CombatScenario.SelectedIndex = 2;
        check(Viewport.CombatScenarioIndex == 2 && ReferenceEquals(report, Viewport.CombatReport) && CombatExport.IsEnabled, "Seed selection displays existing evidence without rerunning native tests");
        check(Viewport.SelectCombatPoint(Viewport.CombatPoint(68.5, 64.5)) && CombatPointInfo.Text.Contains("seed 9") && CombatPointInfo.Text.Contains("player shots"), "Selected path details use the displayed seed's actual native weapon and health evidence");
        CombatMode.SelectedIndex = 2;
        check(!report.NoDamageObserved(0) && Viewport.CombatView == CombatPreviewView.NoDamage, "Hit overrides remain excluded from multi-seed no-damage highlighting");
        CombatMode.SelectedIndex = 1; UpdateLayout(); CaptureView(this, Path.Combine(output, "moving-scenarios.png"));
        check(report.Scenarios.SelectMany(s => s.Points.SelectMany(p => p.Sample!.Path)).All(p =>
            Viewport.CombatPoint(p.X, p.Y) is var screen && screen.X >= 20 && screen.X <= Viewport.ActualWidth - 20 && screen.Y >= 100 && screen.Y <= Viewport.ActualHeight - 80),
            "Preview camera fits complete tested paths across every seed inside the viewport");
        CombatSettings.IsExpanded = true; UpdateLayout(); CaptureView(this, Path.Combine(output, "moving-settings.png"));
        check(CombatInputMode.IsVisible && CombatSeedCount.IsVisible && CombatSettings.ActualWidth <= CombatPreviewControls.ActualWidth,
            "Expanded movement and seed settings remain within the preview panel");
        CombatSettings.IsExpanded = false;
        var export = Path.Combine(output, "moving-scenarios.json"); File.WriteAllText(export, JsonSerializer.Serialize(report, BehaviorDocument.JsonOptions));
        var reopened = JsonSerializer.Deserialize<CombatPreviewReport>(File.ReadAllText(export), BehaviorDocument.JsonOptions)!;
        check(reopened.Scenarios.Length == 3 && reopened.Scenarios.All(s => s.Reference.Path.Length == 31) && reopened.Options.Inputs.SequenceEqual(report.Options.Inputs), "Desktop result export retains every native seed, sampled path and exact timed input");
        CombatInputMode.SelectedIndex = 1;
        check(Viewport.CombatPreviewOpen && Viewport.CombatReport == null && !CombatExport.IsEnabled && CombatRerun.IsEnabled, "Changing movement settings removes stale evidence while keeping editable preview controls");
        ClearCombatPreview(); check(await StartSessionAsync(), "Play initializes a native session for recorded-input preview");
        running = false; await session!.AdvanceAsync(new(0, 1, true, 64.5, 64.5), 200); Pause(this, new());
        CombatInputMode.SelectedIndex = 6; CombatSeedCount.SelectedIndex = 0; await BeginCombatPreview();
        var recorded = Viewport.CombatReport!;
        check(!CombatFiring.IsEnabled && recorded.Options.Inputs.Length == 1 && recorded.Options.Inputs[0] == new CombatInputStep(200, new(0, 1, true, 64.5, 64.5)), "Recorded-input mode captures native Play input before stopping and retains absolute firing aim");
        check(recorded.Reference.PlayerMoved && recorded.Reference.PlayerProjectiles > 0 && recorded.Reference.Path.Where(p => p.Ms >= 200).Select(p => (p.X, p.Y)).Distinct().Count() == 1, "Short recordings move and fire natively then idle for the selected horizon");
        var cancellation = BeginCombatPreview(); CombatFiring.IsChecked = false; await cancellation;
        check(Viewport.CombatReport == null && !CombatExport.IsEnabled && combatCancellation == null, "Settings changes during native sampling cancel the request and reject stale installation");
        check(File.ReadAllText(suppliedFile) == supplied, "Movement and firing previews preserve supplied game source");
        CombatInputMode.SelectedIndex = 0; CombatSeedCount.SelectedIndex = 0; CombatGrid.SelectedIndex = 0; CombatMode.SelectedIndex = 0;
        ApplyProject(previous, null); WorkspaceTabs.SelectedIndex = 0;
    }
}
