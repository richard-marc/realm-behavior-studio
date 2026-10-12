using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    const string ClipboardFormat = "RealmBehaviorStudio.Nodes.v1", DragFormat = "RealmBehaviorStudio.Drag.v1";
    readonly HashSet<Guid> nodeSelection = [];
    readonly Dictionary<BehaviorDocument, HashSet<Guid>> collapsedByDocument = [];
    HashSet<Guid> CollapsedNodes
    {
        get { if (!collapsedByDocument.TryGetValue(document, out var nodes)) collapsedByDocument[document] = nodes = []; return nodes; }
    }
    Guid? selectionAnchor; bool retainNodeSelection, dragStarted; Point? dragStart;
    Func<BehaviorClipboard?> readNodeClipboard = ReadNodeClipboard;
    Action<BehaviorClipboard> writeNodeClipboard = WriteNodeClipboard;
    sealed record NodeDrag(long Revision, string EntityId, Guid[] Ids);
    static DataObject DragData(NodeDrag drag) => new(DragFormat, JsonSerializer.Serialize(drag));
    NodeDrag? ReadNodeDrag(IDataObject data)
    {
        if (!data.GetDataPresent(DragFormat) || data.GetData(DragFormat) is not string json || json.Length > 100_000) return null;
        try
        {
            var drag = JsonSerializer.Deserialize<NodeDrag>(json);
            if (drag == null || drag.Revision != document.Revision || drag.EntityId != selectedEntity?.Id || drag.Ids == null || drag.Ids.Length is < 1 or > 2048) return null;
            var valid = AuthoringRoot.Descendants().Select(n => n.Id).ToHashSet();
            return drag.Ids.All(valid.Contains) ? drag : null;
        }
        catch (JsonException) { return null; }
    }
    public sealed record AuthoringCommand(string Title, string Detail, Action Execute, bool Enabled = true);
    static BehaviorClipboard? ReadNodeClipboard()
    {
        if (!Clipboard.ContainsData(ClipboardFormat)) return null;
        return Clipboard.GetData(ClipboardFormat) is string {Length:<=8_000_000} value ? JsonSerializer.Deserialize<BehaviorClipboard>(value, BehaviorDocument.JsonOptions) : null;
    }
    static void WriteNodeClipboard(BehaviorClipboard nodes)
    {
        var json = JsonSerializer.Serialize(nodes, BehaviorDocument.JsonOptions); var data = new DataObject();
        data.SetData(ClipboardFormat, json); data.SetText(json); Clipboard.SetDataObject(data, true);
    }
    IEnumerable<TreeViewItem> TreeItems()
    {
        IEnumerable<TreeViewItem> Walk(TreeViewItem item) => new[] { item }.Concat(item.Items.OfType<TreeViewItem>().SelectMany(Walk));
        return BehaviorTree.Items.OfType<TreeViewItem>().SelectMany(Walk);
    }
    static T? Ancestor<T>(DependencyObject? item) where T : DependencyObject
    {
        while (item != null) { if (item is T match) return match; item = item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item); }
        return null;
    }
    void InitializeSelectionControls()
    {
        BehaviorTree.AllowDrop = true;
        BehaviorTree.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (Ancestor<ToggleButton>(e.OriginalSource as DependencyObject) != null || Ancestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { Tag: BehaviorNode node }) return;
            dragStart = e.GetPosition(BehaviorTree); dragStarted = false;
            if (!nodeSelection.Contains(node.Id) || Keyboard.Modifiers != ModifierKeys.None) SelectAuthoringNode(node.Id, Keyboard.Modifiers);
            else { retainNodeSelection = true; try { SelectNode(node.Id); } finally { retainNodeSelection = false; } }
            BehaviorTree.Focus(); e.Handled = true;
        };
        BehaviorTree.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!dragStarted && Keyboard.Modifiers == ModifierKeys.None && Ancestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { Tag: BehaviorNode node } && Ancestor<ToggleButton>(e.OriginalSource as DependencyObject) == null)
                SelectAuthoringNode(node.Id, ModifierKeys.None);
            dragStart = null;
        };
        BehaviorTree.PreviewMouseMove += (_, e) =>
        {
            if (dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed || dragStarted || nodeSelection.Count == 0) return;
            var point = e.GetPosition(BehaviorTree);
            if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (nodeSelection.Any(id => !document.CanOrganize(id) || !RegionEditable(document.Find(id)))) return;
            dragStarted = true;
            DragDrop.DoDragDrop(BehaviorTree, DragData(new NodeDrag(document.Revision, selectedEntity!.Id, nodeSelection.ToArray())), DragDropEffects.Move | DragDropEffects.Copy);
            dragStart = null; RefreshSelectionVisuals();
        };
        BehaviorTree.DragOver += (_, e) =>
        {
            var scroll = FindScrollViewer(BehaviorTree); var pointer = e.GetPosition(BehaviorTree);
            if (pointer.Y < 22) scroll?.LineUp(); else if (pointer.Y > BehaviorTree.ActualHeight - 22) scroll?.LineDown();
            e.Effects = DragDropEffects.None;
            if (ReadNodeDrag(e.Data) is { } drag && Ancestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { Tag: BehaviorNode node } item)
            {
                var (parent, index) = DropDestination(item, node, e.GetPosition(item).Y);
                if (parent != null && !drag.Ids.Any(id => parent.Path == document.Find(id).Path || parent.Path.StartsWith(document.Find(id).Path + "/", StringComparison.Ordinal)) && !document.LockedNodes.Select(document.Find).Any(l => parent.Path == l.Path || parent.Path.StartsWith(l.Path + "/", StringComparison.Ordinal)))
                {
                    e.Effects = (e.KeyStates & DragDropKeyStates.ControlKey) != 0 ? DragDropEffects.Copy : DragDropEffects.Move;
                    RefreshSelectionVisuals(); item.Background = EncounterViewport.Brush("#30434C");
                    Status.Text = $"Drop into {parent.Label} · position {index + 1} · hold Ctrl to copy";
                }
            }
            e.Handled = true;
        };
        BehaviorTree.DragLeave += (_, _) => RefreshSelectionVisuals();
        BehaviorTree.Drop += (_, e) =>
        {
            if (ReadNodeDrag(e.Data) is { } drag && Ancestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { Tag: BehaviorNode node } item)
            {
                var (parent, index) = DropDestination(item, node, e.GetPosition(item).Y);
                if (parent != null) Try(() => ApplyNodeDrop(drag, parent.Id, index, (e.KeyStates & DragDropKeyStates.ControlKey) != 0));
            }
            e.Handled = true; RefreshSelectionVisuals();
        };
    }
    (BehaviorNode? Parent, int Index) DropDestination(TreeViewItem item, BehaviorNode node, double y)
    {
        var header = item.Template.FindName("PART_Header", item) as FrameworkElement;
        var height = (header?.ActualHeight ?? 20) + item.Padding.Top + item.Padding.Bottom;
        if (document.CanEditChildren(node.Id) && y >= height * .25 && y <= height * .75) return (document.ChildContainer(node.Id), document.ChildContainer(node.Id).Children.Count);
        var parent = document.Parent(node.Id); if (parent == null || !document.CanOrganize(node.Id)) return (document.CanEditChildren(node.Id) ? document.ChildContainer(node.Id) : null, document.CanEditChildren(node.Id) ? document.ChildContainer(node.Id).Children.Count : 0);
        return (parent, parent.Children.ToList().FindIndex(n => n.Id == node.Id) + (y > height * .5 ? 1 : 0));
    }
    void ApplyNodeDrop(NodeDrag drag, Guid destination, int index, bool copy)
    {
        if (drag.Revision != document.Revision || drag.EntityId != selectedEntity?.Id) throw new ArgumentException("The dragged selection belongs to an older revision or another encounter.");
        if(copy&&(adapter.HasAdditionalSources||definitionViewKey!=null)){PasteWorkspaceNodes(destination,index,document.CopyNodes(drag.Ids));return;}
        var ids = copy ? document.PasteNodes(destination, index, document.CopyNodes(drag.Ids), ValidateAuthoring) : drag.Ids;
        if (!copy) document.MoveNodes(ids, destination, index, ValidateAuthoring);
        ChangedSelection(ids);
    }
    void SelectAuthoringNode(Guid id, ModifierKeys modifiers)
    {
        if (modifiers.HasFlag(ModifierKeys.Shift) && selectionAnchor is { } anchor)
        {
            var visible = TreeItems().Where(item => IsVisibleTreeItem(item)).Select(item => ((BehaviorNode)item.Tag).Id).ToList();
            var from = visible.IndexOf(anchor); var to = visible.IndexOf(id);
            if (from >= 0 && to >= 0) { if (!modifiers.HasFlag(ModifierKeys.Control)) nodeSelection.Clear(); nodeSelection.UnionWith(visible.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1)); }
        }
        else if (modifiers.HasFlag(ModifierKeys.Control)) { if (!nodeSelection.Add(id)) nodeSelection.Remove(id); selectionAnchor = id; }
        else { nodeSelection.Clear(); nodeSelection.Add(id); selectionAnchor = id; }
        retainNodeSelection = true; try { SelectNode(id); } finally { retainNodeSelection = false; }
    }
    bool IsVisibleTreeItem(TreeViewItem item)
    {
        var node = (BehaviorNode)item.Tag;
        return !CollapsedNodes.Any(id => node.Path.StartsWith(document.Find(id).Path + "/", StringComparison.Ordinal));
    }
    void RefreshSelectionVisuals()
    {
        foreach (var item in TreeItems()) item.Background = nodeSelection.Contains(((BehaviorNode)item.Tag).Id) ? EncounterViewport.Brush("#263B33") : Brushes.Transparent;
        Graph.Selection = nodeSelection.ToHashSet(); SelectionCount.Text = $"{nodeSelection.Count} selected";
        SelectionCount.ToolTip = "Ctrl-click to add/remove · Shift-click for a range · drag to move · Ctrl-drag to copy. Commands use the full selection; parameter edits apply to the focused item.";
    }
    void ChangedSelection(IEnumerable<Guid> ids)
    {
        var selected = ids.ToArray(); Changed(selected.FirstOrDefault() is var first && first != Guid.Empty ? first : null);
        nodeSelection.Clear(); nodeSelection.UnionWith(selected); selectionAnchor = selected.FirstOrDefault(); RefreshSelectionVisuals();
    }
    void CopySelectedNodes() => Try(() => { writeNodeClipboard(document.CopyNodes(nodeSelection)); Status.Text = $"Copied {document.NormalizeSelection(nodeSelection).Count} branches/actions"; });
    void PasteSelectedNodes() => Try(() =>
    {
        var clipboard = readNodeClipboard() ?? throw new ArgumentException("Copy phases or actions from Studio first.");
        var target = selectedNode != null && document.CanEditChildren(selectedNode.Id) ? document.ChildContainer(selectedNode.Id) : selectedNode == null ? null : document.Parent(selectedNode.Id);
        if (target == null) return;
        if(adapter.HasAdditionalSources||definitionViewKey!=null){PasteWorkspaceNodes(target.Id,target.Children.Count,clipboard);return;}
        ChangedSelection(document.PasteNodes(target.Id, target.Children.Count, clipboard, ValidateAuthoring));
    });
    void DeleteSelectedNodes() => Try(() => {
        if(adapter.HasAdditionalSources&&document.NormalizeSelection(nodeSelection).Any(n=>n.Descendants().Any(c=>c.Kind=="State"))){DeleteWorkspacePhases(nodeSelection);return;}
        var parent = document.DeleteNodes(nodeSelection, ValidateAuthoring); Changed(parent);
    });
    void GroupSelectedNodes() => Try(() => {
        if(adapter.HasAdditionalSources||definitionViewKey!=null){GroupWorkspacePhases(nodeSelection,NextPhaseName());return;}
        var id = document.GroupNodes(nodeSelection, NextPhaseName(), ValidateAuthoring); Changed(id);
    });
    void UngroupSelectedPhase() => Try(() => { if (selectedNode != null) Changed(document.UngroupState(selectedNode.Id, ValidateAuthoring)); });
    void SyncCollapsedGraph() { if (document != null) Graph.CollapsedNodes = CollapsedNodes.ToHashSet(); }
    void TogglePhaseCollapse(Guid id)
    {
        var item = TreeItems().FirstOrDefault(i => ((BehaviorNode)i.Tag).Id == id); if (item != null) item.IsExpanded = !item.IsExpanded;
    }
    void SetAllPhasesExpanded(bool expanded)
    {
        foreach (var item in TreeItems().Where(i => i.HasItems)) item.IsExpanded = expanded; SyncCollapsedGraph();
    }
    void OpenCommands(object sender, RoutedEventArgs e)
    {
        WorkspaceTabs.SelectedIndex = 1; CommandPanel.Visibility = Visibility.Visible; CommandSearch.Text = ""; PopulateCommands(); CommandSearch.Focus();
    }
    void CloseCommands() { CommandPanel.Visibility = Visibility.Collapsed; BehaviorTree.Focus(); }
    IEnumerable<AuthoringCommand> AvailableCommands()
    {
        var canEdit = nodeSelection.Count > 0 && nodeSelection.All(id => document.CanOrganize(id) && RegionEditable(document.Find(id)));
        yield return new("Copy selection", "Ctrl+C", CopySelectedNodes, canEdit);
        yield return new("Save selection as fragment", "Reusable source snapshot · optional template inputs", () => CaptureFragmentSelection(this, new()), canEdit);
        yield return new("Paste into focused phase", "Ctrl+V · source scope and shared references preserved", PasteSelectedNodes, selectedNode != null);
        yield return new("Group selection into phase", "Ctrl+G · adjacent siblings; changes phase entry/inheritance", GroupSelectedNodes, canEdit);
        yield return new("Delete selection", "Delete · includes incoming phase transitions", DeleteSelectedNodes, canEdit);
        yield return new("Expand all phases", "Show the complete hierarchy", () => SetAllPhasesExpanded(true));
        yield return new("Collapse all phases", "Reduce hierarchy and graph detail", () => SetAllPhasesExpanded(false));
        if (selectedNode != null)
        {
            var focused = selectedNode.Id;
            yield return new("Go to focused source", "Open the original C# expression", () => { ShowEncounterSource(); SourceEditor.Focus(); SourceEditor.Select(document.Find(focused).Start, document.Find(focused).Length); SourceEditor.ScrollToLine(document.Source[..document.Find(focused).Start].Count(c => c == '\n')); });
            if (selectedNode.Kind == "State") yield return new("Toggle focused phase details", "Double-click a phase card", () => TogglePhaseCollapse(focused));
            if (selectedNode.Kind == "State") yield return new("Ungroup focused phase", "Promote its contents · reroute incoming transitions first", UngroupSelectedPhase, document.Parent(focused)?.Kind == "State" && RegionEditable(selectedNode));
        }
        foreach (var node in AuthoringRoot.Descendants())
        {
            var id = node.Id;
            yield return new("Go to " + node.Label, node.Kind + " · " + node.Path, () => SelectNode(id));
            if (canEdit && document.CanEditChildren(node.Id) && document.ChildContainer(node.Id).Id == node.Id && !nodeSelection.Any(selected => node.Path == document.Find(selected).Path || node.Path.StartsWith(document.Find(selected).Path + "/", StringComparison.Ordinal)))
                yield return new("Move selection into " + node.Label, "Append in source order · one undo", () => Try(() => { var ids = nodeSelection.ToArray(); document.MoveNodes(ids, id, document.ChildContainer(id).Children.Count, ValidateAuthoring); ChangedSelection(ids); }));
        }
    }
    void CommandSearchChanged(object sender, TextChangedEventArgs e) { if (document != null) PopulateCommands(); }
    void PopulateCommands()
    {
        var query = CommandSearch.Text.Trim(); CommandResults.ItemsSource = AvailableCommands().Where(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || c.Detail.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(80).ToArray(); CommandResults.SelectedIndex = 0;
    }
    void ExecuteSelectedCommand(object sender, RoutedEventArgs e)
    {
        if (CommandResults.SelectedItem is not AuthoringCommand { Enabled: true } command) return;
        CloseCommands(); command.Execute();
    }
    void CommandKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ExecuteSelectedCommand(sender, e); e.Handled = true; }
        if (e.Key == Key.Escape) { CloseCommands(); e.Handled = true; }
        if (e.Key is Key.Down or Key.Up && sender == CommandSearch) { CommandResults.SelectedIndex = Math.Clamp(CommandResults.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Math.Max(0, CommandResults.Items.Count - 1)); e.Handled = true; }
    }
    bool HandleAuthoringShortcut(KeyEventArgs e)
    {
        if (!initialized || document == null) return false;
        if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { OpenCommands(this, e); e.Handled = true; return true; }
        if (CommandPanel.Visibility == Visibility.Visible) { if (e.Key == Key.Escape) { CloseCommands(); e.Handled = true; return true; } return false; }
        if (WorkspaceTabs.SelectedIndex != 1 || Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem) return false;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.C) CopySelectedNodes(); else if (e.Key == Key.V) PasteSelectedNodes(); else if (e.Key == Key.G) GroupSelectedNodes();
            else if (e.Key == Key.A) { nodeSelection.Clear(); nodeSelection.UnionWith(AuthoringRoot.Descendants().Where(n => n.Id != AuthoringRoot.Id && document.CanOrganize(n.Id)).Select(n => n.Id)); RefreshSelectionVisuals(); }
            else return false;
        }
        else if (e.Key == Key.Delete) DeleteSelectedNodes(); else return false;
        e.Handled = true; return true;
    }
}
