using System.IO;
using System.Windows;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void WorkspaceGroupSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/workspace-group-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunWorkspaceGroupUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunWorkspaceGroupUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var helper=catalog.First(e=>e.SourceFile!=primary).SourceFile;
        var supplied=File.ReadAllText(primary);var suppliedHelper=File.ReadAllText(helper);
        const string source="""
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopGroup=()=>Behav().Init("Cave Pirate Brawler",StudioDesktopGroupCaller()).Init("Pirate Lieutenant",StudioDesktopGroupCaller());
    static State StudioDesktopGroupCaller()=>new State(StudioDesktopGroupShared(),new State("phase-1"),new State("phase-1-2"));
} }
""";
        const string shared="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioDesktopGroupShared()=>new State(new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100),new TKR.WorldServer.logic.behaviors.Shoot(20,count:5,fixedAngle:0,coolDown:100),new State("keep"));
} }
""";
        ApplyProject(previous with{SourceDrafts=[],SelectedSourceFile=null,Source=source,Identities=[],LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[new(helper,suppliedHelper+shared)],Player=previous.Player with{Invulnerable=true}},null);
        var foreign=document.AllNodes.SelectMany(document.ExternalDefinitions).First(r=>r.Label=="StudioDesktopGroupShared");EditExternalDefinition(foreign);
        var root=AuthoringRoot.Id;var actions=AuthoringRoot.Children.Take(2).Select(n=>n.Id).ToArray();var keep=AuthoringRoot.Children.Last().Id;CollapsedNodes.Add(keep);
        nodeSelection.Clear();nodeSelection.UnionWith(actions);SelectNode(actions[0]);nodeSelection.UnionWith(actions);
        check(AvailableCommands().Single(c=>c.Title=="Group selection into phase").Enabled,"Shared action selection exposes the actual grouping command");
        var before=Snapshot();PrimaryDocument.LockedNodes.Add(EncounterRoot.Id);var blocked=false;try{GroupWorkspacePhases(actions,"attacks");}catch(InvalidOperationException){blocked=true;}
        check(blocked&&workspacePhaseUndo.Count==0&&document.Source==suppliedHelper+shared,"A locked external caller blocks shared grouping without history changes");PrimaryDocument.LockedNodes.Clear();
        SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(s=>s.Path==helper);SourceEditor.Text+="\n// protected grouping draft";var draft=SourceEditor.Text;blocked=false;
        try{GroupWorkspacePhases(actions,"attacks");}catch(InvalidOperationException){blocked=true;}
        check(blocked&&SourceEditor.Text==draft&&document.Source==suppliedHelper+shared,"Grouping preserves an affected unapplied source buffer");DiscardSourceDraft(this,new());
        GroupSelectedNodes();var group=selectedNode!;var grouped=Snapshot();
        check(group.Kind=="State"&&document.StateName(group.Id)=="phase-1-3"&&group.Children.Select(n=>n.Id).SequenceEqual(actions),"Desktop grouping avoids external caller name collisions and preserves actual action identities and order");
        check(AuthoringRoot.Id==root&&document.Find(keep).Id==keep&&CollapsedNodes.Contains(keep)&&grouped.Source==source,"Shared grouping retains the declaring body, sibling metadata and original caller source");
        check(workspacePhaseUndo.Count==1&&workspacePhaseUndo.Peek().Documents.Count==1,"Grouping joins coordinated workspace history as one source transaction");
        Undo(this,new());check(document.Source==before.AdditionalSources.Single(s=>s.SourceFile==helper).Source&&actions.All(id=>document.AllNodes.Any(n=>n.Id==id))&&CollapsedNodes.Contains(keep),"One desktop Undo restores shared action positions and view metadata");
        Redo(this,new());check(document.AllNodes.Any(n=>n.Id==group.Id)&&document.Find(group.Id).Children.Select(n=>n.Id).SequenceEqual(actions),"One desktop Redo restores the exact new phase and retained child identities");
        var path=Path.Combine(output,"workspace-group.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(encounterDocument!=null&&AuthoringRoot.Id==root&&document.AllNodes.Any(n=>n.Id==group.Id)&&CollapsedNodes.Contains(keep),"Save and reopen restore the grouped external body and its hierarchy metadata");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Play compiles the complete shared grouping workspace");running=false;await session!.AdvanceAsync(new(),300);UpdateSession();
        check(session.Entities.Single().Phase=="phase-1-3"&&session.Shots.Count>=2&&session.Shots.Select((shot,index)=>shot.Count==(index%2==0?3:5)).All(v=>v),"Native desktop playback enters the group and emits ordered three/five-projectile volleys");Stop(this,new());
        WorkspaceTabs.SelectedIndex=1;SelectNode(group.Id);UpdateLayout();CaptureView(this,Path.Combine(output,"workspace-group-editor.png"));
        check(File.ReadAllText(primary)==supplied&&File.ReadAllText(helper)==suppliedHelper,"Shared grouping, history, persistence and Play leave supplied game files unchanged");
        ApplyProject(previous with{SourceDrafts=[],SelectedSourceFile=null,Source=source+shared,Identities=[],LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[]},null);
        EditDefinition(document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopGroupShared").EntityId);var oneActions=AuthoringRoot.Children.Take(2).Select(n=>n.Id).ToArray();nodeSelection.Clear();nodeSelection.UnionWith(oneActions);
        GroupSelectedNodes();var oneGroup=selectedNode!;
        check(document.StateName(oneGroup.Id)=="phase-1-3"&&oneGroup.Children.Select(n=>n.Id).SequenceEqual(oneActions)&&Snapshot().AdditionalSources.Count==0,"Same-file helper grouping uses every caller's names without inventing dependencies");
        Undo(this,new());check(document.Source==source+shared&&oneActions.All(id=>AuthoringRoot.Children.Any(n=>n.Id==id)),"Same-file coordinated grouping restores its exact original body with one Undo");
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
