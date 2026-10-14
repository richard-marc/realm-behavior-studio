using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    async void TransitionCallbackSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/transition-callback-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunTransitionCallbackUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunTransitionCallbackUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=File.ReadAllText(file);
        const string source="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopTransition=()=>Behav().Init("Cave Pirate Brawler",new State(new State("waiting",new TimedTransition(200,"branch"),new Shoot(20,count:3,coolDown:100)),new State("branch",new State("leaf"))));
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Arena=new(),Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;var transition=document.AllNodes.Single(n=>n.Kind=="TimedTransition");SelectNode(transition.Id);
        check(callbackPanel is{IsExpanded:false}&&callbackPlay is{IsEnabled:true},"Transition evaluation recording uses the existing collapsed native callback inspector");
        Inspector.Children.OfType<TextBox>().Single(t=>t.Text=="200").Text="100";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(document.Find(transition.Id).Parameters.Any(p=>p.Name=="time"&&p.Expression=="100"),"Typed transition timing Apply preserves its source identity");Undo(this,new());Redo(this,new());SelectNode(transition.Id);
        callbackPlay!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var watch=System.Diagnostics.Stopwatch.StartNew();while((session is not CompiledRuntime||runtimeStarting!=null)&&watch.Elapsed.TotalSeconds<120)await Task.Delay(50);check(session is CompiledRuntime&&runtimeStarting==null,"Actual transition recording starts compiled source dispatch");running=false;await session!.AdvanceAsync(new(),300);Pause(this,new());UpdateSession();callbackPanel!.IsExpanded=true;
        var rows=callbackRows!.Children.OfType<Button>().Where(b=>b.Tag is NativeCallbackRecord{Stage:"Transition tick"}).ToArray();check(rows.Any(b=>((NativeCallbackRecord)b.Tag).Transition is{Matched:false})&&rows.Any(b=>((NativeCallbackRecord)b.Tag).Transition is{Matched:true,Target:"branch",EnteredPhase:"leaf"}),"Inspector displays actual failed evaluations and the matched parent target versus entered leaf");
        var matched=rows.Single(b=>((NativeCallbackRecord)b.Tag).Transition?.Matched==true);var record=(NativeCallbackRecord)matched.Tag;check(((TextBlock)matched.Content).Text.Contains("matched → branch")&&matched.ToolTip.ToString()!.Contains("entered phase: leaf"),"Native decision labels and details preserve result and leaf distinctions");var originalSession=session;matched.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(selectedNode?.Id==transition.Id&&ReferenceEquals(session,originalSession),"Actual evaluation row opens the tested transition without discarding playback");
        callbackPanel!.IsExpanded=true;callbackRows.Children.OfType<Button>().Single(b=>b.Tag is NativeTransitionResult).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(selectedNode?.Kind=="State"&&selectedNode.Label=="branch"&&ReferenceEquals(session,originalSession),"Open selected target navigates the actual parent State construction rather than the observed leaf");
        check(!NavigateRuntimeSource(new(record.Ms,"transition-target","stale",PhaseOrigin:record.Transition!.TargetOrigin! with{SourceHash=new('0',64)}))&&ReferenceEquals(session,originalSession),"Changed target source fingerprints reject stale transition navigation");
        SelectNode(transition.Id);callbackPanel!.IsExpanded=true;Status.Text="Paused · observed transition decisions";UpdateLayout();callbackRows.Children.OfType<Button>().Last().BringIntoView();UpdateLayout();CaptureView(this,Path.Combine(output,"transition-decisions-editor.png"));var trace=Path.Combine(output,"transition-decisions-trace.json");session.ExportTrace(trace);check(File.ReadAllText(trace).Contains("EnteredPhase")&&File.ReadAllText(trace).Contains("SelectedIndex"),"Desktop trace exports actual native transition result evidence");Stop(this,new());var path=Path.Combine(output,"transition-decisions.rbs");var identities=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);check(document.Identities.SequenceEqual(identities),"Edited transition source and identities survive save/reopen");check(File.ReadAllText(file)==supplied,"Transition decision controls, Play and navigation preserve supplied game source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
