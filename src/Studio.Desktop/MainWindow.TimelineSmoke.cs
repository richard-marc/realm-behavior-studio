using System.IO;
using System.Windows;
using System.Windows.Threading;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow
{
    async Task RunTimelineUiSmoke(string source, BehaviorNode root, string output, Action<bool,string> check)
    {
        var tree="new State(new State(\"first\", new Follow(.4), new Shoot(20, count: 2, coolDown: 100), new TimedTransition(100, \"second\")), new State(\"second\", new Shoot(20, count: 4, coolDown: 100)))";
        document=new BehaviorDocument(source.Remove(root.Start,root.Length).Insert(root.Start,tree),adapter,sourceFile:selectedEntity!.SourceFile); Changed();
        await StartSessionAsync(); running=false; await session!.AdvanceAsync(new(1,0),200); session.SetBossHpFraction(.8); await session.AdvanceAsync(new(0,1),300); UpdateSession();
        var tape=timelineReplay!; var end=tape.Commands.Last().Result!;
        check(tape.DurationMs==500 && TimelineSaveButton.IsEnabled && TimelineEvents.Items.Count==session.Events.Count && tape.Commands.Any(c=>c.Kind=="boss-hp"),"Desktop timeline records native inputs, health overrides and every original event");
        check(tape.Movements.Any(m=>m.Player) && tape.Movements.Any(m=>!m.Player&&m.PhaseNodeId!=null),"Timeline movement records genuine player steps and native enemy ticks with known phase provenance");
        WorkspaceTabs.SelectedIndex=5; await Dispatcher.InvokeAsync(()=>UpdateLayout(),DispatcherPriority.ApplicationIdle); CaptureView(this,Path.Combine(output,"native-timeline.png"));
        var phase=tape.Events.First(ev=>ev.Kind=="transition"&&ev.NodeId!=null); SelectTimelineEvent(phase);
        check(selectedNode?.Id==phase.NodeId && TimelineVisual.Selected==phase && TimelineEventInfo.Text.Contains("source origin recorded"),"Timeline event inspection follows recorded source identity and actual actor provenance");
        await SeekTimelineAsync(75);
        check(session?.Ms==75 && !running && replayCursor!=null && TimelineLiveButton.IsEnabled && TimelinePlayButton.IsEnabled,"Desktop seeking restores a paused native run at an exact five-millisecond prefix");
        check(TimelineVisual.Replay?.Events.SequenceEqual(tape.Events)==true && TimelineEvents.Items.Count==tape.Events.Length,"Seeking keeps the full recorded timeline available for future navigation");
        await SeekTimelineAsync(0); Breakpoint.SelectedIndex=1; recordedPlayback=true; running=true; await AdvanceRecordedTimeline(200);
        check(!running && session!.Ms==EncounterTimeline.EventSeekMs(phase,tape.DurationMs) && Status.Text.Contains("Recorded breakpoint"),"Recorded playback pauses at the actual selected event boundary and follows its source");
        Breakpoint.SelectedIndex=0; recordedPlayback=false;
        await replayCursor!.AdvanceToAsync(500); UpdateSession();
        check(session!.Recording!.ObserveCurrent(session)==end,"Desktop recorded playback reproduces the original native state through its full run");
        await SeekTimelineAsync(175); ContinueLiveTimeline(this,new()); running=false; await session!.AdvanceAsync(new(-1,0),100); UpdateSession();
        check(replayCursor==null && !recordedPlayback && timelineReplay!.DurationMs==275 && timelineReplay.Commands.Last().Input?.X==-1,"Live from here replaces future inputs with a genuine recorded native branch");
        var saved=Path.Combine(output,"timeline.rbsreplay"); timelineReplay!.Save(saved); await LoadTimelineReplayAsync(EncounterReplay.Load(saved));
        check(WorkspaceTabs.SelectedIndex==5 && session?.Ms==0 && timelineReplay!.DurationMs==275 && document.Source==tape.Project.Source,"Opening a recording restores source, settings, full event history and actual native initialization");
        var layout=CaptureLayout(); WorkspaceTabs.SelectedIndex=0; RestoreLayout(layout);
        check(WorkspaceTabs.SelectedIndex==5,"The timeline workspace participates in layout persistence");
        var origin=document; document=new BehaviorDocument(document.Source,adapter,sourceFile:selectedEntity!.SourceFile); Changed();
        check(timelineReplay==null && replayCursor==null && !TimelineSaveButton.IsEnabled && TimelineEvents.Items.Count==0,"Document changes clear the replay cursor and prevent stale timeline use");
        document=origin; Changed(); await StartSessionAsync("second"); running=false; await session!.AdvanceAsync(new(),100); UpdateSession();
        check(timelineReplay?.Phase=="second" && TimelineInfo.Text.Contains("forced phase second") && timelineReplay.Events.Any(ev=>ev.Kind=="forced-phase"),"Forced-phase recordings visibly retain bypassed initialization provenance");
        var helpers="\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static State StudioTimelineRoot(int count) => new State(new Shoot(20, count: count, coolDown: 100)); } }";
        document=new BehaviorDocument(source.Remove(root.Start,root.Length).Insert(root.Start,"StudioTimelineRoot(5)")+helpers,adapter,sourceFile:selectedEntity!.SourceFile); Changed();
        await StartSessionAsync(); running=false; var firstProcess=((CompiledRuntime)session!).ProcessId; await session.AdvanceAsync(new(1,0),200); UpdateSession();
        var compiledTape=timelineReplay!; await SeekTimelineAsync(75);
        check(session is CompiledRuntime worker && worker.ProcessId!=firstProcess && session.Ms==75 && worker.Stamp.BuildId==compiledTape.Identity.CompiledBuildId,"Desktop compiled timeline seeking starts an independent worker with the recorded native build");
        await replayCursor!.AdvanceToAsync(200); UpdateSession();
        check(session!.Recording!.ObserveCurrent(session)==compiledTape.Commands.Last().Result && session.Shots.Any(s=>s.Count==5),"Desktop compiled timeline playback reproduces actual generated helper attacks");
        await SeekTimelineAsync(0); var pending=AdvanceRecordedTimeline(100);
        document=new BehaviorDocument(source.Remove(root.Start,root.Length).Insert(root.Start,tree),adapter,sourceFile:selectedEntity!.SourceFile); Changed();
        await StartSessionAsync(); running=false; var replacement=session; var cancelled=false;
        try { await pending; } catch(Exception error) when(error is OperationCanceledException or ObjectDisposedException or InvalidOperationException) { cancelled=true; }
        check(cancelled && ReferenceEquals(session,replacement) && session is EncounterSession && replayAdvancing==null && timelineReplay?.Identity.CompiledBuildId=="","Document changes cancel an in-flight recorded worker advance without disturbing the replacement native run");
        Stop(this,new()); WorkspaceTabs.SelectedIndex=0;
    }
}
