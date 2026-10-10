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
    async void CompleteRuntimeUiSmoke(string output, List<string> checks, Action<bool, string> check) => await TryRuntime(async () =>
    {
        var source = File.ReadAllText(selectedEntity!.SourceFile);
        var doc = new BehaviorDocument(source, adapter); var root = doc.Entities.Single(e => e.EntityId == selectedEntity.Id).Root;
        const string expression = "StudioPreviewRoot(count: 4)";
        const string helpers = """

        namespace TKR.WorldServer.logic { public partial class BehaviorDb {
            const double StudioPreviewSpeed = .3;
            const int StudioPreviewCapacity = 4;
            static readonly TKR.WorldServer.logic.behaviors.Shoot StudioPreviewShared = new TKR.WorldServer.logic.behaviors.Shoot(8, count: 2, coolDown: 200);
            static readonly TKR.WorldServer.logic.Behavior[] StudioPreviewCollection = [new TKR.WorldServer.logic.behaviors.Shoot(8, count: 3, coolDown: 200)];
            static State StudioPreviewRoot(int count = 4, double speed = .3) => new State(new State("native preview", StudioPreviewFan(count), new TKR.WorldServer.logic.behaviors.Follow(speed, range: 1)));
            static TKR.WorldServer.logic.behaviors.Shoot StudioPreviewFan(int count) => new TKR.WorldServer.logic.behaviors.Shoot(8, count: count, coolDown: 200);
            static TKR.WorldServer.logic.behaviors.Timed StudioPreviewSlots(TKR.WorldServer.logic.Behavior lead = null, params TKR.WorldServer.logic.Behavior[] tail) => new TKR.WorldServer.logic.behaviors.Timed(1000, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(System.Linq.Enumerable.Where(new[] { lead }, b => b != null), tail)));
            static TKR.WorldServer.logic.behaviors.Timed StudioPreviewTyped(System.Collections.Generic.IEnumerable<TKR.WorldServer.logic.Behavior> first, System.Collections.Generic.IReadOnlyList<TKR.WorldServer.logic.Behavior> second, System.Collections.Generic.List<TKR.WorldServer.logic.Behavior> third = null) => new TKR.WorldServer.logic.behaviors.Timed(1000, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(System.Linq.Enumerable.Concat(first, second), third ?? new System.Collections.Generic.List<TKR.WorldServer.logic.Behavior>())));
        } }
        """;
        RunSourceStructureUiSmoke(source, root, helpers, output, check);
        RunMultipleSlotUiSmoke(source, root, helpers, output, check);
        RunCollectionUiSmoke(source, root, helpers, output, check);
        RunTypedCollectionUiSmoke(source, root, helpers, output, check);
        await RunInitializerUiSmoke(source, root, helpers, output, check);
        document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, expression) + helpers, adapter, sourceFile: selectedEntity.SourceFile);
        Changed(); var factory = document.Entities.Single(e => e.EntityId == selectedEntity.Id).Root;
        check(factory.Kind == "Factory" && factory.Parameters.Single().Name == "count", "Desktop discovers the imported source helper as the encounter root");
        SelectNode(factory.Id);
        var input = Inspector.Children.OfType<TextBox>().Single(); input.Text = "6";
        Inspector.Children.OfType<Button>().Single(b => b.Content is string text && text == "Apply changes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(factory.Id).Parameters.Single().Expression == "6", "Real factory inspector Apply edits the helper argument through the shared document");
        check(Inspector.Children.OfType<Expander>().Any(e => e.Header is string text && text == "More supported parameters"), "Factory inspector exposes omitted optional source parameters");
        check(document.Undo() && document.Find(factory.Id).Parameters.Single().Expression == "4", "Desktop factory edit restores its previous source and identity through undo");
        Changed(factory.Id); WorkspaceTabs.SelectedIndex = 1; UpdateLayout(); CaptureView(Graph, Path.Combine(output, "factory-source-graph.png"));
        check(Graph.PhaseBounds(factory.Id) != null, "Factory root has a selectable source encounter card in the graph");
        var card = Graph.PhaseBounds(factory.Id)!.Value; Graph.SelectAt(new(card.Left + 20, card.Top + 40));
        check(selectedNode?.Id == factory.Id, "Source encounter graph card navigates to the same editable factory identity");
        WorkspaceTabs.SelectedIndex = 0;
        var preparing = StartSessionAsync();
        check(runtimeStarting != null && !PlayButton.IsEnabled, "Compiled preparation exposes its pending state in the real playback control");
        var dispatched = false;
        await Dispatcher.InvokeAsync(() => dispatched = true, DispatcherPriority.Background);
        check(dispatched, "Desktop dispatcher remains available while compiled source is prepared");
        check(await preparing && session is CompiledRuntime && ReferenceEquals(session, Viewport.Session), "Source-context encounter starts in the real viewport through the compiled runtime path");
        var native = (CompiledRuntime)session!;
        running = false; Playback.SelectedIndex = 2;
        var positions = new List<Point>();
        for (var i = 0; i < 12; i++)
        {
            await AdvanceCompiledPlayback(1000d / 60, new(Y: 1));
            positions.Add(EncounterViewport.EntityPosition(session!.Entities.Single(), session.Ms + Viewport.FrameRemainderMs));
        }
        check(native.Ms is >= 195 and <= 200 && native.Shots.Any(s => s.Count == 4), "Compiled preview uses native helper attacks with the shared playback sample clock");
        check(positions.Distinct().Count() == 12 && native.PlayerY > 64.5, "Compiled preview retains smooth enemy presentation and player movement on every sampled frame");
        check(Events.Items.Count > 0 && Coverage.Text.Contains("Compiled encounter") && TimerInfo.Text.Length > 0, "Console and timer inspector consume the authoritative process snapshot");
        var projectPath = Path.Combine(output, "compiled-preview.rbs"); BehaviorDocument.Save(projectPath, Snapshot());
        check(BehaviorDocument.Load(projectPath).Source == document.Source && document.Identities.All(i => BehaviorDocument.Load(projectPath).Identities.Any(saved => saved.Id == i.Id)), "Compiled preview keeps the same saved source document and editor identities");
        native.ExportTrace(Path.Combine(output, "compiled-preview-trace.json"));
        var pid = native.ProcessId; UpdateLayout(); CaptureView(this, Path.Combine(output, "compiled-preview.png"));
        Stop(this, new());
        bool processRunning;
        try { using var observed = System.Diagnostics.Process.GetProcessById(pid); processRunning = !observed.HasExited; } catch (ArgumentException) { processRunning = false; }
        check(session == null && Viewport.Session == null && !processRunning, "Stop clears the viewport and closes the compiled native process");
        var beforeTool = document.Source; var toolArgs = JsonSerializer.SerializeToElement(new { durationMs = 1000, previewProposal = false });
        ShareContext.IsChecked = true;
        var toolResult = JsonSerializer.SerializeToElement(await NativeTool("run_simulation", toolArgs));
        ShareContext.IsChecked = false;
        check(toolResult.GetProperty("Coverage").GetString()!.Contains("Compiled original encounter") && toolResult.GetProperty("events").EnumerateArray().Any(e => e.GetProperty("Kind").GetString() == "attack"), "Bounded application simulation tool can run the same source-context encounter without live AI");
        check(document.Source == beforeTool && session == null, "Application tool preview leaves the shared source unchanged and closes its native process");
        var cancelled = StartSessionAsync(); Stop(this, new());
        try { await cancelled; } catch (OperationCanceledException) { }
        check(session == null && runtimeStarting == null && PlayButton.IsEnabled, "Stopped preparation cannot install a stale compiled session");
        document = doc; Changed();
        check(await StartSessionAsync() && session is EncounterSession, "Ordinary native literal playback remains available after compiled preview cancellation");
        running = false;
        File.WriteAllText(Path.Combine(output, "ui-checks.json"), JsonSerializer.Serialize(new { checks, spriteSheets = sprites.SheetCount, loadedFrames = sprites.LoadedFrames, fallbackNames = sprites.Missing.Order().ToArray() }, BehaviorDocument.JsonOptions));
        Application.Current.Shutdown();
    });
    void RunSourceStructureUiSmoke(string source, BehaviorNode root, string helpers, string output, Action<bool, string> check)
    {
        const string expression = "new State(StudioPreviewFan(4), StudioPreviewShared, new Timed(500, new Wander(StudioPreviewSpeed)))";
        document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, expression) + helpers, adapter, sourceFile: selectedEntity!.SourceFile);
        Changed(); WorkspaceTabs.SelectedIndex = 1;
        var factory = EncounterRoot.Children[0]; var reference = EncounterRoot.Children[1]; var timed = EncounterRoot.Children[2];
        SelectNode(factory.Id);
        check(duplicateStructureButton?.IsEnabled == true && downStructureButton?.IsEnabled == true, "Factory selection exposes enabled desktop Duplicate and reorder controls");
        duplicateStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var copiedFactory = selectedNode!.Id;
        check(copiedFactory != factory.Id && selectedNode.Kind == "Factory" && EncounterRoot.Children.Count == 4 && selectedNode.Parameters.Single().Expression == "4", "Desktop Duplicate copies a source helper with bound arguments and a fresh editor identity");
        upStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(EncounterRoot.Children[0].Id == copiedFactory && selectedNode!.Id == copiedFactory, "Desktop reorder moves a helper in source order and preserves its selection");
        BehaviorClipboard? clipboard = null; var previousRead = readNodeClipboard; var previousWrite = writeNodeClipboard;
        readNodeClipboard = () => clipboard; writeNodeClipboard = value => clipboard = value;
        try
        {
            CopySelectedNodes(); SelectNode(timed.Id); PasteSelectedNodes();
            check(clipboard?.Expressions.Single() == "StudioPreviewFan(4)" && document.Find(timed.Id).Children.Last().Kind == "Factory" && selectedNode!.Id != copiedFactory, "Desktop clipboard paste binds a helper inside a native wrapper's actual source scope");
            Undo(this, new());
            check(document.Find(timed.Id).Children.Count == 1 && EncounterRoot.Children[0].Id == copiedFactory, "Desktop Undo restores a source-helper paste as one complete edit");
        }
        finally { readNodeClipboard = previousRead; writeNodeClipboard = previousWrite; }
        SelectNode(reference.Id);
        check(duplicateStructureButton?.IsEnabled == true && duplicateStructureButton.ToolTip is string hint && hint.Contains("shared source value"), "Shared-reference Duplicate explains that copies retain the same source value");
        duplicateStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode!.Kind == "Reference" && selectedNode.Id != reference.Id && document.Expression(selectedNode) == "StudioPreviewShared", "Desktop Duplicate preserves shared-reference syntax without expanding its definition");
        UpdateLayout(); CaptureView(this, Path.Combine(output, "source-structure.png"));
    }
    void RunMultipleSlotUiSmoke(string source, BehaviorNode root, string helpers, string output, Action<bool, string> check)
    {
        const string expression = "new State(new State(\"slot preview\", StudioPreviewSlots(new Shoot(8, count: 2), new Wander(.2), new Shoot(8, count: 3))))";
        document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, expression) + helpers, adapter, sourceFile: selectedEntity!.SourceFile);
        Changed(); WorkspaceTabs.SelectedIndex = 1;
        var call = EncounterRoot.Children.Single().Children.Single(); var lead = call.Children[0]; var tail = call.Children[1]; var oldLead = lead.Children.Single();
        check(call.Children.Select(n => n.Argument?.Name).SequenceEqual(["lead", "tail"]) && TreeItems().Count(i => ((BehaviorNode)i.Tag).Id == lead.Id || ((BehaviorNode)i.Tag).Id == tail.Id) == 2, "Desktop hierarchy exposes separate scalar and params slots from the actual source helper");
        SelectNode(lead.Id);
        check(chooseChildSlotButton?.Content is string label && label == "Replace child", "Occupied scalar slot offers a clear Replace child action");
        chooseChildSlotButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(LibraryTabs.SelectedIndex == 1 && selectedNode!.Id == lead.Id, "Slot chooser opens the action library while retaining its exact destination");
        Primitives.SelectedItem = adapter.Primitives.Single(p => p.Name == "Shoot"); UseActionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(createNativeButton?.IsEnabled == true && Equals(createNativeButton.Content, "Replace child"), "Selected action opens a type-checked replacement form for the scalar slot");
        constructorInputs["count"].Text = "5"; createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var replacement = selectedNode!.Id;
        check(replacement != oldLead.Id && document.Find(lead.Id).Children.Single().Id == replacement && selectedNode.Parameters.Single(p => p.Name == "count").Expression == "5" && document.Find(tail.Id).Children.Count == 2, "Desktop replacement commits the new child without disturbing the helper's other list");
        Undo(this, new()); check(document.Find(lead.Id).Children.Single().Id == oldLead.Id, "Desktop replacement Undo restores the original scalar child identity");
        SelectNode(oldLead.Id); deleteStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode!.Id == lead.Id && document.Find(lead.Id).Children.Count == 0 && Equals(chooseChildSlotButton!.Content, "Choose child"), "Deleting an optional scalar child reveals an empty slot that can be filled again");
        BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "Wander")); createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var newLead = selectedNode!.Id;
        check(document.Find(lead.Id).Children.Single().Id == newLead && selectedNode.Kind == "Wander" && document.Find(tail.Id).Children.Count == 2, "Desktop creation fills an omitted scalar slot while preserving later params arguments");
        SelectNode(lead.Id); var locked = Inspector.Children.OfType<CheckBox>().Single(); locked.IsChecked = true;
        check(chooseChildSlotButton!.IsEnabled == false, "Locking a child slot immediately disables its chooser"); locked.IsChecked = false;
        SelectAuthoringNode(newLead, System.Windows.Input.ModifierKeys.None); UpdateLayout();
        var item = TreeItems().Single(i => ((BehaviorNode)i.Tag).Id == tail.Id); var data = DragData(new NodeDrag(document.Revision, selectedEntity.Id, [newLead]));
        item.RaiseEvent(SyntheticDragEvent(data, item, new Point(35, 14), DragDrop.DragOverEvent)); item.RaiseEvent(SyntheticDragEvent(data, item, new Point(35, 14), DragDrop.DropEvent));
        check(document.Find(lead.Id).Children.Count == 0 && document.Find(tail.Id).Children.Last().Id == newLead, "Routed desktop drop moves an optional scalar child into the independent native list");
        SelectNode(lead.Id); UpdateLayout(); CaptureView(this, Path.Combine(output, "multiple-child-slots.png")); LibraryTabs.SelectedIndex = 0;
    }
    async Task RunInitializerUiSmoke(string source, BehaviorNode root, string helpers, string output, Action<bool, string> check)
    {
        const string expression = "new State(new State(\"initializer preview\", StudioPreviewTyped(first: new System.Collections.Generic.List<Behavior>(capacity: StudioPreviewCapacity) { new Wander(.2), /* initializer UI note */ StudioPreviewFan(4) }, second: new System.Collections.Generic.List<Behavior>() { new Shoot(8, count: 2) }, third: new(capacity: StudioPreviewCapacity) { new Shoot(8, count: 3) })))";
        document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, expression) + helpers, adapter, sourceFile: selectedEntity!.SourceFile);
        Changed(); WorkspaceTabs.SelectedIndex = 1;
        var call = EncounterRoot.StateChildren.Single().Children.Single(); var first = call.Children[0]; var second = call.Children[1]; var third = call.Children[2]; var moving = first.Children[0]; var oldChild = third.Children.Single();
        check(call.Children.All(n => !n.ArgumentFromCall && document.CanEditChildren(n.Id)) && TreeItems().Any(i => ((BehaviorNode)i.Tag).Id == oldChild.Id), "Desktop hierarchy exposes explicit and target-typed collection initializer children");
        SelectNode(oldChild.Id); duplicateStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(third.Id).Children.Count == 2 && selectedNode!.Id != oldChild.Id && document.Source.Contains("third: new(capacity: StudioPreviewCapacity)"), "Real Duplicate retains a target-typed collection's constructor and creates a fresh child identity");
        Undo(this, new()); SelectNode(oldChild.Id); deleteStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode!.Id == third.Id && document.Find(third.Id).Children.Count == 0 && document.CanEditChildren(third.Id) && Equals(chooseChildSlotButton?.Content, "+ Add child"), "Deleting the final initializer child retains its empty editable list after C# reparses the braces");
        BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "Shoot")); constructorInputs["count"].Text = "5"; createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var created = selectedNode!.Id;
        check(document.Find(third.Id).Children.Single().Id == created && document.Source.Contains("third: new(capacity: StudioPreviewCapacity)") && document.Source.Contains("/* initializer UI note */"), "Desktop native creation repopulates empty initializer braces while preserving constructor arguments and comments");
        SelectAuthoringNode(moving.Id, System.Windows.Input.ModifierKeys.None); UpdateLayout(); var item = TreeItems().Single(i => ((BehaviorNode)i.Tag).Id == second.Id); var data = DragData(new NodeDrag(document.Revision, selectedEntity.Id, [moving.Id]));
        item.RaiseEvent(SyntheticDragEvent(data, item, new Point(35, 14), DragDrop.DragOverEvent)); item.RaiseEvent(SyntheticDragEvent(data, item, new Point(35, 14), DragDrop.DropEvent));
        check(document.Find(first.Id).Children.Single().Kind == "Factory" && document.Find(second.Id).Children.Last().Id == moving.Id && selectedNode!.Id == moving.Id, "Routed desktop drop moves a child across initializer lists with its source identity intact");
        Undo(this, new());
        check(document.Find(first.Id).Children.First().Id == moving.Id && document.Find(second.Id).Children.Count == 1 && document.Find(third.Id).Children.Single().Id == created, "Desktop initializer Undo restores both constructors and preserves unrelated edits");
        SelectNode(third.Id); UpdateLayout(); CaptureView(this, Path.Combine(output, "collection-initializers.png"));
        document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, "new State(\"source initialized\") { Behaviors = { new Shoot(8, count: 7) } }") + helpers, adapter, sourceFile: selectedEntity.SourceFile);
        Changed(); WorkspaceTabs.SelectedIndex = 1; var region = EncounterRoot.Children.Single(); SelectNode(region.Id); UpdateLayout(); CaptureView(this, Path.Combine(output, "source-initializer.png"));
        Inspector.Children.OfType<Button>().Single(b => Equals(b.Content, "Open Source")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(region.Kind == "Opaque" && region.Label == "Source initializer" && WorkspaceTabs.SelectedIndex == 2 && SourceEditor.SelectionStart == region.Start && SourceEditor.SelectionLength == region.Length, "Source initializer navigation opens and selects its exact retained C# region");
        WorkspaceTabs.SelectedIndex = 0;
        check(await StartSessionAsync() && session is CompiledRuntime && ReferenceEquals(session, Viewport.Session), "Desktop Play selects compiled runtime for a native State object initializer");
        running = false; Playback.SelectedIndex = 2; await AdvanceCompiledPlayback(100, new(Y: 1));
        check(session!.Shots.Single().Count == 7 && session.Entities.Single().Phase == "source initialized", "Desktop native playback executes actual object-initializer member additions instead of losing their attacks");
        Stop(this, new());
    }
    void RunTypedCollectionUiSmoke(string source, BehaviorNode root, string helpers, string output, Action<bool, string> check)
    {
        const string expression = "new State(new State(\"typed input preview\", StudioPreviewTyped(first: [new Wander(.2), ..StudioPreviewCollection], second: [new Shoot(8, count: 2)])))";
        document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, expression) + helpers, adapter, sourceFile: selectedEntity!.SourceFile);
        Changed(); WorkspaceTabs.SelectedIndex = 1;
        var call = EncounterRoot.StateChildren.Single().Children.Single(); var first = call.Children[0]; var second = call.Children[1]; var third = call.Children[2]; var moving = first.Children[0]; var spread = first.Children[1];
        check(call.Children.Count == 3 && call.Children.All(n => n.Argument is { IsArray: false, IsCollection: true }) && TreeItems().Any(i => ((BehaviorNode)i.Tag).Id == third.Id), "Desktop exposes independently selectable enumerable, read-only and concrete child lists");
        SelectNode(third.Id);
        check(Equals(chooseChildSlotButton?.Content, "+ Add child") && Inspector.Children.OfType<TextBlock>().Any(t => t.Text.Contains("creates an explicit Behavior collection")), "An omitted concrete list offers Add child and describes actual collection construction");
        BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "Shoot")); constructorInputs["count"].Text = "5"; createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var created = selectedNode!.Id;
        check(document.Find(third.Id).Children.Single().Id == created && document.Source.Contains("third:[") && document.Find(second.Id).Children.Single().Parameters.Single(p => p.Name == "count").Expression == "2", "Desktop creation populates the omitted concrete list while retaining the other formal arguments");
        duplicateStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var copy = selectedNode!.Id;
        check(document.Find(third.Id).Children.Count == 2 && copy != created, "Real Duplicate treats a non-array collection as a multi-child list");
        Undo(this, new());
        SelectAuthoringNode(moving.Id, System.Windows.Input.ModifierKeys.None); UpdateLayout(); var item = TreeItems().Single(i => ((BehaviorNode)i.Tag).Id == second.Id); var data = DragData(new NodeDrag(document.Revision, selectedEntity.Id, [moving.Id]));
        item.RaiseEvent(SyntheticDragEvent(data, item, new Point(35, 14), DragDrop.DragOverEvent)); item.RaiseEvent(SyntheticDragEvent(data, item, new Point(35, 14), DragDrop.DropEvent));
        check(document.Find(first.Id).Children.Single().Id == spread.Id && document.Find(second.Id).Children.Last().Id == moving.Id && selectedNode!.Id == moving.Id, "Routed desktop drop moves a child from enumerable to read-only input with its identity intact");
        BehaviorClipboard? clipboard = null; var previousRead = readNodeClipboard; var previousWrite = writeNodeClipboard; readNodeClipboard = () => clipboard; writeNodeClipboard = value => clipboard = value;
        try
        {
            SelectNode(spread.Id); CopySelectedNodes(); SelectNode(third.Id); PasteSelectedNodes();
            check(document.Find(third.Id).Children.Last().Kind == "Spread" && selectedNode!.Id != spread.Id && document.Find(first.Id).Children.Single().Id == spread.Id, "Desktop clipboard paste copies a bound spread into a concrete collection with a fresh identity");
            Undo(this, new()); check(document.Find(third.Id).Children.Single().Id == created, "Non-array collection clipboard Undo restores the exact earlier list");
        }
        finally { readNodeClipboard = previousRead; writeNodeClipboard = previousWrite; }
        SelectNode(second.Id); var regionLock = Inspector.Children.OfType<CheckBox>().Single(); regionLock.IsChecked = true;
        check(chooseChildSlotButton?.IsEnabled == false && Inspector.Children.OfType<TextBlock>().Any(t => t.Text.Contains("inputs in source order") && Equals(t.ToolTip, second.Argument!.CollectionType)), "Locked collection controls retain the actual target type and describe source input order"); regionLock.IsChecked = false;
        UpdateLayout(); CaptureView(this, Path.Combine(output, "typed-collections.png"));
    }
    void RunCollectionUiSmoke(string source, BehaviorNode root, string helpers, string output, Action<bool, string> check)
    {
        const string expression = "new State(children: [new State(\"collection preview\", new Timed(1000, behaviors: [new Wander(.2), ..StudioPreviewCollection]), new Timed(1000, behaviors: [new Shoot(8, count: 2)]))])";
        document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, expression) + helpers, adapter, sourceFile: selectedEntity!.SourceFile);
        Changed(); WorkspaceTabs.SelectedIndex = 1;
        var phase = EncounterRoot.StateChildren.Single(); var left = phase.Children[0].Children.Single(); var right = phase.Children[1].Children.Single(); var spread = left.Children.Single(n => n.Kind == "Spread");
        UpdateLayout();
        check(TreeItems().Any(i => ((BehaviorNode)i.Tag).Id == spread.Id) && left.Label.Contains("1 + spread") && Graph.PhaseBounds(phase.Id) != null, "Desktop collection hierarchy and graph retain explicit phases and show unknown spread counts honestly");
        SelectNode(spread.Id);
        check(Inspector.Children.OfType<TextBlock>().Any(t => t.Text.StartsWith("Spread input ·")) && duplicateStructureButton?.IsEnabled == true && !Inspector.Children.OfType<Button>().Any(b => Equals(b.Content, "Apply changes")), "Spread inspector explains its source role and offers structure controls without invented scalar fields");
        duplicateStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var copy = selectedNode!.Id;
        check(copy != spread.Id && document.Find(left.Id).Children.Count(n => n.Kind == "Spread") == 2 && document.Source.Contains("behaviors: ["), "Real desktop Duplicate copies a spread expression with a fresh identity and retained brackets");
        upStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(left.Id).Children[1].Id == copy && selectedNode!.Id == copy, "Desktop spread reorder preserves exact input identity and focused selection");
        Undo(this, new()); Undo(this, new());
        SelectAuthoringNode(spread.Id, System.Windows.Input.ModifierKeys.None); UpdateLayout(); var item = TreeItems().Single(i => ((BehaviorNode)i.Tag).Id == right.Id); var data = DragData(new NodeDrag(document.Revision, selectedEntity.Id, [spread.Id]));
        item.RaiseEvent(SyntheticDragEvent(data, item, new Point(35, 14), DragDrop.DragOverEvent)); item.RaiseEvent(SyntheticDragEvent(data, item, new Point(35, 14), DragDrop.DropEvent));
        check(document.Find(left.Id).Children.All(n => n.Kind != "Spread") && document.Find(right.Id).Children.Last().Id == spread.Id && selectedNode!.Id == spread.Id, "Routed desktop drop moves a spread between real collection lists without expanding its source value");
        SelectNode(right.Id); BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "Wander")); createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(right.Id).Children.Last().Kind == "Wander" && document.Find(right.Id).Children.Any(n => n.Id == spread.Id), "Native creation appends a typed child beside a retained collection spread");
        SelectNode(spread.Id); UpdateLayout(); CaptureView(this, Path.Combine(output, "collection-spreads.png"));
    }
}
