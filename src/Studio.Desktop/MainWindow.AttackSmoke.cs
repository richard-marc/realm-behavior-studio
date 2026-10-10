using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;

public partial class MainWindow
{
    async Task RunAttackUiSmoke(string source, BehaviorNode root, string helpers, string output, Action<bool, string> check)
    {
        const string expression = "new State(new State(\"attack preview\", new Shoot(8, count: 5, shootAngle: 15, fixedAngle: 90, angleOffset: 10, rotateAngle: 30, coolDown: 100)))";
        document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, expression) + helpers, adapter, sourceFile: selectedEntity!.SourceFile);
        Changed(); var shot = EncounterRoot.StateChildren.Single().Children.Single(); SelectNode(shot.Id);
        check(attackEditorButton?.IsEnabled == true && Viewport.Attack == null, "Selected native Shoot offers an opt-in visual editor without adding viewport noise");
        attackEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(WorkspaceTabs.SelectedIndex == 0 && Viewport.Attack?.NodeId == shot.Id && session == null && !running, "Real attack-editor button opens the source preview and stops gameplay input");
        var before = document.Source; var revision = document.Revision;
        check(Viewport.BeginAttackDrag(Viewport.AttackHandlePoint(AttackHandle.Aim)), "Viewport hit testing selects the actual aim handle");
        Viewport.MoveAttackDrag(Viewport.AttackPoint(180, 4));
        check(document.Source == before && document.Revision == revision && Viewport.Attack!.Pattern.FixedAngle == 170, "Pointer movement previews aim with the native offset without committing repeated edits");
        Viewport.EndAttackDrag();
        check(document.Revision == revision + 1 && document.Find(shot.Id).Parameters.Single(p => p.Name == "fixedAngle").Expression == "170" && selectedNode!.Id == shot.Id, "Aim release commits one typed source edit and preserves the selected native action identity");
        check(Viewport.BeginAttackDrag(Viewport.AttackHandlePoint(AttackHandle.Spread)), "Spread handle remains independently selectable from the center aim");
        Viewport.MoveAttackDrag(Viewport.AttackPoint(240, 5)); Viewport.EndAttackDrag();
        check(document.Find(shot.Id).Parameters.Single(p => p.Name == "shootAngle").Expression == "30" && document.Find(shot.Id).Parameters.Single(p => p.Name == "rotateAngle").Expression == "30", "Spread release changes adjacent shot spacing while retaining instance rotation");
        Undo(this, new()); Undo(this, new());
        check(document.Source == before && document.Find(shot.Id).Parameters.Single(p => p.Name == "fixedAngle").Expression == "90", "Each visual gesture has one complete source Undo with stable action identity");
        SelectNode(shot.Id); attackEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Viewport.BeginAttackDrag(Viewport.AttackHandlePoint(AttackHandle.Radius)); Viewport.MoveAttackDrag(Viewport.AttackPoint(10, 5.5)); Viewport.EndAttackDrag();
        check(document.Find(shot.Id).Parameters.Single(p => p.Name == "radius").Expression == "5.5", "Radius handle writes exact native tile units rather than a projectile-range approximation");
        var beforeCancel = document.Source; var cancelRevision = document.Revision;
        Viewport.BeginAttackDrag(Viewport.AttackHandlePoint(AttackHandle.Aim)); Viewport.MoveAttackDrag(Viewport.AttackPoint(300, 4)); Viewport.CancelAttackDrag();
        check(document.Source == beforeCancel && document.Revision == cancelRevision && Viewport.Attack!.Pattern.FixedAngle == 90, "Cancelling a visual gesture restores the original preview without touching source or undo history");
        var locked = Inspector.Children.OfType<CheckBox>().Single(); locked.IsChecked = true;
        check(Viewport.Attack is { Editable: false } && !Viewport.BeginAttackDrag(Viewport.AttackHandlePoint(AttackHandle.Aim)) && attackEditorButton!.IsEnabled == false, "Region locking immediately disables attack controls and handle hit testing"); locked.IsChecked = false;
        var stale = Viewport.Attack! with { Revision = document.Revision - 1 };
        var rejected = false;
        try { CommitAttackEdit(stale, "fixedAngle", 200); } catch (InvalidOperationException error) when (error.Message.Contains("stale", StringComparison.OrdinalIgnoreCase)) { rejected = true; }
        check(rejected && document.Source == beforeCancel && document.Revision == cancelRevision, "Stale visual gestures reject atomically through the shared source revision guard");
        var geometry = new EncounterViewport(); geometry.Arrange(new Rect(0, 0, 800, 500)); var cameraBeforeAttack = geometry.AttackPoint(0, 4);
        geometry.Attack = Viewport.Attack! with { Pattern = Viewport.Attack!.Pattern with { FixedAngle = null } }; geometry.FitAttack();
        var gestureCommits = 0; geometry.AttackEdited += (_, _, _) => gestureCommits++;
        geometry.BeginAttackDrag(geometry.AttackHandlePoint(AttackHandle.Aim)); geometry.EndAttackDrag();
        check(gestureCommits == 0 && geometry.Attack.Pattern.FixedAngle == null, "Clicking a player-targeted aim handle without moving retains automatic targeting and creates no edit");
        geometry.Attack = Viewport.Attack! with { Pattern = Viewport.Attack!.Pattern with { Spacing = -15 } };
        geometry.BeginAttackDrag(geometry.AttackHandlePoint(AttackHandle.Spread)); geometry.MoveAttackDrag(geometry.AttackPoint(40, 5));
        check(Math.Abs(geometry.Attack.Pattern.Spacing + 30) < .000001, "Spread gestures preserve negative native angular spacing across the signed angle boundary"); geometry.CancelAttackDrag();
        geometry.Attack = Viewport.Attack! with { Pattern = Viewport.Attack!.Pattern with { Spacing = 200 } };
        geometry.BeginAttackDrag(geometry.AttackHandlePoint(AttackHandle.Spread)); geometry.MoveAttackDrag(geometry.AttackPoint(150, 5));
        check(Math.Abs(geometry.Attack.Pattern.Spacing - 205) < .000001, "Spread gestures retain complete native revolutions rather than collapsing large spacing to one turn"); geometry.CancelAttackDrag();
        geometry.BeginAttackDrag(geometry.AttackHandlePoint(AttackHandle.Radius)); geometry.MoveAttackDrag(geometry.AttackPoint(0, 0));
        check(geometry.Attack.Pattern.Radius == 0, "Search-radius gestures support the native zero-radius value at the host origin"); geometry.CancelAttackDrag();
        geometry.Attack = Viewport.Attack! with { Pattern = Viewport.Attack!.Pattern with { FixedAngle = null, DefaultAngle = null, Radius = 6 } };
        check(!geometry.Attack.Pattern.CanFire(EncounterViewport.TargetDirection(geometry.Attack.Pattern)) && (geometry.AttackHandlePoint(AttackHandle.Aim) - geometry.AttackPoint(10, 4)).Length < .000001, "A target exactly at search radius keeps automatic aim inactive until the user sets an angle or expands radius");
        geometry.Attack = geometry.Attack with { Pattern = geometry.Attack.Pattern with { DefaultAngle = 60 } };
        check((geometry.AttackHandlePoint(AttackHandle.Aim) - geometry.AttackPoint(70, 4)).Length < .000001, "Viewport fallback handle includes the real default angle and offset when the prototype target is outside range");
        geometry.Attack = geometry.Attack with { Pattern = geometry.Attack.Pattern with { Radius = 1000 } }; geometry.FitAttack();
        check(new Rect(0, 0, 800, 500).Contains(geometry.AttackHandlePoint(AttackHandle.Radius)), "Large native search radii fit the authoring canvas with a reachable radius handle");
        geometry.Attack = null;
        check((geometry.AttackPoint(0, 4) - cameraBeforeAttack).Length < .000001, "Leaving attack mode restores the gameplay camera after inspecting a large radius");
        UpdateLayout(); CaptureView(this, Path.Combine(output, "attack-editor.png"));
        var projectPath = Path.Combine(output, "visual-attack.rbs"); BehaviorDocument.Save(projectPath, Snapshot()); var saved = BehaviorDocument.Load(projectPath); var reopened = new BehaviorDocument(saved.Source, adapter, saved.Identities, saved.SourceFile);
        check(reopened.Find(shot.Id).Parameters.Single(p => p.Name == "radius").Expression == "5.5" && reopened.Identities.SequenceEqual(document.Identities), "Visual attack edits and every editor identity persist through save and reopen");
        check(await StartSessionAsync() && Viewport.Attack == null && !attackEditing, "Starting native playback exits attack-edit mode and restores ordinary player input");
        running = false; await session!.AdvanceAsync(new(), 100);
        check(session.Shots.Single().Count == 5 && Math.Abs(session.Shots.Single().Angle - 70 * Math.PI / 180) < .000001, "Desktop playback executes the edited real Shoot fan with its native offset and spacing");
        Stop(this, new());

    }
}
