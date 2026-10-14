using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var file=Path.GetFullPath(adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile);var supplied=File.ReadAllText(file);
const string source="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioMembers=()=>Behav().Init("Cave Pirate Brawler",new State(new State("loop",new Shoot(20,count:2,fixedAngle:0,coolDown:100)) { Behaviors = { new Shoot(20,count:3,fixedAngle:0,coolDown:100),/* member note */new Shoot(20,count:4,fixedAngle:0,coolDown:100) }, Transitions = { new TimedTransition(100,"finish") } },new State("finish")));
} }
""";
var doc=new BehaviorDocument(source,adapter,sourceFile:file);var root=doc.Entities.Single().Root;var phase=root.Children.First();var constructor=phase.Children.Single(n=>n.Argument?.Name=="children");var wrapper=phase.Children.Single(n=>n.IsMemberInitializer);var behaviors=wrapper.Children.Single(n=>n.Argument?.Name=="Behaviors");var transitions=wrapper.Children.Single(n=>n.Argument?.Name=="Transitions");var identities=doc.Identities;
Check(wrapper.Kind=="ArgumentGroup"&&!doc.CanEditChildren(wrapper.Id)&&doc.CanEditChildren(behaviors.Id)&&doc.CanEditChildren(transitions.Id)&&doc.CanEditChildren(constructor.Id),"Actual native constructor, behavior-member and transition-member lists have separate editable input ownership");
Check(phase.StateChildren.Select(n=>n.Kind).SequenceEqual(new[]{"Shoot","Shoot","Shoot","TimedTransition"})&&behaviors.Argument!.IsMember&&transitions.Argument!.ElementType=="Transition","Flattened native phase contents retain constructor actions, member actions and member transitions");
Check(doc.AcceptsChild(behaviors.Id,"new Shoot(20,count:6)")&&!doc.AcceptsChild(behaviors.Id,"new TimedTransition(100,\"finish\")")&&!doc.AcceptsChild(transitions.Id,"new Shoot(20)"),"Member child preflight uses the real native property element type");
var first=behaviors.Children[0];var second=behaviors.Children[1];doc.MoveNode(second.Id,-1,d=>adapter.Validate(d,"Cave Pirate Brawler"));
Check(doc.Find(behaviors.Id).Children.Select(n=>n.Id).SequenceEqual(new[]{second.Id,first.Id})&&doc.Source.Contains("/* member note */")&&doc.Find(constructor.Id).Children.Single().Parameters.Any(p=>p.Name=="count"&&p.Expression=="2"),"Member reorder preserves exact identities, source comments and constructor inputs");
var duplicate=doc.DuplicateNode(first.Id,d=>adapter.Validate(d,"Cave Pirate Brawler"));Check(doc.Find(behaviors.Id).Children.Count==3&&duplicate!=first.Id,"Member action duplication allocates a fresh identity in the real initializer list");
doc.MoveNodes([duplicate],constructor.Id,1,d=>adapter.Validate(d,"Cave Pirate Brawler"));Check(doc.Find(constructor.Id).Children.Last().Id==duplicate&&doc.Find(behaviors.Id).Children.Count==2,"Cross-list movement preserves the action identity across constructor and member initialization inputs");
doc.DeleteNodes(doc.Find(behaviors.Id).Children.Select(n=>n.Id),d=>adapter.Validate(d,"Cave Pirate Brawler"));Check(doc.Find(behaviors.Id).Children.Count==0&&doc.CanEditChildren(behaviors.Id),"Deleting every member input retains an empty editable collection initializer");
doc.AddChild(behaviors.Id,"new Shoot(20,count:7,fixedAngle:0,coolDown:100)",d=>adapter.Validate(d,"Cave Pirate Brawler"));var created=doc.Find(behaviors.Id).Children.Single().Id;Check(doc.Source.Contains("Behaviors"),"Native action creation fills an empty member initializer without replacing the property getter");
doc.LockedNodes.Add(wrapper.Id);Reject(()=>doc.DeleteNode(created),"A member initializer lock protects all nested lists and native parameter regions");doc.LockedNodes.Clear();
Reject(()=>doc.MoveNodes([created],transitions.Id,0,d=>adapter.Validate(d,"Cave Pirate Brawler")),"Moving an action into the transition member list rejects before source mutation");
while(doc.Source!=source)if(!doc.Undo())throw new Exception("Member history could not restore original source");Check(doc.Identities.Select(i=>i.Id).SequenceEqual(identities.Select(i=>i.Id)),"Complete member history restores original constructor/list/action/transition identities");
var project=new StudioProject{SourceFile=file,Source=doc.Source,EntityId="Cave Pirate Brawler",Identities=doc.Identities,Player=new(Invulnerable:true),Seed=7};var finish=root.Children.Last();
var renamed=SourcePhaseRefactor.Rename(adapter,project,file,finish.Id,"done");Check(renamed.After.Source.Contains("TimedTransition(100,\"done\")"),"Coordinated phase rename discovers native transitions stored in a member initializer");
var stateBoundary=new BehaviorDocument(source.Replace("Transitions = { new TimedTransition(100,\"finish\") }","States = { new State(\"unparented\") }"),adapter,sourceFile:file);Check(stateBoundary.AllNodes.Any(n=>n.Kind=="Opaque"&&stateBoundary.Expression(n).StartsWith("States")),"Native States member additions retain source ownership because Add does not assign the constructor parent relationship");
var indexed=new BehaviorDocument(source.Replace("Behaviors = { new Shoot(20,count:3,fixedAngle:0,coolDown:100),/* member note */new Shoot(20,count:4,fixedAngle:0,coolDown:100) }","Behaviors = { [0] = new Shoot(20,count:8,fixedAngle:0,coolDown:100) }"),adapter,sourceFile:file);
Check(indexed.AllNodes.Single(n=>n.Argument is {Name:"Behaviors",IsMember:true}).Children.Single().Kind=="InitializerEntry","Indexed State behavior members expose their actual getter-list setter without converting to Add inputs");
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,project with{Source=indexed.Source,Identities=indexed.Identities})){await runtime.AdvanceAsync(new(),100);Check(runtime.Shots.Select(s=>s.Count).SequenceEqual(new[]{8}),"Original State member index assignment replaces the existing constructor behavior through its native list setter");}
var legacy=identities.Where(i=>!i.Path.StartsWith(wrapper.Path,StringComparison.Ordinal)).Append(new NodeIdentity(wrapper.Id,wrapper.Path,"Opaque",doc.Expression(wrapper))).ToList();var migrated=new BehaviorDocument(source,adapter,legacy,file);migrated.LockedNodes.Add(wrapper.Id);
Check(migrated.Find(wrapper.Id).IsMemberInitializer&&!migrated.CanEditRegion(migrated.AllNodes.Single(n=>n.Argument?.Name=="Behaviors").Children.First().Id),"Previously opaque initializer identity and locks migrate to the complete new member wrapper");
var output=Path.GetFullPath(".artifacts/member-initializer");Directory.CreateDirectory(output);var path=Path.Combine(output,"members.rbs");BehaviorDocument.Save(path,project);var loaded=BehaviorDocument.Load(path);var reopened=new BehaviorDocument(loaded.Source,adapter,loaded.Identities,file);
Check(reopened.Identities.SequenceEqual(doc.Identities),"Native member wrapper/list/child identities persist through save/reopen");
const string cloneSource="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioCloneMembers=()=>Behav().Init("Cave Pirate Brawler",new State(StudioMemberFactory(),new State("finish")));
    static State StudioMemberFactory()=>new State("loop",new Shoot(20,count:2,fixedAngle:0,coolDown:100)){Behaviors={new Shoot(20,count:3,fixedAngle:0,coolDown:100)},Transitions={new TimedTransition(200,"finish")}};
} }
""";
var cloneDoc=new BehaviorDocument(cloneSource,adapter,sourceFile:file);var call=cloneDoc.Entities.Single().Root.Children.First();var cloneProject=project with{Source=cloneSource,Identities=cloneDoc.Identities};var cloned=SourcePhaseRefactor.CloneDefinition(adapter,cloneProject,file,call.Id,"StudioIndependentMembers");
Check(cloned.After.AdditionalSources.All(s=>Path.GetFileName(s.SourceFile)!="State.cs")&&cloned.After.Source.Contains("StudioIndependentMembers")&&cloned.After.Source.Contains("\"loop-copy\""),"Independent cloning retains native member property semantics instead of cloning game State storage declarations");
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,cloned.After,phase:"loop-copy")){await runtime.AdvanceAsync(new(),100);Check(runtime.Shots.Select(s=>s.Count).SequenceEqual(new[]{2,3}),"The independently cloned native State executes original constructor and member additions");}
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,project)){await runtime.AdvanceAsync(new(),100);Check(runtime.Shots.Select(s=>s.Count).SequenceEqual(new[]{2,3,4}),"Actual native member initialization retains constructor-then-member attack order");await runtime.AdvanceAsync(new(),100);Check(runtime.Entities.Single().Phase=="finish","Native member transition executes its actual timer and target phase");}
Check(File.ReadAllText(file)==supplied,"Member initializer edits, history and native execution leave supplied source unchanged");Console.WriteLine($"{checks} member initializer checks passed.");
