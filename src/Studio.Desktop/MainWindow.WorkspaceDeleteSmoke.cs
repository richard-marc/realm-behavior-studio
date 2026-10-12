using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void WorkspaceDeleteSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/workspace-delete-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunWorkspaceDeleteUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunWorkspaceDeleteUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var other=catalog.First(e=>e.SourceFile!=primary).SourceFile;
        var third=catalog.First(e=>e.SourceFile!=primary&&e.SourceFile!=other).SourceFile;
        var supplied=File.ReadAllText(primary);var suppliedOther=File.ReadAllText(other);var suppliedThird=File.ReadAllText(third);
        const string source="""
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopDelete=()=>Behav().Init("Cave Pirate Brawler",StudioDesktopDeleteCaller()).Init("Pirate Lieutenant",StudioDesktopDeleteCaller());
    static State StudioDesktopDeleteCaller()=>new State(new State("entry",new TimedTransition(100,"loop")),StudioDesktopDeleteShared(),new State("finish"),new State("choice",new TimedRandomTransition(100,false,new[]{"loop",/* repeated */"loop","finish"})),new State("nested-choice",new TimedTransition(100,"nested")));
} }
""";
        const string helper="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioDesktopDeleteShared()=>new State(new State("loop",new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100),new State("nested")),new State("keep",new TKR.WorldServer.logic.behaviors.Shoot(20,count:5,fixedAngle:0,coolDown:100)));
} }
""";
        const string orders="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static Behavior[] StudioDesktopDeleteOrders()=>[new TKR.WorldServer.logic.behaviors.Order(10,"Cave Pirate Brawler","loop"),new TKR.WorldServer.logic.behaviors.OrderOnDeath(10,"Pirate Lieutenant","nested"),new TKR.WorldServer.logic.behaviors.Order(10,"Cave Pirate Captain","loop")];
} }
""";
        ApplyProject(previous with{SourceDrafts=[],SelectedSourceFile=null,Source=source,Identities=[],LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[new(other,suppliedOther+helper),new(third,suppliedThird+orders)],Player=previous.Player with{Invulnerable=true}},null);
        EditDefinition(document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopDeleteCaller").EntityId);SelectNode(AuthoringRoot.Children.Single(n=>n.Kind=="Factory").Id);
        Inspector.Children.OfType<StackPanel>().SelectMany(p=>p.Children.OfType<Button>()).Single(b=>Equals(b.Content,"Edit StudioDesktopDeleteShared")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var root=AuthoringRoot.Id;var branch=AuthoringRoot.Children.Single(n=>document.StateName(n.Id)=="loop");var keep=AuthoringRoot.Children.Single(n=>document.StateName(n.Id)=="keep");
        CollapsedNodes.Add(branch.Id);var originalIds=document.Identities;SelectNode(branch.Id);
        check(deleteStructureButton!.IsEnabled&&encounterDocument!=null,"A shared child phase exposes the real Delete control in its actual source document");
        PrimaryDocument.LockedNodes.Add(EncounterRoot.Id);var blocked=false;try{DeleteWorkspacePhase(branch.Id);}catch(InvalidOperationException){blocked=true;}
        check(blocked&&document.AllNodes.Any(n=>n.Id==branch.Id)&&workspacePhaseUndo.Count==0,"Caller locks reject all coordinated deletion files before history changes");PrimaryDocument.LockedNodes.Clear();
        SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(s=>s.Path==other);SourceEditor.Text+="\n// protected deletion draft";var draft=SourceEditor.Text;blocked=false;
        try{DeleteWorkspacePhase(branch.Id);}catch(InvalidOperationException){blocked=true;}
        check(blocked&&SourceEditor.Text==draft&&document.AllNodes.Any(n=>n.Id==branch.Id),"Shared deletion preserves an affected unapplied source draft");DiscardSourceDraft(this,new());WorkspaceTabs.SelectedIndex=1;
        var before=Snapshot();SelectNode(branch.Id);deleteStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var deleted=Snapshot();
        check(!document.AllNodes.Any(n=>n.Id==branch.Id)&&!deleted.Source.Contains("TimedRandomTransition")&&!deleted.Source.Contains("TimedTransition(100,\"loop\")")&&!deleted.Source.Contains("TimedTransition(100,\"nested\")"),"Real Delete removes the shared nested branch and known caller transitions in one workspace edit");
        check(deleted.AdditionalSources.Single(s=>s.SourceFile==third).Source.Contains("\"Cave Pirate Captain\",\"loop\"")&&!deleted.AdditionalSources.Single(s=>s.SourceFile==third).Source.Contains("\"Cave Pirate Brawler\",\"loop\"")&&workspacePhaseUndo.Count==1,"Incoming native orders join deletion history while foreign actor destinations remain intact");
        check(AuthoringRoot.Id==root&&selectedNode!.Id==root&&!CollapsedNodes.Contains(branch.Id)&&document.AllNodes.Any(n=>n.Id==keep.Id),"Deletion focuses its surviving parent and retains neighboring identities without stale collapse state");
        Undo(this,new());check(document.Source==before.AdditionalSources.Single(s=>s.SourceFile==other).Source&&Snapshot().Source==source&&Snapshot().AdditionalSources.Single(s=>s.SourceFile==third).Source==suppliedThird+orders&&CollapsedNodes.Contains(branch.Id)&&originalIds.All(i=>document.Identities.Any(n=>n.Id==i.Id)),"One desktop Undo restores every deleted source reference, identity and collapsed branch");
        Redo(this,new());check(Snapshot().Source==deleted.Source&&document.Source==deleted.AdditionalSources.Single(s=>s.SourceFile==other).Source&&!CollapsedNodes.Contains(branch.Id),"One desktop Redo restores the entire deletion and its surviving view metadata");
        var shot=document.Find(keep.Id).Children.Single(n=>n.Kind=="Shoot");SelectNode(shot.Id);Inspector.Children.OfType<TextBox>().Single(t=>t.Text=="5").Text="6";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Undo(this,new());Undo(this,new());check(document.AllNodes.Any(n=>n.Id==branch.Id)&&document.Source.Contains("count:5")&&Snapshot().Source==source,"Ordinary helper edits preserve the preceding multi-file deletion history");
        Redo(this,new());Redo(this,new());check(!document.AllNodes.Any(n=>n.Id==branch.Id)&&document.Source.Contains("count:6"),"Coordinated deletion and its later ordinary edit redo in the correct order");Undo(this,new());
        Undo(this,new());nodeSelection.Clear();nodeSelection.Add(branch.Id);nodeSelection.Add(keep.Id);DeleteSelectedNodes();
        check(!document.AllNodes.Any(n=>n.Id==branch.Id||n.Id==keep.Id)&&workspacePhaseRedo.Count==0&&workspacePhaseUndo.Count==1,"Bulk Delete reconciles multiple shared branches and discards the old complete redo branch");Undo(this,new());
        check(document.AllNodes.Any(n=>n.Id==branch.Id)&&document.AllNodes.Any(n=>n.Id==keep.Id)&&Snapshot().Source==source,"One Undo restores the complete bulk deletion");
        DeleteWorkspacePhase(branch.Id);var path=Path.Combine(output,"workspace-delete.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(encounterDocument!=null&&AuthoringRoot.Id==root&&!document.AllNodes.Any(n=>n.Id==branch.Id)&&Snapshot().Source.Contains("new State(\"choice\")"),"Saved workspace reopens the surviving shared body and complete coordinated deletion");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Play uses the full accepted source snapshot after coordinated deletion");running=false;await session!.AdvanceAsync(new(),300);UpdateSession();
        check(session.Entities.Single().Phase=="entry"&&session.Shots.Count==0,"Desktop native playback loads both edited caller and helper without stale phase references");Stop(this,new());
        UpdateLayout();CaptureView(this,Path.Combine(output,"workspace-delete-editor.png"));
        check(File.ReadAllText(primary)==supplied&&File.ReadAllText(other)==suppliedOther&&File.ReadAllText(third)==suppliedThird,"Desktop deletion, history, bulk edits, persistence and Play leave supplied game files unchanged");
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
