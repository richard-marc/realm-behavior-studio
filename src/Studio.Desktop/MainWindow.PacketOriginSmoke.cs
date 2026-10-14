using System.Windows;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    async void PacketOriginSmoke()=>await TryRuntime(async()=>
    {
        var output=System.IO.Path.GetFullPath(".artifacts/packet-origin-desktop");System.IO.Directory.CreateDirectory(output);var checks=new List<string>();
        await RunPacketOriginUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunPacketOriginUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=System.IO.File.ReadAllText(file);
        const string source="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic{public partial class BehaviorDb{
    private _ StudioPacketOrigin=()=>Behav().Init("Cave Pirate Brawler",new State(new State("go",new Shoot(20,count:3,fixedAngle:0,coolDown:100),new Shoot(20,count:5,fixedAngle:0,coolDown:100),new TimedTransition(0,"wait")),new State("wait")));
}}
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);var nodes=document.AllNodes.Where(n=>n.Kind=="Shoot").ToArray();
        check(await StartSessionAsync(observeCallbacks:true),"Native recording starts causal attack playback");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();var attacks=session.Events.Where(e=>e.Kind=="attack").ToArray();var active=session;
        check(attacks.Length==2&&session.Entities.Single().Phase=="wait"&&attacks.All(e=>e.PacketOrigin!=null),"Native UI receives distinct emitters after the same-tick phase transition");
        SelectTimelineEvent(attacks[0]);check(selectedNode?.Id==nodes[0].Id&&TimelineEventInfo.Text.Contains("emitted by Shoot")&&TimelineEventInfo.Text.Contains("call #"),"Actual timeline selection opens the first emitting action with callback evidence");SelectTimelineEvent(attacks[1]);check(selectedNode?.Id==nodes[1].Id&&ReferenceEquals(session,active),"Second same-index attack navigates its own action while preserving native playback");
        check(!NavigateRuntimeSource(attacks[0] with{PacketOrigin=attacks[0].PacketOrigin! with{Source=attacks[0].PacketOrigin!.Source! with{SourceHash=new('0',64)}}})&&ReferenceEquals(session,active),"Changed causal source fingerprints reject instead of falling back to observed phase");check(!NavigateRuntimeSource(attacks[0] with{PacketOrigin=attacks[0].PacketOrigin! with{Source=null}})&&ReferenceEquals(session,active),"An untracked callback construction cannot masquerade as phase-source attribution");
        SelectTimelineEvent(attacks[0]);WorkspaceTabs.SelectedIndex=1;Status.Text="Paused · native emission source";UpdateLayout();CaptureView(this,System.IO.Path.Combine(output,"causal-attack-editor.png"));var path=System.IO.Path.Combine(output,"causal-trace.json");session.ExportTrace(path);using(var trace=System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path)))check(trace.RootElement.GetProperty("events").EnumerateArray().Any(e=>e.TryGetProperty("PacketOrigin",out var origin)&&origin.ValueKind==System.Text.Json.JsonValueKind.Object),"Desktop trace exports explicit packet-to-callback links");Stop(this,new());
        const string effects="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioPacketUiStrike=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"strike\",new SkyStrike(-1,StrikeLayout.Targets,fuseMs:300,intervalMs:2000,startupMs:0,aoeRadius:1,aoeDamage:100)));}}";
        ApplyProject(previous with{Source=effects,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);check(await StartSessionAsync(observeCallbacks:true),"Native effect recording starts original SkyStrike playback");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();var effect=session.Events.First(e=>e.Kind=="effect");SelectTimelineEvent(effect);check(selectedNode?.Kind=="SkyStrike"&&TimelineEventInfo.Text.Contains("emitted by SkyStrike")&&effect.PacketIndex==0,"Actual effect timeline selection navigates its native emitting controller");Stop(this,new());
        check(System.IO.File.ReadAllText(file)==supplied,"Causal event navigation and recording preserve supplied game source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
