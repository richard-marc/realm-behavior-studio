using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow
{
    async Task RunFragmentUiSmoke(string source, BehaviorNode root, string output, Action<bool, string> check)
    {
        fragmentDirectory = Path.Combine(output, "fragments"); RefreshFragments();
        const string scene = "new State(new State(\"saved fan\", new Shoot(12, count: 2, coolDown: 1000), new TimedTransition(1000, \"finish\")), new State(\"finish\"), new State(\"destination\"), new State(\"done\"))";
        document = new BehaviorDocument(source.Remove(root.Start, root.Length).Insert(root.Start, scene), adapter, sourceFile: selectedEntity!.SourceFile);
        Changed(); var phase = EncounterRoot.StateChildren.Single(n => n.Label == "saved fan"); var target = EncounterRoot.StateChildren.Single(n => n.Label == "destination");
        SelectAuthoringNode(phase.Id, ModifierKeys.None);
        FragmentCaptureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(WorkspaceTabs.SelectedIndex == 4 && fragmentCapturing && FragmentNameFields.Visibility == Visibility.Visible && fragmentFields.Count > 0 && FragmentSaveButton.Visibility == Visibility.Visible, "Actual Save selection control opens the fragment template editor with available native fields");
        var field = fragmentFields.Single(f => f.Parameter.Label.EndsWith("· count")); field.Expose.IsChecked = true; field.Input.Text = "shots";
        FragmentName.Text = "Studio fragment fan"; FragmentDescription.Text = "Reusable native fan with a mapped finish";
        var before = document.Source;
        FragmentSaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(!fragmentCapturing && fragmentEditing?.Parameters.Single().Name == "shots" && File.Exists(Path.Combine(fragmentDirectory, fragmentEditing.Id + ".rbsfrag")) && document.Source == before, "Save template persists the chosen exposed input without changing the encounter");
        FragmentSearch.Text = "Studio fragment fan";
        check(FragmentLibrary.Items.Count >= 1, "Local fragment search lists the saved reusable template");
        var saved = fragmentEditing!; SelectNode(target.Id); ShowFragment(saved, false);
        fragmentFields.Single().Input.Text = "6"; fragmentPhaseTargets["finish"].SelectedItem = "done";
        FragmentPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(FragmentInsertButton.IsEnabled && FragmentPreview.Text.Contains("count: 6") && FragmentPreview.Text.Contains("saved fan-copy") && FragmentPreview.Text.Contains("\"done\"") && document.Source == before, "Preview validates actual bound values, copied phase names and external mappings without changing source");
        await Dispatcher.InvokeAsync(() => UpdateLayout(), DispatcherPriority.ApplicationIdle);
        CaptureView(this, Path.Combine(output, "fragment-template.png"));
        fragmentFields.Single().Input.Text = "7";
        check(!FragmentInsertButton.IsEnabled, "Editing a template input disables insertion of its stale preview");
        FragmentPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var revision = document.Revision;
        FragmentInsertButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var copied = selectedNode!;
        check(document.Revision == revision + 1 && copied.Label == "saved fan-copy" && document.Find(target.Id).Children.Single().Id == copied.Id && copied.Children.Single(n => n.Kind == "Shoot").Parameters.Any(p => p.Name == "count" && p.Expression == "7"), "Actual Insert creates the bound fragment inside the selected native phase in one revision");
        var inserted = document.Source; Undo(this, new());
        check(document.Source == before && document.Find(target.Id).Children.Count == 0, "Desktop Undo restores the source before the entire fragment insertion");
        Redo(this, new()); check(document.Source == inserted, "Desktop Redo restores the inserted template and values");
        WorkspaceTabs.SelectedIndex = 4; var layout = CaptureLayout(); WorkspaceTabs.SelectedIndex = 0; RestoreLayout(layout);
        check(WorkspaceTabs.SelectedIndex == 4, "The fragments workspace participates in saved layout restoration");
        SelectNode(target.Id); ShowFragment(saved, false); fragmentPhaseTargets["finish"].SelectedItem = "done"; PrepareFragmentPreview();
        SelectNode(EncounterRoot.StateChildren.Single(n => n.Label == "done").Id);
        check(!FragmentInsertButton.IsEnabled, "Changing the destination selection clears the previous fragment insertion preview");
        SelectNode(target.Id); ShowFragment(saved, false); fragmentPhaseTargets["finish"].SelectedItem = "done"; PrepareFragmentPreview();
        document.LockedNodes.Add(target.Id); var rejected = false; var stable = document.Source;
        try { CommitFragmentInsertion(); } catch (InvalidOperationException) { rejected = true; }
        document.LockedNodes.Clear(); check(rejected && document.Source == stable, "A newly locked destination rejects a previously validated fragment insertion atomically");
        var native = await StartSessionAsync("destination"); running = false;
        check(native && session is EncounterSession, "Imported literal template source remains playable through native playback");
        await session!.AdvanceAsync(new(), 100); UpdateSession(); check(session.Shots.Any(s => s.Count == 7), "Desktop playback emits the template's actual bound native projectile count");
        Stop(this, new()); WorkspaceTabs.SelectedIndex = 0; FragmentSearch.Text = "";
    }
}
