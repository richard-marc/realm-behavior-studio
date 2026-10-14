using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void CollectionEntrySmoke()=>await TryRuntime(async()=>
    {
        var output=System.IO.Path.GetFullPath(".artifacts/collection-entry-desktop");System.IO.Directory.CreateDirectory(output);var checks=new List<string>();
        await RunCollectionEntryUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunCollectionEntryUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primaryFile=selectedEntity!.SourceFile;var supplied=System.IO.File.ReadAllText(primaryFile);
        const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopCollectionEntry=()=>Behav().Init("Cave Pirate Brawler",new State("loop",StudioDesktopCollectionWrap(new StudioDesktopEntryLists(){ {20,new CycleBehavior[]{new Shoot(20,count:4,fixedAngle:0,coolDown:100), /* list UI note */ new Shoot(20,count:2,fixedAngle:0,coolDown:100)},new Shoot(20,count:1,fixedAngle:0,coolDown:100)}, {"small",new System.Collections.Generic.List<CycleBehavior>{new Shoot(20,count:3,fixedAngle:0,coolDown:100)},null} })));
    static Prioritize StudioDesktopCollectionWrap(System.Collections.Generic.IEnumerable<CycleBehavior> children)=>new Prioritize(System.Linq.Enumerable.ToArray(children));
    sealed class StudioDesktopEntryLists:System.Collections.Generic.IEnumerable<CycleBehavior> {
        readonly System.Collections.Generic.SortedDictionary<int,CycleBehavior[]> values=new();
        public void Add(int key,CycleBehavior[] primary,CycleBehavior after=null)=>values.Add(key,System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(System.Linq.Enumerable.Reverse(primary),System.Linq.Enumerable.Where(new[]{after},c=>c!=null))));
        public void Add(string key,System.Collections.Generic.IReadOnlyList<CycleBehavior> primary,CycleBehavior after=null)=>values.Add(key.Length,System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(System.Linq.Enumerable.Reverse(primary),System.Linq.Enumerable.Where(new[]{after},c=>c!=null))));
        public System.Collections.Generic.IEnumerator<CycleBehavior> GetEnumerator()=>System.Linq.Enumerable.SelectMany(values.Values,v=>v).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;
        var list=EncounterRoot.Descendants().Single(n=>n.Kind=="ArgumentGroup"&&n.Children.Any(c=>c.Kind=="InitializerEntry"));var first=list.Children[0];var second=list.Children[1];var array=first.Children.Single(n=>n.Argument?.Name=="primary");var after=first.Children.Single(n=>n.Argument?.Name=="after");SelectNode(array.Id);
        check(chooseChildSlotButton?.IsEnabled==true&&(string)chooseChildSlotButton.Content=="+ Add child","An entry-owned native list offers its actual Add child chooser");
        SelectNode(array.Children[1].Id);upStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(document.Find(array.Id).Children.First().Id==array.Children[1].Id&&document.Source.Contains("/* list UI note */"),"Actual Up reorders a list element while preserving entry comments");
        deleteStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(document.Find(array.Id).Children.Count==1&&document.Find(after.Id).Children.Single().Id==after.Children.Single().Id,"Actual Delete removes only the list child and retains the neighboring input");
        SelectNode(array.Id);BeginNativeCreation(adapter!.Primitives.Single(p=>p.Name=="Shoot"));constructorInputs["radius"].Text="20";constructorInputs["count"].Text="8";constructorInputs["fixedAngle"].Text="0";constructorInputs["coolDown"].Text="100";createNativeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(array.Id).Children.Count==2&&document.Find(array.Id).Children.Last().Parameters.Any(p=>p.Name=="count"&&p.Expression=="8"),"The real native constructor form appends a typed list child");
        SelectNode(first.Id);check(entryCreationChildren["primary"].Text.StartsWith("new CycleBehavior[]")&&entryCreationChildren["after"].Text.StartsWith("new Shoot"),"New entry shows the complete collection expression separately from the scalar behavior input");
        entryCreationInputs["key"].Text="7";entryCreationChildren["primary"].Text="[new Shoot(20,count:5,fixedAngle:0,coolDown:100),new Shoot(20,count:9,fixedAngle:0,coolDown:100)]";entryCreationChildren["after"].Text="";var before=document.Source;var revision=document.Revision;entryCreationButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var created=selectedNode!.Id;
        check(created!=first.Id&&document.Revision==revision+1&&document.Find(created).Children.Single(n=>n.Argument?.Name=="primary").Children.Count==2,"Actual Create commits a complete target-typed list in one revision");
        check(document.Find(created).Children.Single(n=>n.Argument?.Name=="after").Children.Count==0,"Blank optional creation input uses its actual native default");
        Undo(this,new());check(document.Source==before&&!document.AllNodes.Any(n=>n.Id==created),"One editor Undo removes the complete collection-valued entry");Redo(this,new());check(document.Find(created).Children.Single(n=>n.Argument?.Name=="primary").Children.Count==2,"One editor Redo restores the whole list and its descendants");
        var concrete=second.Children.Single(n=>n.Argument?.Name=="primary");SelectNode(concrete.Id);check(chooseChildSlotButton?.IsEnabled==true&&concrete.Argument?.CollectionType?.Contains("IReadOnlyList")==true,"A concrete list initializer retains its formal read-only Add input in the editor");
        document.LockedNodes.Add(array.Id);SelectNode(array.Id);check(chooseChildSlotButton?.IsEnabled==false,"A locked entry-owned list disables native insertion");document.LockedNodes.Clear();
        var path=System.IO.Path.Combine(output,"collection-entry.rbs");var identities=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);check(document.Identities.SequenceEqual(identities),"Save/reopen retains collection slots and every element identity");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Collection-valued Add entries use compiled native playback");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{3,9,5,8,4,1}),"Native Play uses the actual Add reversal and sorted entry enumeration");Stop(this,new());WorkspaceTabs.SelectedIndex=1;
        SelectNode(first.Id);Inspector.Children.OfType<Expander>().Single(e=>Equals(e.Header,"New entry")).IsExpanded=true;UpdateLayout();CaptureView(this,System.IO.Path.Combine(output,"collection-entry-editor.png"));
        check(System.IO.File.ReadAllText(primaryFile)==supplied,"Collection-valued editing and native playback leave supplied game source unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
