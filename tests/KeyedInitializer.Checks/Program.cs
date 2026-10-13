using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var file=Path.GetFullPath(adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile);var supplied=File.ReadAllText(file);
const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioKeyed=()=>Behav().Init("Cave Pirate Brawler",new State("loop",StudioKeyedWrap(new StudioKeyedChildren(){ {20,new Shoot(20,count:4,fixedAngle:0,coolDown:100)},/* keyed source note */{11,new Shoot(20,count:3,fixedAngle:0,coolDown:100)} })));
    static Prioritize StudioKeyedWrap(System.Collections.Generic.IEnumerable<CycleBehavior> children)=>new Prioritize(System.Linq.Enumerable.ToArray(children));
    sealed class StudioKeyedChildren:System.Collections.Generic.IEnumerable<CycleBehavior> {
        readonly System.Collections.Generic.SortedDictionary<int,CycleBehavior> values=new();
        public void Add(int key,CycleBehavior child)=>values.Add(key,child);
        public void Add(string key,CycleBehavior child)=>values.Add(key.Length,child);
        public System.Collections.Generic.IEnumerator<CycleBehavior> GetEnumerator()=>values.Values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
} }
""";
var doc=new BehaviorDocument(source,adapter,sourceFile:file);var list=doc.Entities.Single().Root.Descendants().Single(n=>n.Kind=="ArgumentGroup"&&n.Children.Any(c=>c.Kind=="InitializerEntry"));var first=list.Children[0];var second=list.Children[1];var child=first.Children.Single();var identities=doc.Identities;
Check(list.Children.All(n=>n.Kind=="InitializerEntry")&&doc.CanEditChildren(list.Id)&&first.Parameters.Single().Name=="key"&&doc.SchemaAt(first)!.DescribeExpression(doc.Expression(first))!.Parameters.First().Type=="Int32","Multi-input initializer entries expose the compiler-selected int Add overload and its actual scalar input");
Check(first.Children.Single().Kind=="Shoot"&&doc.IsScalarChildSlot(first.Id)&&doc.AcceptsChild(first.Id,"new Shoot(20,count:8)",replace:true)&&!doc.AcceptsChild(first.Id,"new TimedTransition(100,\"loop\")",replace:true),"An Add entry owns one required native CycleBehavior child with exact conversion checks");
var stringDoc=new BehaviorDocument(source.Replace("{20,new Shoot","{\"label\",new Shoot"),adapter,sourceFile:file);var stringEntry=stringDoc.Entities.Single().Root.Descendants().First(n=>n.Kind=="InitializerEntry");
Check(stringDoc.SchemaAt(stringEntry)!.DescribeExpression(stringDoc.Expression(stringEntry))!.Parameters.First().Type=="String"&&stringEntry.Parameters.Single().Expression=="\"label\"","String-key Add overloads retain scalar string semantics rather than being mistaken for child collections");
var optional=new BehaviorDocument(source.Replace("CycleBehavior child)","CycleBehavior child=null)"),adapter,sourceFile:file);
Check(!optional.Entities.Single().Root.Descendants().Any(n=>n.Kind=="InitializerEntry")&&optional.AllNodes.Any(n=>n.Kind=="Opaque"),"Optional Add child ownership stays in visible source instead of receiving an invented required-child contract");
doc.Apply(new(doc.Revision,"Edit native Add key",[new(first.Id,"key","5")]),d=>adapter.Validate(d,"Cave Pirate Brawler"));Check(doc.Find(first.Id).Parameters.Single().Expression=="5"&&doc.Find(first.Id).Children.Single().Id==child.Id,"Scalar Add key edits preserve the entry and its native behavior identity");
var replaced=doc.ReplaceChild(first.Id,"new Shoot(20,count:8,fixedAngle:0,coolDown:100)",d=>adapter.Validate(d,"Cave Pirate Brawler"));Check(replaced!=child.Id&&doc.Find(first.Id).Parameters.Single().Expression=="5"&&doc.Find(first.Id).Children.Single().Id==replaced,"Typed child replacement retains the key and requires a fresh child identity");
Reject(()=>doc.DeleteNode(replaced,d=>adapter.Validate(d,"Cave Pirate Brawler")),"A required Add child cannot be removed while leaving an invalid collection entry");
doc.MoveNode(second.Id,-1,d=>adapter.Validate(d,"Cave Pirate Brawler"));Check(doc.Find(list.Id).Children.First().Id==second.Id&&doc.Source.Contains("/* keyed source note */"),"Entry reorder preserves complete key/behavior pairs and source comments without guessing enumeration order");
var copy=doc.DuplicateNode(first.Id,d=>adapter.Validate(d,"Cave Pirate Brawler"));Check(copy!=first.Id&&doc.Find(copy).Kind=="InitializerEntry"&&doc.Find(copy).Children.Single().Id!=replaced&&doc.Find(copy).Parameters.Single().Expression=="5","Independent entry duplication retains its complete Add arguments and allocates new descendant identities");
doc.Apply(new(doc.Revision,"Set independent key",[new(copy,"key","7")]),d=>adapter.Validate(d,"Cave Pirate Brawler"));doc.DeleteNode(copy,d=>adapter.Validate(d,"Cave Pirate Brawler"));
Check(doc.Find(list.Id).Children.Count==2&&doc.Find(first.Id).Children.Single().Id==replaced,"Entry deletion removes the whole native Add input and leaves neighboring pairs intact");
doc.LockedNodes.Add(first.Id);Reject(()=>doc.Apply(new(doc.Revision,"Blocked key",[new(first.Id,"key","9")]),d=>adapter.Validate(d,"Cave Pirate Brawler")),"Entry region locks protect key edits and native child storage");doc.LockedNodes.Clear();
var project=new StudioProject{SourceFile=file,Source=doc.Source,EntityId="Cave Pirate Brawler",Identities=doc.Identities,Player=new(Invulnerable:true),Seed=7};
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,project)){await runtime.AdvanceAsync(new(),100);Check(runtime.Shots.Select(s=>s.Count).SequenceEqual(new[]{8,3}),"Actual native SortedDictionary Add/enumeration behavior follows edited keys despite reversed source input order");}
var output=Path.GetFullPath(".artifacts/keyed-initializer");Directory.CreateDirectory(output);var path=Path.Combine(output,"keyed.rbs");BehaviorDocument.Save(path,project);var saved=BehaviorDocument.Load(path);var reopened=new BehaviorDocument(saved.Source,adapter,saved.Identities,file);
Check(reopened.Identities.SequenceEqual(doc.Identities)&&reopened.Find(first.Id).Parameters.Single().Expression=="5","Native Add entry/child identities and edited keys persist through save/reopen");
while(doc.Source!=source)if(!doc.Undo())throw new Exception("Could not restore keyed source");Check(doc.Identities.SequenceEqual(identities),"Atomic keyed-entry history restores exact original input order and every identity");
var revision=doc.Revision;var created=doc.CreateInitializerEntry(first.Id,new Dictionary<string,string>{{"key","7"}},"new Shoot(20,count:6,fixedAngle:0,coolDown:100)",d=>adapter.Validate(d,"Cave Pirate Brawler"));
Check(doc.Revision==revision+1&&doc.Find(created).Parameters.Single().Expression=="7"&&doc.Find(created).Children.Single().Parameters.Any(p=>p.Name=="count"&&p.Expression=="6"),"New key and behavior creation commits together in one source revision");
var createdSource=doc.Source;var createdIds=doc.Identities;Check(doc.Undo()&&doc.Source==source&&doc.Redo()&&doc.Source==createdSource&&doc.Identities.SequenceEqual(createdIds),"One Undo/Redo restores the entire newly created entry and its descendant identity");
var failedSource=doc.Source;var failedRevision=doc.Revision;
Reject(()=>doc.CreateInitializerEntry(first.Id,new Dictionary<string,string>{{"key","9"}},"new TimedTransition(100,\"loop\")",d=>adapter.Validate(d,"Cave Pirate Brawler")),"An incompatible new child rejects the whole entry creation");
Check(doc.Source==failedSource&&doc.Revision==failedRevision,"Rejected entry creation retains accepted source, revision and history");
Reject(()=>doc.CreateInitializerEntry(first.Id,new Dictionary<string,string>(),"new Shoot(20)"),"New entry creation requires every scalar input of the actual Add overload");
doc.LockedNodes.Add(second.Id);var withSibling=doc.CreateInitializerEntry(first.Id,new Dictionary<string,string>{{"key","9"}},"new Shoot(20,count:9,fixedAngle:0,coolDown:100)",d=>adapter.Validate(d,"Cave Pirate Brawler"));
Check(doc.Find(second.Id).Parameters.Single().Expression=="11"&&doc.Find(withSibling).Id==withSibling,"New entry creation preserves an unrelated locked sibling and its exact identity");doc.LockedNodes.Clear();doc.Undo();
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,project with{Source=doc.Source,Identities=doc.Identities})){await runtime.AdvanceAsync(new(),100);Check(runtime.Shots.Select(s=>s.Count).SequenceEqual(new[]{6,3,4}),"Actual native Add and sorted enumeration execute the complete newly created key/behavior pair");}
Check(File.ReadAllText(file)==supplied,"Keyed Add edits, history and native execution leave supplied game source unchanged");Console.WriteLine($"{checks} keyed initializer checks passed.");
