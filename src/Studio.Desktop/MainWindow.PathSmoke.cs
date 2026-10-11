using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow
{
    async Task RunPathUiSmoke(string source, BehaviorNode root, string helpers, string output, Action<bool, string> check)
    {
        void Fixture(string expression, string extra = "")
        {
            ClearViewportEditors(); document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, expression) + helpers + extra, adapter, sourceFile: selectedEntity!.SourceFile); Changed();
        }
        void Open(BehaviorNode node) { SelectNode(node.Id); pathEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        Fixture("new State(new MoveTo(.3f, 68.5f, 64.5f))"); var destination = EncounterRoot.Children.Single(); SelectNode(destination.Id);
        check(pathEditorButton?.IsEnabled == true && Viewport.Path == null && pathPreviewButton?.Visibility == Visibility.Collapsed, "Native paths expose a quiet opt-in editor and keep phase preview controls hidden until opened");
        pathEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(Viewport.Path?.NodeId == destination.Id && Viewport.Path.Pattern.Anchor == NativePathAnchor.Map && session == null && Viewport.Input() == new EncounterInput(), "Path editor uses the actual native map anchor and suspends gameplay input");
        var before = document.Source; var revision = document.Revision; var handle = Viewport.PathHandlePoint(PathHandle.Destination) + new Vector(4, 2);
        check(Viewport.BeginPathDrag(handle), "Path hit testing accepts a real destination handle with a small pointer offset");
        Viewport.MovePathDrag(handle);
        check(Viewport.Path!.Pattern is { First: 68.5, Second: 64.5 }, "Grabbing the edge of a destination handle does not jump its native coordinates");
        Viewport.MovePathDrag(Viewport.PathDestinationDragPoint(67.25, 66.75));
        check(document.Source == before && document.Revision == revision && Viewport.Path.Pattern is { First: 67.25, Second: 66.75 }, "Destination dragging previews both coordinates without intermediate source edits");
        Viewport.EndPathDrag();
        check(document.Revision == revision + 1 && document.Find(destination.Id).Parameters.Single(p => p.Name == "x").Expression == "67.25f" && document.Find(destination.Id).Parameters.Single(p => p.Name == "y").Expression == "66.75f" && document.Find(destination.Id).Parameters.Single(p => p.Name == "speed").Expression == ".3f", "Destination release commits both native float coordinates once while retaining speed and identity");
        var savedPath = Path.Combine(output, "native-path.rbs"); BehaviorDocument.Save(savedPath, Snapshot()); var saved = BehaviorDocument.Load(savedPath);
        var reopened = new BehaviorDocument(saved.Source, adapter, saved.Identities, saved.SourceFile);
        check(adapter.ReadPathPattern(reopened, reopened.Find(destination.Id)) is { First: 67.25, Second: 66.75 } && reopened.Identities.SequenceEqual(document.Identities), "Native paths and all source identities persist through save and reopen");
        var sampledBefore = document.Source; var sampledRevision = document.Revision; pathPreviewButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await pathPreviewTask!;
        check(Viewport.PathTrace is { Points.Length: 31, ForcedPhase: null, Seed: 7 } trace && trace.Points.Last() is { X: 67.25f, Y: 66.75f }, "The real preview button samples the complete native phase and records exact MoveTo arrival");
        check(document.Source == sampledBefore && document.Revision == sampledRevision && session == null, "Native phase sampling does not change source, undo history or the main playback session");
        UpdateLayout(); CaptureView(this, Path.Combine(output, "destination-path.png"));
        Viewport.BeginPathDrag(Viewport.PathHandlePoint(PathHandle.Destination)); Viewport.MovePathDrag(Viewport.PathDestinationDragPoint(70, 67)); Viewport.CancelPathDrag();
        check(document.Source == sampledBefore && Viewport.Path!.Pattern is { First: 67.25, Second: 66.75 }, "Cancelled destination dragging restores the previous native point without a patch");
        var locked = Inspector.Children.OfType<CheckBox>().Single(); locked.IsChecked = true;
        check(!pathEditorButton!.IsEnabled && Viewport.Path is { Editable: false } && !Viewport.BeginPathDrag(Viewport.PathHandlePoint(PathHandle.Destination)), "Source locks immediately disable destination gestures and editor commits"); locked.IsChecked = false;
        var stale = false; try { CommitPathEdit(Viewport.Path! with { Revision = document.Revision - 1 }, new Dictionary<string, double> { ["x"] = 69 }); } catch (InvalidOperationException error) when (error.Message.Contains("stale")) { stale = true; }
        check(stale && document.Source == sampledBefore, "Stale native path gestures reject through the shared atomic revision guard");
        Undo(this, new()); check(document.Source == before && document.Find(destination.Id).Id == destination.Id, "One Undo restores both original destination values and the action identity");
        Open(document.Find(destination.Id));
        check(await StartSessionAsync() && Viewport.Path == null && !pathEditing, "Play closes native path authoring and returns to the real input path"); running = false; await session!.AdvanceAsync(new(), 100);
        check(Math.Abs(session.Entities.Single().X - 64.65) < .00001, "Desktop native playback executes the original MoveTo speed and tick displacement"); Stop(this, new());

        Fixture("new State(new MoveTo2(2f, 0f, speed: .3, once: true))"); var relative = EncounterRoot.Children.Single(); Open(relative);
        Viewport.BeginPathDrag(Viewport.PathHandlePoint(PathHandle.Destination)); Viewport.MovePathDrag(Viewport.PathDestinationDragPoint(68.5, 65.5)); Viewport.EndPathDrag();
        check(adapter.ReadPathPattern(document, document.Find(relative.Id)) is { Anchor: NativePathAnchor.StateEntry, First: 4, Second: 1 } && document.Find(relative.Id).Parameters.Single(p => p.Name == "once").Expression == "true", "Entry-relative destination dragging stores offsets and preserves the native once flag");
        Fixture("new State(new SpawnRelativeMove(.3f, 2f, 0f))"); var spawnPath = EncounterRoot.Children.Single(); Open(spawnPath);
        Viewport.BeginPathDrag(Viewport.PathHandlePoint(PathHandle.Destination)); Viewport.MovePathDrag(Viewport.PathDestinationDragPoint(67.5, 66.5)); Viewport.EndPathDrag();
        check(adapter.ReadPathPattern(document, document.Find(spawnPath.Id)) is { Anchor: NativePathAnchor.Spawn, First: 3, Second: 2 }, "Spawn-relative editing preserves its independent native spawn anchor and offset parameter names");
        Fixture("new State(new MoveTo3(2f, 0f, instant: true))"); var third = EncounterRoot.Children.Single(); Open(third);
        check(Viewport.Path!.Pattern is { Anchor: NativePathAnchor.StateEntry, Instant: true }, "MoveTo3 retains its actual entry/instant mode instead of being normalized to MoveTo semantics");

        Fixture("new State(new MoveLine(.3, direction: 90, distance: 2))"); var line = EncounterRoot.Children.Single(); Open(line); var lineRevision = document.Revision;
        Viewport.BeginPathDrag(Viewport.PathHandlePoint(PathHandle.Destination)); Viewport.MovePathDrag(Viewport.PathDestinationDragPoint(67.5, 68.5)); Viewport.EndPathDrag();
        check(document.Revision == lineRevision + 1 && adapter.ReadPathPattern(document, document.Find(line.Id)) is { First: 53.13, Second: 5 }, "Vector destination dragging commits native degrees and tile distance together as one edit");
        Viewport.BeginPathDrag(Viewport.PathHandlePoint(PathHandle.Direction)); Viewport.MovePathDrag(Viewport.PathDirectionDragPoint(0)); Viewport.EndPathDrag();
        check(adapter.ReadPathPattern(document, document.Find(line.Id)) is { First: 0, Second: 5 }, "The separate direction handle retains finite distance while changing orientation");
        Viewport.BeginPathDrag(Viewport.PathHandlePoint(PathHandle.Direction) + new Vector(3, 2)); var rotated = Viewport.PathDirectionDragPoint(90); Viewport.MovePathDrag(rotated); Viewport.MovePathDrag(rotated);
        check(Viewport.Path!.Pattern is { First: 90, Second: 5 }, "Repeated vector pointer samples preserve the captured handle offset without angular drift"); Viewport.CancelPathDrag();
        pathPreviewButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await pathPreviewTask!; UpdateLayout(); CaptureView(this, Path.Combine(output, "line-path.png"));
        check(Viewport.PathTrace!.Points.Length == 31 && Math.Abs(Viewport.PathTrace.Points.Last().X - 69) < .0002, "Line preview displays actual tick samples rather than snapping to its requested five-tile endpoint");
        Fixture("new State(new MoveLine(.3))"); var continuous = EncounterRoot.Children.Single(); Open(continuous);
        Viewport.BeginPathDrag(Viewport.PathHandlePoint(PathHandle.Direction)); Viewport.MovePathDrag(Viewport.PathDirectionDragPoint(90)); Viewport.EndPathDrag();
        check(adapter.ReadPathPattern(document, document.Find(continuous.Id)) is { Continuous: true, First: 90 } && document.Find(continuous.Id).Parameters.All(p => p.Name != "distance"), "Editing continuous direction keeps the omitted zero-distance mode unchanged");
        Viewport.BeginPathDrag(Viewport.PathHandlePoint(PathHandle.Destination)); Viewport.MovePathDrag(Viewport.PathDestinationDragPoint(64.5, 66.5)); Viewport.EndPathDrag();
        check(adapter.ReadPathPattern(document, document.Find(continuous.Id)) is { Continuous: false, First: 90, Second: 2 }, "Dragging Set finite distance explicitly converts a continuous action into the native finite mode");

        Fixture("new State(new " + adapter.CreationTypeName("OrderedBehavior") + "(new " + adapter.CreationTypeName("NewMove") + "(speed: .3f, distance: -2f, angle: 0f, cooldown: 10f)))"); var ordered = EncounterRoot.Descendants().Single(n => n.Kind == "NewMove"); Open(ordered);
        Viewport.BeginPathDrag(Viewport.PathHandlePoint(PathHandle.Destination)); Viewport.MovePathDrag(Viewport.PathDestinationDragPoint(64.5, 67.5)); Viewport.EndPathDrag();
        check(adapter.ReadPathPattern(document, document.Find(ordered.Id)) is { First: 270, Second: -3, Cooldown: 10 } && document.Find(ordered.Id).Parameters.Single(p => p.Name == "distance").Expression == "-3f", "Ordered NewMove preserves signed distance, float types and its native seconds cooldown during vector editing");

        Fixture("new State()"); SelectNode(EncounterRoot.Id); BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "MoveTo"));
        check(createNativeButton!.IsEnabled && constructorInputs["speed"].Text == "0.3f" && constructorInputs["x"].Text == "68.5f", "Native destination creation offers valid float seeds and a usable initial destination");
        createNativeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(adapter.ReadPathPattern(document, EncounterRoot.Children.Single()) is { Kind: "MoveTo", Anchor: NativePathAnchor.Map, First: 68.5 }, "The native creation form inserts a real editable MoveTo into the phase");
        SelectNode(EncounterRoot.Id); BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "OrderedBehavior"));
        check(createNativeButton!.IsEnabled && constructorPreview!.Text.Contains("global::TKR.WorldServer.logic.behaviors.@new.movements.NewMove"), "Creation qualifies native escaped namespaces independently of the imported file's using directives");
        createNativeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(EncounterRoot.Children.Any(n => n.Kind == "OrderedBehavior") && EncounterRoot.Descendants().Any(n => n.Kind == "NewMove") && adapter.Validate(document, selectedEntity!.Id).All(d => !d.IsError), "Qualified ordered movement creation binds its real wrapper and child class in original source context");

        const string getters = "\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static float StudioPathUnknownValue { get { throw new System.InvalidOperationException(\"path authoring must not execute getters\"); } } } }";
        Fixture("new State(new MoveTo(StudioPathUnknownValue, 68.5f, 64.5f))", getters); var unknownSpeed = EncounterRoot.Children.Single(); Open(unknownSpeed);
        check(Viewport.Path!.Pattern.Speed == null, "Unknown source speed getters retain independent native coordinate authoring without execution");
        Viewport.BeginPathDrag(Viewport.PathHandlePoint(PathHandle.Destination)); Viewport.MovePathDrag(Viewport.PathDestinationDragPoint(67.5, 65.5)); Viewport.EndPathDrag();
        check(document.Find(unknownSpeed.Id).Parameters.Single(p => p.Name == "speed").Expression == "StudioPathUnknownValue", "Coordinate gestures preserve unknown source speed expressions exactly");
        Fixture("new State(new MoveTo(.3f, StudioPathUnknownValue, 64.5f))", getters); SelectNode(EncounterRoot.Children.Single().Id);
        check(pathEditorButton == null && Inspector.Children.OfType<TextBlock>().Any(t => t.Text.Contains("known coordinates")), "Unknown coordinate getters keep an explicit source boundary with no invented viewport endpoint");

        Fixture("new State(new MoveTo(.3f, 68.5f, 64.5f))"); var cancelNode = EncounterRoot.Children.Single(); Open(cancelNode); var cancelBefore = document.Source;
        pathPreviewButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); ClearPathEditor(); await pathPreviewTask!;
        check(Viewport.PathTrace == null && Viewport.Path == null && pathPreviewCancellation == null && document.Source == cancelBefore, "Closing the editor cancels an active native sample and disposes it without a late trace or source edit");
        Open(cancelNode); pathPreviewButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); SelectNode(EncounterRoot.Id); await pathPreviewTask!;
        check(Viewport.PathTrace == null && !pathEditing && document.Source == cancelBefore, "Changing selection invalidates an in-flight phase sample and prevents stale presentation");
    }
}
