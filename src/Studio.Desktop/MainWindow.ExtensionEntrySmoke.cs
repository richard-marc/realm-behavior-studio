using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    async void ExtensionEntrySmoke()=>await TryRuntime(async()=>
    {
        var output=System.IO.Path.GetFullPath(".artifacts/extension-entry-desktop");System.IO.Directory.CreateDirectory(output);var checks=new List<string>();
        await RunExtensionEntryUiSmoke(output,(passed,label)=>{if(!passed)throw new InvalidOperationException("UI check failed: "+label);checks.Add(label);});
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));Application.Current.Shutdown(0);
    });
    async Task RunExtensionEntryUiSmoke(string output,Action<bool,string> check)
    {
        var previous=Snapshot();var file=selectedEntity!.SourceFile;var supplied=System.IO.File.ReadAllText(file);
        const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic{
    public partial class BehaviorDb{
        private _ StudioExtensionFixture=()=>Behav().Init("Cave Pirate Brawler",new State("motion",StudioExtensionWrap(new StudioExtensionChildren(){ /* extension entry */ {7,new Shoot(20,count:4,fixedAngle:0,coolDown:100)} })));
        static Prioritize StudioExtensionWrap(StudioExtensionChildren children)=>new Prioritize(System.Linq.Enumerable.ToArray(children));
    }
    sealed class StudioExtensionChildren:System.Collections.Generic.IEnumerable<CycleBehavior>{
        internal readonly System.Collections.Generic.List<CycleBehavior> Values=new();
        public System.Collections.Generic.IEnumerator<CycleBehavior> GetEnumerator()=>Values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    static class StudioExtensionAdd{
        public static void Add(this StudioExtensionChildren target,int key,CycleBehavior child)=>target.Values.Insert(0,child);
        public static void Add(this StudioExtensionChildren target,string key,CycleBehavior child)=>target.Values.Add(child);
    }
}
""";
        ApplyProject(previous with{Source=source,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;var collection=EncounterRoot.Descendants().Single(n=>n.Kind=="ArgumentGroup"&&document.Expression(n).StartsWith("new StudioExtensionChildren"));var original=collection.Children.Single();SelectNode(original.Id);check(entryCreationInputs.ContainsKey("key")&&entryCreationChildren.ContainsKey("child")&&!entryCreationInputs.ContainsKey("target"),"Imported extension entry form excludes its receiver and exposes actual native inputs");SelectNode(collection.Id);check(entryOverloadChoices?.Items.Count==2&&entryCreationButton?.IsEnabled==true,"Collection New entry presents actual in-scope extension overloads");entryOverloadChoices!.SelectedItem=entryOverloadChoices.Items.OfType<InitializerEntryOverload>().Single(c=>c.Metadata.Parameters[0].Type=="Int32");entryCreationInputs["key"].Text="2";entryCreationChildren["child"].Text="new Shoot(20,count:6,fixedAngle:0,coolDown:100)";var revision=document.Revision;entryCreationButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var created=selectedNode!.Id;
        check(selectedNode.Kind=="InitializerEntry"&&document.Revision==revision+1&&document.Source.Contains("/* extension entry */"),"Actual Create commits the selected extension Add entry and retains comments");Undo(this,new());check(document.Source==source,"One editor Undo restores the original extension initializer");Redo(this,new());check(document.Find(created).Kind=="InitializerEntry","One editor Redo retains the extension entry identity");SelectNode(collection.Id);entryOverloadChoices!.SelectedItem=entryOverloadChoices.Items.OfType<InitializerEntryOverload>().Single(c=>c.Metadata.Parameters[0].Type=="String");entryCreationInputs["key"].Text="\"last\"";entryCreationChildren["child"].Text="new Shoot(20,count:8,fixedAngle:0,coolDown:100)";entryCreationButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(document.Find(collection.Id).Children.Count==3,"Actual overload selection invokes a separate string-key extension contract");document.LockedNodes.Add(collection.Id);SelectNode(collection.Id);check(entryCreationButton?.IsEnabled==false,"Extension collection region locks disable insertion");document.LockedNodes.Clear();
        var path=System.IO.Path.Combine(output,"extension-entry.rbs");var identities=document.Identities;BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);check(document.Identities.SequenceEqual(identities),"Extension binding and every identity survive save/reopen");check(await StartSessionAsync(),"Native Play compiles the original extension methods");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{6,4,8}),"Native Play follows actual extension prepend/append semantics");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(collection.Id);Inspector.Children.OfType<Expander>().Single(e=>Equals(e.Header,"New entry")).IsExpanded=true;UpdateLayout();CaptureView(this,System.IO.Path.Combine(output,"extension-entry-editor.png"));check(System.IO.File.ReadAllText(file)==supplied,"Extension entry controls and native Play preserve supplied source");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
