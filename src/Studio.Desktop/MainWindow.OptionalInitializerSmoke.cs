using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void OptionalInitializerSmoke()=>await TryRuntime(async()=>
    {
        var output=System.IO.Path.GetFullPath(".artifacts/optional-initializer-desktop");System.IO.Directory.CreateDirectory(output);var checks=new List<string>();
        await RunOptionalInitializerUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunOptionalInitializerUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var primaryFile=selectedEntity!.SourceFile;var supplied=System.IO.File.ReadAllText(primaryFile);
        const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioOptionalUi=()=>Behav().Init("Cave Pirate Brawler",new State("loop",StudioOptionalUiWrap(new StudioOptionalUiChildren(){ {20}, /* optional UI note */ {11,null,System.DayOfWeek.Friday,new Shoot(20,count:3,fixedAngle:0,coolDown:100)} })));
    static Prioritize StudioOptionalUiWrap(System.Collections.Generic.IEnumerable<CycleBehavior> children)=>new Prioritize(System.Linq.Enumerable.ToArray(children));
    sealed class StudioOptionalUiChildren:System.Collections.Generic.IEnumerable<CycleBehavior> {
        readonly System.Collections.Generic.SortedDictionary<int,CycleBehavior[]> values=new();
        public void Add(int key,CycleBehavior primary=null,System.DayOfWeek mode=System.DayOfWeek.Friday,CycleBehavior fallback=null)=>values.Add(key,System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(new[]{primary,fallback},c=>c!=null)));
        public System.Collections.Generic.IEnumerator<CycleBehavior> GetEnumerator()=>System.Linq.Enumerable.SelectMany(values.Values,v=>v).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;
        var list=EncounterRoot.Descendants().Single(n=>n.Kind=="ArgumentGroup"&&n.Children.Any(c=>c.Kind=="InitializerEntry"));var first=list.Children[0];var second=list.Children[1];SelectNode(first.Id);
        check(entryCreationChildren.Count==2&&entryCreationChildren.Values.All(n=>n.Text==""),"New entry shows blank optional input fields for omitted native defaults");
        check(Inspector.Children.OfType<TextBlock>().First().Text=="Collection entry","Optional entries use the compact collection inspector heading");
        entryCreationInputs["key"].Text="7";entryCreationChildren["fallback"].Text="new Shoot(20,count:5,fixedAngle:0,coolDown:100)";
        var revision=document.Revision;entryCreationButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var created=selectedNode!.Id;
        check(created!=first.Id&&document.Revision==revision+1&&document.Find(created).Children.Single(n=>n.Argument?.Name=="primary").Children.Count==0,"Actual Create entry accepts a blank optional input and commits one source revision");
        check(document.Find(created).Parameters.Any(p=>p.Name=="mode"&&p.Expression=="(global::System.DayOfWeek)5")&&document.Find(created).Children.Single(n=>n.Argument?.Name=="fallback").Children.Single().Kind=="Shoot","Creation pads omitted positions with actual typed defaults before the later input");
        Undo(this,new());check(document.Source==source&&!document.AllNodes.Any(n=>n.Id==created),"One editor Undo restores the original omitted-input entry syntax");Redo(this,new());check(document.Find(created).Children.Count==2,"One editor Redo restores empty and populated optional slots");
        var fallback=document.Find(created).Children.Single(n=>n.Argument?.Name=="fallback");var child=fallback.Children.Single().Id;SelectNode(child);
        check(deleteStructureButton?.IsEnabled==true,"A populated optional input enables actual child removal");deleteStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(document.Find(fallback.Id).Children.Count==0&&document.Find(created).Parameters.Any(p=>p.Name=="mode"&&p.Expression=="(global::System.DayOfWeek)5"),"Actual Delete restores the native null default without moving scalar inputs");
        SelectNode(fallback.Id);check(chooseChildSlotButton?.IsEnabled==true&&(string)chooseChildSlotButton.Content=="Choose child","The emptied optional slot offers refill through the native child chooser");
        document.AddChild(fallback.Id,"new Shoot(20,count:6,fixedAngle:0,coolDown:100)",ValidateAuthoring);Changed(fallback.Id);
        check(document.Find(fallback.Id).Children.Single().Parameters.Any(p=>p.Name=="count"&&p.Expression=="6"),"Refill preserves the selected optional slot and its source position");
        document.LockedNodes.Add(fallback.Id);SelectNode(fallback.Id);check(chooseChildSlotButton?.IsEnabled==false,"A locked optional slot disables its child chooser");document.LockedNodes.Clear();
        SelectNode(second.Id);entryCreationInputs["key"].Text="9";entryCreationChildren["fallback"].Text="";entryCreationButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var empty=selectedNode!.Id;
        check(empty!=second.Id&&document.Find(empty).Children.All(n=>n.Children.Count==0),"Blank creation fields reset populated prototype inputs to native defaults");
        var path=System.IO.Path.Combine(output,"optional-initializer.rbs");var identities=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(document.Identities.SequenceEqual(identities),"Save/reopen retains every empty and populated optional slot identity");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Optional Add entries use compiled native playback");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{6,3}),"Actual native Add defaults skip empty entries while executing populated inputs in sorted order");Stop(this,new());WorkspaceTabs.SelectedIndex=1;
        SelectNode(first.Id);Inspector.Children.OfType<Expander>().Single(e=>Equals(e.Header,"New entry")).IsExpanded=true;UpdateLayout();CaptureView(this,System.IO.Path.Combine(output,"optional-entry-editor.png"));
        check(System.IO.File.ReadAllText(primaryFile)==supplied,"Optional editing, persistence and native playback leave supplied game source unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
