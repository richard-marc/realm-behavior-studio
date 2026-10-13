using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void BoundedFollowSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/bounded-follow-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunBoundedFollowUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunBoundedFollowUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var supplied=File.ReadAllText(primary);
        foreach(var kind in new[]{"FollowDistance","LockedFollowDistance"})
        {
            var source="using TKR.WorldServer.logic.behaviors; namespace TKR.WorldServer.logic { public partial class BehaviorDb { private _ StudioBoundedFollowDesktop=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"segment\",new "+kind+"(1,10,2,stopRange:1))); } }";
            ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[],LockedNodes=[],CollapsedNodes=[],Layout=[],Player=previous.Player with{Invulnerable=true}},null);
            var node=EncounterRoot.Children.Single();SelectNode(node.Id);
            check(movementEditorButton?.IsEnabled==true&&Viewport.Movement==null,kind+" offers an opt-in editor without adding default viewport noise");if(!movementEditing)movementEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach(var (handle,value,parameter) in new[]{(MovementHandle.Travel,3.25,"distance"),(MovementHandle.Range,2.5,"stopRange"),(MovementHandle.Acquire,12d,"acquireRange")})
            {
                var before=document.Source;var revision=document.Revision;
                check(Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(handle)),kind+" actual "+parameter+" handle is selectable");Viewport.MoveMovementDrag(Viewport.MovementPoint(handle,value));
                check(document.Source==before&&document.Revision==revision,kind+" "+parameter+" gesture previews without mutating source");Viewport.EndMovementDrag();
                check(document.Revision==revision+1&&Math.Abs(double.Parse(document.Find(node.Id).Parameters.Single(p=>p.Name==parameter).Expression,System.Globalization.CultureInfo.InvariantCulture)-value)<1e-6,kind+" "+parameter+" release commits one native input");
                Undo(this,new());check(document.Source==before&&document.Find(node.Id).Id==node.Id,kind+" "+parameter+" Undo restores source and stable identity");Redo(this,new());SelectNode(node.Id);if(!movementEditing)movementEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            var unchanged=document.Source;Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Travel));Viewport.MoveMovementDrag(Viewport.MovementPoint(MovementHandle.Travel,5));Viewport.CancelMovementDrag();
            check(document.Source==unchanged&&Viewport.Movement!.Pattern.TravelDistance==3.25,kind+" cancelled travel gesture restores geometry and source");
            if(kind=="LockedFollowDistance")
            {
                Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Range));Viewport.MoveMovementDrag(Viewport.MovementPoint(MovementHandle.Range,20));Viewport.EndMovementDrag();
                var pattern=adapter.ReadMovementPattern(document,document.Find(node.Id))!;check(pattern.Range<pattern.AcquireRange,"Locked stop handle preserves the native strict acquisition relationship");Undo(this,new());SelectNode(node.Id);if(!movementEditing)movementEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Acquire));Viewport.MoveMovementDrag(Viewport.MovementPoint(MovementHandle.Acquire,0));Viewport.EndMovementDrag();
                pattern=adapter.ReadMovementPattern(document,document.Find(node.Id))!;check(pattern.AcquireRange>pattern.Range,"Locked acquisition handle cannot collapse through the native stopping radius");Undo(this,new());SelectNode(node.Id);if(!movementEditing)movementEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;check(!Viewport.BeginMovementDrag(Viewport.MovementHandlePoint(MovementHandle.Travel)),kind+" source locks protect the cumulative travel handle");document.LockedNodes.Clear();SelectNode(node.Id);if(!movementEditing)movementEditorButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var rejected=false;try{CommitMovementEdit(Viewport.Movement! with{Revision=document.Revision-1},"distance",4);}catch(InvalidOperationException){rejected=true;}check(rejected&&document.Source==unchanged,kind+" stale travel edit rejects atomically");
            UpdateLayout();CaptureView(this,Path.Combine(output,kind+"-editor.png"));var saved=Path.Combine(output,kind+".rbs");BehaviorDocument.Save(saved,Snapshot());ApplyProject(BehaviorDocument.Load(saved),saved);
            check(adapter.ReadMovementPattern(document,document.Find(node.Id)) is{TravelDistance:3.25,Range:2.5,AcquireRange:12},kind+" reopening keeps all three native bounds and identity");
            check(await StartSessionAsync(),kind+" Play executes accepted source in the native worker");running=false;await session!.AdvanceAsync(new(),1000);UpdateSession();check(Math.Abs(session.Entities.Single().X-67.75)<1e-5,kind+" native playback stops at the edited cumulative travel limit");Stop(this,new());
        }
        check(File.ReadAllText(primary)==supplied,"Bounded follow controls, history and playback leave original game source unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
