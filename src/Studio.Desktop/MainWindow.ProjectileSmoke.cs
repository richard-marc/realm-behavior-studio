using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow
{
    async Task RunProjectileUiSmoke(string output, Action<bool, string> check)
    {
        var previousEntity = selectedEntity!; Library.SelectedItem = catalog.Single(e => e.Id == "Ghost Ship");
        var source = document.Source; var root = EncounterRoot;
        const string expression = "new State(new Shoot(8, projectileIndex: 999, fixedAngle: 0, coolDown: 100))";
        document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, expression), adapter, sourceFile: selectedEntity!.SourceFile); Changed();
        var shot = EncounterRoot.Children.Single(); SelectNode(shot.Id);
        check(projectilePicker is { IsExpanded: false } && projectileChoices!.Items.Count == 2 && projectileChoices.SelectedItem == null && projectileInfo!.Text.Contains("absent"), "Projectile picker stays collapsed, retains real XML indices and explains an absent current index");
        projectilePicker!.IsExpanded = true; var before = document.Source; var revision = document.Revision;
        projectileSearch!.Text = "giant";
        var row = projectileChoices!.Items.Cast<ListBoxItem>().Single(); var choice = (EnemyProjectile)row.Tag; projectileChoices.SelectedItem = row;
        check(choice.Index == 1 && ((StackPanel)row.Content).Children.OfType<Image>().Single().Source != null, "Search selects the actual Giant Cannonball XML index and real client sprite");
        check(document.Source == before && document.Revision == revision && useProjectileButton!.IsEnabled && projectileInfo!.Text.Contains("250"), "Inspecting a projectile shows its native damage without changing source or undo history");
        var locked = Inspector.Children.OfType<CheckBox>().Single(); locked.IsChecked = true;
        check(!useProjectileButton!.IsEnabled, "Region locks immediately disable projectile commits while retaining inspection"); locked.IsChecked = false;
        check(useProjectileButton.IsEnabled, "Unlocking restores a valid selected projectile choice");
        UpdateLayout(); CaptureView(this, Path.Combine(output, "projectile-picker.png"));
        useProjectileButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Revision == revision + 1 && adapter.ReadProjectileIndex(document, document.Find(shot.Id)) == 1 && selectedNode!.Id == shot.Id, "Use projectile commits one typed source change and keeps the native action identity");
        check(!useProjectileButton!.IsEnabled && projectilePicker!.Header.ToString()!.Contains("#1"), "Refreshed picker follows the committed index and disables redundant commits");
        var savedPath = Path.Combine(output, "projectile-choice.rbs"); BehaviorDocument.Save(savedPath, Snapshot()); var saved = BehaviorDocument.Load(savedPath); var reopened = new BehaviorDocument(saved.Source, adapter, saved.Identities, saved.SourceFile);
        check(adapter.ReadProjectileIndex(reopened, reopened.Find(shot.Id)) == 1 && reopened.Identities.SequenceEqual(document.Identities), "XML projectile choices and every source identity persist through save and reopen");
        check(await StartSessionAsync(), "Desktop starts native playback after choosing a real enemy projectile"); running = false; await session!.AdvanceAsync(new(), 100);
        check(session.Shots.Single() is { Index: 1, Damage: 250 } && session.Projectiles.Single().TextureId == "Giant Cannonball", "Desktop firing uses the chosen native index, XML damage and client texture"); Stop(this, new());
        Undo(this, new()); SelectNode(shot.Id);
        check(document.Source == before && adapter.ReadProjectileIndex(document, selectedNode!) == 999, "One Undo restores the complete previous source expression and invalid original index");
        var invalidRejected = false; try { CommitProjectileChoice(shot.Id, document.Revision, 123); } catch (InvalidOperationException) { invalidRejected = true; }
        check(invalidRejected && document.Source == before, "Indices outside this enemy's XML reject without changing source");
        var staleRejected = false; try { CommitProjectileChoice(shot.Id, document.Revision - 1, 1); } catch (InvalidOperationException error) when (error.Message.Contains("stale")) { staleRejected = true; }
        check(staleRejected && document.Source == before, "Stale projectile proposals use the shared atomic revision guard");
        projectileSearch!.Text = "no matching projectile";
        check(projectileChoices!.Items.Count == 0 && !useProjectileButton!.IsEnabled && projectileInfo!.Text.Contains("No matching"), "An empty projectile search has a clear message and cannot commit a stale selection");
        projectileSearch.Text = "";
        check(projectileChoices.Items.Count == 2 && document.Source == before, "Clearing search restores the owner's XML choices without applying anything");
        const string getter = "\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static double StudioProjectileUnknown { get { throw new System.InvalidOperationException(\"authoring must not execute projectile getters\"); } } } }";
        const string unknownExpression = "new State(new Shoot(8, fixedAngle: StudioProjectileUnknown, projectileIndex: (int)StudioProjectileUnknown))";
        document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, unknownExpression) + getter, adapter, sourceFile: selectedEntity!.SourceFile); Changed(); var unknownShot = EncounterRoot.Children.Single(); SelectNode(unknownShot.Id);
        check(attackEditorButton == null && projectilePicker != null && projectileChoices!.SelectedItem == null && projectilePicker.Header.ToString()!.Contains("source expression"), "Unknown angle and index getters retain a usable XML picker without pretending the current index is zero or executing source");
        var unknownBefore = document.Source; var unknownRevision = document.Revision; projectileChoices!.SelectedItem = projectileChoices.Items.Cast<ListBoxItem>().Single(item => ((EnemyProjectile)item.Tag).Index == 1); useProjectileButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Revision == unknownRevision + 1 && adapter.ReadProjectileIndex(document, document.Find(unknownShot.Id)) == 1 && document.Find(unknownShot.Id).Parameters.Single(p => p.Name == "fixedAngle").Expression == "StudioProjectileUnknown", "Choosing a projectile replaces only its source expression while unknown geometry remains untouched");
        Undo(this, new()); check(document.Source == unknownBefore, "One Undo restores the unknown projectile source expression without invoking its getter");
        Library.SelectedItem = previousEntity;
    }
}
