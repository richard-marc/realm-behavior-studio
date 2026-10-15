using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    async void FragmentDependencySmoke() => await TryRuntime(async () =>
    {
        var output=Path.GetFullPath(".artifacts/fragment-dependency-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool passed,string label){if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);}
        var source=File.ReadAllText(selectedEntity!.SourceFile);var imported=new BehaviorDocument(source,adapter);var root=imported.Entities.Single(e=>e.EntityId==selectedEntity.Id).Root;
        await RunFragmentUiSmoke(source,root,output,Check);await RunFragmentDependencyUiSmoke(output,Check);await RunPartialFragmentUiSmoke(output,Check);
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunFragmentDependencyUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=File.ReadAllText(file);var previousDirectory=fragmentDirectory;
        fragmentDirectory=Path.Combine(output,"library");RefreshFragments();
        const string source="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{const int PortableDefault=3;static Behavior PortableFan(int count)=>new Shoot(20,count:count,coolDown:100);private _ StudioPortableDesktop=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"origin\",PortableFan(PortableDefault)));}}";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true},Arena=new()},null);
        WorkspaceTabs.SelectedIndex=1;var factory=EncounterRoot.Children.Single();SelectAuthoringNode(factory.Id,ModifierKeys.None);FragmentCaptureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));UpdateLayout();
        check(fragmentCapturing&&FragmentIncludeDependencies is{IsVisible:true,IsChecked:true},"Save selection offers helper bundling for source-dependent native fragments");
        var count=fragmentFields.Single(f=>f.Parameter.Label.EndsWith("· count"));count.Expose.IsChecked=true;count.Input.Text="shots";FragmentName.Text="Portable shared fan";
        FragmentSaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var saved=fragmentEditing!;
        check(saved.Dependencies?.Members.Count==2&&saved.Version==2&&FragmentInfo.Text.Contains("includes 2"),"Actual Save stores the helper and constant as a versioned portable fragment");
        check(fragmentFields.Single().Input.Text=="PortableDefault","Template fields display their original source default rather than internal import tokens");
        const string targetSource="namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioPortableDesktop=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"destination\"));}}";
        ApplyProject(previous with{Source=targetSource,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true},Arena=new()},null);
        SelectNode(EncounterRoot.Id);ShowFragment(saved,false);fragmentFields.Single().Input.Text="7";var before=document.Source;FragmentPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Source==before&&FragmentInsertButton.IsEnabled&&FragmentPreview.Text.Contains("Included source definitions")&&FragmentPreview.Text.Contains("Fragment_PortableFan"),"Preview shows actual destination helper declarations and changed inputs without editing accepted source");
        WorkspaceTabs.SelectedIndex=4;UpdateLayout();CaptureView(this,Path.Combine(output,"portable-fragment-preview.png"));
        if(FragmentPreview.Parent is Expander preview){preview.IsExpanded=true;UpdateLayout();CaptureView(this,Path.Combine(output,"portable-fragment-definitions.png"));preview.IsExpanded=false;}
        fragmentFields.Single().Input.Text="8";check(!FragmentInsertButton.IsEnabled&&document.Source==before,"Changing a portable template value invalidates its preview without editing source");fragmentFields.Single().Input.Text="7";PrepareFragmentPreview();
        document.LockedNodes.Add(EncounterRoot.Id);var locked=false;try{CommitFragmentInsertion();}catch(InvalidOperationException){locked=true;}document.LockedNodes.Clear();check(locked&&document.Source==before,"A new destination lock rejects declarations and selected behavior insertion together");PrepareFragmentPreview();
        FragmentInsertButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var imported=document.Source;
        check(document.Revision==1&&selectedNode?.Kind=="Factory"&&document.Source.Contains("Fragment_PortableDefault"),"Insert commits new declarations and the native helper call together");
        Undo(this,new());check(document.Source==before,"One desktop Undo removes all imported dependencies and behaviors");Redo(this,new());check(document.Source==imported,"Desktop Redo restores the complete portable fragment");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Imported desktop helpers initialize through the original compiled runtime");running=false;await session!.AdvanceAsync(new(),100);Pause(this,new());
        check(session.Shots.Single().Count==7,"Actual desktop template input changes the native emitted projectile count");Stop(this,new());
        var path=Path.Combine(output,"portable-import.rbs");BehaviorDocument.Save(path,Snapshot());var ids=document.Identities;ApplyProject(BehaviorDocument.Load(path),path);
        check(document.Source==imported&&document.Identities.SequenceEqual(ids),"Portable declarations and source identities survive desktop save/reopen");
        check(File.ReadAllText(file)==supplied,"Portable helper capture, preview and Play preserve supplied game files");
        fragmentDirectory=previousDirectory;RefreshFragments();ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
