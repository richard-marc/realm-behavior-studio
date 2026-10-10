using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow
{
    Expander? lifecyclePanel;
    readonly List<Button> lifecycleRows = [];
    ComboBox? constructorChoices;
    readonly Dictionary<string, TextBox> constructorInputs = [];
    TextBox? constructorPreview;
    Button? createNativeButton;
    Button? chooseChildSlotButton;
    void AddArgumentControls(BehaviorNode node)
    {
        chooseChildSlotButton = null;
        if (node.Kind != "ArgumentGroup" || node.Argument is not { } slot) return;
        var description = node.ArgumentRequiresSource ? "This collection comes from source. Open Source to edit its definition." : slot.IsCollection ? node.ArgumentFromCall && !slot.IsParams && node.Children.Count == 0 ? "No list is assigned. Adding a child creates an explicit " + slot.ElementType + (slot.IsArray ? " array." : " collection.") : (slot.IsArray ? "Ordered " + slot.ElementType + " list." : slot.ElementType + " inputs in source order.") + " Add a child or organize its contents." : (slot.Minimum == 0 ? "Optional " : "Required ") + slot.ElementType + " child. " + (node.Children.Count == 0 ? "Choose an action to fill this slot." : "Choose an action to replace this child in one edit.");
        Inspector.Children.Add(new TextBlock { Text = description, ToolTip = slot.CollectionType, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray, Margin = new(0, 12, 0, 8) });
        if (node.ArgumentRequiresSource)
        {
            var source = new Button { Content = "Open Source", Style = (Style)FindResource("QuietButton") };
            source.Click += (_, _) => { WorkspaceTabs.SelectedIndex = 2; SourceEditor.Select(node.Start, node.Length); SourceEditor.Focus(); }; Inspector.Children.Add(source); return;
        }
        chooseChildSlotButton = new Button { Content = slot.IsCollection ? "+ Add child" : node.Children.Count > 0 ? "Replace child" : "Choose child", Style = (Style)FindResource("QuietButton"), IsEnabled = RegionEditable(node) };
        chooseChildSlotButton.Click += (_, _) => { LibraryTabs.SelectedIndex = 1; PrimitiveSearch.Text = ""; PrimitiveSearch.Focus(); }; Inspector.Children.Add(chooseChildSlotButton);
    }
    BehaviorNode OwnerPhase(BehaviorNode node) => node.Kind == "State" ? node : OwnerPhase(document.Parent(node.Id) ?? throw new ArgumentException("Select a phase first."));
    void AddLifecycleControls(BehaviorNode node)
    {
        if (node.Kind is "ArgumentGroup" or "Factory" or "Reference" or "Spread" or "Opaque" || node.Kind != "State" && !document.Entities.SelectMany(e => e.Root.Descendants()).Any(p => p.Kind == "State" && node.Path.StartsWith(p.Path + "/", StringComparison.Ordinal)))
        { lifecycleRows.Clear(); lifecyclePanel = null; return; }
        lifecycleRows.Clear(); var body = new StackPanel();
        var actions = node.Kind == "State" ? adapter.PhaseActions(document, node.Id) : [new LifecycleAction(node, OwnerPhase(node), false, adapter.EffectiveCallbacks(document, node))];
        foreach (var stage in new[] { "Entry", "Ongoing", "Exit", "Death" })
        {
            var members = actions.Where(a => a.Callbacks.Any(c => c.Stage == stage)).ToArray();
            if (members.Length == 0) continue;
            body.Children.Add(new TextBlock { Text = stage, FontWeight = FontWeights.SemiBold, Margin = new(0, 12, 0, 4) });
            foreach (var action in members)
            {
                var callback = action.Callbacks.Single(c => c.Stage == stage);
                var row = new Button { Content = new TextBlock { Text = action.Node.Kind + (action.Inherited ? " · from " + action.Phase.Label : ""), TextWrapping = TextWrapping.Wrap },
                    Tag = action.Node.Id, Style = (Style)FindResource("QuietButton"), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new(0, 2, 0, 2),
                    ToolTip = callback.DeclaringType + "." + callback.Method + " · " + System.IO.Path.GetFileName(callback.SourceFile) + ":" + callback.Line };
                row.Click += (_, _) => SelectNode(action.Node.Id); body.Children.Add(row); lifecycleRows.Add(row);
            }
        }
        body.Children.Add(new TextBlock { Text = "Callbacks come from the game source. Parent actions remain active in child phases. Select an action to edit it; drag it to a different phase to change its scope.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = EncounterViewport.Brush("#979DA7"), Margin = new(0, 12, 0, 4) });
        lifecyclePanel = new Expander { Header = "Lifecycle · " + actions.Count + " actions", Content = body, Margin = new(0, 14, 0, 6) };
        if (lifecycleRows.Count > 0) Inspector.Children.Add(lifecyclePanel);
    }
    void BeginNativeCreation(Primitive primitive)
    {
        ClearAttackEditor();
        var focus = selectedNode ?? throw new ArgumentException("Select a phase or argument list first.");
        if (focus.Children.Count(n => n.Kind == "ArgumentGroup") > 1) throw new ArgumentException("Select the named child slot in this wrapper first.");
        var target = document.CanEditChildren(focus.Id) ? focus : document.Parent(focus.Id) is { } parent && document.CanEditChildren(parent.Id) ? parent : OwnerPhase(focus);
        var owner = document.ChildContainer(target.Id);
        var replace = document.IsScalarChildSlot(owner.Id) && owner.Children.Count > 0;
        var overloads = adapter.Overloads(primitive.Name);
        if (overloads.Count == 0) throw new ArgumentException("No native constructor is available.");
        ParametersTab.IsSelected = true; Inspector.Children.Clear();
        Inspector.Children.Add(new TextBlock { Text = (replace ? "Replace with " : "Add ") + primitive.Name, FontSize = 21, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        Inspector.Children.Add(new TextBlock { Text = "Into " + owner.Label, Foreground = Brushes.LightSlateGray, Margin = new(0, 6, 0, 18) });
        constructorChoices = new ComboBox { ItemsSource = overloads, MaxDropDownHeight = 300, ToolTip = "Actual constructor overloads from the game" };
        constructorChoices.ItemTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(TextBlock)) };
        constructorChoices.ItemTemplate.VisualTree.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Signature"));
        constructorChoices.ItemTemplate.VisualTree.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        constructorChoices.ItemTemplate.VisualTree.SetValue(FrameworkElement.MaxWidthProperty, 255d);
        Inspector.Children.Add(constructorChoices);
        var fields = new StackPanel(); Inspector.Children.Add(fields);
        constructorPreview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Code, Consolas"), FontSize = 11, Margin = new(0, 18, 0, 8) };
        Inspector.Children.Add(new Expander { Header = "Source preview", Content = constructorPreview, Margin = new(0, 14, 0, 4) });
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = EncounterViewport.Brush("#DAAE84"), Margin = new(0, 8, 0, 8) }; Inspector.Children.Add(message);
        createNativeButton = new Button { Content = replace ? "Replace child" : "Add to " + owner.Label, Style = (Style)FindResource("PrimaryButton"), Margin = new(0, 12, 0, 0) }; Inspector.Children.Add(createNativeButton);
        var cancel = new Button { Content = "Cancel", Style = (Style)FindResource("QuietButton"), Margin = new(0, 6, 0, 0) }; cancel.Click += (_, _) => SelectNode(owner.Id); Inspector.Children.Add(cancel);
        void Preview()
        {
            if (constructorChoices.SelectedItem is not NativeOverload choice) return;
            var missing = choice.Parameters.Where(p => p.Default == "required" && string.IsNullOrWhiteSpace(constructorInputs[p.Name].Text)).ToArray();
            var supplied = choice.Parameters.Where(p => !string.IsNullOrWhiteSpace(constructorInputs[p.Name].Text)).Select(p => p.Name + ": " + constructorInputs[p.Name].Text.Trim());
            constructorPreview.Text = "new " + choice.Name + "(" + string.Join(", ", supplied) + ")";
            var binding = RapleAdapter.Binding(constructorPreview.Text);
            var compatible = document.AcceptsChild(owner.Id, constructorPreview.Text, replace);
            createNativeButton.IsEnabled = missing.Length == 0 && binding.Errors.Length == 0 && !binding.RequiresContext && RegionEditable(owner) && compatible;
            message.Text = missing.Length > 0 ? "Enter " + string.Join(", ", missing.Select(p => p.Name)) + "." : binding.Errors.FirstOrDefault() ?? (binding.RequiresContext ? "This value requires source context. Edit it in Source." : primitive.Simulatable ? "Uses the game constructor. Blank optional fields keep its defaults." : "Source editing is supported; this primitive has no local simulation coverage yet.");
            message.Foreground = EncounterViewport.Brush(createNativeButton.IsEnabled && primitive.Simulatable ? "#979DA7" : "#DAAE84");
            if (missing.Length == 0 && binding.Errors.Length == 0 && !binding.RequiresContext && !compatible) message.Text = "This constructor cannot be inserted into " + owner.Label + ". Choose a compatible child or a different destination.";
        }
        constructorChoices.SelectionChanged += (_, _) =>
        {
            fields.Children.Clear(); constructorInputs.Clear();
            var choice = (NativeOverload)constructorChoices.SelectedItem;
            foreach (var parameter in choice.Parameters)
            {
                fields.Children.Add(new TextBlock { Text = parameter.Name + " · " + parameter.Type, Margin = new(0, 12, 0, 4), TextWrapping = TextWrapping.Wrap });
                var input = new TextBox { Text = CreationSeed(primitive.Name, parameter, owner), Tag = parameter.Default == "required" ? "Required C# value" : "Default: " + parameter.Default,
                    ToolTip = parameter.Explanation + (parameter.Type.EndsWith("[]") ? "\nEnter an array, for example new[] { \"first\", \"second\" }." : ""), Margin = new(0, 0, 0, 4) };
                constructorInputs.Add(parameter.Name, input); fields.Children.Add(input); input.TextChanged += (_, _) => Preview();
            }
            Preview();
        };
        createNativeButton.Click += (_, _) => Try(() =>
        {
            if (replace) Changed(document.ReplaceChild(owner.Id, constructorPreview.Text, d => adapter.Validate(d, selectedEntity!.Id)));
            else { document.AddChild(owner.Id, constructorPreview.Text, d => adapter.Validate(d, selectedEntity!.Id)); Changed(document.Find(owner.Id).Children.Last().Id); }
        });
        constructorChoices.SelectedIndex = primitive.Name == "State" ? overloads.ToList().FindIndex(o => o.Parameters.Any(p => p.Name == "name")) : 0;
    }
    string CreationSeed(string kind, PrimitiveParameter parameter, BehaviorNode owner)
    {
        if (kind == "State" && parameter.Name == "name") return PhaseLiteral(NextPhaseName());
        if (kind == "Taunt" && parameter.Name == "text") return "new[] { \"Ready.\" }";
        if (kind == "Prioritize" && parameter.Name == "children") return "new CycleBehavior[] { new Wander(.3) }";
        if (kind == "OnDeathBehavior" && parameter.Name == "behavior") return "new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable)";
        if (parameter.Default != "required") return "";
        return parameter.Name switch {
            "radius" => "8", "speed" => "0.3", "time" or "period" => "3000", "threshold" => "0.7", "coolDown" or "cooldown" => "new Cooldown(1000, 0)",
            "targetState" => document.StateName(owner.Id) is { } name ? PhaseLiteral(name) : "",
            "effect" => "ConditionEffectIndex.Invulnerable", _ => "" };
    }
}
