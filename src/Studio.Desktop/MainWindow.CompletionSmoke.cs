using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void CompletionSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/completion-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunCompletionUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunCompletionUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var supplied=File.ReadAllText(primary);
        foreach(var kind in new[]{"FollowDistance","LockedFollowDistance"})
        {
            var source="using TKR.WorldServer.logic.behaviors;using TKR.WorldServer.logic.transitions;namespace TKR.WorldServer.logic {public partial class BehaviorDb {private _ StudioCompletionDesktop=()=>Behav().Init(\"Cave Pirate Brawler\",StudioCompletionDesktopRoot());static State StudioCompletionDesktopRoot(){var move=new "+kind+"(1,10,2,stopRange:1);return new State(new State(\"moving\",move),new State(\"done\"));}}}";
            ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[],LockedNodes=[],CollapsedNodes=[],Layout=[],Player=previous.Player with{Invulnerable=true}},null);
            var definition=document.Definitions.Single(d=>d.Definition!.Label=="StudioCompletionDesktopRoot");EditDefinition(definition.EntityId);var phase=document.AllNodes.Single(n=>n.Kind=="State"&&document.StateName(n.Id)=="moving");SelectNode(phase.Id);
            check(completionPanel is{IsExpanded:false}&&completionMovements!.Items.Count==1,kind+" phase exposes one exact movement instance in a quiet collapsed route form");
            var movement=adapter.CompletionMovements(document,phase).Single();completionPanel!.IsExpanded=true;UpdateLayout();completionTarget!.Text="done";await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(completionApply!.IsEnabled,kind+" destination and actual instance enable route creation");var before=document.Source;var revision=document.Revision;completionApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var transition=selectedNode!;
            check(document.Revision==revision+1&&transition.Kind==kind+"CompleteTransition"&&adapter.ReadCompletionBinding(document,transition)?.MovementReferenceId==movement.ReferenceId,kind+" actual Add creates a native route to the existing movement in one source transaction");
            check(completionPanel!=null&&completionNavigate?.IsEnabled==true&&!completionApply!.IsEnabled,kind+" route inspector identifies the current shared relationship without a redundant apply");
            check(Inspector.Children.OfType<TextBlock>().First().Text==(kind=="LockedFollowDistance"?"Locked follow complete":"Bounded follow complete")&&BehaviorAnalysis.Condition(transition).EndsWith("follow complete"),kind+" inspector and graph use readable condition labels while retaining native types");
            completionNavigate!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(selectedNode!.Id==movement.ReferenceId,kind+" navigation opens the actual running movement reference");
            Undo(this,new());check(document.Source==before&&document.Find(phase.Id).Id==phase.Id,kind+" workspace Undo restores full source and phase identity");Redo(this,new());check(document.Find(transition.Id).Kind==kind+"CompleteTransition",kind+" Redo retains the new route identity");
            var accepted=document.Source;var rejected=false;try{CommitCompletionEdit(phase.Id,movement.ReferenceId,document.Revision-1,"done");}catch(InvalidOperationException){rejected=true;}check(rejected&&document.Source==accepted,kind+" stale relationship edit rejects without changing accepted source");
            SelectNode(transition.Id);Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;check(completionMovements?.IsEnabled==false&&completionApply?.IsEnabled==false,kind+" source lock protects native relationship controls");document.LockedNodes.Clear();SelectNode(transition.Id);completionPanel!.IsExpanded=true;WorkspaceTabs.SelectedIndex=1;UpdateLayout();CaptureView(this,Path.Combine(output,kind+"-completion.png"));
            var saved=Path.Combine(output,kind+".rbs");BehaviorDocument.Save(saved,Snapshot());ApplyProject(BehaviorDocument.Load(saved),saved);
            check(adapter.ReadCompletionBinding(document,document.Find(transition.Id))?.MovementReferenceId==movement.ReferenceId,kind+" reopening retains both ends of the exact movement relationship");
            check(await StartSessionAsync(),kind+" Play executes the accepted source factory and completion transition");running=false;await session!.AdvanceAsync(new(),800);UpdateSession();check(session.Entities.Single() is{Phase:"done",X:66.5f},kind+" native playback changes phase only after the actual movement budget completes");Stop(this,new());
        }
        const string repairSource="using TKR.WorldServer.logic.behaviors;using TKR.WorldServer.logic.transitions;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioCompletionRepair=()=>Behav().Init(\"Cave Pirate Brawler\",StudioCompletionRepairRoot());static State StudioCompletionRepairRoot(){var move=new FollowDistance(1,10,2,stopRange:1);return new State(new State(\"moving\",move,new FollowDistanceCompleteTransition(new FollowDistance(1,10,2,stopRange:1),\"done\")),new State(\"done\"));}}}";
        ApplyProject(previous with{Source=repairSource,Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[],LockedNodes=[],CollapsedNodes=[],Layout=[],Player=previous.Player with{Invulnerable=true}},null);var repairDefinition=document.Definitions.Single(d=>d.Definition!.Label=="StudioCompletionRepairRoot");EditDefinition(repairDefinition.EntityId);var repair=document.AllNodes.Single(n=>n.Kind=="FollowDistanceCompleteTransition");SelectNode(repair.Id);
        check(adapter.ReadCompletionBinding(document,repair)?.MovementReferenceId==null&&completionApply?.IsEnabled==true,"A separate inline movement exposes an actionable repair using the existing phase instance");var repairMovement=adapter.CompletionMovements(document,repair).Single();var repairBefore=document.Source;
        SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(s=>s.Path.Equals(primary,StringComparison.OrdinalIgnoreCase));SourceEditor.Text+="\n// retained relationship draft";var draft=SourceEditor.Text;var blocked=false;
        try{CommitCompletionEdit(repair.Id,repairMovement.ReferenceId,document.Revision,null);}catch(InvalidOperationException){blocked=true;}
        check(blocked&&document.Source==repairBefore&&SourceEditor.Text==draft&&workspacePhaseUndo.Count==0,"An affected source draft blocks relationship repair without losing either buffer or history");DiscardSourceDraft(this,new());SelectNode(repair.Id);completionApply!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(repair.Id).Id==repair.Id&&adapter.ReadCompletionBinding(document,document.Find(repair.Id))?.MovementReferenceId==repairMovement.ReferenceId,"Actual repair replaces the native child input while preserving the completion transition");Undo(this,new());check(document.Source==repairBefore&&adapter.ReadCompletionBinding(document,document.Find(repair.Id))?.MovementReferenceId==null,"Repair Undo restores the complete independent-constructor source");Redo(this,new());check(adapter.ReadCompletionBinding(document,document.Find(repair.Id))?.MovementReferenceId==repairMovement.ReferenceId,"Repair Redo restores the exact shared instance relationship");
        check(await StartSessionAsync(),"Repaired relationship loads its accepted factory in the native worker");running=false;await session!.AdvanceAsync(new(),800);check(session.Entities.Single().Phase=="done","Actual repaired desktop route reaches its native completion destination");Stop(this,new());
        var exactSource=repairSource.Replace("new FollowDistanceCompleteTransition(new FollowDistance(1,10,2,stopRange:1),\"done\")","new Taunt(true,\"exact target\")").Replace("new State(\"done\")","new State(\" done \")");ApplyProject(previous with{Source=exactSource,Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[],LockedNodes=[],CollapsedNodes=[],Layout=[],Player=previous.Player with{Invulnerable=true}},null);EditDefinition(document.Definitions.Single(d=>d.Definition!.Label=="StudioCompletionRepairRoot").EntityId);var exactPhase=document.AllNodes.Single(n=>n.Kind=="State"&&document.StateName(n.Id)=="moving");SelectNode(exactPhase.Id);completionPanel!.IsExpanded=true;UpdateLayout();completionTarget!.SelectedItem=" done ";await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);completionApply!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode!.Parameters.Any(p=>p.Name=="targetState"&&BehaviorDocument.LiteralString(p.Expression)==" done "),"Actual destination selection preserves native phase whitespace rather than altering its literal identity");
        check(File.ReadAllText(primary)==supplied,"Completion relationship authoring, history and native playback leave supplied source unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
