using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow
{
    async Task RunOwnedActorsUiSmoke(string source, BehaviorNode root, string helpers, string output, Action<bool, string> check)
    {
        void Fixture(string expression) { ClearViewportEditors(); document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, expression) + helpers, adapter, sourceFile: selectedEntity!.SourceFile); Changed(); }
        Fixture("new State()"); SelectNode(EncounterRoot.Id); BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "InstantSpawnRing"));
        check(creationEntityPicker != null && constructorInputs.ContainsKey("childName"), "Native ring creation exposes its actual helper field and a lazy XML entity picker");
        creationEntityPicker!.IsExpanded = true; creationEntitySearch!.Text = "Ghost Ship";
        creationEntityChoices!.SelectedItem = creationEntityChoices.Items.Cast<EntityChoice>().Single(c => c.Id == "Ghost Ship"); creationUseEntityButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        constructorInputs["radius"].Text = "2"; createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(EncounterRoot.Children.Single().Kind == "InstantSpawnRing" && entityPicker != null && lifecycleRows.Count == 3, "Native ring creation produces an editable resource-backed action with real entry, exit and death lifecycle rows");
        Fixture("new State(new State(\"ring\", new " + adapter.CreationTypeName("InstantSpawnRing") + "(\"Ghost Ship\", radius: 2), new " + adapter.CreationTypeName("OrderOnce") + "(10, \"Ghost Ship\", \"pause\"), new TimedTransition(300, \"finished\")), new State(\"finished\"))");
        var order = EncounterRoot.Descendants().Single(n => n.Kind == "OrderOnce"); SelectNode(order.Id);
        check(entityPicker != null && Inspector.Children.OfType<TextBlock>().Any(t => t.Text.StartsWith("Orders address nearby")), "Order inspection explains target-entity phase ownership and exposes its real entity selector");
        BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "OrderOnce"));
        check(constructorInputs["targetState"].Text == "" && !createNativeButton!.IsEnabled, "Order creation never seeds a target entity's phase with an unrelated host phase name"); SelectNode(order.Id);
        check(await StartSessionAsync(), "Desktop starts the edited native ring and helper-order encounter"); running = false; await session!.AdvanceAsync(new(), 100); UpdateSession();
        var owner = session.Entities.Single(e => e.Name == selectedEntity!.Id); var helper = session.Entities.First(e => e.ParentId == owner.Id);
        RuntimeActors.SelectedItem = RuntimeActors.Items.Cast<RuntimeActorChoice>().Single(c => c.Id == helper.Id);
        check(RuntimeActors.Items.Count == 3 && RuntimeActorInfo.Text.Contains("Native parent: #" + owner.Id) && RuntimeActorInfo.Text.Contains("Phase: pause") && RuntimeActorInfo.Text.Contains("Spawned by behavior: yes"), "Runtime actor rows show actual helper sprites, actor IDs, parent IDs, phases and native behavior-spawn flags");
        var retainedRows = RuntimeActors.ItemsSource; RefreshRuntimeActors();
        check(ReferenceEquals(retainedRows, RuntimeActors.ItemsSource) && (RuntimeActors.SelectedItem as RuntimeActorChoice)?.Id == helper.Id, "Unchanged native actor snapshots retain list rows and the selected helper");
        WorkspaceTabs.SelectedIndex = 3; DetailsTabs.SelectedIndex = 2; UpdateLayout(); CaptureView(this, Path.Combine(output, "owned-actors.png"));
        await session.AdvanceAsync(new(), 400); UpdateSession();
        check(RuntimeActors.Items.Count == 1 && RuntimeActorInfo.Text.Contains("Phase: finished"), "Native phase cleanup removes runtime helper rows and selects the surviving host");
        Stop(this, new()); check(RuntimeActors.Items.Count == 0 && RuntimeActorInfo.Text.StartsWith("Play an encounter"), "Stopping playback clears runtime ownership data and restores the empty actor view");
    }
}
