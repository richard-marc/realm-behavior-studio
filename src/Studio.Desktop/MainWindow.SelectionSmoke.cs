using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    void RunSelectionUiSmoke(string output, Action<bool, string> check)
    {
        var baseline = document.Source; var root = EncounterRoot;
        const string fixture = """
        new State(new State("opening", new Shoot(radius: 10, count: 2), new Wander(speed: .2), new TimedTransition(1000, "loop")),
            new State("loop", new State("inner", new TimedTransition(1000, "opening"))))
        """;
        document.ReplaceSource(document.Source.Remove(root.Start, root.Length).Insert(root.Start, fixture)); Changed(); WorkspaceTabs.SelectedIndex = 1;
        var opening = EncounterRoot.Children[0]; var loop = EncounterRoot.Children[1]; var shoot = opening.Children[0]; var wander = opening.Children[1];
        SelectAuthoringNode(shoot.Id, ModifierKeys.None); SelectAuthoringNode(wander.Id, ModifierKeys.Control);
        check(nodeSelection.SetEquals(new[] { shoot.Id, wander.Id }) && Graph.Selection.SetEquals(nodeSelection) && SelectionCount.Text == "2 selected", "Ctrl selection highlights multiple tree nodes and synchronizes the graph and count");
        SelectAuthoringNode(wander.Id, ModifierKeys.Control); UpdateLayout();
        var focusedItem = TreeItems().Single(i => ((BehaviorNode)i.Tag).Id == wander.Id);
        var focusedBody = (Border)focusedItem.Template.FindName("Body", focusedItem);
        check(nodeSelection.SetEquals(new[] { shoot.Id }) && focusedBody.Background is SolidColorBrush { Color: var color } && color == Colors.Transparent, "Ctrl toggle removes the selected fill while retaining the focused inspector item");
        SelectAuthoringNode(shoot.Id, ModifierKeys.None); SelectAuthoringNode(opening.Children[2].Id, ModifierKeys.Shift);
        check(nodeSelection.Count == 3, "Shift selection includes the visible source-order range");
        SelectAuthoringNode(shoot.Id, ModifierKeys.None); SelectAuthoringNode(wander.Id, ModifierKeys.Control);
        BehaviorClipboard? clipboard = null; var originalRead = readNodeClipboard; var originalWrite = writeNodeClipboard;
        readNodeClipboard = () => clipboard; writeNodeClipboard = value => clipboard = value;
        try
        {
            CopySelectedNodes(); SelectNode(loop.Id); PasteSelectedNodes();
            check(clipboard?.Expressions.Count == 2 && nodeSelection.Count == 2 && document.Find(loop.Id).Children.Count == 3, "Desktop copy/paste inserts independent multi-node selections through the clipboard boundary");
            Undo(this, new());
            SelectAuthoringNode(shoot.Id, ModifierKeys.None); SelectAuthoringNode(wander.Id, ModifierKeys.Control);
            UpdateLayout();
            var dropItem = TreeItems().Single(i => ((BehaviorNode)i.Tag).Id == loop.Id);
            var dropData = DragData(new NodeDrag(document.Revision, selectedEntity!.Id, nodeSelection.ToArray()));
            var over = SyntheticDragEvent(dropData, dropItem, new Point(35, 14), DragDrop.DragOverEvent);
            dropItem.RaiseEvent(over);
            check(over.Effects == DragDropEffects.Move && Status.Text.Contains("Drop into loop"), "Routed drag-over identifies the phase destination and move feedback");
            var drop = SyntheticDragEvent(dropData, dropItem, new Point(35, 14), DragDrop.DropEvent);
            dropItem.RaiseEvent(drop);
            check(document.Find(loop.Id).Children.Skip(1).Select(n => n.Id).SequenceEqual(new[] { shoot.Id, wander.Id }) && selectedNode!.Id == shoot.Id, "Routed drop moves a selected block across phases and keeps its stable selection");
            Undo(this, new());
            SelectAuthoringNode(shoot.Id, ModifierKeys.None); SelectAuthoringNode(wander.Id, ModifierKeys.Control); UpdateLayout();
            dropItem = TreeItems().Single(i => ((BehaviorNode)i.Tag).Id == loop.Id);
            dropData = DragData(new NodeDrag(document.Revision, selectedEntity.Id, nodeSelection.ToArray()));
            var copyOver = SyntheticDragEvent(dropData, dropItem, new Point(35, 14), DragDrop.DragOverEvent, DragDropKeyStates.ControlKey); dropItem.RaiseEvent(copyOver);
            check(copyOver.Effects == DragDropEffects.Copy, "Ctrl drag-over advertises copy rather than move");
            var copyDrop = SyntheticDragEvent(dropData, dropItem, new Point(35, 14), DragDrop.DropEvent, DragDropKeyStates.ControlKey); dropItem.RaiseEvent(copyDrop);
            check(document.Find(opening.Id).Children.Count == 3 && document.Find(loop.Id).Children.Count == 3 && !nodeSelection.Contains(shoot.Id) && !nodeSelection.Contains(wander.Id), "Ctrl routed drop creates fresh nodes while preserving the source selection's branches");
            Undo(this, new()); UpdateLayout();
            var edgeItem = TreeItems().Single(i => ((BehaviorNode)i.Tag).Id == wander.Id);
            var before = DropDestination(edgeItem, document.Find(wander.Id), 0); var after = DropDestination(edgeItem, document.Find(wander.Id), double.MaxValue);
            check(before.Parent!.Id == opening.Id && before.Index == 1 && after.Index == 2, "Drop row edges resolve before/after source insertion positions");
            SelectAuthoringNode(shoot.Id, ModifierKeys.None); SelectAuthoringNode(wander.Id, ModifierKeys.Control); GroupSelectedNodes();
            var groupId = selectedNode!.Id;
            check(selectedNode.Kind == "State" && selectedNode.Children.Select(n => n.Id).SequenceEqual(new[] { shoot.Id, wander.Id }), "Desktop group command creates a nested phase around the selected actions");
            TogglePhaseCollapse(groupId); UpdateLayout(); CaptureView(this, Path.Combine(output, "selection-authoring.png"));
            check(CollapsedNodes.Contains(groupId) && Graph.CollapsedNodes.Contains(groupId) && !TreeItems().Single(i => ((BehaviorNode)i.Tag).Id == groupId).IsExpanded, "Collapsing a phase synchronizes tree and graph details");
            var snapshot = Snapshot(); var projectPath = Path.Combine(output, "selection-layout.rbs"); BehaviorDocument.Save(projectPath, snapshot);
            check(BehaviorDocument.Load(projectPath).CollapsedNodes.Contains(groupId), "Project persistence retains collapsed phase identities");
            LoadProject(projectPath); UpdateLayout();
            check(CollapsedNodes.Contains(groupId) && !TreeItems().Single(i => ((BehaviorNode)i.Tag).Id == groupId).IsExpanded, "Desktop project reopening restores collapsed phase details");
            SelectNode(shoot.Id);
            check(!CollapsedNodes.Contains(groupId) && TreeItems().Single(i => ((BehaviorNode)i.Tag).Id == groupId).IsExpanded, "Navigation to a hidden action expands its ancestors");
            OpenCommands(this, new()); CommandSearch.Text = "Move selection into loop";
            check(CommandResults.Items.Count == 1 && CommandResults.SelectedItem is AuthoringCommand { Enabled: true }, "Command search finds a source-backed cross-parent move");
            ExecuteSelectedCommand(this, new());
            check(document.Find(loop.Id).Children.Last().Id == shoot.Id && CommandPanel.Visibility == Visibility.Collapsed, "Executing the searched move updates the document and closes the palette");
            OpenCommands(this, new()); CommandSearch.Text = "Go to inner"; UpdateLayout(); CaptureView(this, Path.Combine(output, "command-search.png")); ExecuteSelectedCommand(this, new());
            check(selectedNode!.Label == "inner", "Command search navigates to named phases");
            var staleSource = document.Source;
            try { ApplyNodeDrop(new(document.Revision - 1, selectedEntity.Id, [shoot.Id]), loop.Id, 0, false); throw new Exception("Stale drag was accepted"); }
            catch (ArgumentException) { check(document.Source == staleSource, "Stale drag payload is rejected without mutating source"); }
        }
        finally { readNodeClipboard = originalRead; writeNodeClipboard = originalWrite; }
        CollapsedNodes.Clear(); document.ReplaceSource(baseline); projectPath = null; Changed(); CloseCommands();
    }
    static DragEventArgs SyntheticDragEvent(IDataObject data, DependencyObject target, Point point, RoutedEvent routedEvent, DragDropKeyStates keyStates = DragDropKeyStates.None)
    {
        // WPF's drag event constructor is internal. Exercise the real routed handlers without an OS mouse/clipboard takeover.
        var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single(c => c.GetParameters().Any(p => p.ParameterType == typeof(Point)));
        var values = constructor.GetParameters().Select(p => p.ParameterType == typeof(IDataObject) ? (object)data :
            p.ParameterType == typeof(DragDropKeyStates) ? keyStates : p.ParameterType == typeof(DragDropEffects) ? DragDropEffects.Move | DragDropEffects.Copy :
            p.ParameterType == typeof(Point) ? point : p.ParameterType == typeof(DependencyObject) ? target : throw new InvalidOperationException("Unknown WPF drag parameter: " + p.Name)).ToArray();
        var args = (DragEventArgs)constructor.Invoke(values); args.RoutedEvent = routedEvent; args.Effects = DragDropEffects.None; return args;
    }
}
