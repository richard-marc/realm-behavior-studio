using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;

public partial class MainWindow
{
    StudioProject? recordedProject;
    string? recordedPhase;
    ReplayIdentity? recordedIdentity;
    EncounterReplay? timelineReplay;
    ReplayCursor? replayCursor;
    CompiledRuntime.PreparedRuntime? replayPrepared;
    CancellationTokenSource? replayAdvancing;
    bool timelineReplacing, timelineUpdating, recordedPlayback;
    int timelineGeneration, timelineEventsCount = -1;
    readonly System.Diagnostics.Stopwatch timelineRefreshClock=System.Diagnostics.Stopwatch.StartNew();
    void InitializeTimelineControls()
    {
        TimelineVisual.SeekRequested += async (ms, ev) => await TryRuntime(async () => { if(ev != null) SelectTimelineEvent(ev); await SeekTimelineAsync(ms); });
    }
    void ClearTimeline()
    {
        replayAdvancing?.Cancel();
        timelineGeneration++; recordedPlayback=false; replayCursor=null; recordedProject=null; recordedIdentity=null; recordedPhase=null; replayPrepared=null; timelineReplay=null; timelineEventsCount=-1;
        if (TimelineVisual == null) return;
        TimelineVisual.Replay=null; TimelineVisual.InvalidateVisual(); TimelineEvents.ItemsSource=null;
        timelineUpdating=true; TimelineSeek.Maximum=5; TimelineSeek.Value=0; timelineUpdating=false; RefreshTimelineControls();
    }
    void BeginTimelineRecording(string? phase)
    {
        recordedProject=Snapshot(); recordedPhase=phase;
        recordedIdentity=EncounterReplay.Identify(adapter,recordedProject,(session as CompiledRuntime)?.Stamp.BuildId ?? "");
        if (session is not CompiledRuntime && recordedIdentity.ResourceHash != adapter.LoadedResourceHash) throw new InvalidOperationException("Loaded resources changed. Restart Studio before recording.");
        replayPrepared=(session as CompiledRuntime)?.Prepared; session!.Recording=new(session);
    }
    void RefreshTimeline(bool force=false)
    {
        if (TimelineVisual == null) return;
        if (!recordedPlayback && replayCursor==null && recordedProject!=null && recordedIdentity!=null && session?.Recording!=null && (timelineReplay==null || timelineReplay.Commands.Length!=session.Recording.CommandCount) && (force || !running || timelineReplay==null || timelineRefreshClock.ElapsedMilliseconds>=250))
        { timelineReplay=EncounterReplay.Capture(recordedProject,recordedPhase,recordedIdentity,session); timelineRefreshClock.Restart(); }
        TimelineVisual.Replay=timelineReplay; TimelineVisual.CursorMs=Math.Min(session?.Ms ?? 0,timelineReplay?.DurationMs??0); TimelineVisual.InvalidateVisual();
        timelineUpdating=true; TimelineSeek.Maximum=Math.Max(5,timelineReplay?.DurationMs ?? 0); TimelineSeek.Value=session?.Ms ?? 0; timelineUpdating=false;
        if(timelineReplay!=null && timelineEventsCount!=timelineReplay.Events.Length)
        { timelineEventsCount=timelineReplay.Events.Length; TimelineEvents.ItemsSource=timelineReplay.Events.Select(ev=>new EventRow(ev)).ToArray(); }
        RefreshTimelineControls();
    }
    void RefreshTimelineControls()
    {
        if(TimelinePlayButton==null)return;
        var available=timelineReplay!=null && runtimeStarting==null;
        TimelineSeek.IsEnabled=available; TimelineSeekButton.IsEnabled=available; TimelineCancelButton.IsEnabled=runtimeStarting!=null; TimelinePlayButton.IsEnabled=available && timelineReplay!.DurationMs>0;
        TimelineSaveButton.IsEnabled=available; TimelineGoButton.IsEnabled=available && TimelineEvents.SelectedItem is EventRow;
        TimelineLiveButton.IsEnabled=available && replayCursor!=null && session!=null && replayAdvancing==null;
        BossHp.IsEnabled=runtimeStarting==null && replayAdvancing==null;
        TimelinePlayButton.Content=recordedPlayback && running ? "Ⅱ Pause recording" : "▶ Play recording";
        TimelineTime.Text=$"{(session?.Ms??0)/1000d:0.000} / {(timelineReplay?.DurationMs??0)/1000d:0.000} s";
        TimelineInfo.Text=timelineReplay==null ? "Play an encounter to record its native events." : $"Recorded seed {timelineReplay.Project.Seed} · {timelineReplay.Commands.Length} inputs / overrides · {(timelineReplay.Phase==null ? "encounter start" : "forced phase "+timelineReplay.Phase)}";
    }
    async Task SeekTimelineAsync(long ms)
    {
        RefreshTimeline(true);
        if(timelineReplay==null)throw new ArgumentException("Record or open an encounter first.");
        var tape=timelineReplay; var expectedDocument=document; var revision=document.Revision; var generation=++timelineGeneration;
        replayAdvancing?.Cancel();
        await AwaitCombatCancellation(); ClearViewportEditors(); if(pathPreviewTask!=null)await pathPreviewTask;
        if(generation!=timelineGeneration || document!=expectedDocument || document.Revision!=revision)return;
        running=false; recordedPlayback=false; replayCursor=null;
        timelineReplacing=true; try { session=null; Viewport.Session=null; } finally { timelineReplacing=false; }
        using var cancellation=new CancellationTokenSource(); runtimeStarting=cancellation; UpdatePlayButton(); RefreshTimelineControls();
        Status.Text="Replaying native encounter…";
        var progress=new Progress<long>(time=>{ if(runtimeStarting==cancellation && generation==timelineGeneration) Status.Text=$"Replaying native encounter · {time/1000d:0.000} s"; });
        try
        {
            var restored=await ReplayCursor.RestoreAsync(adapter,tape,ms,replayPrepared,progress,cancellation.Token);
            if(cancellation.IsCancellationRequested || generation!=timelineGeneration || document!=expectedDocument || document.Revision!=revision)
            { restored.Session.Dispose(); return; }
            timelineReplacing=true; try { session=restored.Session; } finally { timelineReplacing=false; }
            // Assigning a session cancels preparation; the successfully restored run is already complete.
            runtimeStarting=null; replayCursor=restored; timelineReplay=tape;
            recordedProject=tape.Project; recordedPhase=tape.Phase; recordedIdentity=tape.Identity; replayPrepared=(session as CompiledRuntime)?.Prepared;
            Viewport.Session=session; accumulator=0; clearedEvents=0; UpdateSession();
            Status.Text=$"Native replay · {ms/1000d:0.000} s · recorded inputs and overrides";
        }
        catch(OperationCanceledException) { if(generation==timelineGeneration)Status.Text="Replay cancelled"; throw; }
        finally { if(ReferenceEquals(runtimeStarting,cancellation))runtimeStarting=null; UpdatePlayButton(); RefreshTimelineControls(); }
    }
    async void SeekTimeline(object sender, RoutedEventArgs e) => await TryRuntime(async()=>await SeekTimelineAsync((long)TimelineSeek.Value));
    void CancelTimelineReplay(object sender, RoutedEventArgs e) { runtimeStarting?.Cancel(); Status.Text="Replay cancelled"; }
    void TimelineSeekChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if(!timelineUpdating && TimelineTime!=null) TimelineTime.Text=$"{e.NewValue/1000d:0.000} / {(timelineReplay?.DurationMs??0)/1000d:0.000} s · press Seek"; }
    void SelectTimelineEvent(EncounterEvent ev)
    {
        TimelineVisual.Selected=ev; TimelineVisual.InvalidateVisual();
        TimelineEventInfo.Text=ev.Message+(ev.ActorId is {} actor ? $" · actor #{actor}" : "")+(ev.NodeId==null ? " · source origin unavailable" : ev.Kind=="movement" ? " · observed phase source" : " · source origin recorded");
        if(ev.NodeId is {} id && document.Identities.Any(n=>n.Id==id)) SelectNode(id);
    }
    void TimelineEventSelected(object sender, SelectionChangedEventArgs e)
    { if(TimelineEvents.SelectedItem is EventRow row) SelectTimelineEvent(row.Event); RefreshTimelineControls(); }
    async void GoToTimelineEvent(object sender, RoutedEventArgs e) => await TryRuntime(async()=>{
        if(TimelineEvents.SelectedItem is EventRow row && timelineReplay!=null) await SeekTimelineAsync(EncounterTimeline.EventSeekMs(row.Event,timelineReplay.DurationMs));
    });
    async void PlayRecordedTimeline(object sender, RoutedEventArgs e) => await TryRuntime(async()=>{
        RefreshTimeline(true);
        if(recordedPlayback && running) { Pause(sender,e); return; }
        if(timelineReplay==null)return;
        if(replayCursor==null || session==null || session.Ms>=timelineReplay.DurationMs) await SeekTimelineAsync(0);
        if(replayCursor==null || session==null)return;
        recordedPlayback=true; running=true; clock.Restart(); UpdatePlayButton(); RefreshTimelineControls();
    });
    void ContinueLiveTimeline(object sender, RoutedEventArgs e)
    {
        if(session==null || replayCursor==null || replayAdvancing!=null)return;
        BranchTimeline();
        session.BreakpointKind=Breakpoint.SelectedIndex>0?((ComboBoxItem)Breakpoint.SelectedItem).Content.ToString():null;
        running=true; clock.Restart(); Viewport.Focus(); WorkspaceTabs.SelectedIndex=0; UpdateSession(); Status.Text="Live from replay position · future recording replaced";
    }
    void BranchTimeline()
    { if(replayCursor==null)return; if(replayAdvancing!=null)throw new InvalidOperationException("Wait for the current recorded sample before branching."); replayCursor=null; recordedPlayback=false; timelineReplay=null; timelineEventsCount=-1; }
    async Task AdvanceRecordedTimeline(double elapsedMs)
    {
        var cursor=replayCursor; var tape=timelineReplay; if(cursor==null||tape==null)return;
        var duration=PlaybackDuration(elapsedMs); if(duration<=0)return;
        var target=Math.Min(tape.DurationMs,cursor.Session.Ms+duration);
        var kind=Breakpoint.SelectedIndex>0?((ComboBoxItem)Breakpoint.SelectedItem).Content.ToString():null;
        var stop=tape.Events.FirstOrDefault(ev=>ev.Kind==kind && EncounterTimeline.EventSeekMs(ev,tape.DurationMs)>cursor.Session.Ms && EncounterTimeline.EventSeekMs(ev,tape.DurationMs)<=target);
        if(stop!=null)target=EncounterTimeline.EventSeekMs(stop,tape.DurationMs);
        using var cancellation=new CancellationTokenSource(); replayAdvancing=cancellation; RefreshTimelineControls();
        try { await cursor.AdvanceToAsync(target,cancellation:cancellation.Token); }
        finally { if(ReferenceEquals(replayAdvancing,cancellation))replayAdvancing=null; RefreshTimelineControls(); }
        if(!ReferenceEquals(cursor,replayCursor)||!ReferenceEquals(cursor.Session,session))return;
        if(session.Ms>=tape.DurationMs) { running=false; Status.Text="End of recorded encounter"; }
        else if(stop!=null) { running=false; SelectTimelineEvent(stop); Status.Text="Recorded breakpoint · "+stop.Kind; }
        UpdateSession();
    }
    void SaveTimelineReplay(object sender, RoutedEventArgs e)=>Try(()=>{
        RefreshTimeline(true); if(timelineReplay==null)return;
        var dialog=new SaveFileDialog{Filter="Realm encounter recording|*.rbsreplay",FileName="encounter.rbsreplay"};
        if(dialog.ShowDialog()==true)timelineReplay.Save(dialog.FileName);
    });
    async void OpenTimelineReplay(object sender, RoutedEventArgs e)=>await TryRuntime(async()=>{
        var dialog=new OpenFileDialog{Filter="Realm encounter recording|*.rbsreplay"}; if(dialog.ShowDialog()!=true)return;
        await LoadTimelineReplayAsync(EncounterReplay.Load(dialog.FileName));
    });
    async Task LoadTimelineReplayAsync(EncounterReplay replay)
    {
        var expected=document; var revision=document.Revision;var expectedScenario=scenarioRevision;
        replay.Validate(); var current=await Task.Run(()=>EncounterReplay.Identify(adapter,replay.Project,replay.Identity.CompiledBuildId));
        if(document!=expected || document.Revision!=revision || scenarioRevision!=expectedScenario)throw new InvalidOperationException("The document or arena changed while opening the recording. Open it again.");
        if(current!=replay.Identity)throw new InvalidOperationException("Recording source, resources or runtime changed. Record a fresh run.");
        ApplyProject(replay.Project,null); timelineReplay=replay; recordedProject=replay.Project; recordedPhase=replay.Phase; recordedIdentity=replay.Identity;
        timelineEventsCount=-1; await SeekTimelineAsync(0); WorkspaceTabs.SelectedIndex=5;
    }
}
