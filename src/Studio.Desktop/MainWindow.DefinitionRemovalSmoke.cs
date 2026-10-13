using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void DefinitionRemovalSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/definition-removal-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunDefinitionRemovalUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunDefinitionRemovalUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var helper=catalog.First(e=>e.SourceFile!=primary).SourceFile;var supplied=new[]{primary,helper}.ToDictionary(p=>p,File.ReadAllText);
        const string source="using TKR.WorldServer.logic.behaviors; namespace TKR.WorldServer.logic { public partial class BehaviorDb { private _ StudioDesktopRemoval=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"live\",new Shoot(20,count:3,fixedAngle:0,coolDown:100))); } }";
        const string declaration="\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static State StudioDesktopUnused()=>new State(\"unused\",StudioDesktopRetained); static readonly TKR.WorldServer.logic.behaviors.Shoot StudioDesktopRetained=new TKR.WorldServer.logic.behaviors.Shoot(20,count:2); } }";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[new(helper,supplied[helper]+declaration)],LockedNodes=[],CollapsedNodes=[],Layout=[],Player=previous.Player with{Invulnerable=true}},null);
        var command=AvailableCommands().Single(c=>c.Title=="Edit definition StudioDesktopUnused");command.Execute();
        var root=AuthoringRoot.Id;var retained=document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopRetained").Root.Id;var before=document.Source;var primaryIds=PrimaryDocument.Identities;
        check(encounterDocument!=null&&DefinitionTitle.Text.Contains("0 callers")&&AuthoringRoot.Kind=="State"&&removeDefinitionButton?.IsEnabled==true,"Actual command search opens an unused declaring body with a quiet removal control");
        Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;check(removeDefinitionButton?.IsEnabled==false,"A definition body lock immediately disables declaration removal");document.LockedNodes.Clear();SelectNode(root);
        SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(s=>s.Path==helper);SourceEditor.Text+="\n// retained removal draft";var draft=SourceEditor.Text;var blocked=false;
        try{var plan=SourceDefinitionRemoval.Remove(adapter,Snapshot(),helper,root);ApplyWorkspacePhaseEdit(plan,plan.Selection[0],"Removed");}catch(InvalidOperationException){blocked=true;}
        check(blocked&&document.Source==before&&SourceEditor.Text==draft&&workspacePhaseUndo.Count==0,"An affected source draft blocks removal without changing accepted source or workspace history");DiscardSourceDraft(this,new());SelectNode(root);removeDefinitionButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(encounterDocument==null&&Snapshot().Source==source&&!dependencyDocuments[helper].Definitions.Any(d=>d.Root.Id==root)&&workspacePhaseUndo.Count==1,"Actual Remove commits only the declaration file and returns to the selected encounter");
        check(dependencyDocuments[helper].Find(retained).Id==retained&&primaryIds.All(i=>PrimaryDocument.Identities.Any(n=>n.Id==i.Id)),"Declaration removal retains dependency and encounter identities");
        Undo(this,new());check(encounterDocument!=null&&AuthoringRoot.Id==root&&document.Source==before&&selectedNode!.Id==root,"One workspace Undo restores the removed declaration and its original external body view");Redo(this,new());
        check(encounterDocument==null&&!dependencyDocuments[helper].Definitions.Any(d=>d.Root.Id==root)&&Snapshot().Source==source,"One workspace Redo removes the same declaration and returns to the live encounter");
        var saved=Path.Combine(output,"removed.rbs");BehaviorDocument.Save(saved,Snapshot());ApplyProject(BehaviorDocument.Load(saved),saved);
        check(!Snapshot().AdditionalSources.Single().Source.Contains("static State StudioDesktopUnused")&&Snapshot().AdditionalSources.Single().Identities.Any(i=>i.Id==retained),"Save/reopen retains the accepted removal and untouched dependency identity");
        check(await StartSessionAsync(),"Native Play compiles the accepted declaration removal with the original live source");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();check(session.Shots.Single().Count==3,"Native playback retains the live encounter's actual attack after cleanup");Stop(this,new());
        AvailableCommands().Single(c=>c.Title=="Edit definition StudioDesktopRetained").Execute();WorkspaceTabs.SelectedIndex=1;UpdateLayout();CaptureView(this,Path.Combine(output,"retained-definition-editor.png"));
        check(File.ReadAllText(primary)==supplied[primary]&&File.ReadAllText(helper)==supplied[helper],"Definition cleanup, history, persistence and playback leave supplied game files unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
