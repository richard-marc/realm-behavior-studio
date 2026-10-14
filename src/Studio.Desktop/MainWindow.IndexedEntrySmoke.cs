using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    async void IndexedEntrySmoke()=>await TryRuntime(async()=>
    {
        var output=System.IO.Path.GetFullPath(".artifacts/indexed-entry-desktop");System.IO.Directory.CreateDirectory(output);var checks=new List<string>();
        await RunIndexedEntryUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunIndexedEntryUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=System.IO.File.ReadAllText(file);
        const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic{public partial class BehaviorDb{
    private _ StudioIndexFixture=()=>Behav().Init("Cave Pirate Brawler",new State("motion",StudioIndexWrap(new StudioIndexChildren(){ [7]=new Shoot(20,count:4,fixedAngle:0,coolDown:100),/* index note */[2]=new Shoot(20,count:6,fixedAngle:0,coolDown:100) })));
    static Prioritize StudioIndexWrap(StudioIndexChildren children)=>new Prioritize(System.Linq.Enumerable.ToArray(children));
    sealed class StudioIndexChildren:System.Collections.Generic.IEnumerable<CycleBehavior>{
        readonly System.Collections.Generic.SortedDictionary<int,CycleBehavior> values=new();
        public CycleBehavior this[int key]{get=>values[key];set=>values[key]=value;}
        public System.Collections.Generic.IEnumerator<CycleBehavior> GetEnumerator()=>values.Values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}}
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;var collection=EncounterRoot.Descendants().Single(n=>n.Kind=="ArgumentGroup"&&document.Expression(n).StartsWith("new StudioIndexChildren"));var first=collection.Children[0];var second=collection.Children[1];SelectNode(first.Id);
        check(first.Kind=="InitializerEntry"&&entryCreationInputs.ContainsKey("key")&&entryCreationChildren.ContainsKey("value"),"Indexed inspector presents the actual scalar key and native setter value");SelectNode(second.Id);upStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(document.Find(collection.Id).Children[0].Id==second.Id&&document.Source.Contains("/* index note */"),"Actual Up preserves indexed source syntax and comments");Undo(this,new());SelectNode(collection.Id);check(entryOverloadChoices?.Items.Count==1&&entryOverloadChoices.Items.OfType<InitializerEntryOverload>().Single().IsIndexer,"New entry discovers the actual indexed setter contract");entryCreationInputs["key"].Text="3";entryCreationChildren["value"].Text="new Shoot(20,count:8,fixedAngle:0,coolDown:100)";var revision=document.Revision;entryCreationButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var created=selectedNode!.Id;check(selectedNode.Kind=="InitializerEntry"&&document.Revision==revision+1&&document.Expression(selectedNode).StartsWith("[3]"),"Actual Create commits one indexed key/value assignment");Undo(this,new());check(document.Source==source,"One editor Undo restores the original indexed initializer");Redo(this,new());check(document.Find(created).Kind=="InitializerEntry","One editor Redo restores the indexed entry identity");document.LockedNodes.Add(collection.Id);SelectNode(collection.Id);check(entryCreationButton?.IsEnabled==false,"Indexed collection locks disable creation");document.LockedNodes.Clear();
        var path=System.IO.Path.Combine(output,"indexed-entry.rbs");var identities=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);check(document.Identities.SequenceEqual(identities),"Indexed source and all descendant identities survive save/reopen");check(await StartSessionAsync(),"Native Play compiles the original indexer property setter");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{6,8,4}),"Play retains actual setter and sorted enumeration order");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(collection.Id);Inspector.Children.OfType<Expander>().Single(e=>Equals(e.Header,"New entry")).IsExpanded=true;UpdateLayout();CaptureView(this,System.IO.Path.Combine(output,"indexed-entry-editor.png"));check(System.IO.File.ReadAllText(file)==supplied,"Indexed controls and native Play preserve supplied source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
