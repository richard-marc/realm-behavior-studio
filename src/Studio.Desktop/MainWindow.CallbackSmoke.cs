using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    async void CallbackSmoke()=>await TryRuntime(async()=>
    {
        var output=System.IO.Path.GetFullPath(".artifacts/callback-desktop");System.IO.Directory.CreateDirectory(output);var checks=new List<string>();
        await RunCallbackUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunCallbackUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=System.IO.File.ReadAllText(file);
        const string source="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
using TKR.Shared.resources;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;
namespace TKR.WorldServer.logic {public partial class BehaviorDb {
    private _ StudioCallbackFixture=()=>Behav().Init("Cave Pirate Brawler",new State(new State("go",new StudioCallbackWrapper(new ConditionEffectBehavior(ConditionEffectIndex.Slowed),new Shoot(20,count:3,fixedAngle:0,coolDown:100),new ConditionEffectBehavior(ConditionEffectIndex.Armored),new ConditionEffectBehavior(ConditionEffectIndex.Speedy)),new TimedTransition(100,"wait")),new State("wait")));
    sealed class StudioCallbackWrapper:Behavior {
        readonly Behavior entry,tick,exit,death;
        public StudioCallbackWrapper(Behavior entry,Behavior tick,Behavior exit,Behavior death){this.entry=entry;this.tick=tick;this.exit=exit;this.death=death;}
        protected override void OnStateEntry(Entity host,TickTime time,ref object state)=>entry.OnStateEntry(host,time);
        protected override void TickCore(Entity host,TickTime time,ref object state)=>tick.Tick(host,time);
        protected override void OnStateExit(Entity host,TickTime time,ref object state)=>exit.OnStateEntry(host,time);
        public override void OnDeath(Entity host,ref TickTime time)=>death.OnStateEntry(host,time);
    }
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;
        var wrapper=EncounterRoot.Descendants().Single(n=>n.Kind.EndsWith("StudioCallbackWrapper"));SelectNode(wrapper.Id);check(callbackPanel is{IsExpanded:false}&&callbackPlay is{IsEnabled:true},"Observed callbacks stay collapsed and offer explicit native recording");callbackPlay!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var watch=System.Diagnostics.Stopwatch.StartNew();while((session is not CompiledRuntime||runtimeStarting!=null)&&watch.Elapsed.TotalSeconds<120)await Task.Delay(50);check(session is CompiledRuntime&&runtimeStarting==null,"Actual recording button starts compiled native execution");running=false;await session!.AdvanceAsync(new(),200);Pause(this,new());UpdateSession();
        check(callbackRows!.Children.OfType<Button>().Any(b=>b.Tag is NativeCallbackRecord{Stage:"Exit"})&&callbackSummary!.Text.Contains("Actual native calls"),"Custom controller panel exposes observed exit and nested native dispatch");check(!session.Events.Any(e=>e.Kind=="callback"),"Callback recording keeps routine console events quiet");var originalSession=session;var childRow=callbackRows.Children.OfType<Button>().First(b=>b.Tag is NativeCallbackRecord c&&c.Behavior.EndsWith("Shoot"));var child=(NativeCallbackRecord)childRow.Tag;childRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(selectedNode?.Kind=="Shoot"&&ReferenceEquals(session,originalSession),"Actual callback row navigates the action's recorded constructor without interrupting playback");check(!NavigateRuntimeSource(new(child.Ms,"callback","changed",PhaseOrigin:child.Origin! with{SourceHash=new('0',64)}))&&ReferenceEquals(session,originalSession),"Changed callback source fingerprints reject stale navigation without discarding playback");
        WorkspaceTabs.SelectedIndex=1;SelectNode(wrapper.Id);callbackPanel!.IsExpanded=true;UpdateLayout();CaptureView(this,System.IO.Path.Combine(output,"observed-callbacks-editor.png"));var trace=System.IO.Path.Combine(output,"callbacks-trace.json");session.ExportTrace(trace);check(System.IO.File.ReadAllText(trace).Contains("callbacks"),"Desktop trace retains complete native callback records");Stop(this,new());
        var path=System.IO.Path.Combine(output,"callbacks.rbs");var identities=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);check(document.Identities.SequenceEqual(identities),"Callback-enabled source and identities survive save/reopen");check(System.IO.File.ReadAllText(file)==supplied,"Observed callback controls preserve supplied game source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
