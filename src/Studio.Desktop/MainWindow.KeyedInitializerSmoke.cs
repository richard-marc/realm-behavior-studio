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
        SelectNode(first.Id);check(entryCreationButton?.IsEnabled==true,"A bound Add entry exposes the new pair creation form");entryCreationInputs["key"].Text="7";entryCreationChild!.Text="new Shoot(20,count:6,fixedAngle:0,coolDown:100)";
        var beforeCreation=document.Source;var creationRevision=document.Revision;entryCreationButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var created=selectedNode!.Id;
        check(created!=first.Id&&document.Revision==creationRevision+1&&document.Find(created).Parameters.Single().Expression=="7"&&document.Find(created).Children.Single().Parameters.Any(p=>p.Expression=="6"),"Actual Create entry accepts the key and behavior in one revision with new identities");
        Undo(this,new());check(document.Source==beforeCreation&&!document.AllNodes.Any(n=>n.Id==created),"One editor Undo removes the complete new Add entry");Redo(this,new());check(document.Find(created).Parameters.Single().Expression=="7","One editor Redo restores the new pair and its entry identity");
        var previousRead=readNodeClipboard;var previousWrite=writeNodeClipboard;BehaviorClipboard? captured=null;
        try
        {
            readNodeClipboard=()=>captured;writeNodeClipboard=value=>captured=value;SelectNode(first.Id);CopySelectedNodes();
            check(captured?.Expressions.Count==1&&captured.Symbols.Count>0,"Actual keyed Copy captures a complete pair and its native Add binding");var beforePaste=document.Source;SelectNode(list.Id);PasteSelectedNodes();var imported=selectedNode!.Id;
            check(imported!=first.Id&&document.Find(imported).Kind=="InitializerEntry"&&document.Find(imported).Children.Single().Id!=child,"Actual keyed Paste creates independent entry and child identities in the selected collection");
            Undo(this,new());check(document.Source==beforePaste&&!document.AllNodes.Any(n=>n.Id==imported),"One editor Undo restores source after keyed clipboard insertion");Redo(this,new());check(document.Find(imported).Parameters.Single().Expression=="5","One editor Redo retains the pasted pair and its key");SelectNode(imported);deleteStructureButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        finally{readNodeClipboard=previousRead;writeNodeClipboard=previousWrite;}
        SelectNode(first.Id);Inspector.Children.OfType<CheckBox>().Single().IsChecked=true;check(parameterApplyButton?.IsEnabled==false&&duplicateStructureButton?.IsEnabled==false,"A keyed-entry region lock protects scalar and structural controls");document.LockedNodes.Remove(first.Id);SelectNode(first.Id);
        var path=Path.Combine(output,"keyed-initializer.rbs");BehaviorDocument.Save(path,Snapshot());ApplyProject(BehaviorDocument.Load(path),path);
        check(document.Find(first.Id).Parameters.Single().Expression=="5"&&document.Find(child).Id==child&&document.Find(list.Id).Children.First().Id==second.Id,"Save/reopen retains keyed input order, edits and replacement identity");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Play compiles the actual custom Add and sorted-enumeration implementation");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();
        check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{8,6,3}),"Native playback follows edited key sorting including the newly created pair");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(first.Id);UpdateLayout();CaptureView(this,Path.Combine(output,"keyed-initializer-editor.png"));
        var multiSource=source.Replace("{20,new Shoot(20,count:4,fixedAngle:0,coolDown:100)}","{20,new Shoot(20,count:4,fixedAngle:0,coolDown:100),new Shoot(20,count:2,fixedAngle:0,coolDown:100)}").Replace("{11,new Shoot(20,count:3,fixedAngle:0,coolDown:100)}","{11,new Shoot(20,count:3,fixedAngle:0,coolDown:100),new Shoot(20,count:1,fixedAngle:0,coolDown:100)}").Replace("CycleBehavior child)=>values.Add(key,child)","CycleBehavior child,CycleBehavior fallback)=>values.Add(key,new[]{child,fallback})").Replace("SortedDictionary<int,CycleBehavior>","SortedDictionary<int,CycleBehavior[]>").Replace("values.Values.GetEnumerator()","System.Linq.Enumerable.SelectMany(values.Values,v=>v).GetEnumerator()");
        ApplyProject(previous with{Source=multiSource,Identities=[],SourceDrafts=[],SelectedSourceFile=null,LockedNodes=[],CollapsedNodes=[],Layout=[],AdditionalSources=[],Player=previous.Player with{Invulnerable=true}},null);WorkspaceTabs.SelectedIndex=1;
        var multiList=EncounterRoot.Descendants().Single(n=>n.Kind=="ArgumentGroup"&&n.Children.Any(c=>c.Kind=="InitializerEntry"));var multiEntry=multiList.Children[0];SelectNode(multiEntry.Id);
        check(entryCreationChildren.Keys.SequenceEqual(new[]{"child","fallback"})&&entryCreationChildren.Values.All(n=>n.Text.StartsWith("new Shoot")),"The New entry form exposes each compiler-bound behavior input by name");
        entryCreationInputs["key"].Text="7";entryCreationChildren["child"].Text="new Shoot(20,count:6,fixedAngle:0,coolDown:100)";entryCreationChildren["fallback"].Text="new Shoot(20,count:5,fixedAngle:0,coolDown:100)";
        var multiRevision=document.Revision;entryCreationButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var multiCreated=selectedNode!.Id;
        check(multiCreated!=multiEntry.Id&&document.Revision==multiRevision+1&&document.Find(multiCreated).Children.Count==2,"Actual Create entry commits all keyed behavior inputs in one source revision");
        Undo(this,new());check(document.Source==multiSource&&!document.AllNodes.Any(n=>n.Id==multiCreated),"One editor Undo removes the complete multi-input entry");Redo(this,new());check(document.Find(multiCreated).Children.Select(n=>n.Argument!.Name).SequenceEqual(new[]{"child","fallback"}),"One editor Redo retains both typed slot identities");
        var left=document.Find(multiEntry.Id).Children.Single(n=>n.Argument?.Name=="child");var right=document.Find(multiEntry.Id).Children.Single(n=>n.Argument?.Name=="fallback");var rightChild=right.Children.Single().Id;SelectNode(left.Id);
        document.ReplaceChild(left.Id,"new Shoot(20,count:8,fixedAngle:0,coolDown:100)",ValidateAuthoring);Changed(left.Id);
        check(document.Find(right.Id).Children.Single().Id==rightChild&&document.Find(left.Id).Children.Single().Parameters.Any(p=>p.Name=="count"&&p.Expression=="8"),"Selecting a typed Add slot and replacing it retains the neighboring native input");
        SelectNode(multiEntry.Id);var multiPath=Path.Combine(output,"multi-keyed-initializer.rbs");BehaviorDocument.Save(multiPath,Snapshot());var multiIds=document.Identities;ApplyProject(BehaviorDocument.Load(multiPath),multiPath);
        check(document.Identities.SequenceEqual(multiIds),"Editor save/reopen retains every multi-input Add entry, slot and child identity");
        check(await StartSessionAsync()&&session is CompiledRuntime,"Multi-input Add entries use compiled native playback");running=false;await session!.AdvanceAsync(new(),100);UpdateSession();check(session.Shots.Select(s=>s.Count).SequenceEqual(new[]{6,5,3,1,8,2}),"Native playback runs all inputs in the actual Add and key-sorted order");Stop(this,new());WorkspaceTabs.SelectedIndex=1;SelectNode(multiEntry.Id);Inspector.Children.OfType<Expander>().Single(e=>Equals(e.Header,"New entry")).IsExpanded=true;UpdateLayout();CaptureView(this,Path.Combine(output,"multi-keyed-initializer-editor.png"));
        check(File.ReadAllText(primary)==supplied,"Keyed Add authoring, history, persistence and native playback leave supplied game source unchanged");ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;
    }
}
