using System.Globalization;
using System.Collections.Immutable;
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
    ImmutableArray<CombatInputStep> combatRecordedInputs = [];
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
        CombatInputMode.SelectionChanged += (_, _) =>
        {
            CombatFiring.IsEnabled = CombatInputMode.SelectedIndex != 6;
            InvalidateCombatSettings();
        };
        CombatSeedCount.SelectionChanged += (_, _) => InvalidateCombatSettings();
        CombatFiring.Checked += (_, _) => InvalidateCombatSettings();
        CombatFiring.Unchecked += (_, _) => InvalidateCombatSettings();
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
            if (CombatInputMode.SelectedIndex == 6)
            {
                var recorded = session?.Inputs.Select(i => new CombatInputStep(i.DurationMs, i.Input)).ToArray() ?? timelineReplay?.Commands.Where(c => c.Kind == "advance" && c.Input != null).Select(c => new CombatInputStep(c.DurationMs, c.Input!)).ToArray();
                if (recorded is { Length: > 0 }) combatRecordedInputs = recorded.ToImmutableArray();
                if (combatRecordedInputs.IsEmpty) throw new ArgumentException("Record movement or firing in Play before choosing Recorded inputs.");
            }
            ClearViewportEditors(); Stop(this, new()); var request = combatRequest;
            if (pathPreviewTask != null) await pathPreviewTask;
            if (request != combatRequest) return;
            var project = Snapshot();
            var phase = CombatPhase.IsChecked == true ? selectedNode is { Kind: "State", Label: not "Root" } ? selectedNode.Label : throw new ArgumentException("Select a named phase in Behavior before enabling Selected phase.") : null;
            var options = CombatOptions(project);
            options.Validate();
            var expectedDocument = document; var revision = document.Revision; var selected = selectedNode?.Id; var expectedScenario = scenarioRevision;
            cancellation = new(); combatCancellation = cancellation;
            Viewport.CombatPreviewOpen = true; CombatPreviewControls.Visibility = Visibility.Visible;
            CombatPreviewButton.Content = "Close preview"; CombatRerun.IsEnabled = false; CombatExport.IsEnabled = false;
            CombatPointInfo.Text = "Preparing independent native runs…"; Viewport.CombatProgress = "Preparing native runs…";
            WorkspaceTabs.SelectedIndex = 0;
            bool Current() => !cancellation.IsCancellationRequested && document == expectedDocument && document.Revision == revision && scenarioRevision == expectedScenario && selectedEntity?.Id == project.EntityId && selectedNode?.Id == selected;
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
            CombatScenario.ItemsSource = result.Scenarios.Select(s => "Seed " + s.Seed).ToArray(); CombatScenario.SelectedIndex = 0; CombatScenario.Visibility = result.Scenarios.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            CombatPointInfo.Text = "Choose a dot to inspect its native test. Settings and source changes clear this result.";
            Status.Text = "Attack preview complete · " + result.Points.Count(p => p.Sample != null) + " tested starts per seed · " + result.Scenarios.Length + " seeds · " + result.Runtime;
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
    CombatPreviewOptions CombatOptions(StudioProject project)
    {
        var duration = new[] { 3000, 5000, 10000 }[CombatDuration.SelectedIndex];
        var inputs = ImmutableArray.CreateBuilder<CombatInputStep>();
        var firing = CombatFiring.IsChecked == true;
        EncounterInput Input(double x, double y) => new(x, y, firing, project.Arena.BossX, project.Arena.BossY);
        if (CombatInputMode.SelectedIndex == 6)
        {
            var remaining = duration;
            foreach (var step in combatRecordedInputs)
            {
                var ms = Math.Min(remaining, step.DurationMs);
                if (ms <= 0) break;
                inputs.Add(step with { DurationMs = ms }); remaining -= ms;
            }
        }
        else if (CombatInputMode.SelectedIndex == 5)
        {
            foreach (var (x, y) in new[] { (1d, 0d), (0d, 1d), (-1d, 0d), (0d, -1d) })
                inputs.Add(new(duration / 4, Input(x, y)));
        }
        else
        {
            var (x, y) = CombatInputMode.SelectedIndex switch { 1 => (1d, 0d), 2 => (-1d, 0d), 3 => (0d, -1d), 4 => (0d, 1d), _ => (0d, 0d) };
            inputs.Add(new(duration, Input(x, y)));
        }
        var label = ((ComboBoxItem)CombatInputMode.SelectedItem).Content.ToString()! + (firing && CombatInputMode.SelectedIndex != 6 ? " + firing" : "");
        var seeds = CombatSeedCount.SelectedIndex == 0 ? ImmutableArray<int>.Empty : Enumerable.Range(0, 3).Select(i => checked(project.Seed + i)).ToImmutableArray();
        return new(duration, new[] { 5, 9, 1 }[CombatGrid.SelectedIndex], double.Parse(CombatSpacing.Text, CultureInfo.InvariantCulture),
            CombatGrid.SelectedIndex == 2 ? project.Player.StartX : 64.5, CombatGrid.SelectedIndex == 2 ? project.Player.StartY : 64.5)
        { Inputs = inputs.ToImmutable(), Seeds = seeds, InputLabel = label };
    }
    void CombatModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Viewport == null) return;
        Viewport.CombatView = (CombatPreviewView)Math.Max(0, CombatMode.SelectedIndex); Viewport.InvalidateVisual();
    }
    void CombatScenarioChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Viewport == null) return; Viewport.CombatScenarioIndex = Math.Max(0, CombatScenario.SelectedIndex); CombatPointInfo.Text = "Choose a dot to inspect this seed's native input path.";
    }
    void DescribeCombatPoint(int index)
    {
        if (Viewport.CombatReport is not { } report || index < 0 || index >= report.Points.Length) return;
        var scenario = report.Scenario(Viewport.CombatScenarioIndex); var point = scenario.Points[index];
        var prefix = string.Create(CultureInfo.InvariantCulture, $"Start ({point.X:0.###}, {point.Y:0.###}) · ");
        CombatPointInfo.Text = prefix + (point.Sample is not { } sample ? "Unavailable: " + point.Error :
            $"{sample.HealthLoss} HP lost · {sample.RemainingHp} / {sample.InitialHp} HP · {sample.ActualMs} ms · {sample.EmittedProjectiles} emitted · " +
            (sample.PlayerDied ? "player died" : sample.IncompleteMotion ? "partial motion coverage" : "completed") +
            $" · seed {scenario.Seed} · {sample.PlayerProjectiles} player shots · reference exposure: {scenario.Reference.ExposureSamples[index]} samples" + (sample.PlayerMoved ? " · native player position changed" : "") + (report.NoDamageObserved(index) ? " · no damage across tested seeds" : ""));
    }
    void ExportCombatPreview(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (Viewport.CombatReport is not { } report) return;
        var dialog = new SaveFileDialog { Filter = "Attack preview JSON|*.json", FileName = "attack-preview.json" };
        if (dialog.ShowDialog() == true) { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, BehaviorDocument.JsonOptions)); Status.Text = "Attack preview results exported"; }
    });
}
