using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    void RunNativeUiSmoke(string output, Action<bool, string> check)
    {
        var baseline = document.Source; var root = EncounterRoot;
        const string fixture = """
        new State(new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
            new State("opening", new Taunt("one", "two"), new Shoot(8), new TimedTransition(1000, "closing")),
            new State("closing", new Wander(.2)))
        """;
        document.ReplaceSource(document.Source.Remove(root.Start, root.Length).Insert(root.Start, fixture), d => adapter.Validate(d, selectedEntity!.Id)); Changed();
        WorkspaceTabs.SelectedIndex = 1;
        var phase = EncounterRoot.Children.Single(n => n.Label == "opening"); SelectNode(phase.Id);
        lifecyclePanel!.IsExpanded = true; UpdateLayout();
        check(lifecycleRows.Any(row => ((TextBlock)row.Content).Text.Contains("from Root")) && lifecycleRows.Count == 6, "Phase inspector shows inherited native entry and exit actions alongside direct callbacks");
        CaptureView(this, Path.Combine(output, "native-lifecycle.png"));
        var parent = EncounterRoot.Children.Single(n => n.Kind == "ConditionEffectBehavior");
        lifecycleRows.First(row => row.Tag is Guid id && id == parent.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode!.Id == parent.Id && Inspector.Children.OfType<TextBlock>().Any(t => t.Text == "effect"), "Inherited lifecycle row opens the actual editable parent action");
        var taunt = phase.Children.Single(n => n.Kind == "Taunt"); SelectNode(taunt.Id);
        var inputs = Inspector.Children.OfType<TextBox>().ToArray();
        check(inputs.Length == 2 && Inspector.Children.OfType<TextBlock>().Any(t => t.Text == "text[1]"), "Selected overload renders repeated strings as separate fields without unrelated optional controls");
        inputs[1].Text = "\"edited\""; Inspector.Children.OfType<Button>().Single(b => Equals(b.Content, "Apply changes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(taunt.Id).Parameters.Select(p => p.Expression).SequenceEqual(["\"one\"", "\"edited\""]), "Desktop repeated-parameter edit changes only the second argument");
        SelectNode(phase.Id); BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "Taunt"));
        check(constructorChoices!.Items.Count == 8 && createNativeButton!.IsEnabled, "Native creation form offers all overloads and validates its initial array");
        constructorChoices.SelectedIndex = 7;
        constructorInputs["probability"].Text = ".5"; constructorInputs["broadcast"].Text = "true"; constructorInputs["cooldown"].Text = "new Cooldown(1000, 0)";
        constructorInputs["text"].Text = "new[] { \"created\", \"again\" }";
        check(createNativeButton.IsEnabled && constructorPreview!.Text.Contains("probability: .5"), "Creation preview respects required values, named arguments and native Cooldown types");
        constructorInputs["probability"].Text = "\"invalid\"";
        check(!createNativeButton.IsEnabled, "Invalid constructor conversion disables creation before committing");
        constructorInputs["probability"].Text = ".5"; UpdateLayout();
        CaptureView(this, Path.Combine(output, "native-creation.png"));
        createNativeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(phase.Id).Children.Count(n => n.Kind == "Taunt") == 2 && selectedNode!.Parameters.Single(p => p.Name == "text").Expression.Contains("created"), "Creation commits the selected native overload into the selected phase");
        document.LockedNodes.Add(phase.Id); SelectNode(phase.Id); BeginNativeCreation(adapter.Primitives.Single(p => p.Name == "Wander"));
        check(!createNativeButton!.IsEnabled, "Native creation honors parent region locks"); document.LockedNodes.Clear();
        document.ReplaceSource(baseline); Changed(); WorkspaceTabs.SelectedIndex = 0;
    }
}
