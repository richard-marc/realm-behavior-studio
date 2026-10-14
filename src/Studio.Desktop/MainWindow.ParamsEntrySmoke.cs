using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void ParamsEntrySmoke()=>await TryRuntime(async()=>
    {
        var output=System.IO.Path.GetFullPath(".artifacts/params-entry-desktop");System.IO.Directory.CreateDirectory(output);var checks=new List<string>();
        await RunParamsEntryUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunParamsEntryUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=System.IO.File.ReadAllText(file);
        const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopParams=()=>Behav().Init("Cave Pirate Brawler",new State("loop",StudioDesktopParamsWrap(new StudioDesktopParamsChildren(){ {20,new Shoot(20,count:4,fixedAngle:0,coolDown:100),/* params UI note */new Shoot(20,count:2,fixedAngle:0,coolDown:100)},{11,new CycleBehavior[]{new Shoot(20,count:3,fixedAngle:0,coolDown:100)}},{9} })));
    static Prioritize StudioDesktopParamsWrap(System.Collections.Generic.IEnumerable<CycleBehavior> children)=>new Prioritize(System.Linq.Enumerable.ToArray(children));
    sealed class StudioDesktopParamsChildren:System.Collections.Generic.IEnumerable<CycleBehavior> {
        readonly System.Collections.Generic.SortedDictionary<int,CycleBehavior[]> values=new();
        public void Add(int key,params CycleBehavior[] children)=>values.Add(key,System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Reverse(children)));
        public System.Collections.Generic.IEnumerator<CycleBehavior> GetEnumerator()=>System.Linq.Enumerable.SelectMany(values.Values,v=>v).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;
        var list=EncounterRoot.Descendants().Single(n=>n.Kind=="ArgumentGroup"&&n.Children.Any(c=>c.Kind=="InitializerEntry"));var first=list.Children[0];var paramsSlot=first.Children.Single();var empty=list.Children[2].Children.Single();SelectNode(paramsSlot.Id);
        check(chooseChildSlotButton?.IsEnabled==true&&paramsSlot.Argument is{IsParams:true}&&paramsSlot.Children.Count==2,"Expanded native Add inputs expose the real typed child-list chooser");SelectNode(paramsSlot.Children[1].Id);upStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(document.Find(paramsSlot.Id).Children.First().Id==paramsSlot.Children[1].Id&&document.Source.Contains("/* params UI note */"),"Actual Up retains expanded params syntax and internal comments");deleteStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(document.Find(paramsSlot.Id).Children.Count==1&&document.Find(first.Id).Parameters.Single().Expression=="20","Actual Delete removes only one expanded argument while retaining its scalar key");Undo(this,new());Undo(this,new());
        SelectNode(first.Id);check(entryCreationChildren["children"].Text.Contains("CycleBehavior[]")&&entryCreationChildren["children"].Text.Contains("count:4")&&entryCreationChildren["children"].Text.Contains("count:2"),"New entry presents the complete expanded params input as a native array expression");entryCreationInputs["key"].Text="7";entryCreationChildren["children"].Text="[new Shoot(20,count:5,fixedAngle:0,coolDown:100),new Shoot(20,count:9,fixedAngle:0,coolDown:100)]";var revision=document.Revision;entryCreationButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var created=selectedNode!.Id;check(created!=first.Id&&document.Revision==revision+1&&document.Find(created).Children.Single().Children.Count==2,"Actual Create commits the complete params tail in one source revision");Undo(this,new());check(document.Source==source,"One editor Undo restores the original expanded/explicit/omitted source");Redo(this,new());check(document.Find(created).Children.Single().Children.Count==2,"One editor Redo restores params entry descendants");
        SelectNode(empty.Id);check(chooseChildSlotButton?.IsEnabled==true&&document.Find(empty.Id).Children.Count==0,"An omitted params input offers native refill");document.LockedNodes.Add(paramsSlot.Id);SelectNode(paramsSlot.Id);check(chooseChildSlotButton?.IsEnabled==false,"A params region lock disables insertion");document.LockedNodes.Clear();
        var path=System.IO.Path.Combine(output,"params-entry.rbs");var identities=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);check(document.Identities.SequenceEqual(identities),"Save/reopen retains entry, params slot and child identities");check(await StartSessionAsync()&&session is CompiledRuntime,"Play compiles the original custom params Add method");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{9,5,3,2,4}),"Native Play executes the actual params reversal and sorted entry order");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(first.Id);Inspector.Children.OfType<Expander>().Single(e=>Equals(e.Header,"New entry")).IsExpanded=true;UpdateLayout();CaptureView(this,System.IO.Path.Combine(output,"params-entry-editor.png"));
        check(System.IO.File.ReadAllText(file)==supplied,"Params controls, persistence and native Play preserve supplied source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
