using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? completionPanel;ComboBox? completionMovements,completionTarget;Button? completionApply,completionNavigate;
    void AddCompletionControls(BehaviorNode node)
    {
        completionPanel=null;completionMovements=null;completionTarget=null;completionApply=null;completionNavigate=null;
        var transition=adapter.IsMovementCompletion(document,node);if(node.Kind!="State"&&!transition)return;
        var choices=adapter.CompletionMovements(document,node);if(!transition&&choices.Count==0)return;
        var binding=transition?adapter.ReadCompletionBinding(document,node):null;var body=new StackPanel();var revision=document.Revision;
        if(binding!=null)body.Children.Add(new TextBlock{Text=binding.Detail,TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#A0B3A8"),Margin=new(0,8,0,10)});
        completionMovements=new(){ItemsSource=choices,SelectedIndex=choices.Count==1?0:-1,IsEnabled=RegionEditable(node),MaxDropDownHeight=220};
        if(binding?.MovementReferenceId is {} current)completionMovements.SelectedItem=choices.FirstOrDefault(c=>c.ReferenceId==current);
        body.Children.Add(new TextBlock{Text="Existing movement instance",FontSize=11,Margin=new(0,8,0,5)});body.Children.Add(completionMovements);
        if(!transition)
        {
            var root=document.AllRoots.Single(r=>r.Root.Descendants().Any(n=>n.Id==node.Id)).Root;
            completionTarget=new(){ItemsSource=root.Descendants().Where(n=>n.Kind=="State").Select(n=>document.StateName(n.Id)).OfType<string>().Distinct().ToArray(),IsEditable=true,IsEnabled=RegionEditable(node)};
            body.Children.Add(new TextBlock{Text="Destination phase",FontSize=11,Margin=new(0,10,0,5)});body.Children.Add(completionTarget);
        }
        completionApply=new(){Content=transition?"Use movement instance":"Add completion route",Style=(Style)FindResource("QuietButton"),Margin=new(0,10,0,0)};
        completionNavigate=new(){Content="Open movement reference",Style=(Style)FindResource("QuietButton"),Margin=new(0,5,0,0)};body.Children.Add(completionApply);body.Children.Add(completionNavigate);
        var list=completionMovements;var destination=completionTarget;var apply=completionApply;var navigate=completionNavigate;
        void Update(){apply.IsEnabled=RegionEditable(node)&&list.SelectedItem is CompletionMovement chosen&&(transition?chosen.ReferenceId!=binding?.MovementReferenceId:!string.IsNullOrWhiteSpace(destination?.Text));navigate.IsEnabled=list.SelectedItem is CompletionMovement;}
        void QueueUpdate()=>Dispatcher.BeginInvoke(new Action(()=>{if(completionApply==apply)Update();}),System.Windows.Threading.DispatcherPriority.DataBind);
        list.SelectionChanged+=(_,_)=>Update();if(destination!=null){destination.SelectionChanged+=(_,_)=>QueueUpdate();destination.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,_)=>QueueUpdate()));}Update();
        apply.Click+=(_,_)=>Try(()=>
        {
            if(list.SelectedItem is not CompletionMovement movement)return;
            CommitCompletionEdit(node.Id,movement.ReferenceId,revision,transition?null:destination!.Text);
        });
        navigate.Click+=(_,_)=>{if(list.SelectedItem is CompletionMovement movement)SelectNode(movement.ReferenceId);};
        body.Children.Add(new TextBlock{Text=choices.Count==0?"Introduce or inspect a stable shared movement reference in Source. An inline new movement inside the transition creates a different instance.":"Native completion comes from this instance's per-actor state. Acquisition, stop range, travel budget, blocked movement and timeout retain their original controller rules. New routes are appended in native transition order.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#83958B"),Margin=new(0,10,0,6)});
        completionPanel=new(){Header="Movement completion",Content=body,Margin=new(0,10,0,6)};Inspector.Children.Add(completionPanel);
    }
    void UpdateCompletionControls()
    {
        if(selectedNode==null)return;var editable=RegionEditable(selectedNode);
        if(completionMovements!=null)completionMovements.IsEnabled=editable;if(completionTarget!=null)completionTarget.IsEnabled=editable;
        if(completionApply!=null)completionApply.IsEnabled=editable&&completionMovements?.SelectedItem is CompletionMovement chosen&&(adapter.IsMovementCompletion(document,selectedNode)?chosen.ReferenceId!=adapter.ReadCompletionBinding(document,selectedNode)?.MovementReferenceId:!string.IsNullOrWhiteSpace(completionTarget?.Text));
    }
    void CommitCompletionEdit(Guid selected,Guid movementReference,long revision,string? destination)
    {
        if(document.Revision!=revision)throw new InvalidOperationException("This completion proposal is stale.");
        var plan=SourceCompletionRefactor.Apply(adapter,Snapshot(),document.SourceFile!,selected,movementReference,destination);
        ApplyWorkspacePhaseEdit(plan,plan.Selection[0],"Linked completion");
    }
}
