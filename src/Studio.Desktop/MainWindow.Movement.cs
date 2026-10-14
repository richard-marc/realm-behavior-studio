using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;

public partial class MainWindow
{
    bool movementEditing;
    Button? movementEditorButton;
    ComboBox? movementDirection;
    void ClearMovementEditor()
    {
        movementEditing = false; Viewport.Movement = null;
        if (movementEditorButton != null) movementEditorButton.Content = "Edit movement bounds";
    }
    void ClearViewportEditors() { ClearArenaEditor(); ClearAttackEditor(); ClearMovementEditor(); ClearPathEditor(); ClearSweepEditor(); ClearRingEditor(); ClearCrossCutEditor(); ClearSnareEditor(); ClearCustomMoveEditor(); ClearCombatPreview(); }
    void RefreshMovementSelection()
    {
        if (!movementEditing) return;
        if (selectedNode == null || adapter.ReadMovementPattern(document, selectedNode) is not { } pattern) { ClearMovementEditor(); return; }
        Viewport.Movement = new(selectedNode.Id, document.Revision, pattern, RegionEditable(selectedNode));
    }
    void AddMovementControls(BehaviorNode node)
    {
        movementEditorButton = null;
        movementDirection = null;
        if (adapter.MovementKind(document, node) == null) return;
        if (adapter.ReadMovementPattern(document, node) is not { } pattern)
        {
            Inspector.Children.Add(new TextBlock { Text = "Visual movement bounds need known ranges and target expressions. Edit these in Parameters or Source.", TextWrapping = TextWrapping.Wrap, Foreground = EncounterViewport.Brush("#929D96"), FontSize = 11, Margin = new(0, 12, 0, 0) }); return;
        }
        movementEditorButton = new Button { Content = movementEditing ? "Close movement editor" : "Edit movement bounds", Style = (Style)FindResource("QuietButton"), Margin = new(0, 10, 0, 0), IsEnabled = RegionEditable(node) };
        movementEditorButton.Click += (_, _) =>
        {
            if (movementEditing) { ClearMovementEditor(); return; }
            ClearAttackEditor(); ClearPathEditor(); ClearSweepEditor(); ClearRingEditor(); Stop(this, new()); movementEditing = true; RefreshMovementSelection();
            movementEditorButton!.Content = "Close movement editor"; WorkspaceTabs.SelectedIndex = 0; UpdateLayout(); Viewport.FitMovement(); Viewport.Focus();
            Status.Text = "Movement editor · drag a boundary to change one parameter · Play runs native movement";
        };
        Inspector.Children.Add(movementEditorButton);
        if (pattern.Kind == "Orbit")
        {
            Inspector.Children.Add(new TextBlock { Text = "Orbit direction", FontSize = 11, Margin = new(0, 10, 0, 5) });
            movementDirection = new ComboBox { ItemsSource = new[] { "Counterclockwise", "Clockwise", "Seeded choice" }, SelectedIndex = pattern.Clockwise == null ? 2 : pattern.Clockwise.Value ? 1 : 0, IsEnabled = RegionEditable(node) };
            var direction = movementDirection; var revision = document.Revision;
            direction.SelectionChanged += (_, _) => Try(() =>
            {
                if (direction.SelectedIndex < 0) return;
                CommitMovementValue(node.Id, revision, "orbitClockwise", new[] { "false", "true", "null" }[direction.SelectedIndex]);
            }); Inspector.Children.Add(direction);
        }
    }
    void ApplyMovementEdit(MovementSelection selection, string parameter, double value)
    {
        Try(() => CommitMovementEdit(selection, parameter, value)); RefreshMovementSelection();
    }
    void CommitMovementEdit(MovementSelection selection, string parameter, double value)
    {
        var expression=selection.Pattern.Kind=="Charge"&&parameter=="range"?((float)value).ToString("R",CultureInfo.InvariantCulture)+"f":value.ToString(selection.Pattern.TravelDistance!=null?"R":"0.###", CultureInfo.InvariantCulture);
        CommitMovementValue(selection.NodeId, selection.Revision, parameter, expression);
    }
    void CommitMovementValue(Guid nodeId, long revision, string parameter, string expression)
    {
        document.Apply(new(revision, "Adjust movement " + parameter, [new(nodeId, parameter, expression)]), ValidateAuthoring);
        Changed(nodeId);
    }
}
