using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow
{
    Expander? projectilePicker;
    ListBox? projectileChoices;
    TextBox? projectileSearch;
    Button? useProjectileButton;
    TextBlock? projectileInfo;
    void UpdateProjectileControls()
    {
        if (useProjectileButton == null || projectileChoices == null || selectedNode == null) return;
        useProjectileButton.IsEnabled = RegionEditable(selectedNode) && projectileChoices.SelectedItem is ListBoxItem { Tag: EnemyProjectile choice } && adapter.ReadProjectileIndex(document, selectedNode) != choice.Index;
    }
    void AddProjectileControls(BehaviorNode node)
    {
        projectilePicker = null; projectileChoices = null; projectileSearch = null; useProjectileButton = null; projectileInfo = null;
        if (adapter.ProjectileParameter(document,node)==null) return;
        if (!adapter.HasKnownProjectileOwner(document, selectedEntity!.Id, node))
        {
            Inspector.Children.Add(new TextBlock { Text = "Projectile owner comes from source. Inspect the controller before choosing an index.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = EncounterViewport.Brush("#929D96"), Margin = new(0, 8, 0, 0) }); return;
        }
        var choices = EnemyProjectiles.ForEntity(selectedEntity.Id);
        if(adapter.IsDelayedHazard(document,node))choices=choices.Where(p=>adapter.HazardProjectiles(selectedEntity.Id).Any(h=>h.Index==p.Index)).ToArray();
        var emptyMessage=adapter.IsDelayedHazard(document,node)?"This enemy has no compatible dormant hazard projectiles.":"This enemy has no XML projectiles.";
        var index = adapter.ReadProjectileIndex(document, node); var revision = document.Revision;
        var panel = new StackPanel();
        projectileSearch = new TextBox { Tag = "Search projectiles", ToolTip = "Search projectile names, effects or values", Margin = new(0, 8, 0, 6) }; panel.Children.Add(projectileSearch);
        projectileChoices = new ListBox { MaxHeight = 200 }; panel.Children.Add(projectileChoices);
        projectileInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = EncounterViewport.Brush("#929D96"), Margin = new(0, 8, 0, 0) }; panel.Children.Add(projectileInfo);
        useProjectileButton = new Button { Content = "Use projectile", Margin = new(0, 10, 0, 0), IsEnabled = false }; panel.Children.Add(useProjectileButton);
        projectilePicker = new Expander { Header = "Projectile · " + (index is { } known ? "#" + known : "source expression"), Content = panel, Margin = new(0, 10, 0, 0), ToolTip = "XML projectiles owned by " + selectedEntity.Id }; Inspector.Children.Add(projectilePicker);
        var list = projectileChoices; var search = projectileSearch; var info = projectileInfo;
        list.SelectionChanged += (_, _) =>
        {
            info.Text = list.SelectedItem is ListBoxItem { Tag: EnemyProjectile choice } ? choice.Summary + (choice.Traits.Length == 0 ? "" : "\n" + choice.Traits) + (choice.PreviewLimitation == null ? "" : "\n" + choice.PreviewLimitation) : choices.Count == 0 ? emptyMessage : index == null ? "Choose a projectile to replace the source expression." : choices.All(p => p.Index != index) ? "The current index is absent from the compatible choices." : "Select a projectile to inspect its XML definition.";
            UpdateProjectileControls();
        };
        void Filter()
        {
            var previous = (list.SelectedItem as ListBoxItem)?.Tag as EnemyProjectile; list.Items.Clear();
            foreach (var choice in choices.Where(choice => (choice.Title + " " + choice.Summary + " " + choice.Traits).Contains(search.Text, StringComparison.OrdinalIgnoreCase)))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new Image { Source = sprites.Get(choice.TextureId), Width = 24, Height = 24, Stretch = Stretch.Uniform, Margin = new(0, 0, 9, 0) });
                row.Children.Add(new TextBlock { Text = choice.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 205 });
                var item = new ListBoxItem { Tag = choice, Content = row, ToolTip = choice.TextureId }; list.Items.Add(item);
                if (choice.Index == (previous?.Index ?? index)) list.SelectedItem = item;
            }
            if (list.SelectedItem == null) { info.Text = choices.Count == 0 ? emptyMessage : list.Items.Count == 0 ? "No matching projectiles." : index == null ? "Choose a projectile to replace the source expression." : choices.All(p => p.Index != index) ? "The current index is absent from the compatible choices." : "Select a projectile to inspect its XML definition."; UpdateProjectileControls(); }
        }
        search.TextChanged += (_, _) => Filter(); Filter();
        useProjectileButton.Click += (_, _) => Try(() =>
        {
            if (list.SelectedItem is not ListBoxItem { Tag: EnemyProjectile choice }) return;
            CommitProjectileChoice(node.Id, revision, choice.Index);
        });
    }
    void CommitProjectileChoice(Guid nodeId, long revision, int index)
    {
        var node = document.Find(nodeId);
        if (!adapter.HasKnownProjectileOwner(document, selectedEntity!.Id, node) || !EnemyProjectiles.ForEntity(selectedEntity.Id).Any(choice => choice.Index == index)) throw new InvalidOperationException("Choose a projectile owned by this attack's native enemy.");
        document.Apply(new(revision, "Choose enemy projectile", [new(nodeId, adapter.ProjectileParameter(document,node)!, index.ToString(System.Globalization.CultureInfo.InvariantCulture))]), candidate => adapter.Validate(candidate, selectedEntity.Id));
        Changed(nodeId);
    }
}
