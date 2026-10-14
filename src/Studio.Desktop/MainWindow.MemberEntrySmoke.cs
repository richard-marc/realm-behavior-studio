using System.IO;
using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    async void MemberEntrySmoke()=>await TryRuntime(async()=>
    {
        var output=Path.GetFullPath(".artifacts/member-entry-desktop");Directory.CreateDirectory(output);var checks=new List<string>();
        await RunMemberEntryUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        File.WriteAllText(Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunMemberEntryUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=File.ReadAllText(file);
        const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopMemberEntries=()=>Behav().Init("Cave Pirate Brawler",new State("loop",new StudioDesktopEntryController {
        Actions = { {7,new Shoot(20,count:4,fixedAngle:0,coolDown:100)},/* member UI note */{2,new Shoot(20,count:6,fixedAngle:0,coolDown:100)} },
        Indexed = { [3] = new Shoot(20,count:5,fixedAngle:0,coolDown:100) }
    }));
    sealed class StudioDesktopEntryController : CycleBehavior {
        public StudioDesktopEntryList Actions {get;}=new();
        public System.Collections.Generic.Dictionary<int,CycleBehavior> Indexed {get;}=new(){[0]=new Shoot(20,count:8,fixedAngle:0,coolDown:100)};
        protected override void TickCore(TKR.WorldServer.core.objects.Entity host,TKR.WorldServer.core.worlds.TickTime time,ref object state){foreach(var b in Actions)b.Tick(host,time);foreach(var b in Indexed.Values)b.Tick(host,time);}
    }
    sealed class StudioDesktopEntryList : System.Collections.Generic.IEnumerable<CycleBehavior> {
        readonly System.Collections.Generic.SortedDictionary<int,CycleBehavior> values=new(){[0]=new Shoot(20,count:9,fixedAngle:0,coolDown:100)};
        public void Add(int key,CycleBehavior child)=>values[key]=child;
        public System.Collections.Generic.IEnumerator<CycleBehavior> GetEnumerator()=>values.Values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;
        var actions=document.AllNodes.Single(n=>n.Argument is{Name:"Actions",IsMember:true});var indexed=document.AllNodes.Single(n=>n.Argument is{Name:"Indexed",IsMember:true});var first=actions.Children[0];var second=actions.Children[1];SelectNode(actions.Id);
        check(entryOverloadChoices?.Items.Count==1&&entryCreationInputs.ContainsKey("key")&&entryCreationChildren.ContainsKey("child")&&chooseChildSlotButton==null,"Getter-filled keyed member offers complete entry creation without an incompatible single-child control");
        SelectNode(second.Id);upStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(document.Find(actions.Id).Children.First().Id==second.Id&&document.Source.Contains("/* member UI note */"),"Actual Up preserves member entries, comments and identities");Undo(this,new());SelectNode(actions.Id);
        entryCreationInputs["key"].Text="1";entryCreationChildren["child"].Text="new Shoot(20,count:10,fixedAngle:0,coolDown:100)";var revision=document.Revision;entryCreationButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var created=selectedNode!.Id;
        check(selectedNode.Kind=="InitializerEntry"&&document.Revision==revision+1&&document.Source.Contains("Actions = {"),"Actual member Create commits a complete Add entry without replacing its getter");Undo(this,new());check(document.Source==source,"One Undo restores the complete original member initializer");Redo(this,new());check(document.Find(created).Kind=="InitializerEntry","One Redo restores the created member entry identity");
        SelectNode(indexed.Id);check(entryOverloadChoices?.Items.OfType<InitializerEntryOverload>().Single().IsIndexer==true&&entryCreationChildren.ContainsKey("value"),"Indexed member form uses the actual Dictionary setter value contract");entryCreationInputs["key"].Text="6";entryCreationChildren["value"].Text="new Shoot(20,count:11,fixedAngle:0,coolDown:100)";entryCreationButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var indexedCreated=selectedNode!.Id;check(document.Expression(selectedNode).StartsWith("[6]"),"Actual indexed member Create retains assignment syntax");
        var wrapper=document.Parent(actions.Id)!;document.LockedNodes.Add(wrapper.Id);SelectNode(actions.Id);check(entryCreationButton?.IsEnabled==false,"Member wrapper locks disable native entry creation");document.LockedNodes.Clear();
        var path=Path.Combine(output,"member-entries.rbs");var ids=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);check(document.Identities.SequenceEqual(ids)&&document.Find(indexedCreated).Kind=="InitializerEntry","Member entry source and descendant identities survive save/reopen");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Member entry Play starts original compiled getter initialization");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{9,10,6,4,8,5,11}),"Play preserves getter constructor content, sorted Add order and Dictionary setter enumeration");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(indexed.Id);Inspector.Children.OfType<Expander>().Single(e=>Equals(e.Header,"New entry")).IsExpanded=true;UpdateLayout();CaptureView(this,Path.Combine(output,"member-entry-editor.png"));
        check(File.ReadAllText(file)==supplied,"Member entry controls, persistence and native Play preserve supplied game source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
