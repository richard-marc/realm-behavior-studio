using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow
{
    CancellationTokenSource? combatCancellation;
    Task? combatTask;
    int combatRequest;
    void InitializeCombatControls()
    {
        Viewport.CombatPointSelected += DescribeCombatPoint;
        foreach (var input in new[] { PlayerHp, PlayerSpeed, PlayerDefense, PlayerAttack, PlayerDexterity, PlayerStartX, PlayerStartY, Seed, ArenaSize })
            input.TextChanged += (_, _) => ClearCombatPreview();
        foreach (var input in new[] { Invulnerable, Spectator })
        { input.Checked += (_, _) => ClearCombatPreview(); input.Unchecked += (_, _) => ClearCombatPreview(); }
        Weapons.SelectionChanged += (_, _) => ClearCombatPreview();
        CombatSpacing.TextChanged += (_, _) => InvalidateCombatSettings();
        CombatPhase.Checked += (_, _) => InvalidateCombatSettings(); CombatPhase.Unchecked += (_, _) => InvalidateCombatSettings();
        CombatDuration.SelectionChanged += (_, _) => InvalidateCombatSettings(); CombatGrid.SelectionChanged += (_, _) => InvalidateCombatSettings();
    }
    void InvalidateCombatSettings()
    {
        combatRequest++; combatCancellation?.Cancel(); CombatExport.IsEnabled = false;
        if (!Viewport.CombatPreviewOpen) return;
        Viewport.CombatReport = null; Viewport.CombatProgress = "Settings changed · choose Run again";
        CombatPointInfo.Text = "Settings changed. Run again to test these values."; Viewport.InvalidateVisual();
    }
    void ClearCombatPreview()
    {
        combatRequest++;
        combatCancellation?.Cancel();
        Viewport.CombatPreviewOpen = false; CombatPreviewControls.Visibility = Visibility.Collapsed;
        CombatPreviewButton.Content = "Preview attacks"; CombatExport.IsEnabled = false;
    }
    async Task AwaitCombatCancellation()
    {
        ClearCombatPreview();
        var pending = combatTask;
        if (pending != null) await pending;
    }
    async void ToggleCombatPreview(object sender, RoutedEventArgs e)
    {
        if (Viewport.CombatPreviewOpen) { ClearCombatPreview(); return; }
        await BeginCombatPreview();
    }
    async void RerunCombatPreview(object sender, RoutedEventArgs e) => await BeginCombatPreview();
    async Task BeginCombatPreview()
    {
        ClearCombatPreview(); var request = combatRequest; var pending = combatTask;
        if (pending != null) await pending;
        if (request != combatRequest) return;
        combatTask = RunCombatPreview();
        await combatTask;
    }
    async Task RunCombatPreview()
    {
        CancellationTokenSource? cancellation = null;
        try
        {
            ClearViewportEditors(); Stop(this, new()); var request = combatRequest;
            if (pathPreviewTask != null) await pathPreviewTask;
            if (request != combatRequest) return;
            var project = Snapshot();
            var phase = CombatPhase.IsChecked == true ? selectedNode is { Kind: "State", Label: not "Root" } ? selectedNode.Label : throw new ArgumentException("Select a named phase in Behavior before enabling Selected phase.") : null;
            var options = new CombatPreviewOptions(new[] { 3000, 5000, 10000 }[CombatDuration.SelectedIndex], CombatGrid.SelectedIndex == 0 ? 5 : 9, double.Parse(CombatSpacing.Text, CultureInfo.InvariantCulture));
            options.Validate();
            var expectedDocument = document; var revision = document.Revision; var selected = selectedNode?.Id;var expectedScenario=scenarioRevision;
            cancellation = new(); combatCancellation = cancellation;
            Viewport.CombatPreviewOpen = true; CombatPreviewControls.Visibility = Visibility.Visible;
            CombatPreviewButton.Content = "Close preview"; CombatRerun.IsEnabled = false; CombatExport.IsEnabled = false;
            CombatPointInfo.Text = "Preparing independent native runs…"; Viewport.CombatProgress = "Preparing native runs…";
            WorkspaceTabs.SelectedIndex = 0;
            bool Current() => !cancellation.IsCancellationRequested && document == expectedDocument && document.Revision == revision && scenarioRevision==expectedScenario && selectedEntity?.Id == project.EntityId && selectedNode?.Id == selected;
            var completed = false;
            var progress = new Progress<CombatPreviewProgress>(value =>
            {
                if (!Current() || completed) return;
                Viewport.CombatProgress = $"Running native tests · {value.Completed} / {value.Total}";
                CombatPointInfo.Text = Viewport.CombatProgress; Viewport.InvalidateVisual();
            });
            var result = await CombatPreview.RunAsync(adapter, project, phase, options, progress, cancellation.Token);
            completed = true;
            if (!Current()) return;
            Viewport.CombatReport = result; CombatExport.IsEnabled = true;
            CombatPointInfo.Text = "Choose a dot to inspect its native test. Settings and source changes clear this result.";
            Status.Text = "Attack preview complete · " + result.Points.Count(p => p.Sample != null) + " tested starts · " + result.Runtime;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (cancellation == null || !cancellation.IsCancellationRequested)
            { ClearCombatPreview(); Try(() => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw()); }
        }
        finally
        {
            if (ReferenceEquals(combatCancellation, cancellation)) { combatCancellation = null; CombatRerun.IsEnabled = true; }
            cancellation?.Dispose();
        }
    }
    void CombatModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Viewport == null) return;
        Viewport.CombatView = (CombatPreviewView)Math.Max(0, CombatMode.SelectedIndex); Viewport.InvalidateVisual();
    }
    void DescribeCombatPoint(int index)
    {
        if (Viewport.CombatReport is not { } report || index < 0 || index >= report.Points.Length) return;
        var point = report.Points[index];
        var prefix = string.Create(CultureInfo.InvariantCulture, $"Start ({point.X:0.###}, {point.Y:0.###}) · ");
        CombatPointInfo.Text = prefix + (point.Sample is not { } sample ? "Unavailable: " + point.Error :
            $"{sample.HealthLoss} HP lost · {sample.RemainingHp} / {sample.InitialHp} HP · {sample.ActualMs} ms · {sample.EmittedProjectiles} emitted · " +
            (sample.PlayerDied ? "player died" : sample.IncompleteMotion ? "partial motion coverage" : "completed") +
            $" · reference exposure: {report.Reference.ExposureSamples[index]} samples" + (sample.PlayerMoved ? " · native player position changed" : ""));
    }
    void ExportCombatPreview(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (Viewport.CombatReport is not { } report) return;
        var dialog = new SaveFileDialog { Filter = "Attack preview JSON|*.json", FileName = "attack-preview.json" };
        if (dialog.ShowDialog() == true) { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, BehaviorDocument.JsonOptions)); Status.Text = "Attack preview results exported"; }
    });
}
