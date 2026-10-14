using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void OrderedControllerSmoke()=>await TryRuntime(async()=>
    {
        var output=System.IO.Path.GetFullPath(".artifacts/ordered-controller-desktop");System.IO.Directory.CreateDirectory(output);var checks=new List<string>();
        await RunOrderedControllerUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunOrderedControllerUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=System.IO.File.ReadAllText(file);
        const string source="using TKR.WorldServer.logic.behaviors;using TKR.WorldServer.logic.behaviors.@new;using TKR.WorldServer.logic.behaviors.@new.movements;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioDesktopOrdered=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"motion\",new OrderedBehavior(new NewFollow(speed:1,range:1),new NewWander(0)),new NewCharge()));}}";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true,StartX=70.5,StartY=64.5},Arena=new()},null);WorkspaceTabs.SelectedIndex=1;
        var wrapper=document.AllNodes.Single(n=>n.Kind=="OrderedBehavior");var follow=document.AllNodes.Single(n=>n.Kind=="NewFollow");var wander=document.AllNodes.Single(n=>n.Kind=="NewWander");SelectNode(wrapper.Id);
        check(orderedPanel is{IsExpanded:false}&&orderedRows.Count==2&&orderedSummary!.Text.Contains("first true"),"Ordered dispatch is a collapsed, source-ordered inspector section");orderedRows[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(selectedNode?.Id==follow.Id&&orderedSummary!.Text.Contains("state survives"),"Actual dispatch row selects the native child and explains unforwarded exit");
        downStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(adapter.OrderedChildren(document,document.Find(wrapper.Id))![0].Node.Id==wander.Id,"Actual Down changes ordered execution priority");Undo(this,new());check(adapter.OrderedChildren(document,document.Find(wrapper.Id))![0].Node.Id==follow.Id,"One editor Undo restores original dispatch priority");Redo(this,new());check(adapter.OrderedChildren(document,document.Find(wrapper.Id))![0].Node.Id==wander.Id,"One editor Redo restores the priority edit");Undo(this,new());
        document.LockedNodes.Add(wrapper.Id);SelectNode(follow.Id);check(downStructureButton?.IsEnabled==false,"Ordered parent locks disable child priority edits");document.LockedNodes.Clear();
        SelectNode(document.AllNodes.Single(n=>n.Kind=="NewCharge").Id);check(orderedSummary!.Text.Contains("direct phase action")&&adapter.Validate(document,selectedEntity!.Id).Any(d=>d.Message.Contains("direct phase action")),"A directly placed ordered movement exposes its inactive callback boundary");
        SelectNode(EncounterRoot.Id);BeginNativeCreation(adapter.Primitives.Single(p=>p.Name=="NewFollow"));check(createNativeButton!.IsEnabled&&constructorInputs.ContainsKey("duration")&&constructorInputs.ContainsKey("predictive"),"Native movement creation offers actual optional constructor fields");SelectNode(wrapper.Id);
        var path=System.IO.Path.Combine(output,"ordered-controller.rbs");var identities=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);check(document.Identities.SequenceEqual(identities),"Ordered priority and every identity survive save/reopen");
        check(await StartSessionAsync(),"Native Play starts the authored ordered controller");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();check(session.Movements.Any(m=>!m.Player&&Math.Abs(m.ToX-65)<.0001),"Native Play executes the original ordered follow step");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(wrapper.Id);orderedPanel!.IsExpanded=true;UpdateLayout();CaptureView(this,System.IO.Path.Combine(output,"ordered-dispatch-editor.png"));
        check(System.IO.File.ReadAllText(file)==supplied,"Ordered controls and playback preserve supplied game source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
