using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void MemberInitializerSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/member-initializer-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunMemberInitializerUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunMemberInitializerUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var supplied=File.ReadAllText(primary);
        const string source="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopMembers=()=>Behav().Init("Cave Pirate Brawler",new State(new State("loop",new Shoot(20,count:2,fixedAngle:0,coolDown:100)) { Behaviors = { new Shoot(20,count:3,fixedAngle:0,coolDown:100),/* member UI note */new Shoot(20,count:4,fixedAngle:0,coolDown:100) }, Transitions = { new TimedTransition(200,"finish") } },new State("finish")));
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;
        var phase=EncounterRoot.Children.First();var constructor=phase.Children.Single(n=>n.Argument?.Name=="children");var wrapper=phase.Children.Single(n=>n.IsMemberInitializer);var behaviors=wrapper.Children.Single(n=>n.Argument?.Name=="Behaviors");var transitions=wrapper.Children.Single(n=>n.Argument?.Name=="Transitions");var first=behaviors.Children[0];SelectNode(behaviors.Id);
        check(chooseChildSlotButton?.IsEnabled==true&&Inspector.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("Behavior")),"A native behavior member list exposes its actual typed Add child controls");
        SelectNode(first.Id);Inspector.Children.OfType<TextBox>().Single(t=>t.Text=="3").Text="5";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(first.Id).Parameters.Any(p=>p.Name=="count"&&p.Expression=="5")&&document.Find(constructor.Id).Children.Single().Parameters.Any(p=>p.Name=="count"&&p.Expression=="2"),"Typed parameter edits change the member action while preserving constructor input and identity");
        duplicateStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var copy=selectedNode!.Id;
        check(copy!=first.Id&&document.Find(behaviors.Id).Children.Count==3&&document.Source.Contains("/* member UI note */"),"Actual member Duplicate creates a new native action identity and retains initializer source comments");
        ApplyNodeDrop(new NodeDrag(document.Revision,selectedEntity.Id,[copy]),constructor.Id,1,false);
        check(document.Find(constructor.Id).Children.Last().Id==copy&&document.Find(behaviors.Id).Children.Count==2&&selectedNode?.Id==copy,"Routed desktop drag moves a member action into constructor inputs with its source identity intact");
        Undo(this,new());check(document.Find(behaviors.Id).Children.Any(n=>n.Id==copy)&&document.Find(constructor.Id).Children.Count==1,"One Undo restores both native input lists after cross-list movement");Redo(this,new());
        SelectNode(wrapper.Id);Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;SelectNode(first.Id);
        check(parameterApplyButton?.IsEnabled==false&&deleteStructureButton?.IsEnabled==false,"A wrapper region lock protects every nested member action control");document.LockedNodes.Remove(wrapper.Id);SelectNode(first.Id);
        check(adapter.PhaseActions(document,phase.Id).Count(a=>a.Node.Kind=="Shoot")==4,"Lifecycle views include actual constructor and member actions under the same native State");
        var finish=EncounterRoot.Children.Last();SelectNode(finish.Id);phaseNameInput!.Text="done";renamePhaseButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(transitions.Id).Children.Single().Parameters.Any(p=>p.Name=="targetState"&&p.Expression=="\"done\""),"Actual phase Rename reconciles the transition stored in the member initializer");
        var path=Path.Combine(output,"member-initializer.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(document.Find(wrapper.Id).IsMemberInitializer&&document.Find(first.Id).Parameters.Any(p=>p.Expression=="5")&&document.Find(copy).Id==copy,"Save/reopen restores member ownership, edits and constructor-moved identity");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Play selects native compiled execution for the actual member initializer code");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();
        check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{2,5,5,4}),"Native playback executes constructor inputs followed by member additions in their edited order");await session.AdvanceAsync(new(),200);UpdateSession();check(session.Entities.Single().Phase=="done","Native member transition retains its timer and renamed target");Stop(this,new());
        WorkspaceTabs.SelectedIndex=1;SelectNode(behaviors.Id);UpdateLayout();CaptureView(this,Path.Combine(output,"member-initializer-editor.png"));
        check(File.ReadAllText(primary)==supplied,"Member initializer authoring, history, rename, persistence and Play leave supplied source unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
