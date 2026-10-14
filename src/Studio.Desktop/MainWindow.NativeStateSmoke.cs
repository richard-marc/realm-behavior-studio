using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    async void NativeStateSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/native-state-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunNativeStateUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunNativeStateUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=File.ReadAllText(file);
        const string source="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopStoredState=()=>Behav().Init("Cave Pirate Brawler",new State("loop",new Timed(250,new Shoot(20,count:2,coolDown:1000),new Shoot(20,count:3,coolDown:1000)),new StudioDesktopCounter(1)));
    sealed class StudioDesktopCounter:Behavior {
        readonly int step;public StudioDesktopCounter(int step)=>this.step=step;
        sealed class CounterState {public int Count;public bool Enabled=true;public int Danger=>throw new System.Exception("Do not execute this getter.");}
        protected override void TickCore(Entity host,TickTime time,ref object state){var value=state as CounterState??new CounterState();value.Count+=host.Y>65?step*2:step;state=value;}
    }
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Arena=new(){Actors=[new("Cave Pirate Brawler",64.5,67.5)]},Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;
        var timed=document.AllNodes.Single(n=>n.Kind=="Timed");var counter=document.AllNodes.Single(n=>n.Kind.EndsWith("StudioDesktopCounter"));SelectNode(timed.Id);
        check(nativeStatePanel is{IsExpanded:false}&&nativeStatePlay is{IsEnabled:true}&&Inspector.Children.OfType<Expander>().Any(e=>Equals(e.Header,"Native cycle")),"Native loop and stored-state controls stay collapsed with explicit compiled Play");
        var cycle=Inspector.Children.OfType<Expander>().Single(e=>Equals(e.Header,"Native cycle"));var cycleBody=(StackPanel)cycle.Content;check(cycleBody.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("once per child"))&&cycleBody.Children.OfType<Button>().Count()==2,"Timed inspector explains actual per-child counting and links native child inputs");
        Inspector.Children.OfType<TextBox>().Single(t=>t.Text=="250").Text="350";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(adapter.ReadLoop(document,document.Find(timed.Id))!.Period==350,"Actual typed period Apply edits the native loop constructor");Undo(this,new());check(adapter.ReadLoop(document,document.Find(timed.Id))!.Period==250,"One Undo restores original native loop period");Redo(this,new());SelectNode(timed.Id);
        nativeStatePlay!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var watch=System.Diagnostics.Stopwatch.StartNew();while((session is not CompiledRuntime||runtimeStarting!=null||nativeStatePlay?.IsEnabled==false)&&watch.Elapsed.TotalSeconds<120)await Task.Delay(50);check(session is CompiledRuntime&&runtimeStarting==null,"Actual live-state Play starts compiled native source observation");running=false;await session!.AdvanceAsync(new(),100);Pause(this,new());UpdateSession();nativeStatePanel!.IsExpanded=true;
        var loopRows=nativeStateRows!.Children.OfType<Button>().Where(b=>b.Tag is NativeStateSlot s&&s.Owner.EndsWith(".Timed")).ToArray();check(loopRows.Length==2&&loopRows.All(b=>((NativeStateSlot)b.Tag).Meaning.Contains("milliseconds")),"Live loop region shows separate actor counters and original units alongside its nested child state");
        var originalSession=session;var row=loopRows.First();var slot=(NativeStateSlot)row.Tag;row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(selectedNode?.Id==timed.Id&&ReferenceEquals(session,originalSession),"Live-state source navigation selects the exact loop without discarding playback");
        check(!NavigateRuntimeSource(new(session.Ms,"state","stale",PhaseOrigin:slot.Origin! with{SourceHash=new('0',64)}))&&ReferenceEquals(session,originalSession),"Changed live-state source fingerprints reject stale navigation");
        SelectNode(counter.Id);nativeStatePanel!.IsExpanded=true;check(nativeStateRows!.Children.OfType<Button>().Count()==2&&nativeStateRows.Children.OfType<Button>().All(b=>b.Tag is NativeStateSlot s&&s.Fields.Any(f=>f.Name=="Count")&&!s.Fields.Any(f=>f.Name=="Danger")),"Custom counter inspector shows actual actor fields without running a throwing getter");
        nativeStatePanel.IsExpanded=false;check(nativeStateRows.Children.Count==0,"Collapsing live state removes row work from the inspector");nativeStatePanel.IsExpanded=true;Status.Text="Paused · observed native state";UpdateLayout();CaptureView(this,Path.Combine(output,"native-state-editor.png"));
        var trace=Path.Combine(output,"native-state-trace.json");session.ExportTrace(trace);check(File.ReadAllText(trace).Contains("nativeState"),"Desktop trace retains current actual stored-state observations");var beforeMs=session.Ms;session.RemoveMinionsForTest();UpdateSession();check(session.Ms==beforeMs&&nativeStateRows.Children.OfType<Button>().Count()==1,"Paused actor removal refreshes live state without requiring another tick");Stop(this,new());var path=Path.Combine(output,"native-state.rbs");var ids=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);check(document.Identities.SequenceEqual(ids)&&adapter.ReadLoop(document,document.Find(timed.Id))!.Period==350,"Loop edits and source identities survive desktop save/reopen");
        check(File.ReadAllText(file)==supplied,"Native loop editing, live state, navigation and Play preserve supplied game source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
