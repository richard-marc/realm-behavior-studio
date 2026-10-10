using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow
{
    async Task RunMovementUiSmoke(string source, BehaviorNode root, string helpers, string output, Action<bool, string> check)
    {
        void Fixture(string expression)
        {
            ClearViewportEditors(); document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, expression) + helpers, adapter, sourceFile: selectedEntity!.SourceFile); Changed();
        }
        Fixture("new State(new Follow(.3))"); var follow = EncounterRoot.Children.Single(); SelectNode(follow.Id);
        check(movementEditorButton?.IsEnabled == true && Viewport.Movement == null, "Native Follow exposes a quiet opt-in movement editor");
        movementEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(Viewport.Movement?.NodeId == follow.Id && session == null && !running && Viewport.Input() == new EncounterInput(), "Movement button opens the real viewport editor and suspends gameplay input");
        var before = document.Source; var revision = document.Revision;
        check(Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Range)), "Movement hit testing selects the real Follow stop-range handle");
        Viewport.MoveMovementDrag(Viewport.MovementPoint(MovementHandle.Range, 3.25));
        check(document.Source == before && document.Revision == revision && Viewport.Movement!.Pattern is { Range: 3.25, ResumeRange: 4.25 }, "Follow dragging previews source units and hysteresis without repeated commits");
        Viewport.EndMovementDrag();
        check(document.Revision == revision + 1 && document.Find(follow.Id).Parameters.Single(p => p.Name == "range").Expression == "3.25" && selectedNode!.Id == follow.Id, "Stop-range release inserts the native optional parameter with one commit and stable identity");
        Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Acquire)); Viewport.MoveMovementDrag(Viewport.MovementPoint(MovementHandle.Acquire, 12)); Viewport.EndMovementDrag();
        check(document.Find(follow.Id).Parameters.Single(p => p.Name == "acquireRange").Expression == "12" && document.Find(follow.Id).Parameters.Single(p => p.Name == "speed").Expression == ".3", "Acquisition drag changes only its own native range and retains speed");
        Undo(this, new()); Undo(this, new());
        check(document.Source == before && document.Find(follow.Id).Id == follow.Id, "Each movement gesture has one complete Undo without losing the action identity");
        SelectNode(follow.Id); movementEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Range)); Viewport.MoveMovementDrag(Viewport.MovementPoint(MovementHandle.Range, 1)); Viewport.CancelMovementDrag();
        check(document.Source == before && Viewport.Movement!.Pattern.Range == 6, "Cancelled movement dragging restores its original geometry without source changes");
        var locked = Inspector.Children.OfType<CheckBox>().Single(); locked.IsChecked = true;
        check(!movementEditorButton!.IsEnabled && Viewport.Movement is { Editable: false } && !Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Range)), "Locked movement disables both inspector and viewport edits immediately"); locked.IsChecked = false;
        var rejected = false; try { CommitMovementEdit(Viewport.Movement! with { Revision = document.Revision - 1 }, "range", 4); } catch (InvalidOperationException error) when (error.Message.Contains("stale")) { rejected = true; }
        check(rejected && document.Source == before, "Stale movement gestures reject atomically through the shared revision guard");
        Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Range)); Viewport.MoveMovementDrag(Viewport.MovementPoint(MovementHandle.Range, 3.5)); Viewport.EndMovementDrag();
        UpdateLayout(); CaptureView(this, Path.Combine(output, "follow-movement.png"));
        var savedPath = Path.Combine(output, "visual-movement.rbs"); BehaviorDocument.Save(savedPath, Snapshot()); var saved = BehaviorDocument.Load(savedPath); var reopened = new BehaviorDocument(saved.Source, adapter, saved.Identities, saved.SourceFile);
        check(adapter.ReadMovementPattern(reopened, reopened.Find(follow.Id))!.Range == 3.5 && reopened.Identities.SequenceEqual(document.Identities), "Movement source units and all identities persist through save and reopen");
        check(await StartSessionAsync() && Viewport.Movement == null && !movementEditing, "Play exits movement authoring and restores the native input path"); running = false; await session!.AdvanceAsync(new(), 100); var moved = session.Entities.Single();
        check(Math.Abs(Math.Sqrt(Math.Pow(moved.X - 64.5, 2) + Math.Pow(moved.Y - 64.5, 2)) - .15) < .00001, "Edited Follow executes its actual native BehaviourTickTime displacement"); Stop(this, new());

        Fixture("new State(new Orbit(.3, 3, radiusVariance: .5, speedVariance: 0, orbitClockwise: true))"); var orbit = EncounterRoot.Children.Single(); SelectNode(orbit.Id); movementEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(Viewport.Movement!.Pattern is { Kind: "Orbit", RadiusVariance: .5, Clockwise: true } && movementDirection!.SelectedIndex == 1, "Orbit editor exposes native variance and its actual direction control");
        Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Range)); Viewport.MoveMovementDrag(Viewport.MovementPoint(MovementHandle.Range, 4.25)); Viewport.EndMovementDrag();
        check(document.Find(orbit.Id).Parameters.Single(p => p.Name == "radius").Expression == "4.25", "Orbit-radius drag commits native fractional tiles rather than an animation-path estimate");
        Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Variance)); Viewport.MoveMovementDrag(Viewport.MovementPoint(MovementHandle.Variance, 6.25) + new Vector(16, 0)); Viewport.EndMovementDrag();
        check(document.Find(orbit.Id).Parameters.Single(p => p.Name == "radiusVariance").Expression == "2" && document.Find(orbit.Id).Parameters.Single(p => p.Name == "speedVariance").Expression == "0", "Variance handle removes its screen offset and edits only the native radial sample width");
        CommitMovementValue(orbit.Id, document.Revision, "radiusVariance", "-2");
        Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Variance)); Viewport.MoveMovementDrag(Viewport.MovementPoint(MovementHandle.Variance, 5.25) + new Vector(16, 0)); Viewport.EndMovementDrag();
        check(document.Find(orbit.Id).Parameters.Single(p => p.Name == "radiusVariance").Expression == "-1", "Variance dragging retains a source-authored negative sign and its seeded semantics");
        var directionRevision = document.Revision; movementDirection!.SelectedIndex = 2;
        check(document.Revision == directionRevision + 1 && document.Find(orbit.Id).Parameters.Single(p => p.Name == "orbitClockwise").Expression == "null", "Seeded direction chooser writes the native nullable bool with one undoable commit");
        Undo(this, new()); SelectNode(orbit.Id);
        check(movementDirection!.SelectedIndex == 1, "Undo restores the source direction and the actual dropdown selection");
        movementEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); movementDirection.SelectedIndex = 0;
        locked = Inspector.Children.OfType<CheckBox>().Single(); locked.IsChecked = true;
        check(!movementDirection!.IsEnabled && !movementEditorButton!.IsEnabled, "Orbit direction and geometry both respect immediate region locking"); locked.IsChecked = false;
        UpdateLayout(); CaptureView(this, Path.Combine(output, "orbit-movement.png"));
        check(await StartSessionAsync(), "Edited Orbit starts real native playback"); running = false; await session!.AdvanceAsync(new(), 100); moved = session.Entities.Single();
        check(moved.Y > 64.5 && Math.Abs(Math.Sqrt(Math.Pow(moved.X - 64.5, 2) + Math.Pow(moved.Y - 64.5, 2)) - .03) < .00001, "Orbit playback uses the chosen native direction, signed variance and real DeltaTime displacement"); Stop(this, new());

        Fixture("new State(new StayCloseToSpawn(.3))"); var home = EncounterRoot.Children.Single(); SelectNode(home.Id); movementEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Range)); Viewport.MoveMovementDrag(Viewport.MovementPoint(MovementHandle.Range, 3.7)); Viewport.EndMovementDrag();
        check(document.Find(home.Id).Parameters.Single(p => p.Name == "range").Expression == "4" && Viewport.Movement!.Pattern.IntegerRange, "Home-range dragging snaps to the native integer parameter rather than writing invalid fractional source");
        UpdateLayout(); CaptureView(this, Path.Combine(output, "home-movement.png"));
        var geometry = new EncounterViewport(); geometry.Arrange(new Rect(0, 0, 800, 500)); var originalCamera = geometry.AttackPoint(0, 4);
        geometry.Movement = Viewport.Movement! with { Pattern = Viewport.Movement!.Pattern with { Range = 1000 } }; geometry.FitMovement();
        check(new Rect(0, 0, 800, 500).Contains(geometry.MovementHandlePoint(MovementHandle.Range)), "Large movement boundaries fit the canvas with an accessible native-unit handle");
        geometry.Movement = null; check((geometry.AttackPoint(0, 4) - originalCamera).Length < .000001, "Closing movement mode restores the gameplay camera");
        geometry.Movement = Viewport.Movement!;
        geometry.Attack = new(home.Id, document.Revision, new ShootPattern(1, 0, 0, 0, 8, null, 0, 0), true);
        check(geometry.Movement == null && geometry.Attack != null, "Entering attack mode clears movement geometry instead of stacking conflicting overlays");
        geometry.Movement = Viewport.Movement!;
        check(geometry.Attack == null && geometry.Movement != null, "Entering movement mode clears attack geometry and its transient camera state");
        ClearViewportEditors();
    }
}
