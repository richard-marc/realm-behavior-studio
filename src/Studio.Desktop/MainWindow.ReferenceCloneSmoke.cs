using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void ReferenceCloneSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/reference-clone-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunReferenceCloneUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunReferenceCloneUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var helper=catalog.First(e=>e.SourceFile!=primary).SourceFile;var third=catalog.First(e=>e.SourceFile!=primary&&e.SourceFile!=helper).SourceFile;var supplied=new[]{primary,helper,third}.ToDictionary(p=>p,File.ReadAllText);
        const string source="""
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopReferenceClone=()=>Behav().Init("Cave Pirate Brawler",StudioDesktopReferenceCaller());
    static State StudioDesktopReferenceCaller()=>new State(StudioDesktopReferenceValue,new State("loop-copy"),new State("finish"));
} }
""";
        const string value="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static readonly TKR.WorldServer.logic.behaviors.Shoot StudioDesktopReferenceAttack=new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100);
    static readonly State StudioDesktopReferenceValue=new State("loop",StudioDesktopReferenceAttack,new State("nested"));
} }
""";
        const string attack="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioDesktopReferenceUnused=>new State("unused-desktop-reference-source");
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[new(helper,supplied[helper]+value),new(third,supplied[third]+attack)],Player=previous.Player with{Invulnerable=true}},null);
        EditDefinition(document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopReferenceCaller").EntityId);var reference=AuthoringRoot.Children.First();SelectNode(reference.Id);
        check(reference.Kind=="Reference"&&cloneDefinitionButton!.IsEnabled,"A source value reference exposes independent cloning alongside linked Duplicate");cloneNameInput!.Text="StudioDesktopIndependentReference";
        var before=Snapshot();cloneDefinitionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var cloned=Snapshot();var newReference=selectedNode!;
        check(newReference.Kind=="Reference"&&newReference.Id!=reference.Id&&document.Expression(newReference)=="StudioDesktopIndependentReference"&&document.Expression(document.Find(reference.Id))=="StudioDesktopReferenceValue","Actual independent reference cloning preserves the original value and selects a new reference");
        check(workspacePhaseUndo.Count==1&&workspacePhaseUndo.Peek().Documents.Count==2&&cloned.AdditionalSources.Single(s=>s.SourceFile==helper).Source.Contains("StudioDesktopReferenceAttackCopy"),"Independent State and Behavior field declarations join one complete source transaction");
        Undo(this,new());check(document.Source==before.Source&&Snapshot().AdditionalSources.Select(s=>s.Source).SequenceEqual(before.AdditionalSources.Select(s=>s.Source)),"One Undo restores reference and child-field declarations across all source files");
        Redo(this,new());check(document.AllNodes.Any(n=>n.Id==newReference.Id)&&Snapshot().Source==cloned.Source,"One Redo restores the independent reference identity against its complete source closure");
        SelectNode(newReference.Id);EditExternalDefinition(document.ExternalDefinitions(newReference).Single(r=>r.Label=="StudioDesktopIndependentReference"));var root=AuthoringRoot.Id;
        check(document.StateName(root)=="loop-copy-2"&&AuthoringRoot.Children.Any(n=>n.Kind=="Reference"&&n.Label.Contains("AttackCopy")),"Edit navigates to the independent State value and its independently cloned Behavior reference");
        var path=Path.Combine(output,"reference-clone.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(encounterDocument!=null&&AuthoringRoot.Id==root&&document.StateName(root)=="loop-copy-2","Save/reopen retains independent reference definition focus and identities");
        check(await StartSessionAsync("loop-copy-2")&&session is CompiledRuntime,"Play compiles the independently initialized State/Behavior field closure");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();
        check(session.Entities.Single().Phase=="nested-copy"&&session.Shots.Any(s=>s.Count==3),"Native reference clone executes actual independent child entry and attack values");Stop(this,new());
        WorkspaceTabs.SelectedIndex=1;SelectNode(root);UpdateLayout();CaptureView(this,Path.Combine(output,"reference-clone-editor.png"));
        check(supplied.All(p=>File.ReadAllText(p.Key)==p.Value),"Independent reference cloning, history, persistence and playback leave supplied files unchanged");
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
