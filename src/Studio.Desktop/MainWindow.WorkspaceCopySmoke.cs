using System.IO;
using System.Windows;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void WorkspaceCopySmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/workspace-copy-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunWorkspaceCopyUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunWorkspaceCopyUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var origin=catalog.First(e=>e.SourceFile!=primary).SourceFile;var destination=catalog.First(e=>e.SourceFile!=primary&&e.SourceFile!=origin).SourceFile;
        var supplied=new[]{primary,origin,destination}.ToDictionary(p=>p,File.ReadAllText);
        const string source="""
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    const int StudioDesktopCopyCount=3;
    private _ StudioDesktopCopy=()=>Behav().Init("Cave Pirate Brawler",StudioDesktopCopyCaller()).Init("Pirate Lieutenant",StudioDesktopCopyCaller());
    static State StudioDesktopCopyCaller()=>new State(StudioDesktopCopyShared(),StudioDesktopCopyDestination(),new State("loop-copy"),new State("nested-copy"),new State("finish"));
} }
""";
        const string shared="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioDesktopCopyShared()=>new State(new State("loop",new TKR.WorldServer.logic.behaviors.Shoot(20,count:StudioDesktopCopyCount,fixedAngle:0,coolDown:100),new TKR.WorldServer.logic.transitions.TimedTransition(100,"nested"),new State("nested")),new State("keep"));
} }
""";
        const string target="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioDesktopCopyDestination()=>new State("destination");
} }
""";
        ApplyProject(previous with{SourceDrafts=[],SelectedSourceFile=null,Source=source,Identities=[],LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[new(origin,supplied[origin]+shared),new(destination,supplied[destination]+target)],Player=previous.Player with{Invulnerable=true}},null);
        EditExternalDefinition(document.AllNodes.SelectMany(document.ExternalDefinitions).First(r=>r.Label=="StudioDesktopCopyShared"));
        var branch=AuthoringRoot.Children.Single(n=>document.StateName(n.Id)=="loop");SelectNode(branch.Id);CollapsedNodes.Add(branch.Id);var originalIds=document.Identities;
        BehaviorClipboard? clipboard=null;var previousRead=readNodeClipboard;var previousWrite=writeNodeClipboard;readNodeClipboard=()=>clipboard;writeNodeClipboard=value=>clipboard=value;
        try
        {
            CopySelectedNodes();check(clipboard?.Symbols.Any(s=>s.Signature.Contains("StudioDesktopCopyCount"))==true,"Actual Copy retains bound source symbols alongside the selected expressions");
            ReturnToEncounter(this,new());EditExternalDefinition(document.AllNodes.SelectMany(document.ExternalDefinitions).First(r=>r.Label=="StudioDesktopCopyDestination"));var root=AuthoringRoot.Id;SelectNode(root);var before=Snapshot();
            PrimaryDocument.LockedNodes.Add(EncounterRoot.Id);var blocked=false;try{PasteWorkspaceNodes(root,0,clipboard!);}catch(InvalidOperationException){blocked=true;}
            check(blocked&&document.Source==supplied[destination]+target&&workspacePhaseUndo.Count==0,"A caller lock blocks cross-file paste without changing either source");PrimaryDocument.LockedNodes.Clear();
            SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(s=>s.Path==destination);SourceEditor.Text+="\n// protected copy draft";var draft=SourceEditor.Text;blocked=false;
            try{PasteWorkspaceNodes(root,0,clipboard!);}catch(InvalidOperationException){blocked=true;}
            check(blocked&&SourceEditor.Text==draft&&workspacePhaseUndo.Count==0,"Cross-file paste preserves an affected source draft");DiscardSourceDraft(this,new());
            PasteSelectedNodes();var copied=selectedNode!;var accepted=Snapshot();
            check(document.StateName(copied.Id)=="loop-copy-2"&&copied.Children.Any(n=>document.StateName(n.Id)=="nested-copy-2"),"Actual Paste avoids all known caller phase names and selects the independent copied branch");
            check(copied.Children.Single(n=>n.Kind=="TimedTransition").Parameters.Any(p=>p.Expression=="\"nested-copy-2\"")&&nodeSelection.SetEquals(new[]{copied.Id}),"Copied transition destination and desktop selection follow the new branch identities");
            check(accepted.Source==source&&accepted.AdditionalSources.Single(s=>s.SourceFile==origin).Source==supplied[origin]+shared&&accepted.AdditionalSources.Single(s=>s.SourceFile==origin).CollapsedNodes.Contains(branch.Id)&&!CollapsedNodes.Contains(copied.Id),"Cross-file paste leaves origin source and collapse metadata intact without transferring view state");
            Undo(this,new());check(document.Source==before.AdditionalSources.Single(s=>s.SourceFile==destination).Source&&AuthoringRoot.Id==root,"One desktop Undo restores the destination while retaining its declaring-body identity");
            Redo(this,new());check(document.AllNodes.Any(n=>n.Id==copied.Id)&&document.StateName(copied.Id)=="loop-copy-2","One Redo restores the exact copied node identities and phase names");
            SelectNode(copied.Id);duplicateStructureButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));var duplicate=selectedNode!;
            check(document.StateName(duplicate.Id)=="loop-copy-2-copy"&&duplicate.Id!=copied.Id&&workspacePhaseUndo.Count==2,"Shared-body Duplicate uses coordinated naming and adds one independent workspace edit");Undo(this,new());
            var path=Path.Combine(output,"workspace-copy.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
            check(encounterDocument!=null&&AuthoringRoot.Id==root&&document.AllNodes.Any(n=>n.Id==copied.Id)&&Snapshot().AdditionalSources.Single(s=>s.SourceFile==origin).Identities.All(i=>originalIds.Any(o=>o.Id==i.Id)),"Save/reopen retains copied destination identities and the original shared source document");
            check(await StartSessionAsync("loop-copy-2")&&session is CompiledRuntime,"Play loads the full copied source workspace with the native phase test");running=false;await session!.AdvanceAsync(new(),300);UpdateSession();
            check(session.Entities.Single().Phase=="nested-copy-2"&&session.Shots.Any(s=>s.Count==3),"Native desktop playback enters the copied child and executes its inherited bound attack");Stop(this,new());
            WorkspaceTabs.SelectedIndex=1;SelectNode(copied.Id);UpdateLayout();CaptureView(this,Path.Combine(output,"workspace-copy-editor.png"));
            check(supplied.All(p=>File.ReadAllText(p.Key)==p.Value),"Copy, paste, duplicate, history, persistence and playback leave supplied game files unchanged");
        }
        finally{readNodeClipboard=previousRead;writeNodeClipboard=previousWrite;}
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
