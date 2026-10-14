using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void DashSmoke() => await TryRuntime(async () =>
    {
        var output = Path.GetFullPath(".artifacts/dash-desktop"); Directory.CreateDirectory(output); var checks = new List<string>();
        await RunDashUiSmoke(output, (passed, label) => { if (!passed) throw new InvalidOperationException("UI check failed: " + label); checks.Add(label); });
        File.WriteAllText(Path.Combine(output, "ui-checks.json"), JsonSerializer.Serialize(new { checks }, BehaviorDocument.JsonOptions)); Application.Current.Shutdown(0);
    });
    async Task RunDashUiSmoke(string output, Action<bool, string> check)
    {
        var previous = Snapshot(); var file = selectedEntity!.SourceFile; var supplied = File.ReadAllText(file);
        const string source = "using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioDesktopDash=()=>{var dash=new Dash(speed:2,distance:4,targetRange:30,initialDelayMs:0,fullDistance:true,cooldownAfterEndMs:300);return Behav().Init(\"Cave Pirate Brawler\",new State(\"dash\",dash));};}}";
        ApplyProject(previous with { Source = source, Identities = [], SourceDrafts = [], SelectedSourceFile = null, LockedNodes = [], CollapsedNodes = [], Layout = [], AdditionalSources = [], Arena = new(), Player = previous.Player with { Invulnerable = true } }, null);
        WorkspaceTabs.SelectedIndex = 1; var phase = EncounterRoot; SelectNode(phase.Id);
        check(addDashFlankButton is { IsEnabled: true }, "A phase using a shared Dash offers its native linked-volley creation control");
        document.LockedNodes.Add(phase.Id); SelectNode(phase.Id); check(addDashFlankButton is { IsEnabled: false } && document.Source == source, "Phase locks disable shared-dash attack creation");
        document.LockedNodes.Clear(); SelectNode(phase.Id); addDashFlankButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var flank = selectedNode!; var dash = document.AllNodes.Single(n => n.Kind == "Dash");
        check(flank.Kind == "DashFlankVolley" && document.Expression(flank).Contains("(dash, 0)") && document.ReferencedDefinitions(flank).Any(d => d.Root.Id == dash.Id), "Actual Add retains the executed source-local Dash instead of constructing another controller");
        check(dashPanel is { IsExpanded: false } && projectilePicker != null && adapter.ReadProjectileIndex(document, flank) == 0, "Flank inspector keeps mechanics collapsed and exposes the real owner XML picker");
        var optional = (StackPanel)Inspector.Children.OfType<Expander>().Single(e => Equals(e.Header, "More supported parameters")).Content;
        var countLabel = optional.Children.OfType<TextBlock>().Single(t => t.Text.StartsWith("countPerSide ·"));
        ((TextBox)optional.Children[optional.Children.IndexOf(countLabel) + 1]).Text = "4"; parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(adapter.ReadDashFlank(document, document.Find(flank.Id)) is { CountPerSide: 4 }, "Typed fan Apply changes the original native count");
        Undo(this, new()); check(adapter.ReadDashFlank(document, document.Find(flank.Id)) is { CountPerSide: 3 }, "One Undo restores the complete fan edit"); Redo(this, new());
        var definition = document.Definitions.Single(d => d.Root.Id == dash.Id); EditDefinition(definition.EntityId);
        check(selectedNode?.Id == dash.Id && dashPanel is { IsExpanded: false } && DefinitionBanner.Visibility == Visibility.Visible, "Shared controller navigation opens the original Dash definition and native movement details");
        Inspector.Children.OfType<TextBox>().Single(t => t.Text == "4").Text = "5"; parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(adapter.ReadDash(document, document.Find(dash.Id))?.Distance == 5 && adapter.DashReferences(document, EncounterRoot).SequenceEqual(new[] { "dash" }), "Editing shared Dash distance preserves both real source references");
        dashPanel!.IsExpanded = true; UpdateLayout(); CaptureView(this, Path.Combine(output, "dash-movement-editor.png"));
        ReturnToEncounter(this, new()); SelectNode(flank.Id); dashPanel!.IsExpanded = true; UpdateLayout(); CaptureView(this, Path.Combine(output, "dash-flank-editor.png"));
        var diagram = ((StackPanel)dashPanel.Content).Children.OfType<Canvas>().Single();
        check(diagram.Children.OfType<Image>().Single().Source != null && diagram.Children.OfType<System.Windows.Shapes.Line>().Count() == 9, "Expanded fan diagram uses original client art and both native four-shot fan directions");
        var accepted = Snapshot(); check(await StartSessionAsync() && session is CompiledRuntime, "Play compiles shared native controllers with their actual source identity");
        running = false; await session!.AdvanceAsync(new(), 600); Pause(this, new());
        check(session.Shots.Count > 0 && session.Shots.All(s => s.Count == 4) && session.Entities.Single().X > 64.5, "Desktop-authored shared Dash moves and emits edited native flank packets");
        var trace = Path.Combine(output, "dash-trace.json"); session.ExportTrace(trace); check(File.ReadAllText(trace).Contains("DashFlankVolley"), "Trace export retains actual native controller callback evidence"); Stop(this, new());
        var saved = Path.Combine(output, "dash.rbs"); BehaviorDocument.Save(saved, accepted); ApplyProject(BehaviorDocument.Load(saved), saved);
        check(document.Source == accepted.Source && document.Identities.SequenceEqual(accepted.Identities), "Desktop save/reopen preserves linked controller source and identities");
        SelectNode(EncounterRoot.Id); BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "Dash"));
        check(createNativeButton is { IsEnabled: true } && constructorInputs.Values.All(t => t.Text == ""), "Dash creation selects the original optional constructor while retaining the legacy overload");
        createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode?.Kind == "Dash" && adapter.ReadDash(document, selectedNode) is { Speed: 20, Distance: 6, InitialDelayMs: 1500 }, "Actual Dash creation uses native defaults rather than an invented movement preset");
        Undo(this, new()); check(document.Source == accepted.Source, "One Undo removes the new native controller without altering its existing shared relationship");
        check(File.ReadAllText(file) == supplied, "Dash controls, edits and native Play preserve supplied source");
        ApplyProject(previous, null); WorkspaceTabs.SelectedIndex = 0;
    }
}
