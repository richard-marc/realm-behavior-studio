using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void ExtractionSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/extraction-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunExtractionUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunExtractionUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var destination=catalog.First(e=>e.SourceFile!=primary).SourceFile;var supplied=File.ReadAllText(primary);var suppliedDestination=File.ReadAllText(destination);
        const string source="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopExtraction=()=>Behav().Init("Cave Pirate Brawler",StudioDesktopExtractionCaller(3)).Init("Pirate Lieutenant",StudioDesktopExtractionCaller(5));
    static State StudioDesktopExtractionCaller(int count)=>new State(new State("loop",new Shoot(20,count:count,fixedAngle:0,coolDown:100),new State("nested"),new TimedTransition(100,"finish")),new State("finish"));
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[new(destination,suppliedDestination)],Player=previous.Player with{Invulnerable=true}},null);
        EditDefinition(document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopExtractionCaller").EntityId);var branch=AuthoringRoot.Children.First();var ids=branch.Descendants().Select(n=>n.Id).ToArray();CollapsedNodes.Add(branch.Id);SelectNode(branch.Id);
        check(extractApplyButton!.IsEnabled&&extractNameInput!=null&&extractDestination!=null,"Actual Organize controls offer helper extraction for a source-backed phase");
        extractNameInput!.Text="StudioDesktopExtractedLoop";extractDestination!.SelectedItem=((SourceFileChoice[])extractDestination.ItemsSource).Single(s=>s.Path==destination);
        var before=Snapshot();PrimaryDocument.LockedNodes.Add(EncounterRoot.Id);var blocked=false;try{ExtractSelectedSource();}catch(InvalidOperationException){blocked=true;}
        check(blocked&&document.Source==source&&workspacePhaseUndo.Count==0,"A caller lock protects every source in the extraction");PrimaryDocument.LockedNodes.Clear();
        SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(s=>s.Path==destination);SourceEditor.Text+="\n// protected extraction draft";var draft=SourceEditor.Text;blocked=false;try{ExtractSelectedSource();}catch(InvalidOperationException){blocked=true;}
        check(blocked&&SourceEditor.Text==draft&&document.Source==source,"Extraction preserves a pending destination source draft");DiscardSourceDraft(this,new());
        extractApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var extracted=Snapshot();
        check(encounterDocument!=null&&document.SourceFile==destination&&AuthoringRoot.Id==branch.Id&&ids.All(id=>document.AllNodes.Any(n=>n.Id==id)),"Extraction opens the actual new helper body with the original phase/action identities");
        check(PrimaryDocument.Source.Contains("StudioDesktopExtractedLoop(ref count)")&&CollapsedNodes.Contains(branch.Id)&&!collapsedByDocument[PrimaryDocument].Contains(branch.Id),"Captured storage and collapsed phase ownership follow the extracted source");
        check(workspacePhaseUndo.Count==1&&workspacePhaseUndo.Peek().Documents.Count==2,"Both source files join one workspace history transaction");
        Undo(this,new());check(encounterDocument==null&&document.Source==source&&selectedNode!.Id==branch.Id&&CollapsedNodes.Contains(branch.Id),"One Undo restores the original body, focus and collapse state without stale helper navigation");
        Redo(this,new());check(encounterDocument!=null&&AuthoringRoot.Id==branch.Id&&selectedNode!.Id==branch.Id&&Snapshot().Source==extracted.Source,"One Redo restores the extracted body and follows its source ownership");
        var path=Path.Combine(output,"extraction.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(encounterDocument!=null&&AuthoringRoot.Id==branch.Id&&ids.All(id=>document.AllNodes.Any(n=>n.Id==id)),"Saved extraction reopens the new helper definition and every moved identity");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Play compiles the complete extracted encounter and helper source");running=false;await session!.AdvanceAsync(new(),300);UpdateSession();
        check(session.Entities.Single().Phase=="finish"&&session.Shots.Any(s=>s.Count==3),"Native playback retains captured count, original child entry and the external transition");Stop(this,new());
        WorkspaceTabs.SelectedIndex=1;SelectNode(branch.Id);UpdateLayout();CaptureView(this,Path.Combine(output,"extraction-editor.png"));
        check(File.ReadAllText(primary)==supplied&&File.ReadAllText(destination)==suppliedDestination,"Extraction, source navigation, history, persistence and playback leave original game files unchanged");
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
