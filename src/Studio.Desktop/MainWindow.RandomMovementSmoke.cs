using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void RandomMovementSmoke() => await TryRuntime(async () =>
    {
        var output = Path.GetFullPath(".artifacts/random-movement-desktop"); Directory.CreateDirectory(output); var checks = new List<string>();
        await RunRandomMovementUiSmoke(output, (passed, label) => { if (!passed) throw new InvalidOperationException("UI check failed: " + label); checks.Add(label); });
        File.WriteAllText(Path.Combine(output, "ui-checks.json"), JsonSerializer.Serialize(new { checks }, BehaviorDocument.JsonOptions)); Application.Current.Shutdown(0);
    });
    async Task RunRandomMovementUiSmoke(string output, Action<bool, string> check)
    {
        var previous = Snapshot(); var file = selectedEntity!.SourceFile; var supplied = File.ReadAllText(file);
        const string source = "using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioDesktopRandomMove=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"motion\",new Swirl()));}}";
        ApplyProject(previous with { Source = source, Identities = [], SourceDrafts = [], SelectedSourceFile = null, LockedNodes = [], CollapsedNodes = [], Layout = [], AdditionalSources = [], Arena = new(), Player = previous.Player with { Invulnerable = true, StartX = 68.5, StartY = 67.5 } }, null);
        WorkspaceTabs.SelectedIndex = 1; var swirl = document.AllNodes.Single(n => n.Kind == "Swirl"); SelectNode(swirl.Id);
        check(randomMovePanel is { IsExpanded: false } && swirlTargeting is { SelectedIndex: 0 }, "Native Swirl details stay collapsed with the original player-targeting default");
        randomMovePanel!.IsExpanded = true; swirlTargeting!.SelectedIndex = 1;
        check(adapter.ReadSwirl(document, document.Find(swirl.Id)) is { Targeted: false } && document.Find(swirl.Id).Id == swirl.Id, "Actual center choice appends the native optional field and preserves identity");
        Undo(this, new()); check(document.Source == source, "One Undo restores the exact original optional constructor"); Redo(this, new());
        randomMovePanel!.IsExpanded = true; UpdateLayout();
        var diagram = ((StackPanel)randomMovePanel.Content).Children.OfType<Canvas>().Single();
        check(diagram.Children.OfType<Image>().Single().Source != null && diagram.Children.OfType<System.Windows.Shapes.Ellipse>().Count() == 1, "Entry-centered diagram uses actual client sprite art and one steering ring");
        CaptureView(this, Path.Combine(output, "swirl-movement.png"));
        Inspector.Children.OfType<CheckBox>().Single().IsChecked = true;
        check(swirlTargeting is { IsEnabled: false }, "Region lock disables the native center chooser");
        document.LockedNodes.Clear(); SelectNode(swirl.Id);
        var optional = (StackPanel)Inspector.Children.OfType<Expander>().Single(e => Equals(e.Header, "More supported parameters")).Content;
        var label = optional.Children.OfType<TextBlock>().Single(t => t.Text.StartsWith("radius ·")); ((TextBox)optional.Children[optional.Children.IndexOf(label) + 1]).Text = "5";
        parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(adapter.ReadSwirl(document, document.Find(swirl.Id)) is { Radius: 5, Targeted: false }, "Typed radius Apply retains the chosen native center mode");
        SelectNode(EncounterRoot.Id); BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "JumpToRandomOffset"));
        check(createNativeButton is { IsEnabled: false } && constructorInputs.Keys.Order().SequenceEqual(new[] { "maxX", "maxY", "minX", "minY" }), "Jump creation requires all four actual integer offset bounds");
        foreach (var name in new[] { "minX", "maxX" }) constructorInputs[name].Text = "2";
        foreach (var name in new[] { "minY", "maxY" }) constructorInputs[name].Text = "-1";
        createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var jump = selectedNode!;
        check(jump.Kind == "JumpToRandomOffset" && adapter.ReadJumpOffset(document, jump) is { Columns: 1, Rows: 1 }, "Actual native creation keeps equal bounds as a fixed landing offset");
        randomMovePanel!.IsExpanded = true; UpdateLayout(); CaptureView(this, Path.Combine(output, "jump-entry-offsets.png"));
        diagram = ((StackPanel)randomMovePanel.Content).Children.OfType<Canvas>().Single();
        check(diagram.Children.OfType<Image>().Single().Source != null && diagram.Children.OfType<System.Windows.Shapes.Ellipse>().Count() == 1, "Jump diagram shows one integer landing point with actual host art");
        var accepted = Snapshot(); check(await StartSessionAsync(observeCallbacks: true) && session is CompiledRuntime, "Desktop Play compiles original movement source");
        running = false; await session!.AdvanceAsync(new(), 700); Pause(this, new());
        check(session.Entities.Single().X > 65 && session.Callbacks.Any(c => c.Origin?.NodeId == jump.Id), "Desktop-authored jump enters through actual source-linked callbacks while Swirl continues movement");
        Stop(this, new());
        var saved = Path.Combine(output, "random-movement.rbs"); BehaviorDocument.Save(saved, accepted); ApplyProject(BehaviorDocument.Load(saved), saved);
        check(document.Source == accepted.Source && document.Identities.SequenceEqual(accepted.Identities), "Save/reopen retains movement source and complete identities");
        SelectNode(jump.Id); var before = document.Source;
        var rejected = false;
        try { document.Apply(new(document.Revision, "Invalid native bounds", [new(jump.Id, "minX", "3")]), ValidateAuthoring); }
        catch (ArgumentException) { rejected = true; }
        check(rejected && document.Source == before && adapter.ReadJumpOffset(document, document.Find(jump.Id)) is { MinX: 2, MaxX: 2 }, "Reversed native range rejects before changing the accepted desktop document");
        check(File.ReadAllText(file) == supplied, "Movement controls, history and compiled Play preserve supplied game source");
        ApplyProject(previous, null); WorkspaceTabs.SelectedIndex = 0;
    }
}
