using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    void RunTransitionUiSmoke(string output, Action<bool, string> check)
    {
        var baseline = document.Source; var root = EncounterRoot;
        const string fixture = """
        new State(
            new State("approach", new Follow(speed: 0.3),
                new HpLessTransition(threshold: .8, targetState: "attack"),
                new HpLessTransition(threshold: .3, targetState: "attack"),
                new TimedTransition(time: 1000, targetState: "attack")),
            new State("attack", new Shoot(radius: 10, count: 7)),
            new State("unused", new Wander(speed: .1)))
        """;
        document.ReplaceSource(document.Source.Remove(root.Start, root.Length).Insert(root.Start, fixture)); Changed();
        WorkspaceTabs.SelectedIndex = 1; PhaseReview.IsExpanded = true;
        UpdateLayout(); CaptureView(this, Path.Combine(output, "transition-review.png"));
        var edges = BehaviorAnalysis.Analyze(EncounterRoot).Transitions;
        check(PhaseDiagnostics.Children.OfType<Button>().Count() == 2 && PhaseReview.Header.ToString()!.Contains("2 issues"), "Phase review groups priority conflicts and unreachable phases without losing identities");
        var row = Graph.TransitionBounds(edges[1].Node.Id) ?? throw new InvalidOperationException("Transition row was not rendered.");
        Graph.SelectAt(new Point(row.Left + 10, row.Top + 10));
        check(selectedNode!.Id == edges[1].Node.Id && Inspector.Children.OfType<TextBlock>().Any(t => t.Text.Contains("Priority 2")), "Graph transition row selects its exact node and exposes native priority in the inspector");
        organizeExpander!.IsExpanded = true; upStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(BehaviorAnalysis.Analyze(EncounterRoot).Transitions.First().Node.Id == edges[1].Node.Id && !ReviewDiagnostics().Any(d => d.Message.StartsWith("Priority conflict")), "Transition move-up updates graph order and clears the conflict immediately");
        Undo(this, new());
        check(ReviewDiagnostics().Any(d => d.Message.StartsWith("Priority conflict")), "Undo restores the transition order and its diagnostic");
        document.ReplaceSource(document.Source.Replace("time: 1000, targetState: \"attack\"", "time: 1000, targetState: \"missing\"")); Changed();
        check(PhaseDiagnostics.Children.OfType<Button>().Count() == 3 && PhaseReview.Header.ToString()!.Contains("3 issues"), "Phase review includes blocking missing-target diagnostics");
        edges = BehaviorAnalysis.Analyze(EncounterRoot).Transitions;
        Button Diagnostic(string prefix) => PhaseDiagnostics.Children.OfType<Button>().Single(b => ((DocumentDiagnostic)b.Tag).Message.StartsWith(prefix));
        Diagnostic("Missing transition target").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode!.Id == edges[2].Node.Id && ParametersTab.IsSelected && Graph.Highlight == edges[2].Node.Id, "Missing-target diagnostic opens the exact transition in graph, tree and inspector");
        Diagnostic("Unreachable phase").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode!.Label == "unused" && BehaviorTree.SelectedItem is TreeViewItem { Tag: BehaviorNode phase } && phase.Id == selectedNode.Id, "Unreachable-phase diagnostic navigates to its phase");
        Diagnostic("Priority conflict").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        organizeExpander!.IsExpanded = true; UpdateLayout(); CaptureView(this, Path.Combine(output, "transition-priority.png"));
        check(selectedNode!.Id == edges[1].Node.Id && Graph.TransitionBounds(selectedNode.Id) != null, "Priority diagnostic focuses the condition row with a destination route");
        var lockBox = Inspector.Children.OfType<CheckBox>().Single(); lockBox.IsChecked = true;
        check(!upStructureButton!.IsEnabled && !downStructureButton!.IsEnabled, "Transition priority controls respect the existing region lock");
        lockBox.IsChecked = false;
        document.ReplaceSource(baseline); PhaseReview.IsExpanded = false; Changed();
        check(!ReviewDiagnostics().Any(d => d.Message.StartsWith("Priority conflict") || d.Message.StartsWith("Missing transition target")), "Switching back to the original encounter clears stale review diagnostics");
    }
}
