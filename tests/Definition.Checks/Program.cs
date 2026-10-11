using Studio.Core;
using Studio.Raple;

var server=Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server");var adapter=new RapleAdapter(server);var checks=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var file=adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile;var supplied=File.ReadAllText(file);
const string source="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDefinitionFixture = () => Behav()
        .Init("Cave Pirate Brawler", new State(new State("caller", StudioRoot(), new TimedTransition(1000, "loop"))))
        .Init("Pirate Lieutenant", StudioRoot());
    static State StudioRoot() => new State("loop", StudioShared, new Timed(1000, StudioCollection));
    static readonly Shoot StudioShared = new Shoot(20, count: 2, fixedAngle: 0, coolDown: 100);
    static Behavior[] StudioCollection = [new Shoot(20, count: 3, fixedAngle: 0, coolDown: 100), new Shoot(20, count: 5, fixedAngle: 0, coolDown: 100)];
    static State StudioUnused => throw new System.Exception("Do not invoke getters during authoring");
} }
""";
var doc=new BehaviorDocument(source,adapter,sourceFile:file);
Check(doc.Entities.Count==2&&doc.Definitions.Count==4,"Native callers remain separate from all declared actual source definitions, including unused bodies");
var root=doc.Definitions.Single(d=>d.Definition!.Label=="StudioRoot");var shared=doc.Definitions.Single(d=>d.Definition!.Label=="StudioShared");var collection=doc.Definitions.Single(d=>d.Definition!.Label=="StudioCollection");
Check(root.Root.Kind=="State"&&root.Root.Label=="loop"&&shared.Root.Kind=="Shoot"&&collection.Root.Kind=="ArgumentGroup"&&collection.Root.Children.Count==2,"Helper return, shared field and typed collection bodies expose genuine editable constructor contents");
Check(doc.Definitions.Where(d=>d.Definition!.Label!="StudioUnused").All(d=>d.Definition!.Encounters.SequenceEqual(new[]{"Cave Pirate Brawler","Pirate Lieutenant"})&&d.Definition.CompletePhaseContents)&&doc.Definitions.Single(d=>d.Definition!.Label=="StudioUnused").Definition is {Encounters.Count:0,CompletePhaseContents:false},"Definition facts retain known native callers and avoid assigning unused bodies a runtime owner");
Check(doc.ReferencedDefinitions(doc.Entities[1].Root).Single().EntityId==root.EntityId&&doc.ReferencedDefinitions(root.Root).Any(d=>d.EntityId==shared.EntityId),"Source references navigate exact helper bodies and their transitive shared inputs");
Check(doc.AllNodes.Select(n=>n.Id).Distinct().Count()==doc.Identities.Count,"Native nodes and definition nodes have unique persistent editor identities");
var identities=doc.Identities;doc.Apply(new(doc.Revision,"Edit shared field",[new(shared.Root.Id,"count","4")]));
Check(doc.Find(shared.Root.Id).Parameters.Single(p=>p.Name=="count").Expression=="4"&&doc.Source.Split("count: 4").Length==2&&doc.Expression(doc.Entities[1].Root)=="StudioRoot()","Visual field edits change one actual shared initializer while keeping caller expressions intact");
Check(doc.Undo()&&doc.Source==source&&identities.All(i=>doc.Identities.Any(next=>next.Id==i.Id)),"Definition edit undo restores complete source and every caller/body identity");
var singleSource=source.Replace("        .Init(\"Pirate Lieutenant\", StudioRoot());","        ;");var singleDoc=new BehaviorDocument(singleSource,adapter,sourceFile:file);
var singleShared=singleDoc.Definitions.Single(d=>d.Definition!.Label=="StudioShared");var singleIds=singleDoc.Identities;singleDoc.LockedNodes.Add(singleShared.Root.Id);
var singleCall=singleDoc.Entities.Single().Root.Descendants().Single(n=>n.Kind=="Factory");var singleParent=singleDoc.Parent(singleCall.Id)!.Id;singleDoc.DeleteNode(singleCall.Id);
Check(singleDoc.Definitions.Single(d=>d.EntityId==singleShared.EntityId).Definition!.Encounters.Count==0&&singleDoc.Find(singleShared.Root.Id).Kind=="Shoot"&&singleDoc.LockedNodes.Contains(singleShared.Root.Id)&&singleIds.Where(i=>i.Path.StartsWith("$definition/",StringComparison.Ordinal)).All(i=>singleDoc.Identities.Any(n=>n.Id==i.Id)),"Removing the last helper call retains unused definition identities and locked source bodies");
singleDoc.AddChild(singleParent,"StudioRoot()");
var retainedLock=false;try{singleDoc.Apply(new(singleDoc.Revision,"Locked restored field",[new(singleShared.Root.Id,"count","9")]));}catch(InvalidOperationException){retainedLock=true;}
Check(retainedLock&&singleDoc.Find(singleShared.Root.Id).Parameters.Any(p=>p.Name=="count"&&p.Expression=="2"),"Restoring a helper call retains the source body's original lock and identity");
doc.LockedNodes.Add(doc.Entities[1].Root.Id);var locked=false;try{doc.Apply(new(doc.Revision,"Locked shared value",[new(shared.Root.Id,"count","4")]));}catch(InvalidOperationException){locked=true;}
Check(locked&&doc.Source==source,"A locked native helper call protects its transitive generated/shared definition contents");doc.LockedNodes.Clear();
doc.AddChild(root.Root.Id,"new Wander(.3)");Check(doc.Find(root.Root.Id).Children.Count==3&&doc.Entities.Count==2&&identities.All(i=>doc.Identities.Any(next=>next.Id==i.Id)),"Helper-body append preserves all callers, existing identities and exact native source binding");doc.Undo();
doc.AddChild(collection.Root.Id,"new Shoot(20, count: 7, fixedAngle: 0, coolDown: 100)");Check(doc.Find(collection.Root.Id).Children.Count==3&&doc.Source.Contains("StudioCollection = ["),"Shared collection insertion edits its real typed brackets instead of copying values into calls");doc.Undo();
var second=collection.Root.Children[1];doc.MoveNode(second.Id,-1);Check(doc.Find(collection.Root.Id).Children[0].Id==second.Id&&doc.Find(second.Id).Parameters.Any(p=>p.Name=="count"&&p.Expression=="5"),"Reordering actual shared collection contents retains native source order and identities");doc.Undo();
var pasted=doc.PasteNodes(collection.Root.Id,1,doc.CopyNodes([second.Id])).Single();
Check(pasted!=second.Id&&doc.Find(collection.Root.Id).Children.Select(n=>n.Parameters.Single(p=>p.Name=="count").Expression).SequenceEqual(new[]{"3","5","5"})&&identities.All(i=>doc.Identities.Any(n=>n.Id==i.Id)),"Definition collection copy/paste binds in its actual source scope and preserves surviving identities");doc.Undo();
var wrongCollection=false;try{doc.AddChild(collection.Root.Id,"new TimedTransition(1000, \"loop\")");}catch(ArgumentException){wrongCollection=true;}
Check(wrongCollection&&doc.Source==source,"A shared Behavior collection rejects a Transition element atomically using native types");
var group=doc.GroupNodes(doc.Find(root.Root.Id).Children.Select(n=>n.Id),"shared group");
Check(doc.Find(group).Kind=="State"&&doc.Find(group).Children.Count==2&&doc.Entities[1].Root.Kind=="Factory"&&identities.All(i=>doc.Identities.Any(n=>n.Id==i.Id)),"Grouping helper body contents edits a native nested State while retaining callers and existing identities");
doc.UngroupState(group);Check(doc.Find(root.Root.Id).Children.Select(n=>n.Id).SequenceEqual(root.Root.Children.Select(n=>n.Id)),"Ungrouping a helper-owned phase restores the actual shared child order and identities");doc.Undo();doc.Undo();
doc.RenameState(root.Root.Id,"cycle");
Check(doc.Find(root.Root.Id).Label=="cycle"&&doc.Entities[0].Root.Descendants().Any(n=>n.Parameters.Any(p=>p.Name=="targetState"&&p.Expression=="\"cycle\"")),"Renaming a helper-owned phase reconciles a native caller transition across the definition boundary");doc.Undo();
Check(File.ReadAllText(file)==supplied,"Visual definition discovery and editing leave supplied game source untouched");
var propertySource=source.Replace("StudioRoot()","StudioRoot");var propertyDoc=new BehaviorDocument(propertySource,adapter,sourceFile:file);
Check(propertyDoc.Entities[1].Root.Kind=="Reference"&&propertyDoc.Definitions.Single(d=>d.Definition!.Label=="StudioRoot").Root.Kind=="State","Expression-bodied property getters expose their original bodies without evaluating the getter");
var throwStart=propertySource.IndexOf("=> new State(\"loop\"",StringComparison.Ordinal);var throwEnd=propertySource.IndexOf(';',throwStart);var throwSource=propertySource.Remove(throwStart,throwEnd-throwStart).Insert(throwStart,"=> throw new System.Exception(\"Discovery must not execute this getter\")");
var throwingDoc=new BehaviorDocument(throwSource,adapter,sourceFile:file);
Check(throwingDoc.Definitions.Single(d=>d.Definition!.Label=="StudioRoot").Root.Kind=="Opaque","A referenced throwing getter stays an opaque source body and is never invoked for discovery");
var conditionalSource=source.Replace("=> new State(\"loop\", StudioShared, new Timed(1000, StudioCollection))","=> System.DateTime.Now.Millisecond >= 0 ? new State(\"loop\", StudioShared, new Timed(1000, StudioCollection)) : new State(\"alternate\", new Shoot(20))");
var conditionalDoc=new BehaviorDocument(conditionalSource,adapter,sourceFile:file);var branches=conditionalDoc.Definitions.Where(d=>d.Definition!.Label.StartsWith("StudioRoot",StringComparison.Ordinal)).ToArray();
Check(branches.Length==2&&branches.All(d=>d.Root.Kind=="State"&&!d.Definition!.CompletePhaseContents),"Conditional helper returns expose both editable source branches without choosing a runtime outcome");
var unsafeRename=false;try{conditionalDoc.RenameState(branches[0].Root.Id,"unsafe");}catch(ArgumentException){unsafeRename=true;}
Check(unsafeRename&&conditionalDoc.Source==conditionalSource,"Conditional definition phase rename keeps an explicit complete-reference boundary and rejects atomically");
var inputSource=source.Replace("StudioRoot()","StudioRoot(new State())").Replace("static State StudioRoot(new State())","static State StudioRoot(State inherited)").Replace("new State(\"loop\", StudioShared,","new State(\"loop\", inherited, StudioShared,");
var inputDoc=new BehaviorDocument(inputSource,adapter,sourceFile:file);var inputBody=inputDoc.Definitions.Single(d=>d.Definition!.Label=="StudioRoot");var inputRename=false;try{inputDoc.RenameState(inputBody.Root.Id,"unsafe");}catch(ArgumentException){inputRename=true;}
Check(!inputBody.Definition!.CompletePhaseContents&&inputRename&&inputDoc.Source==inputSource,"A helper with an unresolved child input cannot claim complete phase-reference reconciliation");
var localSource="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioLocalFixture = () => Behav().Init("Cave Pirate Brawler", StudioLocalA()).Init("Pirate Lieutenant", StudioLocalB());
    static State StudioLocalA() { var child = new Shoot(20, count: 2); return new State(child); }
    static State StudioLocalB() { var child = new Shoot(20, count: 6); return new State(child); }
} }
""";
var localDoc=new BehaviorDocument(localSource,adapter,sourceFile:file);var locals=localDoc.Definitions.Where(d=>d.Definition!.Label=="child").ToArray();
Check(locals.Length==2&&locals.Select(d=>d.EntityId).Distinct().Count()==2&&locals.All(d=>d.Definition!.Encounters.Count==1),"Identical local variable names in different helpers retain separate source bodies and caller scopes");
var lambdaSource="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioLambdaA = () => { var child = new Shoot(20, count: 2); return Behav().Init("Cave Pirate Brawler", new State(child)); };
    private _ StudioLambdaB = () => { var child = new Shoot(20, count: 6); return Behav().Init("Pirate Lieutenant", new State(child)); };
} }
""";
var lambdaDoc=new BehaviorDocument(lambdaSource,adapter,sourceFile:file);var lambdaLocals=lambdaDoc.Definitions.Where(d=>d.Definition!.Label=="child").ToArray();
Check(lambdaLocals.Length==2&&lambdaLocals.Select(d=>d.EntityId).Distinct().Count()==2&&lambdaLocals.All(d=>d.Definition!.Encounters.Count==1),"Identical local names in separate native registration lambdas keep distinct declaration identities and callers");
var localFunctionSource=lambdaSource.Replace("var child = new Shoot(20, count: 2);","Shoot Build() => new Shoot(20, count: 2); var child = Build();").Replace("var child = new Shoot(20, count: 6);","Shoot Build() => new Shoot(20, count: 6); var child = Build();");
var localFunctionDoc=new BehaviorDocument(localFunctionSource,adapter,sourceFile:file);var functions=localFunctionDoc.Definitions.Where(d=>d.Definition!.Label=="Build").ToArray();
Check(functions.Length==2&&functions.Select(d=>d.EntityId).Distinct().Count()==2&&functions.All(d=>d.Definition!.Encounters.Count==1)&&functions.Select(d=>d.Root.Parameters.Single(p=>p.Name=="count").Expression).SequenceEqual(new[]{"2","6"}),"Same-named local functions in separate registration scopes retain distinct native source bodies and callers");
var igrisFile=Path.Combine(server,"TKR.WorldServer/logic/db/BehaviorDb.Igris.cs");var igrisSource=File.ReadAllText(igrisFile);var igrisDoc=new BehaviorDocument(igrisSource,adapter,sourceFile:igrisFile);var glow=igrisDoc.Definitions.Where(d=>d.Definition!.Label.StartsWith("IgrisGlowStep",StringComparison.Ordinal)).ToArray();
Check(glow.Length==2&&glow.All(d=>d.Root.Kind=="State"&&d.Definition!.Uses.Count==6&&!d.Definition.CompletePhaseContents)&&File.ReadAllText(igrisFile)==igrisSource,"Actual Igris helper exposes both native conditional State branches and all six source calls losslessly");
var bindings=doc.AllNodes.Select(n=>(Node:n,Description:doc.SchemaAt(n)?.DescribeExpression(doc.Expression(n)))).Where(p=>p.Description!=null).Select(p=>new RuntimeSourceBinding(p.Node.Start,doc.Expression(p.Node),p.Description!)).DistinctBy(b=>(b.Start,b.Expression)).ToArray();
var prepared=new BehaviorDocument(doc.Source,new PreparedBehaviorSchema(adapter,bindings,doc.EncounterSources,doc.Definitions.Select(d=>d.Definition!).ToArray()),doc.Identities);
Check(prepared.Definitions.Count==4&&prepared.Identities.Select(i=>i.Id).SequenceEqual(doc.Identities.Select(i=>i.Id)),"Compiled host facts restore all definition contents and identities without a second semantic compiler");
doc.Apply(new(doc.Revision,"Native shared initializer",[new(shared.Root.Id,"count","4")]));doc.MoveNode(second.Id,-1);
foreach(var entity in new[]{"Cave Pirate Brawler","Pirate Lieutenant"})
{
    var project=new StudioProject{SourceFile=file,Source=doc.Source,EntityId=entity,Identities=doc.Identities,Seed=7};
    await using var native=await CompiledRuntime.StartAsync(server,project,artifactRoot:Path.GetFullPath(".artifacts/definitions/compiled"));await native.AdvanceAsync(new(),100);
    Check(native.Shots.Select(s=>s.Count).SequenceEqual(new[]{4,5,3}),"Actual compiled caller uses edited shared field and reordered collection through its original helper for "+entity);
}
Check(File.ReadAllText(file)==supplied,"Compiled definition playback preserves supplied game source");
Console.WriteLine($"{checks} source definition checks passed");
