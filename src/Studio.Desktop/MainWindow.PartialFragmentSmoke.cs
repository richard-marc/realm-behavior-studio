using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async Task RunPartialFragmentUiSmoke(string output, Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=File.ReadAllText(file);
        var otherFile=adapter.Catalog().First(e=>e.SourceFile!=file).SourceFile;var otherOriginal=File.ReadAllText(otherFile);var previousDirectory=fragmentDirectory;
        fragmentDirectory=Path.Combine(output,"partial-library");RefreshFragments();
        const string source="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private static partial Behavior PartialDesktopFan(int count);private _ StudioPartialDesktop=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"origin\",PartialDesktopFan(3)));}}";
        const string implementation="namespace TKR.WorldServer.logic{public partial class BehaviorDb{static readonly TKR.WorldServer.logic.behaviors.Shoot PartialDesktopShared=new(20,count:2,coolDown:100);private static partial Behavior PartialDesktopFan(int count)=>new TKR.WorldServer.logic.behaviors.Timed(1000,PartialDesktopShared,new TKR.WorldServer.logic.behaviors.Shoot(20,count:count,coolDown:100));}}";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[new(otherFile,otherOriginal+"\n"+implementation)],Player=previous.Player with{Invulnerable=true},Arena=new()},null);
        WorkspaceTabs.SelectedIndex=1;var factory=EncounterRoot.Children.Single();SelectAuthoringNode(factory.Id,ModifierKeys.None);FragmentCaptureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));UpdateLayout();
        check(fragmentCapturing && FragmentIncludeDependencies is{IsChecked:true},"A partial method selection offers complete helper bundling across accepted source files");
        var count=fragmentFields.Single(f=>f.Parameter.Label.EndsWith("· count"));count.Expose.IsChecked=true;count.Input.Text="shots";FragmentName.Text="Portable partial fan";
        FragmentSaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var fragment=fragmentEditing!;
        check(fragment.Version==3 && fragment.Dependencies!.Members.Count==2 && fragment.Dependencies.Members.Single(m=>m.Parts.Count>0).Parts.Count==1,"Actual library Save writes both partial signatures and their shared native object as version 3");
        check(BehaviorFragment.Load(Path.Combine(fragmentDirectory,fragment.Id+".rbsfrag")).Version==3,"The library file reopens with explicit partial-declaration support");
        const string target="namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioPartialDesktop=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"destination\"));}}";
        ApplyProject(previous with{Source=target,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true},Arena=new()},null);
        SelectNode(EncounterRoot.Id);ShowFragment(fragment,false);fragmentFields.Single().Input.Text="7";FragmentPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Source==target && FragmentInsertButton.IsEnabled && FragmentPreview.Text.Contains("private static partial") && FragmentPreview.Text.Contains("Fragment_PartialDesktopShared"),"Mutation-free destination preview includes both method parts and the implementation's shared dependency");
        WorkspaceTabs.SelectedIndex=4;if(FragmentPreview.Parent is Expander preview)preview.IsExpanded=true;UpdateLayout();CaptureView(this,Path.Combine(output,"partial-fragment-definitions.png"));
        FragmentInsertButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var imported=document.Source;
        check(document.Revision==1 && selectedNode?.Kind=="Factory" && document.Source.Contains("Fragment_PartialDesktopFan"),"Actual Insert accepts all method parts and the template call in one revision");
        Undo(this,new());check(document.Source==target,"One desktop Undo removes complete partial helper declarations");Redo(this,new());check(document.Source==imported,"Desktop Redo restores the full partial bundle");
        check(await StartSessionAsync() && session is CompiledRuntime,"Portable partial desktop code loads in the original compiled runtime without its former source file");running=false;await session!.AdvanceAsync(new(),100);Pause(this,new());
        check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{2,7}),"Actual partial template arguments and preserved shared Shoot execute in native child order");Stop(this,new());
        var path=Path.Combine(output,"partial-import.rbs");BehaviorDocument.Save(path,Snapshot());var identities=document.Identities;ApplyProject(BehaviorDocument.Load(path),path);
        check(document.Source==imported && document.Identities.SequenceEqual(identities) && Snapshot().AdditionalSources.Count==0,"Saved partial imports reopen with their identities and no dependency on the former source overlay");
        check(File.ReadAllText(file)==supplied && File.ReadAllText(otherFile)==otherOriginal,"Desktop partial capture, preview and native Play preserve both supplied game files");
        fragmentDirectory=previousDirectory;RefreshFragments();ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
