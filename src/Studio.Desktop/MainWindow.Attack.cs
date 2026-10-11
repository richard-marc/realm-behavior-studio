using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;

public partial class MainWindow
{
    bool attackEditing;
    Button? attackEditorButton;
    void ClearAttackEditor()
    {
        attackEditing = false; Viewport.Attack = null;
        if (attackEditorButton != null) attackEditorButton.Content = "Edit aim & spacing";
    }
    void RefreshAttackSelection()
    {
        if (!attackEditing) return;
        if (selectedNode == null || adapter.ReadShootPattern(document, selectedNode) is not { } pattern) { ClearAttackEditor(); return; }
        Viewport.Attack = new(selectedNode.Id, document.Revision, pattern, RegionEditable(selectedNode));
    }
    void AddAttackControls(BehaviorNode node)
    {
        attackEditorButton = null;
        if (!adapter.IsNativeShoot(document, node)) return;
        var pattern = adapter.ReadShootPattern(document, node);
        if (pattern == null)
        {
            Inspector.Children.Add(new TextBlock { Text = "Visual aim needs known shot counts and angles. Edit these expressions in Parameters or Source.", TextWrapping = TextWrapping.Wrap, Foreground = EncounterViewport.Brush("#929D96"), FontSize = 11, Margin = new(0, 12, 0, 0) }); return;
        }
        attackEditorButton = new Button { Content = attackEditing ? "Close attack editor" : "Edit aim & spacing", Style = (Style)FindResource("QuietButton"), Margin = new(0, 10, 0, 0), IsEnabled = RegionEditable(node) };
        attackEditorButton.Click += (_, _) =>
        {
            if (attackEditing) { ClearAttackEditor(); return; }
            ClearMovementEditor(); ClearPathEditor(); Stop(this, new()); attackEditing = true; RefreshAttackSelection();
            attackEditorButton!.Content = "Close attack editor"; WorkspaceTabs.SelectedIndex = 0; UpdateLayout(); Viewport.FitAttack(); Viewport.Focus();
            Status.Text = "Attack editor · drag a handle to change one parameter · edits restart playback";
        };
        Inspector.Children.Add(attackEditorButton);
    }
    void ApplyAttackEdit(AttackSelection selection, string parameter, double value)
    {
        Try(() => CommitAttackEdit(selection, parameter, value));
        RefreshAttackSelection();
    }
    void CommitAttackEdit(AttackSelection selection, string parameter, double value)
    {
        document.Apply(new(selection.Revision, "Adjust attack " + parameter, [new(selection.NodeId, parameter, value.ToString("0.###", CultureInfo.InvariantCulture))]), d => adapter.Validate(d, selectedEntity!.Id));
        Changed(selection.NodeId);
    }
}
