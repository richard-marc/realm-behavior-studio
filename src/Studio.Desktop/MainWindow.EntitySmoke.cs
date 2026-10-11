using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow
{
    async Task RunEntityUiSmoke(string source, BehaviorNode root, string helpers, string output, Action<bool, string> check)
    {
        void Fixture(string expression, string extra = "")
        {
            ClearViewportEditors(); document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, expression) + helpers + extra, adapter, sourceFile: selectedEntity!.SourceFile); Changed();
        }
        IEnumerable<DependencyObject> Visuals(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i); yield return child;
                foreach (var descendant in Visuals(child)) yield return descendant;
            }
        }
        Fixture("new State(new Spawn(\"Cave Pirate Brawler\", maxChildren: 1, initialSpawn: 1))");
        var spawn = EncounterRoot.Children.Single(); SelectNode(spawn.Id);
        check(entityPicker is { IsExpanded: false } && entityChoices!.Items.Count == 0, "Entity picker starts collapsed and loads its XML rows only when opened");
        entityPicker!.IsExpanded = true; var before = document.Source; var revision = document.Revision;
        entitySearch!.Text = "Ghost Ship"; var ghost = entityChoices!.Items.Cast<EntityChoice>().Single(c => c.Id == "Ghost Ship"); entityChoices.SelectedItem = ghost;
        check(useEntityButton!.IsEnabled && entityInfo!.Text.Contains("Enemy") && entityInfo.Text.Contains("HP"), "Entity search shows the selected native enemy's XML class and combat values");
        check(document.Source == before && document.Revision == revision, "Browsing XML entity choices does not change source or undo history");
        var locked = Inspector.Children.OfType<CheckBox>().Single(); locked.IsChecked = true;
        check(!useEntityButton!.IsEnabled && entityChoices.SelectedItem == ghost, "Source locks disable entity commits while keeping the selected resource inspectable"); locked.IsChecked = false;
        check(useEntityButton.IsEnabled, "Unlocking restores a valid pending entity choice");
        UpdateLayout(); var row = (ListBoxItem)entityChoices.ItemContainerGenerator.ContainerFromItem(ghost);
        check(Visuals(row).OfType<Image>().Single().Source != null, "The actual WPF entity row resolves its real client sprite");
        CaptureView(this, Path.Combine(output, "entity-picker.png"));
        useEntityButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Revision == revision + 1 && document.Find(spawn.Id).Parameters.Single(p => p.Name == "children").Expression == "\"Ghost Ship\"" && selectedNode!.Id == spawn.Id, "Use entity commits the native Spawn ID once and retains the selected action identity");
        check(entityPicker!.Header is TextBlock { Text: var header } && header.Contains("Ghost Ship") && !useEntityButton!.IsEnabled, "The refreshed collapsed picker follows the committed entity ID");
        var savedPath = Path.Combine(output, "entity-choice.rbs"); BehaviorDocument.Save(savedPath, Snapshot()); var saved = BehaviorDocument.Load(savedPath);
        var reopened = new BehaviorDocument(saved.Source, adapter, saved.Identities, saved.SourceFile);
        check(reopened.Find(spawn.Id).Parameters.Single(p => p.Name == "children").Expression == "\"Ghost Ship\"" && reopened.Identities.SequenceEqual(document.Identities), "Entity choices and all source identities survive save and reopen");
        check(await StartSessionAsync(), "Desktop Play accepts the chosen native XML spawn entity"); running = false;
        await session!.AdvanceAsync(new(), 200);
        check(session.Entities.Any(e => e.Name == "Ghost Ship"), "Desktop native playback creates the selected actual enemy"); Stop(this, new());
        Undo(this, new()); SelectNode(spawn.Id);
        check(document.Source == before, "One Undo restores the whole original spawn source");
        entityPicker!.IsExpanded = true; entitySearch!.Text = "no matching entity resource";
        check(entityChoices!.Items.Count == 0 && !useEntityButton!.IsEnabled && entityInfo!.Text == "No matching entities.", "Empty entity searches explain the result and cannot commit an old selection");
        entitySearch.Text = "";
        check(entityChoices.Items.Count == EntityResources.Catalog(EntityResourceScope.Enemy).Count && document.Source == before, "Clearing search restores only native enemy spawn choices without changing source");

        Fixture("new State(new Follow(.3, target: \"Ghost Ship\"))"); var follow = EncounterRoot.Children.Single(); SelectNode(follow.Id); entityPicker!.IsExpanded = true;
        entitySearch!.Text = "Nearest player"; entityChoices!.SelectedItem = entityChoices.Items.Cast<EntityChoice>().Single(); useEntityButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(follow.Id).Parameters.Single(p => p.Name == "target").Expression == "null" && adapter.ReadMovementPattern(document, document.Find(follow.Id))!.Target == null, "Nearest-player choice uses native null targeting and restores the movement diagram's player prototype");

        const string getter = "\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static string StudioUnknownEntityTarget { get { throw new System.InvalidOperationException(\"entity authoring must not execute source\"); } } } }";
        Fixture("new State(new Orbit(.3, 3, target: StudioUnknownEntityTarget))", getter); var orbit = EncounterRoot.Children.Single(); SelectNode(orbit.Id);
        check(entityPicker?.Header is TextBlock { Text: var unknownHeader } && unknownHeader.Contains("source expression") && movementEditorButton == null, "Unknown entity getters keep a usable resource picker without invented movement geometry or source execution");
        entityPicker!.IsExpanded = true; entitySearch!.Text = "Ghost Ship"; entityChoices!.SelectedItem = entityChoices.Items.Cast<EntityChoice>().Single(c => c.Id == "Ghost Ship");
        var unknownBefore = document.Source; useEntityButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(orbit.Id).Parameters.Single(p => p.Name == "target").Expression == "\"Ghost Ship\"" && document.Find(orbit.Id).Parameters.Single(p => p.Name == "radius").Expression == "3", "Picking an entity replaces only the unknown ID expression and keeps native radius intact");
        Undo(this, new()); check(document.Source == unknownBefore, "Undo restores an unknown entity getter without evaluating it");

        Fixture("new State(new State(\"waiting\", new EntityNotExistsTransition(\"Ghost Ship\", 10, \"done\", checkAttackTarget: true)), new State(\"done\"))");
        var transition = EncounterRoot.Descendants().Single(n => n.Kind == "EntityNotExistsTransition"); SelectNode(transition.Id); entityPicker!.IsExpanded = true;
        entitySearch!.Text = "Cave Pirate Brawler"; entityChoices!.SelectedItem = entityChoices.Items.Cast<EntityChoice>().Single(c => c.Id == "Cave Pirate Brawler");
        check(!useEntityButton!.IsEnabled && entityInfo!.Text.Contains("XML target is unused"), "Native attack-target mode explains and disables its inactive XML entity parameter");
        Fixture("StudioPreviewRoot(count: 4)"); SelectNode(EncounterRoot.Id);
        check(entityPicker == null, "Source factories do not receive entity controls based on guessed parameter names");

        Fixture("new State()"); var owner = EncounterRoot; SelectNode(owner.Id); BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "Spawn"));
        check(creationEntityPicker is { IsExpanded: false } && creationEntityChoices!.Items.Count == 0, "The native Add Spawn form offers a collapsed lazy XML entity chooser");
        var creationBefore = document.Source; var creationRevision = document.Revision;
        creationEntityPicker!.IsExpanded = true; creationEntitySearch!.Text = "Ghost Ship"; creationEntityChoices!.SelectedItem = creationEntityChoices.Items.Cast<EntityChoice>().Single(c => c.Id == "Ghost Ship");
        creationUseEntityButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(constructorInputs["children"].Text == "\"Ghost Ship\"" && constructorPreview!.Text.Contains("children: \"Ghost Ship\"") && createNativeButton!.IsEnabled, "Entity selection fills the native constructor field and validates its source preview");
        check(document.Source == creationBefore && document.Revision == creationRevision, "Choosing a creation input leaves the document untouched until Add");
        creationEntityPicker.IsExpanded = true; UpdateLayout(); CaptureView(this, Path.Combine(output, "entity-creation.png"));
        createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Revision == creationRevision + 1 && EncounterRoot.Children.Single().Parameters.Single(p => p.Name == "children").Expression == "\"Ghost Ship\"", "Add Spawn inserts the chosen actual XML entity through one atomic native constructor edit");
        Undo(this, new()); check(document.Source == creationBefore, "One Undo removes the new resource-backed native Spawn action");
    }
}
