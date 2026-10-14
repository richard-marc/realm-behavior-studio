using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    async void SharedEventSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/shared-event-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunSharedEventUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunSharedEventUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=File.ReadAllText(file);
        const string source="""
using System;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;
using TKR.WorldServer.networking.packets.outgoing;
namespace TKR.WorldServer.logic {public partial class BehaviorDb {
    private _ StudioDesktopRemoval=()=>Behav().Init("Cave Pirate Brawler",new State("hooks",new RemovalHook(100),new RemovalHook(200)));
    sealed class RemovalHook:Behavior {
        readonly int marker;public RemovalHook(int marker)=>this.marker=marker;
        protected override void OnStateEntry(Entity host,TickTime time,ref object state){Action handler=()=>host.World.BroadcastIfVisible(new ShowEffect{EffectType=EffectType.LedgerMark,TargetObjectId=host.Id,Duration=marker},host);((Enemy)host).EncounterRemoved+=handler;((Enemy)host).EncounterRemoved+=handler;((Enemy)host).EncounterRemoved-=handler;state=handler;}
        protected override void OnStateExit(Entity host,TickTime time,ref object state){if(state is Action handler)((Enemy)host).EncounterRemoved-=handler;state=null;}
    }
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Arena=new(){Actors=[new("Cave Pirate Brawler",64.5,67.5)]},Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;
        var hooks=document.AllNodes.Where(n=>n.Kind.EndsWith("RemovalHook")).ToArray();SelectNode(hooks[0].Id);check(callbackPanel is{IsExpanded:false}&&callbackPlay is{IsEnabled:true},"Shared-event evidence starts in the existing collapsed callback inspector");
        Inspector.Children.OfType<TextBox>().Single(t=>t.Text=="100").Text="150";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(document.Find(hooks[0].Id).Parameters.Single().Expression=="150","Typed controller editing preserves its source identity before native shared-event Play");Undo(this,new());Redo(this,new());SelectNode(hooks[0].Id);
        callbackPlay!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var watch=System.Diagnostics.Stopwatch.StartNew();while((session is not CompiledRuntime||runtimeStarting!=null)&&watch.Elapsed.TotalSeconds<120)await Task.Delay(50);check(session is CompiledRuntime&&runtimeStarting==null,"Actual recording starts native shared callback observation");running=false;await session!.AdvanceAsync(new(),100);Pause(this,new());session.RemoveMinionsForTest();UpdateSession();callbackPanel!.IsExpanded=true;
        var row=callbackRows!.Children.OfType<Button>().Single(b=>b.Tag is NativeCallbackRecord{Stage:"Encounter removed"});var record=(NativeCallbackRecord)row.Tag;check(record.SharedEvent is{SubscriberActorId:not null,SubscriptionCallbackSequence:not null}&&((TextBlock)row.Content).Text.Contains("registered by"),"Cleanup row distinguishes actual dispatch from the registering controller and callback");
        check(session.Callbacks.Where(c=>c.Stage=="Encounter removed").Select(c=>c.Origin?.NodeId).SequenceEqual(hooks.Select(h=>(Guid?)h.Id))&&session.Effects.Select(e=>e.DurationMs).SequenceEqual(new[]{150,200}),"Desktop removal preserves original handler order and edited native effect payloads");
        var originalSession=session;row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(selectedNode?.Id==hooks[0].Id&&ReferenceEquals(session,originalSession),"Actual cleanup row opens its registration construction without discarding playback");
        check(!NavigateRuntimeSource(new(record.Ms,"callback","changed",PhaseOrigin:record.Origin! with{SourceHash=new('0',64)}))&&ReferenceEquals(session,originalSession),"Changed shared registration fingerprints reject stale navigation");
        var effect=session.Events.First(e=>e.Kind=="effect");check(effect.PacketOrigin?.SharedEvent==record.SharedEvent&&effect.PacketOrigin.CallbackSequence==record.Sequence,"Desktop effect keeps its exact shared dispatch and registration link");
        check(NavigateRuntimeSource(effect)&&selectedNode?.Id==hooks[0].Id,"Causal effect navigation selects the registered controller source");
        WorkspaceTabs.SelectedIndex=1;callbackPanel!.IsExpanded=true;Status.Text="Paused · observed shared callbacks";UpdateLayout();callbackRows!.Children.OfType<Button>().Last().BringIntoView();UpdateLayout();CaptureView(this,Path.Combine(output,"shared-event-editor.png"));var trace=Path.Combine(output,"shared-event-trace.json");session.ExportTrace(trace);check(File.ReadAllText(trace).Contains("SubscriptionCallbackSequence"),"Desktop trace retains explicit shared callback metadata");Stop(this,new());
        var path=Path.Combine(output,"shared-event.rbs");var identities=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);check(document.Identities.SequenceEqual(identities),"Shared controller source identities survive save/reopen");check(File.ReadAllText(file)==supplied,"Shared callback controls, native Play and source navigation preserve supplied game source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
