using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    BehaviorNode EncounterRoot => PrimaryDocument.Entities.Single(e => e.EntityId == selectedEntity!.Id).Root;
    BehaviorNode AuthoringRoot=>document.Definitions.FirstOrDefault(d=>d.EntityId==definitionViewKey)?.Root??EncounterRoot;
    IEnumerable<DocumentDiagnostic> ReviewDiagnostics() => adapter.Validate(PrimaryDocument, selectedEntity!.Id)
        .Concat(encounterDocument==null?[]:adapter.Validate(document,selectedEntity!.Id))
        .Concat(BehaviorAnalysis.Analyze(AuthoringRoot).Diagnostics);
    void RefreshPhaseReview(IReadOnlyList<DocumentDiagnostic> diagnostics)
    {
        PhaseReview.Header = diagnostics.Count == 0 ? "Phase review · no issues" : $"Phase review · {diagnostics.Count} {(diagnostics.Count == 1 ? "issue" : "issues")}";
        PhaseDiagnostics.Children.Clear();
        foreach (var diagnostic in diagnostics)
        {
            var text = new TextBlock { Text = (diagnostic.IsError ? "Error · " : "Notice · ") + diagnostic.Message, TextWrapping = TextWrapping.Wrap,
                Foreground = EncounterViewport.Brush(diagnostic.IsError ? "#E5A195" : "#DAAE84") };
            var button = new Button { Content = text, Tag = diagnostic, Style = (Style)FindResource("QuietButton"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new(0, 0, 0, 3), ToolTip = "Select this phase or transition" };
            button.Click += (_, _) => NavigateDiagnostic(diagnostic);
            PhaseDiagnostics.Children.Add(button);
        }
    }
    void NavigateDiagnostic(DocumentDiagnostic diagnostic)
    {
        if (diagnostic.NodeId is not { } id) return;
        WorkspaceTabs.SelectedIndex = 1; SelectNode(id);
        var node = document.Find(id);
        var owner = node.Kind == "State" ? node : document.Parent(id);
        // Keep warnings usable in long encounters, without jumping the graph on routine parameter selection.
        if (owner != null)
        {
            Graph.UpdateLayout();
            var transitions = BehaviorAnalysis.Analyze(AuthoringRoot).Transitions;
            var edge = transitions.FirstOrDefault(t => t.Node.Id == id);
            if (edge != null && Graph.PhaseBounds(owner.Id) is { } phase && phase.Height > GraphScroll.ViewportHeight && Graph.TransitionBounds(id) is { } row) Graph.BringIntoView(row);
            else Graph.BringPhaseIntoView(owner.Id);
        }
    }
    void AddTransitionSummary(Guid id)
    {
        var edge = BehaviorAnalysis.Analyze(AuthoringRoot).Transitions.FirstOrDefault(t => t.Node.Id == id);
        if (edge == null) return;
        Inspector.Children.Add(new TextBlock { Text = $"{edge.Condition} → {edge.TargetName ?? "dynamic target"}", TextWrapping = TextWrapping.Wrap,
            Foreground = EncounterViewport.Brush("#CFD3DB"), Margin = new(0, 12, 0, 4) });
        Inspector.Children.Add(new TextBlock { Text = $"Priority {edge.Priority} · first matching transition wins", TextWrapping = TextWrapping.Wrap,
            Foreground = EncounterViewport.Brush("#92969F"), FontSize = 11, ToolTip = "Child transitions take precedence over parents. Use Organize to move this transition up or down. Timer conditions run on the native 100 ms tick." });
        if(edge.Node.Kind=="DamageTakenTransition")Inspector.Children.Add(new TextBlock{Text="Credit persists across phases. In this game, wipeProgress does not clear the stored counter. Details → Actors shows the damage credited to present players.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#92969F"),Margin=new(0,8,0,0)});
    }
    void AddCycleControls(BehaviorNode node)
    {
        var analysis=BehaviorAnalysis.Analyze(AuthoringRoot);var descendants=node.Kind=="State"?node.Descendants().Select(n=>n.Id).ToHashSet():[];
        foreach(var cycle in analysis.Cycles.Where(c=>c.PhaseIds.Contains(node.Id)||c.TransitionIds.Contains(node.Id)||c.ExitTransitionIds.Contains(node.Id)||c.PhaseIds.Any(descendants.Contains)))
        {
            var panel=new StackPanel();panel.Children.Add(new TextBlock{Text=cycle.ExitTransitionIds.Count==0?"No literal transition leaves this group in the analyzed graph. Conditions and native runtime state determine whether it repeats.":"These routes form a possible cycle. Exit conditions and native runtime state determine whether it repeats.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#979DA7"),Margin=new(0,8,0,8)});
            foreach(var id in cycle.PhaseIds){var member=document.Find(id);var button=new Button{Content=member.Label,Tag=id,Style=(Style)FindResource("QuietButton"),HorizontalContentAlignment=HorizontalAlignment.Left,ToolTip="Open this cycle phase"};button.Click+=(_,_)=>{WorkspaceTabs.SelectedIndex=1;SelectNode(id);Graph.BringPhaseIntoView(id);};panel.Children.Add(button);}
            if(cycle.ExitTransitionIds.Count>0)panel.Children.Add(new TextBlock{Text="Possible exits",FontSize=11,Margin=new(0,10,0,4)});
            foreach(var id in cycle.ExitTransitionIds){var edge=analysis.Transitions.Single(t=>t.Node.Id==id);var button=new Button{Content=new TextBlock{Text=edge.Condition+" → "+edge.TargetName,TextWrapping=TextWrapping.Wrap},Tag=id,Style=(Style)FindResource("QuietButton"),HorizontalContentAlignment=HorizontalAlignment.Left};button.Click+=(_,_)=>NavigateDiagnostic(new("Cycle exit",id,false));panel.Children.Add(button);}
            Inspector.Children.Add(new Expander{Header=$"Phase cycle · {cycle.PhaseIds.Count} phase{(cycle.PhaseIds.Count==1?"":"s")}",Content=new ScrollViewer{Content=panel,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Margin=new(0,12,0,6)});
        }
    }
}
