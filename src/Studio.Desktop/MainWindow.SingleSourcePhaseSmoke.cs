using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void SingleSourcePhaseSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/single-source-phase-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunSingleSourcePhaseUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunSingleSourcePhaseUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var supplied=File.ReadAllText(primary);
        const string source="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioSingleSourcePhases=()=>Behav()
        .Init("Cave Pirate Brawler",new State(new State("entry",new TimedTransition(100,"loop")),new State("loop",new Shoot(20,count:3,fixedAngle:0,coolDown:100)),new State("finish")))
        .Init("Pirate Lieutenant",new State(new Order(10,"Cave Pirate Brawler","loop"),new Order(10,"Cave Pirate Captain","loop"),new State("foreign-loop")));
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;
        var loop=EncounterRoot.Children.Single(n=>document.StateName(n.Id)=="loop");var entry=EncounterRoot.Children.Single(n=>document.StateName(n.Id)=="entry");var originalIds=document.Identities;SelectNode(loop.Id);
        check(!adapter.HasAdditionalSources&&renamePhaseButton?.IsEnabled==true,"A single accepted source exposes the actual phase rename control");
        var incoming=document.AllNodes.Single(n=>n.Kind=="Order"&&n.Parameters.Any(p=>p.Expression=="\"Cave Pirate Brawler\""));document.LockedNodes.Add(incoming.Id);var blocked=false;
        try{RenameWorkspacePhase(loop.Id,"single-renamed");}catch(InvalidOperationException){blocked=true;}
        check(blocked&&document.Source==source&&workspacePhaseUndo.Count==0,"A locked incoming order protects a phase rename even without additional source files");document.LockedNodes.Remove(incoming.Id);
        phaseNameInput!.Text="single-renamed";renamePhaseButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var renamed=Snapshot();
        check(document.StateName(loop.Id)=="single-renamed"&&renamed.Source.Contains("TimedTransition(100,\"single-renamed\")")&&renamed.Source.Contains("\"Cave Pirate Brawler\",\"single-renamed\"")&&renamed.Source.Contains("\"Cave Pirate Captain\",\"loop\""),"Actual single-file Rename reconciles incoming native orders and transitions while preserving foreign targets");
        check(workspacePhaseUndo.Count==1&&workspacePhaseUndo.Peek().Documents.Count==1&&originalIds.All(i=>document.Identities.Any(n=>n.Id==i.Id)),"Single-source rename is one complete transaction with all surviving identities");
        Undo(this,new());check(document.Source==source&&document.StateName(loop.Id)=="loop","One Undo restores declaration, incoming orders and transition destinations");Redo(this,new());check(document.Source==renamed.Source,"One Redo restores the complete single-source rename");
        SelectNode(loop.Id);deleteStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var deleted=Snapshot();
        check(!document.AllNodes.Any(n=>n.Id==loop.Id)&&!deleted.Source.Contains("single-renamed")&&deleted.Source.Contains("\"Cave Pirate Captain\",\"loop\""),"Actual single-file Delete removes the phase, incoming native order and transition together");
        Undo(this,new());check(document.Source==renamed.Source&&document.Find(loop.Id).Id==loop.Id&&document.Find(incoming.Id).Id==incoming.Id,"Delete Undo restores the phase and incoming order with their exact identities");
        nodeSelection.Clear();nodeSelection.UnionWith(new[]{entry.Id,loop.Id});DeleteSelectedNodes();
        check(!document.AllNodes.Any(n=>n.Id==loop.Id||n.Id==entry.Id||n.Id==incoming.Id)&&workspacePhaseRedo.Count==0,"Single-source bulk Delete reconciles incoming orders and replaces the old redo branch");Undo(this,new());check(document.Source==renamed.Source,"One Undo restores the entire single-source bulk deletion");
        SelectNode(loop.Id);deleteStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var path=Path.Combine(output,"single-source-phase.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(!document.AllNodes.Any(n=>n.Id==loop.Id)&&!document.Source.Contains("single-renamed"),"Save/reopen retains complete single-source phase reconciliation");
        check(await StartSessionAsync()&&session!=null,"Play starts the accepted single-source phase deletion through native execution");running=false;await session!.AdvanceAsync(new(),200);UpdateSession();
        check(session.Entities.Single().Phase=="entry"&&session.Shots.Count==0,"Native playback has no stale transition or attack from the deleted phase");Stop(this,new());UpdateLayout();CaptureView(this,Path.Combine(output,"single-source-phase-editor.png"));
        check(File.ReadAllText(primary)==supplied,"Single-source rename/deletion, history and native playback leave supplied game source unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
