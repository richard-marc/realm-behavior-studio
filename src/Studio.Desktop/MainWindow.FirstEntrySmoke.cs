using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async void FirstEntrySmoke()=>await TryRuntime(async()=>
    {
        var output=System.IO.Path.GetFullPath(".artifacts/first-entry-desktop");System.IO.Directory.CreateDirectory(output);var checks=new List<string>();
        await RunFirstEntryUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunFirstEntryUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=System.IO.File.ReadAllText(file);
        const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDesktopFirst=()=>Behav().Init("Cave Pirate Brawler",new State("loop",StudioDesktopFirstWrap(new StudioDesktopFirstChildren(){ /* first entry note */ })));
    static Prioritize StudioDesktopFirstWrap(System.Collections.Generic.IEnumerable<CycleBehavior> children)=>new Prioritize(System.Linq.Enumerable.ToArray(children));
    sealed class StudioDesktopFirstChildren:System.Collections.Generic.IEnumerable<CycleBehavior> {
        readonly System.Collections.Generic.List<CycleBehavior> values=new();
        public void Add(int key,params CycleBehavior[] children)=>values.AddRange(System.Linq.Enumerable.Reverse(children));
        public void Add(string key,CycleBehavior child)=>values.Add(child);
        public System.Collections.Generic.IEnumerator<CycleBehavior> GetEnumerator()=>values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
} }
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;
        var collection=EncounterRoot.Descendants().Single(n=>n.Kind=="ArgumentGroup"&&document.Expression(n).Contains("new StudioDesktopFirstChildren"));SelectNode(collection.Id);
        check(collection.Children.Count==0&&entryOverloadChoices?.Items.Count==2&&entryCreationButton?.IsEnabled==true,"An empty collection offers its real Add overloads without an existing entry");entryOverloadChoices!.SelectedItem=entryOverloadChoices.Items.OfType<InitializerEntryOverload>().Single(o=>o.Metadata.Children.Single().IsParams);
        check(entryCreationInputs.ContainsKey("key")&&entryCreationChildren.ContainsKey("children")&&entryOverloadChoices.SelectedItem.ToString()!.StartsWith("Add("),"Selected params overload presents readable native labels and scalar/child-list inputs");entryCreationInputs["key"].Text="7";entryCreationChildren["children"].Text="new CycleBehavior[]{new Shoot(20,count:5,fixedAngle:0,coolDown:100),new Shoot(20,count:9,fixedAngle:0,coolDown:100)}";var revision=document.Revision;entryCreationButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var created=selectedNode!.Id;
        check(selectedNode.Kind=="InitializerEntry"&&document.Revision==revision+1&&document.Find(created).Children.Single().Children.Count==2&&document.Source.Contains("/* first entry note */"),"Actual Create commits the first complete native entry while retaining comments");Undo(this,new());check(document.Source==source&&document.Find(collection.Id).Children.Count==0,"One editor Undo restores the truly empty collection");Redo(this,new());check(document.Find(created).Children.Single().Children.Count==2,"One editor Redo retains fresh entry descendants");
        SelectNode(collection.Id);entryOverloadChoices!.SelectedItem=entryOverloadChoices.Items.OfType<InitializerEntryOverload>().Single(o=>o.Metadata.Parameters[0].Type=="String");check(entryCreationChildren.ContainsKey("child")&&!entryCreationChildren.ContainsKey("children"),"Overload selection replaces the form with the actual scalar child contract");entryCreationInputs["key"].Text="\"second\"";entryCreationChildren["child"].Text="new Shoot(20,count:3,fixedAngle:0,coolDown:100)";entryCreationButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(document.Find(collection.Id).Children.Count==2,"A nonempty collection creates a second entry using another native overload");
        document.LockedNodes.Add(collection.Id);SelectNode(collection.Id);check(entryCreationButton?.IsEnabled==false,"Collection region locks disable prototype-free creation");document.LockedNodes.Clear();
        var path=System.IO.Path.Combine(output,"first-entry.rbs");var identities=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);check(document.Identities.SequenceEqual(identities),"First-entry source, overloads and identities survive save/reopen");check(await StartSessionAsync()&&session is CompiledRuntime,"Play compiles original custom Add implementations");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{9,5,3}),"Native Play follows the original params reversal and string overload");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(collection.Id);Inspector.Children.OfType<Expander>().Single(e=>Equals(e.Header,"New entry")).IsExpanded=true;UpdateLayout();CaptureView(this,System.IO.Path.Combine(output,"first-entry-editor.png"));
        check(System.IO.File.ReadAllText(file)==supplied,"Prototype-free controls and native Play preserve supplied source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
