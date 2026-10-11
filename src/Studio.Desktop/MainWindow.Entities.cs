using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow
{
    sealed record EntityChoice(string? Id, string Title, string SpriteId, string Summary);
    Expander? entityPicker;
    ListBox? entityChoices;
    TextBox? entitySearch;
    Button? useEntityButton;
    TextBlock? entityInfo;
    EntityParameter? entityParameter;
    Expander? creationEntityPicker;
    ListBox? creationEntityChoices;
    TextBox? creationEntitySearch;
    Button? creationUseEntityButton;
    static IReadOnlyList<EntityChoice> EntityChoicesFor(EntityParameter parameter)
    {
        var choices = EntityResources.Catalog(parameter.Scope).Select(resource => new EntityChoice(resource.Id, resource.Title, resource.SpriteId, resource.Summary + (parameter.Scope == EntityResourceScope.Resolvable && resource.RuntimeKind != "Enemy" ? "\nAdditional world context may be required during Play." : ""))).ToList();
        if (parameter.AllowsPlayer) choices.Insert(0, new(null, "Nearest player", "Wizard", "Native null target · searches the player collision index"));
        return choices;
    }
    Expander CreateEntityInputPicker(EntityParameter parameter, TextBox input)
    {
        var panel = new StackPanel(); var loaded = false;
        var search = creationEntitySearch = new TextBox { Tag = "Search entities", Margin = new(0, 8, 0, 6) }; panel.Children.Add(search);
        var list = creationEntityChoices = new ListBox { MaxHeight = 210, ItemTemplate = (DataTemplate)FindResource("EntityChoiceTemplate") }; panel.Children.Add(list);
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = EncounterViewport.Brush("#929D96"), Margin = new(0, 8, 0, 0) }; panel.Children.Add(info);
        var use = creationUseEntityButton = new Button { Content = "Use entity", IsEnabled = false, Margin = new(0, 10, 0, 0) }; panel.Children.Add(use);
        var picker = creationEntityPicker = new Expander { Header = "Choose from game entities", Content = panel, Margin = new(0, 4, 0, 0) };
        IReadOnlyList<EntityChoice> choices = [];
        void Filter()
        {
            if (!loaded) return;
            var previous = list.SelectedItem as EntityChoice;
            list.ItemsSource = choices.Where(choice => (choice.Title + " " + choice.Summary).Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            list.SelectedItem = list.Items.Cast<EntityChoice>().FirstOrDefault(choice => previous != null && choice.Id == previous.Id);
            info.Text = list.SelectedItem is EntityChoice choice ? choice.Summary : list.Items.Count == 0 ? "No matching entities." : "Select an entity to inspect its XML definition.";
        }
        picker.Expanded += (_, _) =>
        {
            if (loaded) return;
            choices = EntityChoicesFor(parameter); loaded = true; Filter();
        };
        search.TextChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) => { use.IsEnabled = list.SelectedItem is EntityChoice; info.Text = list.SelectedItem is EntityChoice choice ? choice.Summary : "Select an entity to inspect its XML definition."; };
        use.Click += (_, _) =>
        {
            if (list.SelectedItem is not EntityChoice choice) return;
            input.Text = choice.Id == null ? "null" : Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Literal(choice.Id).ToFullString();
            picker.IsExpanded = false;
        };
        return picker;
    }
    void UpdateEntityControls()
    {
        if (useEntityButton == null || selectedNode == null || entityParameter == null) return;
        var current = adapter.ReadEntityReference(document, selectedNode, entityParameter);
        useEntityButton.IsEnabled = RegionEditable(selectedNode) && entityParameter.InactiveReason == null && entityChoices?.SelectedItem is EntityChoice choice && (!current.Known || choice.Id != current.Id);
    }
    void AddEntityControls(BehaviorNode node)
    {
        entityPicker = null; entityChoices = null; entitySearch = null; useEntityButton = null; entityInfo = null;
        entityParameter = adapter.EntityParameterFor(document, node); if (entityParameter == null) return;
        var parameter = entityParameter; var current = adapter.ReadEntityReference(document, node, parameter); var revision = document.Revision;
        if (node.Kind is "Order" or "OrderOnce" or "OrderOnDeath")
            Inspector.Children.Add(new TextBlock { Text = "Orders address nearby matching entities, regardless of parent. The destination phase belongs to the target entity, not this host.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = EncounterViewport.Brush("#929D96"), Margin = new(0, 10, 0, 0) });
        var panel = new StackPanel(); var loaded = false;
        var search = entitySearch = new TextBox { Tag = "Search entities", Margin = new(0, 8, 0, 6) }; panel.Children.Add(search);
        var list = entityChoices = new ListBox { MaxHeight = 210, ItemTemplate = (DataTemplate)FindResource("EntityChoiceTemplate") }; panel.Children.Add(list);
        var info = entityInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = EncounterViewport.Brush("#929D96"), Margin = new(0, 8, 0, 0) }; panel.Children.Add(info);
        var use = useEntityButton = new Button { Content = "Use entity", Margin = new(0, 10, 0, 0), IsEnabled = false }; panel.Children.Add(use);
        var title = parameter.Label + " · " + (!current.Known ? "source expression" : current.Id ?? (parameter.AllowsPlayer ? "nearest player" : "no entity"));
        var picker = entityPicker = new Expander { Header = new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230, ToolTip = title }, Content = panel, Margin = new(0, 10, 0, 0) }; Inspector.Children.Add(picker);
        IReadOnlyList<EntityChoice> choices = [];
        string EmptyInfo() => parameter.InactiveReason ?? (!current.Known ? "Choose an entity to replace this source expression." : current.Id == null && !parameter.AllowsPlayer ? "This parameter needs an entity." : choices.All(choice => choice.Id != current.Id) ? "The current ID is absent from this parameter's eligible XML entities." : "Select an entity to inspect its XML definition.");
        list.SelectionChanged += (_, _) => { info.Text = parameter.InactiveReason ?? (list.SelectedItem is EntityChoice choice ? choice.Summary : EmptyInfo()); UpdateEntityControls(); };
        void Filter()
        {
            if (!loaded) return;
            var previous = list.SelectedItem as EntityChoice;
            var filtered = choices.Where(choice => (choice.Title + " " + choice.Summary).Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            list.ItemsSource = filtered; list.SelectedItem = filtered.FirstOrDefault(choice => previous != null ? choice.Id == previous.Id : current.Known && choice.Id == current.Id);
            info.Text = parameter.InactiveReason ?? (list.SelectedItem is EntityChoice choice ? choice.Summary : filtered.Length == 0 ? "No matching entities." : EmptyInfo()); UpdateEntityControls();
        }
        picker.Expanded += (_, _) =>
        {
            if (loaded) return;
            choices = EntityChoicesFor(parameter); loaded = true; Filter();
        };
        search.TextChanged += (_, _) => Filter();
        use.Click += (_, _) => Try(() =>
        {
            if (list.SelectedItem is not EntityChoice choice) return;
            adapter.ApplyEntityReference(document, selectedEntity!.Id, node.Id, revision, parameter.Name, choice.Id); Changed(node.Id);
        });
    }
}
