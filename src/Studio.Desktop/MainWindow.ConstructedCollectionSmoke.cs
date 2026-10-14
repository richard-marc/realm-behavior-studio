using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    async void ConstructedCollectionSmoke()=>await TryRuntime(async()=>
    {
        var output=System.IO.Path.GetFullPath(".artifacts/constructed-collection-desktop");System.IO.Directory.CreateDirectory(output);var checks=new List<string>();
        await RunConstructedCollectionUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunConstructedCollectionUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=System.IO.File.ReadAllText(file);
        const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic{public partial class BehaviorDb{
    private _ StudioConstructedCollection=()=>Behav().Init("Cave Pirate Brawler",new State("motion",StudioConstructedWrap(new StudioConstructedChildren(4) /* retain constructor note */)));
    static Prioritize StudioConstructedWrap(StudioConstructedChildren children)=>new Prioritize(System.Linq.Enumerable.ToArray(children));
    sealed class StudioConstructedChildren:System.Collections.Generic.IEnumerable<CycleBehavior>{
        readonly System.Collections.Generic.List<CycleBehavior> values=new();
        public StudioConstructedChildren(int count)=>values.Add(new Shoot(20,count:count,fixedAngle:0,coolDown:100));
        public void Add(int key,CycleBehavior child)=>values.Add(child);
        public System.Collections.Generic.IEnumerator<CycleBehavior> GetEnumerator()=>values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}}
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;var collection=EncounterRoot.Descendants().Single(n=>n.Kind=="ArgumentGroup"&&document.Expression(n).StartsWith("new StudioConstructedChildren"));SelectNode(collection.Id);
        check(!collection.ArgumentRequiresSource&&entryCreationButton?.IsEnabled==true&&entryOverloadChoices?.Items.Count==1,"A collection without braces offers the actual New entry form");check(Inspector.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("constructor-generated runtime contents")),"Inspector distinguishes zero authored entries from native constructor-generated content");entryCreationInputs["key"].Text="7";entryCreationChildren["child"].Text="new Shoot(20,count:6,fixedAngle:0,coolDown:100)";var revision=document.Revision;entryCreationButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var created=selectedNode!.Id;
        check(selectedNode.Kind=="InitializerEntry"&&document.Revision==revision+1&&document.Source.Contains("StudioConstructedChildren(4)")&&document.Source.Contains("/* retain constructor note */"),"Actual Create materializes the initializer while preserving constructor arguments and comments");Undo(this,new());check(document.Source==source&&!document.Expression(document.Find(collection.Id)).Contains("{"),"One editor Undo restores the exact constructor without braces");Redo(this,new());check(document.Find(created).Kind=="InitializerEntry","One editor Redo retains the fresh native entry identity");document.LockedNodes.Add(collection.Id);SelectNode(collection.Id);check(entryCreationButton?.IsEnabled==false,"Constructor-owned collection locks disable entry creation");document.LockedNodes.Clear();
        var path=System.IO.Path.Combine(output,"constructed-collection.rbs");var identities=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);check(document.Identities.SequenceEqual(identities),"Materialized source and every identity survive project save/reopen");check(await StartSessionAsync(),"Native Play starts the preserved collection constructor");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{4,6}),"Play retains constructor-generated attacks before the newly authored entry");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(collection.Id);Inspector.Children.OfType<Expander>().Single(e=>Equals(e.Header,"New entry")).IsExpanded=true;UpdateLayout();CaptureView(this,System.IO.Path.Combine(output,"constructed-collection-editor.png"));check(System.IO.File.ReadAllText(file)==supplied,"Constructor materialization controls and Play preserve supplied game source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
