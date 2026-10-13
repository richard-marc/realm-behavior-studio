using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void CycleSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/phase-cycle-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool passed,string label){if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);}
        RunTransitionUiSmoke(output,Check);await RunCycleUiSmoke(output,Check);
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunCycleUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var supplied=File.ReadAllText(primary);
        const string source="using TKR.WorldServer.logic.transitions; namespace TKR.WorldServer.logic { public partial class BehaviorDb { private _ StudioDesktopCycle=()=>Behav().Init(\"Cave Pirate Brawler\",new State(new State(\"a\",new TimedTransition(0,\"b\")),new State(\"b\",new TimedTransition(0,\"c\")),new State(\"c\",new TimedTransition(0,\"a\")))); } }";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[],LockedNodes=[],CollapsedNodes=[],Layout=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;PhaseReview.IsExpanded=true;
        var analysis=BehaviorAnalysis.Analyze(EncounterRoot);var cycle=analysis.Cycles.Single();var ids=document.Identities;
        var diagnostic=PhaseDiagnostics.Children.OfType<Button>().Single(b=>((DocumentDiagnostic)b.Tag).Message.StartsWith("Phase cycle:"));
        check(!((DocumentDiagnostic)diagnostic.Tag).IsError&&cycle.PhaseIds.Count==3&&PhaseDiagnostics.Children.OfType<Button>().Count()==1,"Phase review shows one navigable nonblocking notice for the complete three-phase cycle");diagnostic.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode!.Id==cycle.TransitionIds[0]&&Graph.Highlight==selectedNode.Id&&ParametersTab.IsSelected,"The cycle notice selects its exact source transition in graph, tree and inspector");
        Expander Card()=>Inspector.Children.OfType<Expander>().Single(e=>e.Header?.ToString()?.StartsWith("Phase cycle ·")==true);
        StackPanel CardBody()=>(StackPanel)((ScrollViewer)Card().Content).Content;
        check(Card().IsExpanded==false&&CardBody().Children.OfType<Button>().Select(b=>document.Find((Guid)b.Tag).Label).SequenceEqual(new[]{"a","b","c"}),"Collapsed cycle details expose the actual ordered phase identities without extra viewport noise");
        Card().IsExpanded=true;CardBody().Children.OfType<Button>().Single(b=>document.Find((Guid)b.Tag).Label=="b").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode!.Label=="b"&&BehaviorTree.SelectedItem is TreeViewItem{Tag:BehaviorNode phase}&&phase.Id==selectedNode.Id,"Cycle member navigation focuses its exact phase in graph and hierarchy");
        check(document.Source==source&&document.Identities.SequenceEqual(ids),"Cycle inspection and member navigation leave accepted source/history identities unchanged");
        var transition=document.Find(cycle.TransitionIds.Last());document.Apply(new(document.Revision,"Break reviewed cycle",[new(transition.Id,"targetState","\"c\"")]),ValidateAuthoring);Changed(transition.Id);
        check(!ReviewDiagnostics().Any(d=>d.Message.StartsWith("Phase cycle:"))&&ReviewDiagnostics().Any(d=>d.Message.StartsWith("Self transition")),"An accepted destination edit recalculates the multi-phase cycle and retains the actual self-transition notice");
        Undo(this,new());check(document.Source==source&&BehaviorAnalysis.Analyze(EncounterRoot).Cycles.Single().PhaseIds.SequenceEqual(cycle.PhaseIds),"One Undo restores the reviewed cycle and exact member identities");
        var saved=Path.Combine(output,"cycle.rbs");BehaviorDocument.Save(saved,Snapshot());ApplyProject(BehaviorDocument.Load(saved),saved);
        check(BehaviorAnalysis.Analyze(EncounterRoot).Cycles.Single().TransitionIds.SequenceEqual(cycle.TransitionIds),"Save/reopen recomputes cycle routes from the same accepted source identities");
        check(await StartSessionAsync(),"Cycle advisories allow the real native encounter to start");running=false;var visited=new List<string>();for(var i=0;i<4;i++){await session!.AdvanceAsync(new(),100);visited.Add(session.Entities.Single().Phase);}UpdateSession();
        check(visited.SequenceEqual(new[]{"b","c","a","b"}),"Desktop native playback follows the reviewed cycle through repeated phase entries");Stop(this,new());
        var exitSource=source.Replace("new TimedTransition(0,\"b\")","new HpLessTransition(.2,\"c\"),new TimedTransition(500,\"b\")").Replace("new TimedTransition(0,\"c\")","new TimedTransition(500,\"a\")").Replace("new State(\"c\",new TimedTransition(0,\"a\"))","new State(\"c\")");
        ApplyProject(previous with{Source=exitSource,Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[],LockedNodes=[],CollapsedNodes=[],Layout=[]},null);SelectNode(EncounterRoot.StateChildren.Single(n=>n.Label=="a").Id);
        var exit=BehaviorAnalysis.Analyze(EncounterRoot).Cycles.Single().ExitTransitionIds.Single();Card().IsExpanded=true;
        CardBody().Children.OfType<Button>().Single(b=>(Guid)b.Tag==exit).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode!.Id==exit&&Inspector.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("HP < 20%")),"A possible cycle exit opens its actual native condition and destination");WorkspaceTabs.SelectedIndex=1;PhaseReview.IsExpanded=true;Card().IsExpanded=true;UpdateLayout();CaptureView(this,Path.Combine(output,"phase-cycle-review.png"));
        check(File.ReadAllText(primary)==supplied,"Cycle review, edits, history, persistence and native playback leave supplied game source unchanged");ApplyProject(previous,null);PhaseReview.IsExpanded=false;WorkspaceTabs.SelectedIndex=0;
    }
}
