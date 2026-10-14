using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
const string entity="Cave Pirate Brawler";var file=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var supplied=File.ReadAllText(file);
const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioParamsEntry=()=>Behav().Init("Cave Pirate Brawler",new State("loop",StudioParamsWrap(new StudioParamsChildren(){ /* empty list note */ })));
    static Prioritize StudioParamsWrap(System.Collections.Generic.IEnumerable<CycleBehavior> children)=>new Prioritize(System.Linq.Enumerable.ToArray(children));
    sealed class StudioParamsChildren:System.Collections.Generic.IEnumerable<CycleBehavior> {
        readonly System.Collections.Generic.SortedDictionary<int,CycleBehavior[]> values=new();
        public void Add(int key,params CycleBehavior[] children)=>values.Add(key,System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Reverse(children)));
        public System.Collections.Generic.IEnumerator<CycleBehavior> GetEnumerator()=>System.Linq.Enumerable.SelectMany(values.Values,v=>v).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
} }
""";
var doc=new BehaviorDocument(source,adapter,sourceFile:file);var list=doc.Entities.Single().Root.Descendants().Single(n=>n.Kind=="ArgumentGroup"&&doc.Expression(n).Contains("new StudioParamsChildren"));var ids=doc.Identities;
var contracts=doc.EntryOverloads(list.Id);Check(contracts.Count==1&&contracts[0].Metadata.Children.Single().IsParams,"Empty native collection discovers the actual params Add overload without a prototype");
var contract=contracts.Single();var revision=doc.Revision;
var created=doc.CreateCollectionEntry(list.Id,contract.Signature,new Dictionary<string,string>{{"key","7"}},new Dictionary<string,string>{{"children","new CycleBehavior[]{new Shoot(20,count:5,fixedAngle:0,coolDown:100),new Shoot(20,count:9,fixedAngle:0,coolDown:100)}"}},d=>adapter.Validate(d,entity));
Check(doc.Revision==revision+1&&doc.Find(created).Kind=="InitializerEntry"&&doc.Find(created).Children.Single().Children.Count==2,"First entry creates scalar and native params children in one validated revision");Check(doc.Source.Contains("/* empty list note */")&&ids.All(i=>doc.Identities.Any(n=>n.Id==i.Id)),"First entry preserves the empty-list comment and existing identities");
var accepted=doc.Source;var fresh=doc.Identities;Check(doc.Undo()&&doc.Source==source&&doc.Identities.SequenceEqual(ids)&&doc.Redo()&&doc.Source==accepted&&doc.Identities.SequenceEqual(fresh),"One Undo/Redo restores first-entry source and fresh descendants");
var second=doc.CreateCollectionEntry(list.Id,contract.Signature,new Dictionary<string,string>{{"key","8"}},new Dictionary<string,string>(),d=>adapter.Validate(d,entity));Check(doc.Find(second).Children.Single().Children.Count==0,"Omitted params children create an empty array from the actual Add contract");doc.Undo();
Reject(()=>doc.CreateCollectionEntry(list.Id,contract.Signature,new Dictionary<string,string>(),new Dictionary<string,string>()),"Missing required scalar cannot create an entry");Reject(()=>doc.CreateCollectionEntry(list.Id,contract.Signature,new Dictionary<string,string>{{"key","8"}},new Dictionary<string,string>{{"children","new TKR.WorldServer.logic.transitions.TimedTransition(100,\"loop\")"}}),"Incompatible native children reject before source mutation");Check(doc.Source==accepted&&doc.Identities.SequenceEqual(fresh),"Rejected first-entry inputs retain accepted source, identities and history");
doc.LockedNodes.Add(list.Id);Reject(()=>doc.CreateCollectionEntry(list.Id,contract.Signature,new Dictionary<string,string>{{"key","8"}},new Dictionary<string,string>()),"Collection locks protect prototype-free creation");doc.LockedNodes.Clear();
var project=new StudioProject{SourceFile=file,Source=doc.Source,EntityId=entity,Identities=doc.Identities,Player=new(Invulnerable:true)};
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,project)){await native.AdvanceAsync(new(),100);Check(native.Shots.Select(s=>s.Count).SequenceEqual(new[]{9,5}),"Native Play executes the first entry's original Add reversal and enumeration");}
const string overloadSource="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioFirstOverload=()=>Behav().Init("Cave Pirate Brawler",new State("loop",StudioFirstWrap(new StudioFirstDerived() { /* overload note */ })));
    static State StudioFirstWrap(System.Collections.Generic.IEnumerable<IStateChildren> values)=>new State(children:System.Linq.Enumerable.ToArray(values));
    enum StudioFirstMode { Forward=2, Reverse=7 }
    class StudioFirstBase<T>:System.Collections.Generic.IEnumerable<IStateChildren> where T:Behavior {
        protected readonly System.Collections.Generic.List<IStateChildren> values=new();
        public void Add(int key,T child,StudioFirstMode mode=StudioFirstMode.Reverse,Transition gate=null){if(mode==StudioFirstMode.Reverse)values.Insert(0,child);else values.Add(child);if(gate!=null)values.Add(gate);}
        public System.Collections.Generic.IEnumerator<IStateChildren> GetEnumerator()=>values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    sealed class StudioFirstDerived:StudioFirstBase<CycleBehavior> {
        public void Add(string key,CycleBehavior child)=>values.Add(child);
        public void Add(uint key,CycleBehavior child)=>values.Add(child);
        public void Add(int key,int unrelated)=>throw new System.InvalidOperationException("Must not execute during discovery");
    }
} }
""";
var overloaded=new BehaviorDocument(overloadSource,adapter,sourceFile:file);var destination=overloaded.Entities.Single().Root.Descendants().Single(n=>n.Kind=="ArgumentGroup"&&overloaded.Expression(n).Contains("new StudioFirstDerived"));var overloads=overloaded.EntryOverloads(destination.Id);
Check(overloads.Count==3&&overloads.All(o=>o.Metadata.Children.Count>0),"Empty derived collection discovers accessible inherited constructed-generic and concrete Add contracts, excluding non-native inputs");
var inherited=overloads.Single(o=>o.Metadata.Parameters.Any(p=>p.Name=="mode"));Check(inherited.Metadata.Children.Select(c=>c.ElementType).SequenceEqual(new[]{"CycleBehavior","Transition"})&&inherited.Seeds["mode"].Contains("7"),"Inherited generic substitution and typed enum/optional child defaults come from the actual compiler symbol");
var made=overloaded.CreateCollectionEntry(destination.Id,inherited.Signature,new Dictionary<string,string>{{"key","4"}},new Dictionary<string,string>{{"child","new Shoot(20,count:4,fixedAngle:0,coolDown:100)"}},d=>adapter.Validate(d,entity));Check(overloaded.Find(made).Children.Any(n=>n.Argument?.Name=="gate"&&n.Children.Count==0),"First entry pads omitted scalar enum and optional transition defaults without a prototype");
var uintOverload=overloads.Single(o=>o.Metadata.Parameters[0].Type=="UInt32");var before=overloaded.Source;Reject(()=>overloaded.CreateCollectionEntry(destination.Id,uintOverload.Signature,new Dictionary<string,string>{{"key","\"wrong-overload\""}},new Dictionary<string,string>{{"child","new Shoot(20)"}}),"String input cannot silently replace the chosen uint Add contract");Check(overloaded.Source==before,"Overload mismatch preserves the accepted first entry");
var stringOverload=overloads.Single(o=>o.Metadata.Parameters[0].Type=="String");overloaded.CreateCollectionEntry(destination.Id,stringOverload.Signature,new Dictionary<string,string>{{"key","\"second\""}},new Dictionary<string,string>{{"child","new Shoot(20,count:6,fixedAngle:0,coolDown:100)"}},d=>adapter.Validate(d,entity));Check(overloaded.Find(destination.Id).Children.Count==2,"A nonempty collection can create another entry from a separately selected overload");
var inheritedAgain=overloaded.CreateCollectionEntry(destination.Id,inherited.Signature,new Dictionary<string,string>{{"key","2"}},new Dictionary<string,string>{{"child","new Shoot(20,count:2,fixedAngle:0,coolDown:100)"},{"gate","new TimedTransition(1000,\"loop\")"}},d=>adapter.Validate(d,entity));Check(overloaded.Find(inheritedAgain).Children.Single(n=>n.Argument?.Name=="gate").Children.Single().Kind=="TimedTransition","First-entry form supports heterogeneous native child inputs in source order");
var persisted=Path.GetFullPath(".artifacts/first-entry-overloads.rbs");var overloadedProject=project with{Source=overloaded.Source,Identities=overloaded.Identities};BehaviorDocument.Save(persisted,overloadedProject);var reopened=BehaviorDocument.Load(persisted);Check(new BehaviorDocument(reopened.Source,adapter,reopened.Identities,file).Identities.SequenceEqual(overloaded.Identities),"Prototype-free overloaded entry descendants survive project save/reopen");
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,overloadedProject)){await native.AdvanceAsync(new(),100);Check(native.Shots.Select(s=>s.Count).SequenceEqual(new[]{2,4,6}),"Original inherited Add enum defaults and concrete overloads determine native runtime order");}
Reject(()=>overloaded.CreateCollectionEntry(destination.Id,inherited.Signature,new Dictionary<string,string>{{"key","8"}},new Dictionary<string,string>{{"child","new State(\"entryPhase\")"}}),"A State cannot replace a CycleBehavior contract");
var phaseSource=source.Replace("CycleBehavior","IStateChildren").Replace("static Prioritize StudioParamsWrap","static State StudioParamsWrap").Replace("=>new Prioritize(System.Linq.Enumerable.ToArray(children))","=>new State(children:System.Linq.Enumerable.ToArray(children))");var phaseDoc=new BehaviorDocument(phaseSource,adapter,sourceFile:file);var phaseList=phaseDoc.Entities.Single().Root.Descendants().Single(n=>n.Kind=="ArgumentGroup"&&phaseDoc.Expression(n).Contains("new StudioParamsChildren"));var phaseContract=phaseDoc.EntryOverloads(phaseList.Id).Single();Reject(()=>phaseDoc.CreateCollectionEntry(phaseList.Id,phaseContract.Signature,new Dictionary<string,string>{{"key","1"}},new Dictionary<string,string>{{"children","new IStateChildren[]{new State(\"entryPhase\")}"}}),"Compatible entry-owned phases retain their source reference reconciliation gate");Check(phaseDoc.Source==phaseSource,"Rejected phase ownership preserves the empty native collection");
Check(File.ReadAllText(file)==supplied,"First-entry creation and Play preserve supplied game source");Console.WriteLine($"{checks} native first-entry checks passed.");
