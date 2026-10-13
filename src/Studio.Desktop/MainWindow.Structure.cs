using Microsoft.CodeAnalysis.CSharp;
using Studio.Core;
using System.Windows;
using System.Windows.Controls;

namespace Studio.Desktop;

public partial class MainWindow
{
    TextBox? phaseNameInput;
    Button? parameterApplyButton;
    Button? renamePhaseButton, duplicateStructureButton, upStructureButton, downStructureButton, deleteStructureButton;
    Expander? organizeExpander;
    static string PhaseLiteral(string name) => SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(name)).ToFullString();
    string NextPhaseName()
    {
        var names = AuthoringRoot.Descendants().Where(n => n.Kind == "State").Select(n => document.StateName(n.Id)).ToHashSet();
        var index = 1; while (names.Contains("phase-" + index)) index++; return "phase-" + index;
    }
    bool RegionEditable(BehaviorNode node) => (encounterDocument==null||PrimaryDocument.LockedNodes.Count==0)&&document.CanEditRegion(node.Id);
    void UpdateOrganizeButtons()
    {
        if (selectedNode == null) return; var editable = RegionEditable(selectedNode);
        if(parameterApplyButton!=null)parameterApplyButton.IsEnabled=editable;
        if(hazardLayout!=null)hazardLayout.IsEnabled=editable;
        if(hazardOutward!=null)hazardOutward.IsEnabled=editable&&adapter.ReadHazardPattern(document,selectedNode)?.Layout=="Lattice";
        if(entryCreationButton!=null)entryCreationButton.IsEnabled=editable;
        if(removeDefinitionButton!=null)removeDefinitionButton.IsEnabled=editable;
        if(sweepEditorButton!=null)sweepEditorButton.IsEnabled=editable;
        if (attackEditorButton != null) attackEditorButton.IsEnabled = editable;
        RefreshAttackSelection();
        RefreshSweepSelection();
        RefreshRingSelection();if(ringEditorButton!=null)ringEditorButton.IsEnabled=editable;
        if (movementEditorButton != null) movementEditorButton.IsEnabled = editable;
        if (movementDirection != null) movementDirection.IsEnabled = editable;
        RefreshMovementSelection();
        if (pathEditorButton != null) pathEditorButton.IsEnabled = editable;
        RefreshPathSelection();
        UpdateProjectileControls();
        UpdateEntityControls();
        if(ownedLaneAxes!=null)ownedLaneAxes.IsEnabled=editable;
        UpdatePhaseChoiceControls();
        if (renamePhaseButton != null) renamePhaseButton.IsEnabled = editable;
        if (duplicateStructureButton != null) duplicateStructureButton.IsEnabled = editable;
        if (deleteStructureButton != null) deleteStructureButton.IsEnabled = editable;
        if (extractApplyButton != null) extractApplyButton.IsEnabled = editable&&nodeSelection.All(id=>RegionEditable(document.Find(id)));
        if (cloneDefinitionButton != null) cloneDefinitionButton.IsEnabled = editable;
        if (chooseChildSlotButton != null) chooseChildSlotButton.IsEnabled = editable;
        if (document.Parent(selectedNode.Id) is not { } parent) return;
        var siblings = parent.Children.ToList(); var index = siblings.FindIndex(n => n.Id == selectedNode.Id);
        if (upStructureButton != null) upStructureButton.IsEnabled = editable && index > 0 && RegionEditable(siblings[index - 1]);
        if (downStructureButton != null) downStructureButton.IsEnabled = editable && index + 1 < siblings.Count && RegionEditable(siblings[index + 1]);
    }
    void AddOrganizeControls(BehaviorNode node)
    {
        extractNameInput=null;extractDestination=null;extractApplyButton=null;
        cloneNameInput=null;cloneDefinitionButton=null;
        phaseNameInput = null; renamePhaseButton = null; organizeExpander = null;
        duplicateStructureButton = upStructureButton = downStructureButton = deleteStructureButton = null;
        if(node.Kind=="ArgumentGroup"&&!node.ArgumentFromCall&&!node.ArgumentRequiresSource)
        {
            var collectionTools=new StackPanel();AddExtractionControls(node,collectionTools);Inspector.Children.Add(collectionTools);return;
        }
        if (node.Kind == "State" && document.StateName(node.Id) is { } name)
        {
            Inspector.Children.Add(new TextBlock { Text = "Phase name", Margin = new(0, 14, 0, 6) });
            var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            phaseNameInput = new TextBox { Text = name, ToolTip = "Rename this phase and update its transition references." }; row.Children.Add(phaseNameInput);
            renamePhaseButton = new Button { Content = "Rename", Margin = new(6, 0, 0, 0), IsEnabled = RegionEditable(node) }; renamePhaseButton.Click += RenameSelectedPhase; Grid.SetColumn(renamePhaseButton, 1); row.Children.Add(renamePhaseButton); Inspector.Children.Add(row);
        }
        var parent = document.Parent(node.Id);
        if (parent == null || !document.CanOrganize(node.Id)) return;
        var editable = RegionEditable(node); var siblings = parent.Children.ToList(); var index = siblings.FindIndex(n => n.Id == node.Id);
        var panel = new StackPanel(); var tools = new Grid();
        tools.ColumnDefinitions.Add(new()); for (var i = 0; i < 3; i++) tools.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Button Tool(string label, int column, RoutedEventHandler handler, string hint, bool enabled)
        {
            var button = new Button { Content = label, ToolTip = hint, IsEnabled = enabled, Margin = new(column == 0 ? 0 : 5, 0, 0, 0), Padding = new(8, 7, 8, 7) };
            button.Click += handler; Grid.SetColumn(button, column); tools.Children.Add(button); return button;
        }
        duplicateStructureButton = Tool("Duplicate", 0, DuplicateSelectedNode, node.Kind == "Reference" ? "Copy this reference. Both nodes keep using the same shared source value." : "Copy this branch with unique phase names and new editor identities.", editable);
        upStructureButton = Tool("↑", 1, MoveSelectedNodeUp, "Move earlier in source order.", editable && index > 0 && RegionEditable(siblings[index - 1]));
        downStructureButton = Tool("↓", 2, MoveSelectedNodeDown, "Move later in source order.", editable && index + 1 < siblings.Count && RegionEditable(siblings[index + 1]));
        deleteStructureButton = Tool("×", 3, DeleteSelectedNode, "Delete this branch and known incoming phase transitions/orders. Undo restores the complete edit.", editable);
        panel.Children.Add(tools); panel.Children.Add(new TextBlock { Text = "Order controls phase entry and action/transition priority. Delete also reconciles known incoming phase references. Undo restores the entire edit.", TextWrapping = TextWrapping.Wrap, Foreground = EncounterViewport.Brush("#92969F"), FontSize = 11, Margin = new(0, 8, 0, 0) });
        AddExtractionControls(node,panel);
        AddDefinitionCloneControls(node,panel);
        organizeExpander = new Expander { Header = "Organize", Content = panel, Margin = new(0, 12, 0, 4) }; Inspector.Children.Add(organizeExpander);
    }
    void RenameSelectedPhase(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (selectedNode == null || phaseNameInput == null) return;
        RenameWorkspacePhase(selectedNode.Id,phaseNameInput.Text);
    });
    void DuplicateSelectedNode(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (selectedNode == null) return;
        if(selectedNode.Kind=="InitializerEntry"){var copied=document.DuplicateInitializerEntry(selectedNode.Id,ValidateAuthoring);Changed(copied);return;}
        if(adapter.HasAdditionalSources||definitionViewKey!=null)
        {
            var parent=document.Parent(selectedNode.Id)??throw new ArgumentException("Select a child node to duplicate.");
            PasteWorkspaceNodes(parent.Id,parent.Children.ToList().FindIndex(n=>n.Id==selectedNode.Id)+1,document.CopyNodes([selectedNode.Id]));return;
        }
        var copy = document.DuplicateNode(selectedNode.Id, ValidateAuthoring); Changed(copy);
    });
    void MoveSelectedNodeUp(object sender, RoutedEventArgs e) => MoveSelectedNode(-1);
    void MoveSelectedNodeDown(object sender, RoutedEventArgs e) => MoveSelectedNode(1);
    void MoveSelectedNode(int direction) => Try(() =>
    {
        if (selectedNode == null) return; var id = selectedNode.Id; var before = document.Revision; document.MoveNode(id, direction, ValidateAuthoring); if (document.Revision != before) Changed(id);
    });
    void DeleteSelectedNode(object sender, RoutedEventArgs e) => Try(() =>
    {
        if (selectedNode == null) return;
        if(selectedNode.Descendants().Any(n=>n.Kind=="State")){DeleteWorkspacePhase(selectedNode.Id);return;}
        var parent = document.DeleteNode(selectedNode.Id, ValidateAuthoring); Changed(parent);
    });
    void SelectTreeNode(Guid id)
    {
        TreeViewItem? Search(TreeViewItem item)
        {
            if (item.Tag is BehaviorNode node && node.Id == id) return item;
            foreach (var child in item.Items.OfType<TreeViewItem>()) if (Search(child) is { } found) { item.IsExpanded = true; return found; }
            return null;
        }
        foreach (var item in BehaviorTree.Items.OfType<TreeViewItem>()) if (Search(item) is { } found)
        {
            var scroll = FindScrollViewer(BehaviorTree); var horizontal = scroll?.HorizontalOffset ?? 0;
            found.IsSelected = true;
            if (WorkspaceTabs.SelectedIndex == 1) { found.BringIntoView(); scroll?.ScrollToHorizontalOffset(horizontal); }
            break;
        }
    }
}
