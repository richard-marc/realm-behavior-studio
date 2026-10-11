using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow
{
    async Task RunCombatUiSmoke(string source, BehaviorNode root, string output, Action<bool, string> check)
    {
        const string scene = "new State(new State(\"attack area\", new Shoot(20, fixedAngle: 0, coolDown: 1000)))";
        document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, scene), adapter, sourceFile: selectedEntity!.SourceFile);
        Changed(); PlayerDefense.Text = "0"; PlayerStartX.Text = "68.5"; PlayerStartY.Text = "64.5";
        var phase = EncounterRoot.StateChildren.Single(); SelectNode(phase.Id);
        WorkspaceTabs.SelectedIndex = 0; UpdateLayout();
        CombatPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var pending = combatTask;
        check(Viewport.CombatPreviewOpen && CombatPreviewControls.Visibility == Visibility.Visible && !CombatRerun.IsEnabled && session == null, "Attack preview opens its actual controls and runs independently of playback");
        var dispatched = false; await Dispatcher.InvokeAsync(() => dispatched = true, DispatcherPriority.Background);
        check(dispatched, "Desktop dispatcher remains available while independent attack tests run");
        await pending!;
        var report = Viewport.CombatReport!;
        check(report is { Runtime: "Native constructors" } && report.Points.Length == 25 && report.Points.Any(p => p.Sample?.HealthLoss > 0) && CombatExport.IsEnabled, "Desktop preview receives actual native area damage and enables result export");
        check(Viewport.Input() == new EncounterInput(), "Attack inspection suppresses live player input");
        var index = report.Points.Select((p, i) => (p, i)).First(p => p.p.Sample?.HealthLoss > 0).i;
        var hit = report.Points[index]; UpdateLayout();
        check(Viewport.SelectCombatPoint(Viewport.CombatPoint(hit.X, hit.Y)) && CombatPointInfo.Text.Contains($"{hit.Sample!.HealthLoss} HP lost") && CombatPointInfo.Text.Contains("reference exposure"), "Selecting an actual plotted dot inspects its native damage and reference density separately");
        await Dispatcher.InvokeAsync(() => Viewport.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        CaptureView(this, Path.Combine(output, "attack-density.png"));
        CombatMode.SelectedIndex = 1; check(Viewport.CombatView == CombatPreviewView.HealthLoss && ReferenceEquals(Viewport.CombatReport, report), "Health-loss display reuses captured native tests without re-running the encounter");
        await Dispatcher.InvokeAsync(() => Viewport.UpdateLayout(), DispatcherPriority.ApplicationIdle); CaptureView(Viewport, Path.Combine(output, "attack-health-loss.png"));
        CombatMode.SelectedIndex = 2; check(Viewport.CombatView == CombatPreviewView.NoDamage, "No-damage view selects the conservative completed-test display");
        await Dispatcher.InvokeAsync(() => Viewport.UpdateLayout(), DispatcherPriority.ApplicationIdle); CaptureView(Viewport, Path.Combine(output, "attack-no-damage.png"));
        CombatSpacing.Text = "3";
        check(Viewport.CombatPreviewOpen && Viewport.CombatReport == null && !CombatExport.IsEnabled && CombatRerun.IsEnabled && CombatPreviewControls.Visibility == Visibility.Visible, "Preview settings invalidate results while keeping their controls available for continued editing");
        CombatSpacing.Text = "2"; await BeginCombatPreview();
        Seed.Text = "8";
        check(!Viewport.CombatPreviewOpen && Viewport.CombatReport == null && !CombatExport.IsEnabled && CombatPreviewControls.Visibility == Visibility.Collapsed, "Changing the test seed removes stale area results and export immediately");
        Seed.Text = "7"; CombatPhase.IsChecked = true;
        await BeginCombatPreview(); check(Viewport.CombatReport?.ForcedPhase == "attack area", "Selected-phase area preview retains actual forced-phase provenance");
        SelectNode(EncounterRoot.Id); check(!Viewport.CombatPreviewOpen, "Changing the selected authoring region clears its attack preview");
        CombatPhase.IsChecked = false; SelectNode(phase.Id);
        var cancelled = BeginCombatPreview(); PlayerHp.Text = "900"; await cancelled;
        check(!Viewport.CombatPreviewOpen && Viewport.CombatReport == null && combatCancellation == null, "Changing a player preset during sampling cancels the run and prevents stale installation");
        PlayerHp.Text = "1000";
        var stopping = BeginCombatPreview(); var playing = StartSessionAsync(); await stopping;
        check(await playing && session is EncounterSession && !Viewport.CombatPreviewOpen, "Play waits for area cancellation before initializing another native encounter");
        Stop(this, new()); PlayerStartX.Text = "70.5"; PlayerStartY.Text = "64.5"; CombatMode.SelectedIndex = 0;
    }
}
