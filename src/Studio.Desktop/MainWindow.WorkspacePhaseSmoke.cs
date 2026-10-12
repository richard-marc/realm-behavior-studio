using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void WorkspacePhaseSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/workspace-phase-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunWorkspacePhaseUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunWorkspacePhaseUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var other=catalog.First(e=>e.SourceFile!=primary).SourceFile;
        var third=catalog.First(e=>e.SourceFile!=primary&&e.SourceFile!=other).SourceFile;
        var supplied=File.ReadAllText(primary);var suppliedOther=File.ReadAllText(other);var suppliedThird=File.ReadAllText(third);
        const string source="""
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopWorkspacePhase=()=>Behav().Init("Cave Pirate Brawler",StudioDesktopWorkspaceCaller()).Init("Pirate Lieutenant",StudioDesktopWorkspaceCaller());
    static State StudioDesktopWorkspaceCaller()=>new State(new State("entry",new TimedTransition(100,"loop")),StudioDesktopWorkspaceShared(),new State("finish"),new State("choice",new TimedRandomTransition(100,false,new[]{"loop", /* repeated */ "loop","finish"})));
} }
""";
        const string helper="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioDesktopWorkspaceShared()=>new State("loop",new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100),new TKR.WorldServer.logic.transitions.TimedTransition(200,"finish"));
} }
""";
        const string orders="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static Behavior[] StudioDesktopWorkspaceOrders()=>[new TKR.WorldServer.logic.behaviors.Order(10,"Cave Pirate Brawler","loop"),new TKR.WorldServer.logic.behaviors.OrderOnDeath(10,"Pirate Lieutenant","loop")];
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[new(other,suppliedOther+helper),new(third,suppliedThird+orders)],Player=previous.Player with{Invulnerable=true}},null);
        var callerRoot=document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopWorkspaceCaller");EditDefinition(callerRoot.EntityId);
        SelectNode(AuthoringRoot.Children.Single(n=>n.Kind=="Factory").Id);
        Inspector.Children.OfType<StackPanel>().SelectMany(p=>p.Children.OfType<Button>()).Single(b=>Equals(b.Content,"Edit StudioDesktopWorkspaceShared")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var phase=AuthoringRoot.Id;var primaryIds=PrimaryDocument.Identities;var helperIds=document.Identities;
        check(encounterDocument!=null&&document.StateName(phase)=="loop"&&phaseNameInput!.Text=="loop","Shared named phase opens in the actual foreign definition inspector");
        var untouched=catalog.First(e=>e.SourceFile!=primary&&e.SourceFile!=other&&e.SourceFile!=third).SourceFile;
        AttachSourceFile(untouched);SourceEditor.Text+="\n// preserved unrelated draft";var unrelatedDraft=SourceEditor.Text;
        phaseNameInput!.Text="cycle";renamePhaseButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var renamed=Snapshot();
        check(SourceEditor.Text==unrelatedDraft&&sourceDrafts.GetValueOrDefault(untouched)==unrelatedDraft&&renamed.AdditionalSources.Single(s=>s.SourceFile==untouched).Source==File.ReadAllText(untouched),"Coordinated Rename preserves an unapplied draft in an unaffected source file");
        WorkspaceTabs.SelectedIndex=1;
        check(document.StateName(phase)=="cycle"&&renamed.Source.Contains("TimedTransition(100,\"cycle\")")&&renamed.Source.Contains("new[]{\"cycle\", /* repeated */ \"cycle\",\"finish\"}"),"Real Rename coordinates the shared phase, caller transition and repeated random-choice slots");
        check(renamed.AdditionalSources.Single(s=>s.SourceFile==third).Source.Contains("\"Pirate Lieutenant\",\"cycle\"")&&workspacePhaseUndo.Count==1,"Incoming native orders join the same workspace history operation");
        check(primaryIds.All(i=>PrimaryDocument.Identities.Any(n=>n.Id==i.Id))&&helperIds.All(i=>document.Identities.Any(n=>n.Id==i.Id))&&AuthoringRoot.Id==phase,"Coordinated Rename preserves caller/body identities and the focused phase");
        Undo(this,new());check(document.StateName(phase)=="loop"&&Snapshot().Source==source&&Snapshot().AdditionalSources.Single(s=>s.SourceFile==third).Source==suppliedThird+orders,"One desktop Undo restores every changed source file");
        Redo(this,new());check(document.StateName(phase)=="cycle"&&Snapshot().Source==renamed.Source&&Snapshot().AdditionalSources.Select(s=>s.Source).SequenceEqual(renamed.AdditionalSources.Select(s=>s.Source)),"One desktop Redo reapplies the complete coordinated source set");
        var shot=AuthoringRoot.Children.Single(n=>n.Kind=="Shoot");SelectNode(shot.Id);Inspector.Children.OfType<TextBox>().Single(t=>t.Text=="3").Text="4";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Source.Contains("count:4")&&document.StateName(phase)=="cycle","Ordinary helper editing remains available after a coordinated phase transaction");
        Undo(this,new());check(document.Source.Contains("count:3")&&document.StateName(phase)=="cycle","Undo first restores the later ordinary edit without undoing the coordinated rename");
        Undo(this,new());check(document.StateName(phase)=="loop"&&Snapshot().Source==source,"The next Undo restores the preceding coordinated transaction");
        Redo(this,new());check(document.StateName(phase)=="cycle","Coordinated Redo remains available after restoring a later ordinary edit");
        Redo(this,new());check(document.Source.Contains("count:4")&&document.StateName(phase)=="cycle","The later ordinary edit remains redoable after coordinated Redo");Undo(this,new());
        WorkspaceTabs.SelectedItem=SourceWorkspace;SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(s=>s.Path==other);
        SourceEditor.Text=SourceEditor.Text.Replace("count:3","count:5");ApplySourceBuffer();
        Undo(this,new());Undo(this,new());
        check(document.StateName(phase)=="loop"&&document.Source.Contains("count:3")&&Snapshot().Source==source,"Source-editor Apply and Undo retain the preceding coordinated history and document identity");
        Redo(this,new());Undo(this,new());SourceEditor.Text=SourceEditor.Text.Replace("count:3","count:6");ApplySourceBuffer();
        check(workspacePhaseRedo.Count==0&&!PrimaryDocument.CanRestoreSourceHistory(renamed.Source,true),"Editing after coordinated Undo discards the complete old redo branch, including caller history");
        Undo(this,new());Redo(this,new());
        check(document.StateName(phase)=="loop"&&document.Source.Contains("count:6")&&Snapshot().Source==source,"The new branch keeps its own Undo/Redo without replaying any part of the discarded rename");
        Undo(this,new());RenameWorkspacePhase(phase,"cycle");
        check(!sourceRedo.ContainsKey(other)&&document.StateName(phase)=="cycle","A new coordinated rename also discards stale ordinary source redo entries in affected files");
        SourceEditor.Text+="\n// unapplied draft";var draft=SourceEditor.Text;var draftBlocked=false;
        try{RestoreWorkspacePhase(false);}catch(InvalidOperationException){draftBlocked=true;}
        check(draftBlocked&&SourceEditor.Text==draft&&document.StateName(phase)=="cycle"&&workspacePhaseUndo.Count==1,"Coordinated Undo protects unapplied source drafts without changing accepted sources or history");
        SourceEditor.Text=displayedSource;sourceDrafts.Remove(other);WorkspaceTabs.SelectedIndex=1;
        var before=Snapshot();PrimaryDocument.LockedNodes.Add(EncounterRoot.Id);var blocked=false;
        try{RenameWorkspacePhase(phase,"locked rename");}catch(InvalidOperationException){blocked=true;}
        check(blocked&&Snapshot().Source==before.Source&&document.StateName(phase)=="cycle","Caller locks reject all coordinated changes before any source mutation");PrimaryDocument.LockedNodes.Clear();
        SelectNode(phase);var duplicate=false;try{RenameWorkspacePhase(phase,"finish");}catch(ArgumentException){duplicate=true;}
        check(duplicate&&document.StateName(phase)=="cycle"&&workspacePhaseUndo.Count==1,"A duplicate name in another caller rejects atomically and preserves history");
        UpdateLayout();CaptureView(this,Path.Combine(output,"workspace-phase-editor.png"));
        var path=Path.Combine(output,"workspace-phase.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(encounterDocument!=null&&AuthoringRoot.Id==phase&&document.StateName(phase)=="cycle"&&Snapshot().Source.Contains("\"cycle\""),"Saved workspace reopens the renamed shared phase and reconciled callers");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Play uses the complete coordinated native source snapshot");running=false;await session!.AdvanceAsync(new(),200);UpdateSession();
        check(session.Entities.Single().Phase=="cycle","Actual desktop native execution enters the renamed phase through its updated caller transition");Stop(this,new());
        check(File.ReadAllText(primary)==supplied&&File.ReadAllText(other)==suppliedOther&&File.ReadAllText(third)==suppliedThird,"Coordinated UI editing, history, persistence and playback leave all game source files unchanged");
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
