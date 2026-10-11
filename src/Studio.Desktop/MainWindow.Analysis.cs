using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    BehaviorNode EncounterRoot => document.Entities.Single(e => e.EntityId == selectedEntity!.Id).Root;
    BehaviorNode AuthoringRoot=>document.Definitions.FirstOrDefault(d=>d.EntityId==definitionViewKey)?.Root??EncounterRoot;
    IEnumerable<DocumentDiagnostic> ReviewDiagnostics() => adapter.Validate(document, selectedEntity!.Id)
        .Concat(BehaviorAnalysis.Analyze(AuthoringRoot).Diagnostics);
    void RefreshPhaseReview(IReadOnlyList<DocumentDiagnostic> diagnostics)
    {
        PhaseReview.Header = diagnostics.Count == 0 ? "Phase review · no issues" : $"Phase review · {diagnostics.Count} {(diagnostics.Count == 1 ? "issue" : "issues")}";
        PhaseDiagnostics.Children.Clear();
        foreach (var diagnostic in diagnostics)
        {
            var text = new TextBlock { Text = (diagnostic.IsError ? "Error · " : "Notice · ") + diagnostic.Message, TextWrapping = TextWrapping.Wrap,
                Foreground = EncounterViewport.Brush(diagnostic.IsError ? "#E5A195" : "#DAAE84") };
            var button = new Button { Content = text, Tag = diagnostic, Style = (Style)FindResource("QuietButton"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new(0, 0, 0, 3), ToolTip = "Select this phase or transition" };
            button.Click += (_, _) => NavigateDiagnostic(diagnostic);
            PhaseDiagnostics.Children.Add(button);
        }
    }
    void NavigateDiagnostic(DocumentDiagnostic diagnostic)
    {
        if (diagnostic.NodeId is not { } id) return;
        WorkspaceTabs.SelectedIndex = 1; SelectNode(id);
        var node = document.Find(id);
        var owner = node.Kind == "State" ? node : document.Parent(id);
        // Keep warnings usable in long encounters, without jumping the graph on routine parameter selection.
        if (owner != null)
        {
            Graph.UpdateLayout();
            var transitions = BehaviorAnalysis.Analyze(AuthoringRoot).Transitions;
            var edge = transitions.FirstOrDefault(t => t.Node.Id == id);
            if (edge != null && Graph.PhaseBounds(owner.Id) is { } phase && phase.Height > GraphScroll.ViewportHeight && Graph.TransitionBounds(id) is { } row) Graph.BringIntoView(row);
            else Graph.BringPhaseIntoView(owner.Id);
        }
    }
    void AddTransitionSummary(Guid id)
    {
        var edge = BehaviorAnalysis.Analyze(AuthoringRoot).Transitions.FirstOrDefault(t => t.Node.Id == id);
        if (edge == null) return;
        Inspector.Children.Add(new TextBlock { Text = $"{edge.Condition} → {edge.TargetName ?? "dynamic target"}", TextWrapping = TextWrapping.Wrap,
            Foreground = EncounterViewport.Brush("#CFD3DB"), Margin = new(0, 12, 0, 4) });
        Inspector.Children.Add(new TextBlock { Text = $"Priority {edge.Priority} · first matching transition wins", TextWrapping = TextWrapping.Wrap,
            Foreground = EncounterViewport.Brush("#92969F"), FontSize = 11, ToolTip = "Child transitions take precedence over parents. Use Organize to move this transition up or down. Timer conditions run on the native 100 ms tick." });
    }
}
