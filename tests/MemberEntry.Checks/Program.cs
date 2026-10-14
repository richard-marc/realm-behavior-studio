using Studio.Core;
using Studio.Raple;
var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
const string entity="Cave Pirate Brawler";var file=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var supplied=File.ReadAllText(file);
const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioMemberEntriesFixture=()=>Behav().Init("Cave Pirate Brawler",new State("loop",new StudioMemberController {
        Actions = { {7,new Shoot(20,count:4,fixedAngle:0,coolDown:100)},/* member entry note */{2,new Shoot(20,count:6,fixedAngle:0,coolDown:100)} },
        Indexed = { [3] = new Shoot(20,count:5,fixedAngle:0,coolDown:100) },
        Empty = { }
    }));
    sealed class StudioMemberController : CycleBehavior {
        public StudioMemberEntries Actions {get;}=new();
        public System.Collections.Generic.Dictionary<int,CycleBehavior> Indexed {get;}=new(){[0]=new Shoot(20,count:8,fixedAngle:0,coolDown:100)};
        public StudioMemberEntries Empty {get;}=new();
        protected override void TickCore(TKR.WorldServer.core.objects.Entity host,TKR.WorldServer.core.worlds.TickTime time,ref object state){foreach(var b in Actions)b.Tick(host,time);foreach(var b in Indexed.Values)b.Tick(host,time);foreach(var b in Empty)b.Tick(host,time);}
    }
    sealed class StudioMemberEntries : System.Collections.Generic.IEnumerable<CycleBehavior> {
        readonly System.Collections.Generic.SortedDictionary<int,CycleBehavior> values=new(){[0]=new Shoot(20,count:9,fixedAngle:0,coolDown:100)};
        public void Add(int key,CycleBehavior child)=>values[key]=child;
        public System.Collections.Generic.IEnumerator<CycleBehavior> GetEnumerator()=>values.Values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
} }
""";
var doc=new BehaviorDocument(source,adapter,sourceFile:file);
BehaviorNode Slot(string name)=>doc.AllNodes.Single(n=>n.Argument is{IsMember:true}&&n.Argument.Name==name);
var actions=Slot("Actions");var indexed=Slot("Indexed");var empty=Slot("Empty");var ids=doc.Identities;var first=actions.Children[0];var second=actions.Children[1];var index=indexed.Children.Single();
Check(actions.Children.All(n=>n.Kind=="InitializerEntry")&&index.Kind=="InitializerEntry"&&empty.Children.Count==0,"Getter-filled native Add, indexed and empty member bodies project actual entry owners");
Check(first.Parameters.Single().Name=="key"&&first.Children.Single().Kind=="Shoot"&&index.Parameters.Single().Name=="key","Member Add/scalar keys and actual setter values retain native input contracts");
Check(doc.AcceptsChild(actions.Id,"{3,new Shoot(20)}")&&doc.AcceptsChild(indexed.Id,"[4]=new Shoot(20)")&&!doc.AcceptsChild(actions.Id,"{3,new TKR.WorldServer.logic.transitions.TimedTransition(100,\"loop\")}"),"Member entry preflight binds actual getter receivers and compatible native conversions");
doc.Apply(new(doc.Revision,"Change member key",[new(first.Id,"key","1")]),d=>adapter.Validate(d,entity));Check(doc.Find(first.Id).Parameters.Single().Expression=="1"&&Slot("Indexed").Id==indexed.Id,"Member key edits retain neighboring member ownership and identities");
var replacement=doc.ReplaceChild(first.Id,"new Shoot(20,count:7,fixedAngle:0,coolDown:100)",d=>adapter.Validate(d,entity));Check(doc.Find(replacement).Kind=="Shoot"&&doc.Expression(doc.Find(first.Id)).StartsWith("{1,"),"Typed member Add replacement preserves complete key/value entry syntax");
doc.MoveNode(second.Id,-1,d=>adapter.Validate(d,entity));Check(Slot("Actions").Children.First().Id==second.Id&&doc.Source.Contains("/* member entry note */"),"Member entry reorder retains comments and original entry identities");
var copy=doc.DuplicateNode(first.Id,d=>adapter.Validate(d,entity));Check(copy!=first.Id&&doc.Find(copy).Children.Single().Id!=replacement,"Complete member entry Duplicate allocates independent descendants");doc.Undo();
var contract=doc.EntryOverloads(actions.Id).Single();Check(!contract.IsIndexer&&contract.Metadata.Parameters.Select(p=>p.Name).SequenceEqual(new[]{"key","child"}),"Member New entry discovers the actual getter collection Add contract");
var created=doc.CreateCollectionEntry(actions.Id,contract.Signature,new Dictionary<string,string>{{"key","4"}},new Dictionary<string,string>{{"child","new Shoot(20,count:10,fixedAngle:0,coolDown:100)"}},d=>adapter.Validate(d,entity));Check(Slot("Actions").Children.Last().Id==created&&doc.Source.Contains("Actions = {"),"Native member creation keeps getter-fill syntax and commits a complete entry");
var indexContract=doc.EntryOverloads(indexed.Id).Single();Check(indexContract.IsIndexer,"Indexed member creation discovers the original Dictionary setter contract");
var indexedCreated=doc.CreateCollectionEntry(indexed.Id,indexContract.Signature,new Dictionary<string,string>{{"key","6"}},new Dictionary<string,string>{{"value","new Shoot(20,count:11,fixedAngle:0,coolDown:100)"}},d=>adapter.Validate(d,entity));Check(doc.Expression(doc.Find(indexedCreated)).StartsWith("[6]"),"Indexed member creation retains bracket/assignment form");
var emptyContract=doc.EntryOverloads(empty.Id).Single();var emptyCreated=doc.CreateCollectionEntry(empty.Id,emptyContract.Signature,new Dictionary<string,string>{{"key","5"}},new Dictionary<string,string>{{"child","new Shoot(20,count:12,fixedAngle:0,coolDown:100)"}},d=>adapter.Validate(d,entity));Check(Slot("Empty").Children.Single().Id==emptyCreated,"Empty getter-filled member creates its first native entry without a prototype");
var clipboard=doc.CopyNodes([first.Id]);var pasted=doc.PasteNodes(empty.Id,1,clipboard,d=>adapter.Validate(d,entity)).Single();Check(pasted!=first.Id&&doc.Find(pasted).Children.Single().Id!=replacement,"Member entry clipboard import retains bindings and allocates independent identities");doc.Undo();
var accepted=doc.Source;Reject(()=>doc.ReplaceChild(first.Id,"new TKR.WorldServer.logic.transitions.TimedTransition(100,\"loop\")"),"Member entry conversion rejects incompatible native child values");Check(doc.Source==accepted,"Rejected member entry edits preserve accepted source");
doc.LockedNodes.Add(doc.Parent(actions.Id)!.Id);Reject(()=>doc.CreateCollectionEntry(empty.Id,emptyContract.Signature,new Dictionary<string,string>{{"key","8"}},new Dictionary<string,string>{{"child","new Shoot(20)"}}),"Member wrapper locks protect entry creation across getter lists");doc.LockedNodes.Clear();
var project=new StudioProject{SourceFile=file,Source=doc.Source,EntityId=entity,Identities=doc.Identities,Player=new(Invulnerable:true)};
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,project)){await native.AdvanceAsync(new(),100);Check(native.Shots.Select(s=>s.Count).SequenceEqual(new[]{9,7,6,10,8,5,11,9,12}),"Native getter-generated content, sorted Add order, Dictionary setters and empty member insertion execute unchanged");}
var output=Path.GetFullPath(".artifacts/member-entry");Directory.CreateDirectory(output);var path=Path.Combine(output,"entries.rbs");BehaviorDocument.Save(path,project);var reopened=BehaviorDocument.Load(path);Check(new BehaviorDocument(reopened.Source,adapter,reopened.Identities,file).Identities.SequenceEqual(doc.Identities),"Complete member entry, value and getter-wrapper identities survive save/reopen");
while(doc.Source!=source)if(!doc.Undo())throw new Exception("Missing member entry history");Check(doc.Identities.SequenceEqual(ids),"Complete member entry history restores exact source and original identities");
doc.DeleteNodes(Slot("Actions").Children.Select(n=>n.Id),d=>adapter.Validate(d,entity));Check(Slot("Actions").Children.Count==0&&doc.EntryOverloads(actions.Id).Count==1,"Deleting every Add entry retains editable getter-fill ownership and native creation");
doc.CreateCollectionEntry(actions.Id,doc.EntryOverloads(actions.Id).Single().Signature,new Dictionary<string,string>{{"key","8"}},new Dictionary<string,string>{{"child","new Shoot(20)"}},d=>adapter.Validate(d,entity));Check(Slot("Actions").Children.Count==1,"Cleared member entry list refills without replacing its original getter");
var assignedSource=source.Replace("Actions = {", "Actions = new StudioMemberEntries { ").Replace("public StudioMemberEntries Actions {get;}=new();","public StudioMemberEntries Actions {get;set;}=new();");var assigned=new BehaviorDocument(assignedSource,adapter,sourceFile:file);var assignedList=assigned.AllNodes.Single(n=>n.Argument is{Name:"Actions",IsMember:true});Check(assignedList.Children.All(n=>n.Kind=="InitializerEntry")&&assigned.EntryOverloads(assignedList.Id).Count==1,"Assigned member collection constructors expose the same native entry contracts");
assigned.CreateCollectionEntry(assignedList.Id,assigned.EntryOverloads(assignedList.Id).Single().Signature,new Dictionary<string,string>{{"key","6"}},new Dictionary<string,string>{{"child","new Shoot(20)"}},d=>adapter.Validate(d,entity));Check(assigned.Expression(assigned.Find(assignedList.Id)).StartsWith("new StudioMemberEntries"),"Assigned member entry creation retains the original collection constructor");
var extensionSource=source.Replace("sealed class StudioMemberEntries", "internal sealed class StudioMemberEntries").Replace("public void Add(int key,CycleBehavior child)","public void Put(int key,CycleBehavior child)");extensionSource=extensionSource[..extensionSource.LastIndexOf("} }",StringComparison.Ordinal)]+"} internal static class StudioMemberEntryExtensions { public static void Add(this BehaviorDb.StudioMemberEntries target,int key,CycleBehavior child)=>target.Put(key,child); } }";var extensionDoc=new BehaviorDocument(extensionSource,adapter,sourceFile:file);var extensionList=extensionDoc.AllNodes.Single(n=>n.Argument is{Name:"Actions",IsMember:true});Check(extensionList.Children.All(n=>n.Kind=="InitializerEntry")&&extensionDoc.EntryOverloads(extensionList.Id).Single().Metadata.Parameters.Select(p=>p.Name).SequenceEqual(new[]{"key","child"}),"Member Add extensions reduce against the actual getter receiver without exposing it as an authored input");
extensionDoc.CreateCollectionEntry(extensionList.Id,extensionDoc.EntryOverloads(extensionList.Id).Single().Signature,new Dictionary<string,string>{{"key","1"}},new Dictionary<string,string>{{"child","new Shoot(20,count:10,fixedAngle:0,coolDown:100)"}},d=>adapter.Validate(d,entity));await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,project with{Source=extensionDoc.Source,Identities=extensionDoc.Identities})){await native.AdvanceAsync(new(),100);Check(native.Shots.Select(s=>s.Count).SequenceEqual(new[]{9,10,6,4,8,5,9}),"Native extension Add in getter-filled members preserves receiver mutation and original sorted execution");}
Check(File.ReadAllText(file)==supplied,"Member entry editing, history and native Play preserve supplied game source");Console.WriteLine($"{checks} native member-entry checks passed.");
