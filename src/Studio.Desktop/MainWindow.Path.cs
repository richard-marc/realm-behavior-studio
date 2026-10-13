using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow
{
    bool pathEditing;
    Button? pathEditorButton, pathPreviewButton;
    CancellationTokenSource? pathPreviewCancellation;
    BehaviorDocument? pathPreviewDocument;
    long pathPreviewRevision;
    Guid pathPreviewNode;
    Task? pathPreviewTask;
    void ClearPathEditor()
    {
        pathEditing = false; pathPreviewCancellation?.Cancel(); Viewport.Path = null;
        if (pathEditorButton != null) pathEditorButton.Content = "Edit movement path";
        if (pathPreviewButton != null) pathPreviewButton.Visibility = Visibility.Collapsed;
    }
    void RefreshPathSelection()
    {
        if (pathPreviewCancellation != null && (document != pathPreviewDocument || document.Revision != pathPreviewRevision || selectedNode?.Id != pathPreviewNode)) pathPreviewCancellation.Cancel();
        if (!pathEditing) return;
        if (selectedNode == null || adapter.ReadPathPattern(document, selectedNode) is not { } pattern) { ClearPathEditor(); return; }
        Viewport.Path = new(selectedNode.Id, document.Revision, pattern, RegionEditable(selectedNode));
    }
    void AddPathControls(BehaviorNode node)
    {
        pathEditorButton = null; pathPreviewButton = null;
        if (adapter.PathKind(document, node) == null) return;
        if (adapter.PathKind(document, node) == "NewMove" && !adapter.EffectiveCallbacks(document, node).Any(c => c.Method == "TickCoreOrdered"))
            Inspector.Children.Add(new TextBlock { Text = "NewMove needs ordered dispatch. It has no movement tick in a normal State; source controllers may define their own dispatch.", TextWrapping = TextWrapping.Wrap, Foreground = EncounterViewport.Brush("#929D96"), FontSize = 11, Margin = new(0, 12, 0, 0) });
        if (adapter.ReadPathPattern(document, node) == null)
        {
            Inspector.Children.Add(new TextBlock { Text = "Visual paths need known coordinates and anchor mode. Edit these expressions in Parameters or Source.", TextWrapping = TextWrapping.Wrap, Foreground = EncounterViewport.Brush("#929D96"), FontSize = 11, Margin = new(0, 12, 0, 0) }); return;
        }
        pathEditorButton = new Button { Content = pathEditing ? "Close path editor" : "Edit movement path", Style = (Style)FindResource("QuietButton"), Margin = new(0, 10, 0, 0), IsEnabled = RegionEditable(node) };
        pathEditorButton.Click += (_, _) =>
        {
            if (pathEditing) { ClearPathEditor(); return; }
            ClearAttackEditor(); ClearMovementEditor(); ClearSweepEditor(); ClearRingEditor(); Stop(this, new()); pathEditing = true; RefreshPathSelection();
            pathEditorButton!.Content = "Close path editor"; pathPreviewButton!.Visibility = Visibility.Visible; WorkspaceTabs.SelectedIndex = 0; UpdateLayout(); Viewport.FitPath(); Viewport.Focus();
            Status.Text = "Path editor · requested native coordinates · preview samples the whole phase";
        }; Inspector.Children.Add(pathEditorButton);
        pathPreviewButton = new Button { Content = pathPreviewCancellation == null ? "Preview this phase" : "Cancel preview", Visibility = pathEditing ? Visibility.Visible : Visibility.Collapsed, Style = (Style)FindResource("QuietButton"), Margin = new(0, 6, 0, 0), ToolTip = "Runs 3 seconds of the whole phase with the current seed and a stationary test player. Forced phase bypasses earlier encounter setup. All active actions affect the sampled path." };
        pathPreviewButton.Click += (_, _) =>
        {
            if (pathPreviewCancellation != null) { pathPreviewCancellation.Cancel(); return; }
            pathPreviewTask = TryRuntime(RunPathPreviewAsync);
        }; Inspector.Children.Add(pathPreviewButton);
    }
    void ApplyPathEdit(PathSelection selection, IReadOnlyDictionary<string, double> values)
    {
        Try(() => CommitPathEdit(selection, values)); RefreshPathSelection();
    }
    void CommitPathEdit(PathSelection selection, IReadOnlyDictionary<string, double> values)
    {
        adapter.ApplyPathValues(document, selectedEntity!.Id, selection.NodeId, selection.Revision, values); Changed(selection.NodeId);
    }
    async Task RunPathPreviewAsync()
    {
        await AwaitCombatCancellation();
        if (!pathEditing || selectedNode == null) return;
        var expected = document; var revision = document.Revision; var nodeId = selectedNode.Id; var project = Snapshot();var expectedScenario=scenarioRevision;
        BehaviorNode? phase = selectedNode;
        while (phase != null && phase.Kind != "State") phase = document.Parent(phase.Id);
        var phaseName = phase == null || phase.Label == "Root" ? null : document.StateName(phase.Id);
        using var cancellation = new CancellationTokenSource(); pathPreviewCancellation = cancellation; pathPreviewDocument = expected; pathPreviewRevision = revision; pathPreviewNode = nodeId;
        var button = pathPreviewButton!; button.Content = "Cancel preview"; Viewport.PathTrace = null; Status.Text = "Sampling native phase movement…";
        try
        {
            var trace = await NativeMovementPreview.SampleAsync(adapter, project, phaseName, cancellation: cancellation.Token);
            if (cancellation.IsCancellationRequested || document != expected || document.Revision != revision || scenarioRevision!=expectedScenario || selectedNode?.Id != nodeId || !pathEditing) return;
            Viewport.PathTrace = trace; Viewport.FitPath();
            Status.Text = "Native movement samples · " + (phaseName == null ? "full encounter" : "forced phase " + phaseName) + " · seed " + trace.Seed + " · stationary test player";
        }
        finally { if (ReferenceEquals(pathPreviewCancellation, cancellation)) { pathPreviewCancellation = null; button.Content = "Preview this phase"; } }
    }
}
