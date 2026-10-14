using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    async void NativeMemberSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/native-member-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunNativeMemberUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunNativeMemberUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=File.ReadAllText(file);
        const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopMemberInputs=()=>Behav().Init("Cave Pirate Brawler",new State("loop",new StudioDesktopMembers {
        Primary = /* member note */new Shoot(20,count:2,fixedAngle:0,coolDown:100),
        Actions = { new Shoot(20,count:4,fixedAngle:0,coolDown:100) },
        Assigned = new System.Collections.Generic.List<CycleBehavior>(3)
    }));
    sealed class StudioDesktopMembers : CycleBehavior {
        public CycleBehavior Primary;
        public System.Collections.Generic.List<CycleBehavior> Actions {get;}=new(){new Shoot(20,count:8,fixedAngle:0,coolDown:100)};
        public System.Collections.Generic.List<CycleBehavior> Assigned;
        protected override void TickCore(TKR.WorldServer.core.objects.Entity host,TKR.WorldServer.core.worlds.TickTime time,ref object state){Primary?.Tick(host,time);foreach(var b in Actions)b.Tick(host,time);foreach(var b in Assigned)b.Tick(host,time);}
    }
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;
        var scalar=document.AllNodes.Single(n=>n.Argument is{Name:"Primary",IsMember:true});var list=document.AllNodes.Single(n=>n.Argument is{Name:"Actions",IsMember:true});var assigned=document.AllNodes.Single(n=>n.Argument is{Name:"Assigned",IsMember:true});SelectNode(scalar.Id);
        check(chooseChildSlotButton?.IsEnabled==true&&Equals(chooseChildSlotButton.Content,"Replace child")&&Inspector.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("assigned to null")),"Scalar native member shows one replacement control and explicit clear semantics");
        var child=scalar.Children.Single();SelectNode(child.Id);Inspector.Children.OfType<TextBox>().Single(t=>t.Text=="2").Text="7";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(child.Id).Parameters.Any(p=>p.Name=="count"&&p.Expression=="7")&&document.Source.Contains("/* member note */"),"Typed member child edits preserve assignment comments and identity");
        deleteStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(document.Find(scalar.Id).Children.Count==0&&document.Source.Contains("Primary = /* member note */null"),"Actual Delete clears the scalar field through an explicit null assignment");
        Undo(this,new());check(document.Find(scalar.Id).Children.Single().Id==child.Id,"One Undo restores the scalar child and its identity");
        var copy=document.CopyNodes([document.Find(child.Id).Id]);var created=document.PasteNodes(assigned.Id,0,copy,d=>adapter.Validate(d,selectedEntity!.Id)).Single();Changed();SelectNode(assigned.Id);
        check(document.Expression(document.Find(assigned.Id)).Contains("(3)")&&document.Find(assigned.Id).Children.Single().Id==created,"Member collection insertion preserves the original constructor arguments");
        SelectNode(list.Id);check(chooseChildSlotButton?.IsEnabled==true&&Equals(chooseChildSlotButton.Content,"+ Add child"),"Getter-filled native member collection offers typed child insertion");
        var wrapper=document.Parent(scalar.Id)!;document.LockedNodes.Add(wrapper.Id);SelectNode(child.Id);check(parameterApplyButton?.IsEnabled==false&&deleteStructureButton?.IsEnabled==false,"Member wrapper locks disable nested scalar controls");document.LockedNodes.Clear();
        var path=Path.Combine(output,"native-members.rbs");var ids=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);check(document.Identities.SequenceEqual(ids),"Scalar and collection member ownership survives desktop save/reopen");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Native member Play starts actual compiled source");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{7,8,4,7}),"Play retains original getter content and edited member field/list order");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(scalar.Id);UpdateLayout();CaptureView(this,Path.Combine(output,"native-member-editor.png"));
        check(File.ReadAllText(file)==supplied,"Member desktop editing and Play preserve supplied source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
