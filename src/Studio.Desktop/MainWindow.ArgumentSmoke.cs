using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    void RunArgumentUiSmoke(string output, Action<bool, string> check)
    {
        var baseline = document.Source; var root = EncounterRoot;
        const string fixture = """
        new State(children: new IStateChildren[] {
            new State("opening",
                new Prioritize(children: new CycleBehavior[] { new Follow(.3), new Wander(.1) }),
                new Timed(period: 500, behaviors: new Behavior[] { new Shoot(8) }),
                new TimedTransition(1000, "closing")),
            new State("closing", new OnDeathBehavior(new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable))) })
        """;
        document.ReplaceSource(document.Source.Remove(root.Start, root.Length).Insert(root.Start, fixture), d => adapter.Validate(d, selectedEntity!.Id)); Changed(); WorkspaceTabs.SelectedIndex = 1;
        var phase = EncounterRoot.StateChildren.Single(n => n.Label == "opening");
        var cycle = phase.Descendants().Single(n => n.Kind == "ArgumentGroup" && n.Argument!.ElementType == "CycleBehavior");
        var timed = phase.Descendants().Single(n => n.Kind == "ArgumentGroup" && n.Argument!.ElementType == "Behavior");
        var follow = cycle.Children[0]; var wander = cycle.Children[1];
        SelectNode(wander.Id); UpdateLayout();
        check(TreeItems().Count(i => ((BehaviorNode)i.Tag).Kind == "ArgumentGroup") == 3 && upStructureButton!.IsEnabled, "Desktop hierarchy exposes normal child arrays and enables native grouped reorder controls");
        upStructureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(cycle.Id).Children.First().Id == wander.Id && selectedNode!.Id == wander.Id, "Desktop array reorder changes native source while retaining focused identity");
        Undo(this, new());
        SelectNode(cycle.Id);
        check(Inspector.Children.OfType<TextBlock>().Any(t => t.Text == "Native typed argument list") && !Inspector.Children.OfType<Button>().Any(b => Equals(b.Content, "Apply changes")), "Argument container inspector describes its role without fake primitive parameter controls");
        BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "Taunt"));
        check(!createNativeButton!.IsEnabled, "Creation form disables incompatible Behavior insertion into a CycleBehavior array");
        SelectNode(cycle.Id); BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "Wander"));
        check(createNativeButton!.IsEnabled, "Creation form accepts a compatible native cycle child in the selected array");
        createNativeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var created = selectedNode!.Id;
        check(document.Parent(created)!.Id == cycle.Id && document.Find(cycle.Id).Children.Count == 3, "Created native child is inserted into the existing named array argument");
        Undo(this, new());
        SelectAuthoringNode(follow.Id, ModifierKeys.None); UpdateLayout();
        var item = TreeItems().Single(i => ((BehaviorNode)i.Tag).Id == timed.Id);
        var data = DragData(new NodeDrag(document.Revision, selectedEntity!.Id, [follow.Id]));
        var over = SyntheticDragEvent(data, item, new Point(35, 14), DragDrop.DragOverEvent); item.RaiseEvent(over);
        check(over.Effects == DragDropEffects.Move && Status.Text.Contains("Drop into behaviors"), "Routed drag-over targets the typed array destination");
        item.RaiseEvent(SyntheticDragEvent(data, item, new Point(35, 14), DragDrop.DropEvent));
        check(document.Find(timed.Id).Children.Last().Id == follow.Id && document.Find(cycle.Id).Children.Single().Id == wander.Id, "Routed drop moves a cycle into a compatible behavior array and preserves selection");
        SelectNode(phase.Id); lifecyclePanel!.IsExpanded = true; UpdateLayout();
        check(Graph.PhaseBounds(phase.Id) != null && Graph.TransitionBounds(phase.Children.Single(n => n.Kind == "TimedTransition").Id) != null && lifecycleRows.Count == 4, "Graph and lifecycle views see native phases/actions through their argument-array boundary");
        CaptureView(this, Path.Combine(output, "grouped-arguments.png"));
        var deathChild = EncounterRoot.Descendants().Single(n => n.Kind == "ConditionEffectBehavior"); SelectNode(deathChild.Id); lifecyclePanel!.IsExpanded = true;
        check(lifecycleRows.Count == 1 && ((StackPanel)lifecyclePanel.Content).Children.OfType<TextBlock>().Any(t => t.Text == "Death"), "Wrapped condition's inspector shows the actual death-to-entry dispatch without an exit callback");
        UpdateLayout();
        check(FindScrollViewer(BehaviorTree)!.HorizontalOffset == 0, "Selecting a deeply nested long action preserves the parent hierarchy's horizontal position");
        CaptureView(this, Path.Combine(output, "death-wrapper.png"));
        SelectNode(phase.Id); BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "Prioritize"));
        check(createNativeButton!.IsEnabled && constructorInputs["children"].Text.Contains("Wander"), "New cycle wrappers start with an editable native child without requiring an array expression from the user");
        createNativeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode!.Kind == "Prioritize" && selectedNode.Children.Single().Children.Single().Kind == "Wander", "Starter wrapper creation exposes its real typed child array immediately");
        document.ReplaceSource(baseline); Changed(); WorkspaceTabs.SelectedIndex = 0;
    }
}
