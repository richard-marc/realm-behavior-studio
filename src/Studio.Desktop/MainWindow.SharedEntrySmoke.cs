using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void SharedEntrySmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/shared-entry-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunSharedEntryUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunSharedEntryUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var helper=catalog.First(e=>e.SourceFile!=primary).SourceFile;var supplied=new[]{primary,helper}.ToDictionary(p=>p,File.ReadAllText);
        const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopSharedEntries=()=>Behav().Init("Cave Pirate Brawler",new State("loop",StudioDesktopEntryWrap(StudioDesktopEntryList()))).Init("Pirate Lieutenant",new State("loop",StudioDesktopEntryWrap(StudioDesktopEntryList())));
    static Prioritize StudioDesktopEntryWrap(System.Collections.Generic.IEnumerable<CycleBehavior> children)=>new Prioritize(System.Linq.Enumerable.ToArray(children));
    sealed class StudioDesktopEntryCollection:System.Collections.Generic.IEnumerable<CycleBehavior> {
        readonly System.Collections.Generic.SortedDictionary<int,CycleBehavior> values=new();
        public void Add(int key,CycleBehavior child)=>values.Add(key,child);
        public System.Collections.Generic.IEnumerator<CycleBehavior> GetEnumerator()=>values.Values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
} }
""";
        const string declaration="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static StudioDesktopEntryCollection StudioDesktopEntryList()=>new StudioDesktopEntryCollection(){ {11,new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100)} };
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[new(helper,supplied[helper]+declaration)],Player=previous.Player with{Invulnerable=true}},null);
        EditExternalDefinition(document.AllNodes.SelectMany(document.ExternalDefinitions).First(r=>r.Label=="StudioDesktopEntryList"));WorkspaceTabs.SelectedIndex=1;
        var root=AuthoringRoot.Id;var original=AuthoringRoot.Children.Single();var before=document.Source;var primaryIds=PrimaryDocument.Identities;
        check(document.SourceFile==helper&&document.Find(original.Id).Kind=="InitializerEntry","The shared collection opens its actual keyed entry and declaring source");
        var previousRead=readNodeClipboard;var previousWrite=writeNodeClipboard;BehaviorClipboard? clipboard=null;
        try
        {
            readNodeClipboard=()=>clipboard;writeNodeClipboard=value=>clipboard=value;SelectNode(original.Id);CopySelectedNodes();
            check(clipboard?.Symbols.Count>0&&clipboard.Expressions.Single().TrimStart().StartsWith('{'),"Actual shared-entry Copy captures the complete pair and compiler binding");SelectNode(root);
            PrimaryDocument.LockedNodes.Add(EncounterRoot.Id);var blocked=false;try{PasteWorkspaceNodes(root,1,clipboard!);}catch(InvalidOperationException){blocked=true;}
            check(blocked&&document.Source==before&&workspacePhaseUndo.Count==0,"A native caller lock blocks shared-entry Paste atomically");PrimaryDocument.LockedNodes.Clear();
            SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(s=>s.Path==helper);SourceEditor.Text+="\n// retained shared-entry draft";var draft=SourceEditor.Text;blocked=false;
            try{PasteWorkspaceNodes(root,1,clipboard!);}catch(InvalidOperationException){blocked=true;}
            check(blocked&&SourceEditor.Text==draft&&document.Source==before&&workspacePhaseUndo.Count==0,"Shared-entry Paste preserves an affected source draft");DiscardSourceDraft(this,new());SelectNode(root);
            PasteSelectedNodes();var imported=selectedNode!.Id;var child=document.Find(imported).Children.Single().Id;
            check(imported!=original.Id&&child!=original.Children.Single().Id&&AuthoringRoot.Children.Count==2&&nodeSelection.SetEquals([imported]),"Actual Paste selects an independent complete pair in the shared source");
            check(workspacePhaseUndo.Count==1&&Snapshot().Source==source&&primaryIds.All(i=>PrimaryDocument.Identities.Any(n=>n.Id==i.Id)),"Shared-entry Paste commits one workspace edit while retaining caller source and identities");
            Undo(this,new());check(document.Source==before&&AuthoringRoot.Id==root&&!document.AllNodes.Any(n=>n.Id==imported),"One desktop Undo restores the shared collection and focused body");
            Redo(this,new());check(document.Find(imported).Children.Single().Id==child&&AuthoringRoot.Children.Count==2,"One desktop Redo restores exact pasted entry and child identities");
            SelectNode(imported);Inspector.Children.OfType<TextBox>().Single().Text="7";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(document.Find(imported).Parameters.Single().Expression=="7"&&document.Find(original.Id).Parameters.Single().Expression=="11","The pasted shared entry accepts an independent key without editing the original pair");
            var path=Path.Combine(output,"shared-entry.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
            check(encounterDocument!=null&&AuthoringRoot.Id==root&&document.Find(imported).Children.Single().Id==child&&document.Find(imported).Parameters.Single().Expression=="7","Save/reopen restores the shared body, accepted key and exact pair identities");
            check(await StartSessionAsync()&&session is CompiledRuntime,"Play compiles the caller together with the pasted shared collection");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();
            check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{3,3}),"Native desktop playback executes both accepted shared Add entries");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(imported);UpdateLayout();CaptureView(this,Path.Combine(output,"shared-entry-editor.png"));
            check(supplied.All(p=>File.ReadAllText(p.Key)==p.Value),"Shared-entry clipboard, history, persistence and playback leave supplied game files unchanged");
        }
        finally{readNodeClipboard=previousRead;writeNodeClipboard=previousWrite;}
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
