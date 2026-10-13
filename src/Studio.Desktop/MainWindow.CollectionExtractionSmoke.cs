using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void CollectionExtractionSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/collection-extraction-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunCollectionExtractionUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunCollectionExtractionUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var destination=catalog.First(e=>e.SourceFile!=primary).SourceFile;var supplied=File.ReadAllText(primary);var destinationSource=File.ReadAllText(destination);
        const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopCollectionExtract=()=>Behav().Init("Cave Pirate Brawler",StudioDesktopCollectionCaller(3));
    static State StudioDesktopCollectionCaller(int count)=>new State("loop",StudioDesktopCollectionWrap([new Shoot(20,count:count,fixedAngle:0,coolDown:100),new Shoot(20,count:5,fixedAngle:0,coolDown:100)]));
    static Prioritize StudioDesktopCollectionWrap(System.Collections.Generic.IReadOnlyList<CycleBehavior> children)=>new Prioritize(System.Linq.Enumerable.ToArray(children));
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[new(destination,destinationSource)],Player=previous.Player with{Invulnerable=true}},null);
        EditDefinition(document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopCollectionCaller").EntityId);var group=AuthoringRoot.Descendants().First(n=>n.Kind=="ArgumentGroup"&&!n.ArgumentFromCall&&!n.ArgumentRequiresSource);var moved=group.Descendants().Select(n=>n.Id).ToArray();SelectNode(group.Id);CollapsedNodes.Add(group.Id);
        check(extractApplyButton?.IsEnabled==true,"Explicit typed child collections expose the actual Extract helper controls");extractNameInput!.Text="StudioDesktopExtractedChildren";extractDestination!.SelectedItem=((SourceFileChoice[])extractDestination.ItemsSource).Single(s=>s.Path==destination);
        var before=Snapshot();extractApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var extracted=Snapshot();
        check(document.SourceFile==destination&&AuthoringRoot.Kind=="ArgumentGroup"&&AuthoringRoot.Id==group.Id&&moved.All(id=>document.AllNodes.Any(n=>n.Id==id)),"Collection extraction opens the independent child-list body with every original identity");
        check(PrimaryDocument.Source.Contains("StudioDesktopExtractedChildren(ref count)")&&CollapsedNodes.Contains(group.Id),"Captured storage and collection collapse ownership follow the extracted helper");
        check(workspacePhaseUndo.Count==1&&workspacePhaseUndo.Peek().Documents.Count==2,"Collection extraction joins both files in one workspace transaction");
        Undo(this,new());check(encounterDocument==null&&document.Source==before.Source&&selectedNode?.Id==group.Id,"One Undo restores the inline collection and its selected identity");
        Redo(this,new());check(encounterDocument!=null&&AuthoringRoot.Id==group.Id&&Snapshot().Source==extracted.Source,"One Redo returns to the extracted collection's source ownership");
        var path=Path.Combine(output,"collection-extraction.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(encounterDocument!=null&&AuthoringRoot.Id==group.Id&&moved.All(id=>document.AllNodes.Any(n=>n.Id==id)),"Save/reopen restores the collection helper and all ordered child identities");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Play compiles the accepted collection helper with its real native type");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();
        check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{3,5}),"Extracted collection retains both ordered native volleys and Prioritize dispatch");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(group.Id);UpdateLayout();CaptureView(this,Path.Combine(output,"collection-extraction-editor.png"));
        check(File.ReadAllText(primary)==supplied&&File.ReadAllText(destination)==destinationSource,"Collection extraction, history, persistence and playback leave supplied files unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
