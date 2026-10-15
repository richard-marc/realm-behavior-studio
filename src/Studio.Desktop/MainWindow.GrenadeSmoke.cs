using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void GrenadeSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/grenade-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunGrenadeUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunGrenadeUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=File.ReadAllText(file);
        const string source="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioDesktopGrenade=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"grenades\",new Grenade(2,50,range:6,coolDown:10000)));}}";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Arena=new(),Player=previous.Player with{Invulnerable=true,StartX=70.5,StartY=64.5}},null);
        WorkspaceTabs.SelectedIndex=1;var node=document.AllNodes.Single(n=>n.Kind=="Grenade");SelectNode(node.Id);
        check(grenadePanel is{IsExpanded:false}&&grenadeTargeting is{SelectedIndex:0}&&projectilePicker==null,"Grenade details start collapsed with aimed targeting and no invented projectile picker");
        grenadePanel!.IsExpanded=true;grenadeTargeting!.SelectedIndex=1;
        check(adapter.ReadGrenade(document,document.Find(node.Id)) is{FixedAngle:0}&&document.Find(node.Id).Id==node.Id,"Actual target-mode choice writes the original optional fixed bearing and retains node identity");
        Undo(this,new());check(document.Source==source,"One Undo restores the original aimed grenade constructor");Redo(this,new());
        var optional=(StackPanel)Inspector.Children.OfType<Expander>().Single(e=>Equals(e.Header,"More supported parameters")).Content;
        var countLabel=optional.Children.OfType<TextBlock>().Single(t=>t.Text.StartsWith("count ·"));((TextBox)optional.Children[optional.Children.IndexOf(countLabel)+1]).Text="7";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(adapter.ReadGrenade(document,document.Find(node.Id)) is{Count:7,NativeSpacing:51},"Typed count Apply retains the native integer default spacing for seven separate throws");
        grenadePanel!.IsExpanded=true;UpdateLayout();var diagram=((StackPanel)grenadePanel.Content).Children.OfType<Canvas>().Single();
        check(diagram.Children.OfType<Image>().Single().Source!=null&&diagram.Children.OfType<System.Windows.Shapes.Line>().Count()==7,"Fixed landing preview uses actual host sprite art and every authored landing point");
        CaptureView(this,Path.Combine(output,"grenade-editor.png"));
        Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;check(grenadeTargeting is{IsEnabled:false},"Region lock disables the grenade targeting chooser");document.LockedNodes.Clear();SelectNode(node.Id);
        var accepted=Snapshot();check(await StartSessionAsync(observeCallbacks:true)&&session is CompiledRuntime,"Desktop grenade source compiles and loads the original native controller");running=false;
        await session!.AdvanceAsync(new(),1000);await session.AdvanceAsync(new(),600);UpdateSession();Pause(this,new());
        var throws=session.Effects.Where(e=>e.Area==null&&e.Kind==4).ToArray();var impacts=session.Effects.Where(e=>e.Area!=null).ToArray();
        check(throws.Length==7&&impacts.Length==7&&session.Shots.Count==0,"Native desktop playback emits seven throw effects and seven actual delayed area packets");
        check(impacts.Select(e=>(e.X,e.Y)).SequenceEqual(throws.Reverse().Select(e=>(e.X,e.Y))),"Native impact order retains the game's reverse timer iteration for simultaneous throws");
        WorkspaceTabs.SelectedIndex=0;UpdateLayout();CaptureView(this,Path.Combine(output,"grenade-native-impact.png"));
        check(Viewport.NativeTelegraphCount==56,"Viewport projects the received native area particles with the original eight-point layout per blast");
        var impact=session.Events.First(e=>e.Kind=="area");SelectTimelineEvent(impact);
        File.WriteAllText(Path.Combine(output,"timer-navigation-evidence.json"),JsonSerializer.Serialize(new{impact,nodeId=node.Id,selectedId=selectedNode?.Id,text=TimelineEventInfo.Text,status=Status.Text},BehaviorDocument.JsonOptions));
        check(impact.PacketOrigin?.Timer?.SchedulingSource?.NodeId==node.Id&&TimelineEventInfo.Text.Contains("scheduled by Grenade")&&selectedNode?.Id==node.Id,"Delayed impact selection opens the recorded scheduling Grenade while labeling its separate timer dispatch");
        var launch=session.Events.First(e=>e.Kind=="effect");SelectTimelineEvent(launch);
        check(selectedNode?.Id==node.Id&&TimelineEventInfo.Text.Contains("emitted by Grenade"),"Launch timeline selection navigates its genuine compiled Grenade construction");
        var trace=Path.Combine(output,"grenade-trace.json");session.ExportTrace(trace);check(File.ReadAllText(trace).Contains("\"Area\""),"Desktop trace export retains the full discriminated native area payload");Stop(this,new());
        var saved=Path.Combine(output,"grenade.rbs");BehaviorDocument.Save(saved,accepted);ApplyProject(BehaviorDocument.Load(saved),saved);
        check(document.Source==accepted.Source&&document.Identities.SequenceEqual(accepted.Identities),"Grenade targeting/geometry and all source identities survive save/reopen");
        SelectNode(EncounterRoot.Id);BeginNativeCreation(adapter.Primitives.Single(p=>p.Name=="Grenade"));
        constructorInputs["radius"].Text="2";constructorInputs["damage"].Text="35";createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(selectedNode?.Kind=="Grenade"&&adapter.ReadGrenade(document,selectedNode) is{Radius:2,Damage:35,FixedAngle:null,Count:1},"Actual native creation uses required radius/damage and the game's original optional defaults");
        Undo(this,new());check(document.Source==accepted.Source,"One Undo removes a newly created native grenade without altering the existing attack");
        check(File.ReadAllText(file)==supplied,"Desktop grenade authoring, timer playback and traces preserve supplied game source");
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
