using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void TimerSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/timer-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool passed,string label){if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);}
        await RunGrenadeUiSmoke(output,Check);await RunTimerUiSmoke(output,Check);
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunTimerUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=File.ReadAllText(file);
        const string source="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioDesktopTimers=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"timers\",new Grenade(2,50,range:20,coolDown:10000),new Shoot(20,count:3,coolDown:10000)));}}";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Arena=new(),Player=previous.Player with{Invulnerable=true}},null);
        WorkspaceTabs.SelectedIndex=1;var grenade=document.AllNodes.Single(n=>n.Kind=="Grenade");var shoot=document.AllNodes.Single(n=>n.Kind=="Shoot");SelectNode(grenade.Id);
        check(callbackPanel is{IsExpanded:false},"Timer evidence uses the existing quiet collapsed callback inspector");
        check(await StartSessionAsync(observeCallbacks:true)&&session is CompiledRuntime,"Desktop recording starts the original compiled timer dispatch path");running=false;
        await session!.AdvanceAsync(new(),1000);await session.AdvanceAsync(new(),600);UpdateSession();Pause(this,new());callbackPanel!.IsExpanded=true;
        var row=callbackRows!.Children.OfType<Button>().Single(b=>b.Tag is NativeCallbackRecord{Timer:not null});var record=(NativeCallbackRecord)row.Tag;
        check(record is{Stage:"Timer action",Origin:null,Timer:{DelayMs:1500,Returned:true,SchedulingSource.NodeId:not null}}&&record.Timer.SchedulingSource.NodeId==grenade.Id,"Observed timer row keeps native invocation, returned value and separate scheduling construction");
        check(row.IsEnabled&&((TextBlock)row.Content).Text.Contains("scheduled by Grenade")&&((string)row.ToolTip).Contains("entity owner"),"Timer controls label scheduling actor and entity owner without pretending the timer is a Grenade tick");
        var retained=session;row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode?.Id==grenade.Id&&ReferenceEquals(session,retained),"Actual timer row opens its scheduling source while retaining native playback");
        SelectNode(shoot.Id);var impact=session!.Events.Single(e=>e.Kind=="area");SelectTimelineEvent(impact);
        check(selectedNode?.Id==grenade.Id&&TimelineEventInfo.Text.Contains("world timer")&&TimelineEventInfo.Text.Contains("scheduled by Grenade"),"Delayed timeline navigation selects the recorded scheduler instead of the latest unrelated Shoot callback");
        var changed=impact with{PacketOrigin=impact.PacketOrigin! with{Timer=impact.PacketOrigin!.Timer! with{SchedulingSource=impact.PacketOrigin.Timer.SchedulingSource! with{SourceHash=new('0',64)}}}};
        check(!NavigateRuntimeSource(changed)&&ReferenceEquals(session,retained),"Stale timer scheduling fingerprints reject navigation without discarding the run");
        var untracked=impact with{PacketOrigin=impact.PacketOrigin! with{Timer=impact.PacketOrigin!.Timer! with{SchedulingSource=null}}};
        check(!NavigateRuntimeSource(untracked)&&Status.Text.Contains("Timer scheduling source is unavailable"),"Missing timer registration source stays an explicit navigation boundary");
        SourceEditor.Text=source+"\n// unapplied timer draft";
        check(!NavigateRuntimeSource(impact)&&ReferenceEquals(session,retained),"Unapplied source drafts protect timer navigation from replacing editing intent");SourceEditor.Text=source;RememberSourceDraft();
        SelectNode(grenade.Id);callbackPanel!.IsExpanded=true;Status.Text="Paused · observed timer callbacks";UpdateLayout();callbackRows!.Children.OfType<Button>().Last().BringIntoView();UpdateLayout();CaptureView(this,Path.Combine(output,"timer-callback-inspector.png"));
        var trace=Path.Combine(output,"timer-trace.json");session!.ExportTrace(trace);check(File.ReadAllText(trace).Contains("SchedulingCallbackSequence")&&File.ReadAllText(trace).Contains("Timer action"),"Desktop trace retains timer invocation and registration metadata");Stop(this,new());
        var path=Path.Combine(output,"timers.rbs");var identities=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(document.Source==source&&document.Identities.SequenceEqual(identities),"Timer-observed source and identities survive save/reopen");
        check(File.ReadAllText(file)==supplied,"Timer recording, source navigation and traces preserve supplied game source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
