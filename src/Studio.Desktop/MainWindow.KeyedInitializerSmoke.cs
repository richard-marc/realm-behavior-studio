using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void KeyedInitializerSmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/keyed-initializer-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunKeyedInitializerUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunKeyedInitializerUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primary=selectedEntity!.SourceFile;var supplied=File.ReadAllText(primary);
        const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopKeyed=()=>Behav().Init("Cave Pirate Brawler",new State("loop",StudioDesktopKeyedWrap(new StudioDesktopKeyedChildren(){ {20,new Shoot(20,count:4,fixedAngle:0,coolDown:100)},/* keyed UI note */{11,new Shoot(20,count:3,fixedAngle:0,coolDown:100)} })));
    static Prioritize StudioDesktopKeyedWrap(System.Collections.Generic.IEnumerable<CycleBehavior> children)=>new Prioritize(System.Linq.Enumerable.ToArray(children));
    sealed class StudioDesktopKeyedChildren:System.Collections.Generic.IEnumerable<CycleBehavior> {
        readonly System.Collections.Generic.SortedDictionary<int,CycleBehavior> values=new();
        public void Add(int key,CycleBehavior child)=>values.Add(key,child);
        public System.Collections.Generic.IEnumerator<CycleBehavior> GetEnumerator()=>values.Values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;
        var list=EncounterRoot.Descendants().Single(n=>n.Kind=="ArgumentGroup"&&n.Children.Any(c=>c.Kind=="InitializerEntry"));var first=list.Children[0];var second=list.Children[1];SelectNode(first.Id);
        check(parameterApplyButton?.IsEnabled==true&&Inspector.Children.OfType<TextBox>().Single().Text=="20","A keyed Add entry exposes its actual scalar parameter beside the native child");Inspector.Children.OfType<TextBox>().Single().Text="5";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(first.Id).Parameters.Single().Expression=="5"&&document.Find(first.Id).Children.Single().Id==first.Children.Single().Id,"Actual key parameter edits retain entry and child source identities");
        duplicateStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var copied=selectedNode!.Id;
        check(copied!=first.Id&&document.Find(list.Id).Children.Count==3&&document.Find(copied).Children.Single().Id!=first.Children.Single().Id,"Actual keyed Duplicate retains the complete pair and allocates new entry/child identities");Inspector.Children.OfType<TextBox>().Single().Text="7";parameterApplyButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));deleteStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(list.Id).Children.Count==2&&document.Find(first.Id).Parameters.Single().Expression=="5","Actual Delete removes the complete copied Add entry while retaining neighboring keys");
        SelectNode(second.Id);upStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(document.Find(list.Id).Children.First().Id==second.Id&&document.Source.Contains("/* keyed UI note */"),"Entry reorder keeps paired inputs and comments in source order");Undo(this,new());Redo(this,new());
        SelectNode(first.Id);BeginNativeCreation(adapter.Primitives.Single(p=>p.Name=="Shoot"));constructorInputs["radius"].Text="20";constructorInputs["count"].Text="8";constructorInputs["fixedAngle"].Text="0";constructorInputs["coolDown"].Text="100";createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var child=selectedNode!.Id;
        check(document.Find(first.Id).Children.Single().Id==child&&document.Find(first.Id).Parameters.Single().Expression=="5"&&document.Find(child).Parameters.Any(p=>p.Name=="count"&&p.Expression=="8"),"Native constructor replacement changes only the required behavior and preserves its Add key");
        SelectNode(first.Id);Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;check(parameterApplyButton?.IsEnabled==false&&duplicateStructureButton?.IsEnabled==false,"A keyed-entry region lock protects scalar and structural controls");document.LockedNodes.Remove(first.Id);SelectNode(first.Id);
        var path=Path.Combine(output,"keyed-initializer.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(document.Find(first.Id).Parameters.Single().Expression=="5"&&document.Find(child).Id==child&&document.Find(list.Id).Children.First().Id==second.Id,"Save/reopen retains keyed input order, edits and replacement identity");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Play compiles the actual custom Add and sorted-enumeration implementation");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();
        check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{8,3}),"Native playback follows edited key sorting despite the opposite source entry order");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(first.Id);UpdateLayout();CaptureView(this,Path.Combine(output,"keyed-initializer-editor.png"));
        check(File.ReadAllText(primary)==supplied,"Keyed Add authoring, history, persistence and native playback leave supplied game source unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
