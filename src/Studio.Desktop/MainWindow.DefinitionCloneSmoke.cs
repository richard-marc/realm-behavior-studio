using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void DefinitionCloneSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/definition-clone-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunDefinitionCloneUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunDefinitionCloneUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var helper=catalog.First(e=>e.SourceFile!=primary).SourceFile;var third=catalog.First(e=>e.SourceFile!=primary&&e.SourceFile!=helper).SourceFile;var supplied=new[]{primary,helper,third}.ToDictionary(p=>p,File.ReadAllText);
        const string source="""
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopClone=()=>Behav().Init("Cave Pirate Brawler",StudioDesktopCloneCaller()).Init("Pirate Lieutenant",StudioDesktopCloneCaller());
    static State StudioDesktopCloneCaller()=>new State(StudioDesktopCloneShared(3),new State("loop-copy"),new State("finish"));
} }
""";
        const string shared="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioDesktopCloneShared(int count)=>new State("loop",StudioDesktopCloneAttack(count),StudioDesktopCloneChild);
    static TKR.WorldServer.logic.behaviors.Shoot StudioDesktopCloneAttack(int count)=>new TKR.WorldServer.logic.behaviors.Shoot(20,count:count,fixedAngle:0,coolDown:100);
} }
""";
        const string child="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static readonly State StudioDesktopCloneChild=new State("nested");
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[new(helper,supplied[helper]+shared),new(third,supplied[third]+child)],Player=previous.Player with{Invulnerable=true}},null);
        EditDefinition(document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopCloneCaller").EntityId);var call=AuthoringRoot.Children.First();SelectNode(call.Id);
        check(cloneDefinitionButton!.IsEnabled&&cloneNameInput!=null,"A source factory exposes the actual independent-clone control");cloneNameInput!.Text="StudioDesktopIndependentClone";
        var before=Snapshot();PrimaryDocument.LockedNodes.Add(EncounterRoot.Id);var blocked=false;try{CloneSelectedDefinition();}catch(InvalidOperationException){blocked=true;}
        check(blocked&&workspacePhaseUndo.Count==0&&document.Source==source,"Caller locks protect the full independent clone transaction");PrimaryDocument.LockedNodes.Clear();
        SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(s=>s.Path==helper);SourceEditor.Text+="\n// protected clone draft";var draft=SourceEditor.Text;blocked=false;try{CloneSelectedDefinition();}catch(InvalidOperationException){blocked=true;}
        check(blocked&&SourceEditor.Text==draft&&document.Source==source,"Independent cloning preserves an affected shared-source draft");DiscardSourceDraft(this,new());
        cloneDefinitionButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var cloned=Snapshot();var selected=selectedNode!;
        check(selected.Kind=="Factory"&&selected.Id!=call.Id&&document.Find(call.Id).Parameters.Any(p=>p.Expression=="3")&&AuthoringRoot.Children.Count==4,"Real Clone adds a new selected factory call beside the unchanged original");
        check(cloned.AdditionalSources.Single(s=>s.SourceFile==helper).Source.Contains("StudioDesktopCloneAttackCopy")&&cloned.AdditionalSources.Single(s=>s.SourceFile==third).Source.Contains("StudioDesktopCloneChildCopy")&&workspacePhaseUndo.Peek().Documents.Count==3,"Factory, action helper and child field declarations join one three-file workspace edit");
        Undo(this,new());check(document.Source==before.Source&&Snapshot().AdditionalSources.Select(s=>s.Source).SequenceEqual(before.AdditionalSources.Select(s=>s.Source)),"One Undo restores every original declaration and callsite");
        var redoPlan=workspacePhaseRedo.Peek();File.WriteAllText(Path.Combine(output,"clone-redo-before.json"),System.Text.Json.JsonSerializer.Serialize(new{Current=Snapshot(),Expected=redoPlan.Plan.Before,Histories=redoPlan.Documents.Select((d,i)=>new{d.SourceFile,d.Source,Expected=redoPlan.Plan.Changes[i].After,Ready=d.CanRestoreSourceHistory(redoPlan.Plan.Changes[i].After,true)}).ToArray()},BehaviorDocument.JsonOptions));
        Redo(this,new());File.WriteAllText(Path.Combine(output,"clone-redo-after.json"),System.Text.Json.JsonSerializer.Serialize(new{Status=Status.Text,Undo=workspacePhaseUndo.Count,Redo=workspacePhaseRedo.Count,Source=document.Source,Ids=document.Identities,Selected=selected.Id},BehaviorDocument.JsonOptions));check(document.AllNodes.Any(n=>n.Id==selected.Id)&&Snapshot().Source==cloned.Source,"One Redo restores all clone declarations and the exact new call identity");
        SelectNode(selected.Id);EditExternalDefinition(document.ExternalDefinitions(selected).Single(r=>r.Label=="StudioDesktopIndependentClone"));var root=AuthoringRoot.Id;
        check(document.StateName(root)=="loop-copy-2"&&AuthoringRoot.Children.Any(n=>n.Kind=="Reference"&&n.Label.Contains("ChildCopy")),"Edit opens the independent helper's own phase and cloned child source reference");
        var path=Path.Combine(output,"definition-clone.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(encounterDocument!=null&&AuthoringRoot.Id==root&&document.StateName(root)=="loop-copy-2","Save and reopen retain independent helper focus, names and identities");
        check(await StartSessionAsync("loop-copy-2")&&session is CompiledRuntime,"Play uses the full independently cloned source closure");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();
        check(session.Entities.Single().Phase=="nested-copy"&&session.Shots.Any(s=>s.Count==3),"Native desktop playback enters the cloned child field and executes its bound cloned attack helper");Stop(this,new());
        WorkspaceTabs.SelectedIndex=1;SelectNode(root);UpdateLayout();CaptureView(this,Path.Combine(output,"independent-clone-editor.png"));
        check(supplied.All(p=>File.ReadAllText(p.Key)==p.Value),"Cloning, history, independent editing, persistence and playback leave supplied game files unchanged");
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
