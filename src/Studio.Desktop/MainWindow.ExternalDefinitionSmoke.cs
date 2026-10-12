using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void ExternalDefinitionSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/external-definition-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunExternalDefinitionUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunExternalDefinitionUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var other=catalog.First(e=>e.SourceFile!=primary).SourceFile;
        var supplied=File.ReadAllText(primary);var suppliedOther=File.ReadAllText(other);
        const string source="""
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopExternalFixture=()=>Behav().Init("Cave Pirate Brawler",StudioDesktopExternalRoot()).Init("Pirate Lieutenant",StudioDesktopExternalRoot());
} }
""";
        const string helper="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioDesktopExternalRoot()=>new State("loop",StudioDesktopExternalShot,new TKR.WorldServer.logic.behaviors.Timed(1000,StudioDesktopExternalList));
    static readonly TKR.WorldServer.logic.behaviors.Shoot StudioDesktopExternalShot=new TKR.WorldServer.logic.behaviors.Shoot(20,count:2,fixedAngle:0,coolDown:100);
    static Behavior[] StudioDesktopExternalList=[new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100),new TKR.WorldServer.logic.behaviors.Shoot(20,count:5,fixedAngle:0,coolDown:100)];
} }
""";
        ApplyProject(previous with{SourceDrafts=[],SelectedSourceFile=null,Source=source,Identities=[],LockedNodes=[],CollapsedNodes=[],AdditionalSources=[new(other,suppliedOther+helper)],Layout=[],Player=previous.Player with{Invulnerable=true}},null);
        Button EditButton(string name)=>Inspector.Children.OfType<StackPanel>().SelectMany(p=>p.Children.OfType<Button>()).Single(b=>Equals(b.Content,"Edit "+name));
        var primaryIds=document.Identities;var caller=EncounterRoot.Id;
        check(EditButton("StudioDesktopExternalRoot").ToolTip!.ToString()==Path.GetFileName(other),"Native caller inspector exposes its actual external helper with file provenance");
        EditButton("StudioDesktopExternalRoot").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var root=document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopExternalRoot");
        var shot=document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopExternalShot");var list=document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopExternalList");
        check(encounterDocument!=null&&document.SourceFile==other&&PrimaryDocument.Source==source&&selectedEntity.SourceFile==primary,"Opening an external helper changes the authoring document while preserving the selected native caller");
        check(AuthoringRoot.Id==root.Root.Id&&Graph.Root!.Id==root.Root.Id&&TreeItems().Any(i=>((BehaviorNode)i.Tag).Id==root.Root.Id),"External definition hierarchy and graph use the original helper body");
        check(DefinitionTitle.Text.Contains(Path.GetFileName(other))&&root.Definition!.Encounters.Count==2,"Definition banner names the real file and both discovered callers");
        SelectNode(root.Root.Children.Single(n=>n.Kind=="Reference").Id);EditButton("StudioDesktopExternalShot").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(AuthoringRoot.Id==shot.Root.Id&&EncounterRoot.Kind=="Factory","Transitive field navigation preserves the primary encounter root");
        Inspector.Children.OfType<TextBox>().Single(t=>t.Text=="2").Text="7";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(Snapshot().Source==source&&Snapshot().AdditionalSources.Single().Source.Contains("count:7")&&primaryIds.All(i=>PrimaryDocument.Identities.Any(n=>n.Id==i.Id)),"Inspector Apply edits only the shared source and retains all caller identities");
        Undo(this,new());check(document.Source.Contains("count:2")&&AuthoringRoot.Id==shot.Root.Id,"External visual Undo restores the shared file and focused body identity");
        Redo(this,new());check(document.Source.Contains("count:7")&&Snapshot().Source==source,"External visual Redo retains the caller source");
        PrimaryDocument.LockedNodes.Add(caller);UpdateOrganizeButtons();var before=document.Source;var rejected=false;
        try{document.Apply(new(document.Revision,"Locked caller",[new(shot.Root.Id,"count","9")]),ValidateAuthoring);}catch(InvalidOperationException){rejected=true;}
        check(!parameterApplyButton!.IsEnabled&&rejected&&document.Source==before,"Caller locks immediately disable Apply and guard external edits atomically");PrimaryDocument.LockedNodes.Clear();UpdateOrganizeButtons();
        var invalid=new BehaviorDocument(document.Source.Replace("count:7","count:0"),adapter,document.Identities,document.SourceFile);
        check(ValidateAuthoring(invalid).Any(d=>d.IsError&&d.Message.Contains("Invalid value for count")),"Helper-only authoring validates native value limits alongside the complete project");
        document.LockedNodes.Add(shot.Root.Id);UpdateOrganizeButtons();
        check(Snapshot().AdditionalSources.Single().LockedNodes.Contains(shot.Root.Id)&&!parameterApplyButton.IsEnabled,"External locks persist in the dependency snapshot and disable editing");document.LockedNodes.Clear();UpdateOrganizeButtons();
        EditDefinition(root.EntityId);var timed=AuthoringRoot.Children.Single(n=>n.Kind=="Timed");SelectNode(timed.Children.Single().Id);EditButton("StudioDesktopExternalList").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SelectNode(AuthoringRoot.Children[1].Id);upStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(AuthoringRoot.Children[0].Parameters.Single(p=>p.Name=="count").Expression=="5"&&Snapshot().Source==source,"Organize reorders the genuine collection in another file without rewriting the caller");
        Undo(this,new());check(AuthoringRoot.Children[0].Parameters.Single(p=>p.Name=="count").Expression=="3","Shared collection Undo restores its original order");Redo(this,new());
        document.LockedNodes.Add(shot.Root.Id);CollapsedNodes.Add(list.Root.Id);var bodyIds=document.Identities;
        var path=Path.Combine(output,"external-definition.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(encounterDocument!=null&&AuthoringRoot.Id==list.Root.Id&&DefinitionBanner.Visibility==Visibility.Visible&&bodyIds.All(i=>document.Identities.Any(n=>n.Id==i.Id)),"Save/reopen restores the external definition view and every body identity");
        check(document.LockedNodes.Contains(shot.Root.Id)&&CollapsedNodes.Contains(list.Root.Id)&&Snapshot().Source==source,"Reopened shared source retains its locks and collapse state alongside the caller");
        UpdateLayout();CaptureView(this,Path.Combine(output,"external-definition-editor.png"));
        check(await StartSessionAsync()&&session is CompiledRuntime,"Play from the external definition uses the complete native caller project");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();
        check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{7,5,3}),"Desktop native playback observes the edited external field and reordered collection");Stop(this,new());
        SelectNode(caller);check(encounterDocument==null&&selectedNode!.Id==caller,"Runtime caller navigation safely returns from a foreign definition to its actual source document");
        EditButton("StudioDesktopExternalRoot").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        DefinitionBack.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(encounterDocument==null&&AuthoringRoot.Id==EncounterRoot.Id&&document.Source==source&&Snapshot().AdditionalSources.Single().Source.Contains("count:7"),"Return to encounter restores the caller view while retaining accepted shared edits");
        check(File.ReadAllText(primary)==supplied&&File.ReadAllText(other)==suppliedOther,"Cross-file visual authoring and native Play leave supplied game files unchanged");
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
