using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void SelectionExtractionSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/selection-extraction-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunSelectionExtractionUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunSelectionExtractionUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var destination=catalog.First(e=>e.SourceFile!=primary).SourceFile;var supplied=File.ReadAllText(primary);var suppliedDestination=File.ReadAllText(destination);
        const string source="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopSelectionExtract=()=>Behav().Init("Cave Pirate Brawler",StudioDesktopSelectionCaller(2));
    static State StudioDesktopSelectionCaller(int count)=>new State(new State("loop",new Shoot(20,count:++count,fixedAngle:0,coolDown:100),/* selected source note */new Shoot(20,count:count+1,fixedAngle:0,coolDown:100),new Shoot(20,count:count+2,fixedAngle:0,coolDown:100),new TimedTransition(100,"finish")),new State("finish"));
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[new(destination,suppliedDestination)],Player=previous.Player with{Invulnerable=true}},null);
        EditDefinition(document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopSelectionCaller").EntityId);var phase=AuthoringRoot.Children.First();var actions=phase.Children.Take(2).ToArray();var remaining=phase.Children.Skip(2).Select(n=>n.Id).ToArray();SelectAuthoringNode(actions[0].Id,ModifierKeys.None);SelectAuthoringNode(actions[1].Id,ModifierKeys.Control);
        check(nodeSelection.Count==2&&extractApplyButton?.IsEnabled==true,"Actual multi-selection exposes helper extraction for both adjacent native actions");extractNameInput!.Text="StudioDesktopSelectedActions";extractDestination!.SelectedItem=((SourceFileChoice[])extractDestination.ItemsSource).Single(s=>s.Path==destination);
        document.LockedNodes.Add(actions[0].Id);UpdateOrganizeButtons();var blocked=false;try{ExtractSelectedSource();}catch(InvalidOperationException){blocked=true;}
        check(blocked&&document.Source==source&&extractApplyButton?.IsEnabled==false&&workspacePhaseUndo.Count==0,"A lock on any selected action disables and rejects the complete extraction");document.LockedNodes.Remove(actions[0].Id);UpdateOrganizeButtons();
        var before=Snapshot();extractApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var extracted=Snapshot();
        check(encounterDocument!=null&&document.SourceFile==destination&&AuthoringRoot.Kind=="ArgumentGroup"&&AuthoringRoot.Children.Select(n=>n.Id).SequenceEqual(actions.Select(n=>n.Id)),"Extracted selection opens one ordered native child-array helper with both original action identities");
        check(PrimaryDocument.Source.Contains("..StudioDesktopSelectedActions(ref count)")&&remaining.All(id=>PrimaryDocument.AllNodes.Any(n=>n.Id==id))&&PrimaryDocument.Find(phase.Id).Id==phase.Id,"Spread call retains constructor-side mutation, the original parent and all following action/transition identities");
        check(workspacePhaseUndo.Count==1&&workspacePhaseUndo.Peek().Documents.Count==2,"Both selected expressions and their helper source join one workspace history transaction");
        Undo(this,new());check(encounterDocument==null&&document.Source==before.Source&&actions.All(n=>document.AllNodes.Any(a=>a.Id==n.Id)),"One Undo restores both inline actions and their original source order");
        Redo(this,new());check(encounterDocument!=null&&AuthoringRoot.Children.Select(n=>n.Id).SequenceEqual(actions.Select(n=>n.Id))&&Snapshot().Source==extracted.Source,"One Redo follows both retained identities into the extracted array helper");
        var path=Path.Combine(output,"selection-extraction.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(encounterDocument!=null&&AuthoringRoot.Children.Select(n=>n.Id).SequenceEqual(actions.Select(n=>n.Id)),"Save/reopen restores selected helper ownership and both ordered action identities");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Play compiles the accepted multi-expression helper and spread call");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();
        check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{3,4,5}),"Native selection playback retains mutation and all three original ordered volley counts");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(AuthoringRoot.Id);UpdateLayout();CaptureView(this,Path.Combine(output,"selection-extraction-editor.png"));
        check(File.ReadAllText(primary)==supplied&&File.ReadAllText(destination)==suppliedDestination,"Selection extraction, history, persistence and native playback leave supplied files unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
