using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void ConstantFragmentSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/constant-fragment-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunConstantFragmentUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunConstantFragmentUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=File.ReadAllText(file);var oldDirectory=fragmentDirectory;fragmentDirectory=Path.Combine(output,"library");RefreshFragments();
        const string source="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{static State StudioDesktopConstants(){const int shots=3;const double range=20;return new State(\"origin\",new Shoot(range,count:shots,coolDown:100));}private _ StudioDesktopConstantOrigin=()=>Behav().Init(\"Cave Pirate Brawler\",StudioDesktopConstants());}}";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[],LockedNodes=[],CollapsedNodes=[],Layout=[],Player=previous.Player with{Invulnerable=true},Arena=new()},null);
        EditDefinition(document.Definitions.Single(d=>d.Definition!.Label=="StudioDesktopConstants").EntityId);var shoot=AuthoringRoot.Descendants().Single(n=>n.Kind=="Shoot");SelectAuthoringNode(shoot.Id,ModifierKeys.None);FragmentCaptureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(fragmentCapturing&&FragmentIncludeDependencies.Visibility==Visibility.Visible&&FragmentIncludeDependencies.IsChecked==true,"Actual Save selection offers portable source capture for a lexical constructor");
        var cadence=fragmentFields.Single(f=>f.Parameter.Label.EndsWith("· coolDown"));cadence.Expose.IsChecked=true;cadence.Input.Text="cadence";FragmentName.Text="Portable local constants";FragmentSaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var saved=fragmentEditing!;
        check(!fragmentCapturing&&saved.Version==2&&saved.Contents.Expressions.Single().Contains("const int @shots = 3")&&File.Exists(Path.Combine(fragmentDirectory,saved.Id+".rbsfrag")),"Actual Save writes the typed lexical scope into the reusable library file");
        const string target="namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioDesktopConstantTarget=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"destination\"));}}";
        ApplyProject(previous with{Source=target,Identities=[],SourceDrafts=[],SelectedSourceFile=null,AdditionalSources=[],LockedNodes=[],CollapsedNodes=[],Layout=[],Player=previous.Player with{Invulnerable=true},Arena=new()},null);SelectNode(EncounterRoot.Id);ShowFragment(saved,false);fragmentFields.Single().Input.Text="200";var before=document.Source;FragmentPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(FragmentInsertButton.IsEnabled&&document.Source==before&&FragmentPreview.Text.Contains("@shots")&&FragmentPreview.Text.Contains("200"),"Preview compiles local constants and the exposed input in the actual destination without mutation");
        UpdateLayout();CaptureView(this,Path.Combine(output,"constant-fragment-preview.png"));FragmentInsertButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var imported=document.Source;
        check(document.Revision==1&&document.Definitions.Any(d=>d.Definition!.Label=="Inline factory"),"Insert commits one native expression with a navigable inline factory body");
        Undo(this,new());check(document.Source==before,"One desktop Undo removes the complete lexical fragment");Redo(this,new());check(document.Source==imported,"Redo restores the full lexical expression and scope");
        EditDefinition(document.Definitions.Single(d=>d.Definition!.Label=="Inline factory").EntityId);var nativeShoot=AuthoringRoot.Descendants().Single(n=>n.Kind=="Shoot");SelectNode(nativeShoot.Id);
        check(selectedNode?.Id==nativeShoot.Id&&Inspector.Children.OfType<TextBox>().Any(t=>t.Text=="shots"),"The original Shoot remains visually editable inside its accepted inline source body");
        check(await StartSessionAsync(observeCallbacks:true),"Desktop Play compiles and loads the portable lexical expression");running=false;await session!.AdvanceAsync(new(),100);Pause(this,new());
        check(session.Shots.Single().Count==3&&session.Callbacks.Any(c=>c.Behavior.EndsWith(".Shoot")&&c.Origin?.NodeId==nativeShoot.Id),"Actual native volley retains constant inputs and exact callback construction identity");Stop(this,new());
        var path=Path.Combine(output,"constants.rbs");BehaviorDocument.Save(path,Snapshot());var identities=document.Identities;ApplyProject(BehaviorDocument.Load(path),path);
        check(document.Source==imported&&document.Identities.SequenceEqual(identities)&&document.AllNodes.Any(n=>n.Id==nativeShoot.Id),"Save/reopen retains the complete lexical source and editable native identities");
        check(File.ReadAllText(file)==supplied,"Portable local capture, navigation and Play preserve supplied game files");fragmentDirectory=oldDirectory;RefreshFragments();ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
