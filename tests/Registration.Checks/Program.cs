using Studio.Raple;
using Studio.Core;

var server=Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server");var adapter=new RapleAdapter(server);
var file=Path.Combine(server,"TKR.WorldServer/logic/db/BehaviorDb.Pets.cs");var source=File.ReadAllText(file);var checks=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var schema=adapter.ForSource(source,file);var ranges=schema.AdditionalEncounters();
var resources=EntityResources.Catalog(EntityResourceScope.Resolvable);var first=resources.Single(e=>e.Id=="Black Cat").ObjectType;var last=resources.Single(e=>e.Id=="Snowman").ObjectType;
var expected=resources.Where(e=>e.ObjectType>=first&&e.ObjectType<=last).OrderBy(e=>e.ObjectType).Select(e=>e.SpriteId).Distinct().ToArray();
Check(ranges.Select(r=>r.EntityId).SequenceEqual(expected),"Actual pet InitMany discovery uses inclusive numeric XML bounds and canonical native IDs");
Check(ranges.All(r=>r.Shared&&r.Boundary.Contains("every applicable"))&&ranges.Select(r=>(r.RootStart,r.RootLength)).Distinct().Count()==1,"Range variants describe one explicitly shared source template");
Check(ranges.All(r=>source.Substring(r.RootStart,r.RootLength).StartsWith("new State")),"Inline range roots retain exact native source spans");
Check(schema.At(ranges[0].RootStart).DescribeExpression(source.Substring(ranges[0].RootStart,ranges[0].RootLength))?.IsState==true,"Native range lambda body binds as State at its original compiler position");
// Use a complete replacement of the range call for a platform-independent fixture.
var callStart=source.LastIndexOf(".InitMany",StringComparison.Ordinal);var callEnd=source.IndexOf(";",callStart,StringComparison.Ordinal);
var named=source.Remove(callStart,callEnd-callStart).Insert(callStart,".InitMany(rootState: _ => new State(new PetFollow()), objTypeMax: \"Snowman\", objTypeMin: \"Black Cat\")");
Check(adapter.ForSource(named,file).AdditionalEncounters().Select(r=>r.EntityId).SequenceEqual(expected),"Named range arguments bind by actual native formal parameters");
var reverse=named.Replace("objTypeMax: \"Snowman\", objTypeMin: \"Black Cat\"","objTypeMax: \"Black Cat\", objTypeMin: \"Snowman\"");
Check(adapter.ForSource(reverse,file).AdditionalEncounters().Count==0,"Reversed native range bounds produce no candidates");
Check(File.ReadAllText(file)==source,"Registration inspection leaves supplied game source unchanged");
var document=new BehaviorDocument(source,adapter,sourceFile:file);var variants=document.Entities.Where(e=>e.Registration?.Shared==true).ToArray();
Check(variants.Select(e=>e.EntityId).SequenceEqual(expected)&&variants.Select(e=>e.Root.Id).Distinct().Count()==expected.Length,"Document projects every shared native range member with independent editor identities");
var ids=document.Identities.ToArray();var locked=variants[1].Root;document.LockedNodes.Add(locked.Id);var rejected=false;
try{document.AddChild(variants[0].Root.Id,"new Wander(.3)");}catch(InvalidOperationException){rejected=true;}
Check(rejected&&document.Source==source,"A lock in another projected range member protects the same underlying source");document.LockedNodes.Clear();
document.AddChild(variants[0].Root.Id,"new Wander(.3)");
Check(document.Entities.Where(e=>e.Registration?.Shared==true).All(e=>e.Root.Children.Count==2)&&document.Source.Split("new Wander(.3)").Length==2,"One shared template edit updates every range projection without duplicating source");
Check(document.Undo()&&document.Source==source&&ids.All(id=>document.Identities.Any(next=>next.Id==id.Id)),"Shared source authoring Undo restores source and all surviving variant identities");
var bindings=document.Entities.SelectMany(e=>e.Root.Descendants()).Select(n=>(Node:n,Description:document.SchemaAt(n)?.DescribeExpression(document.Expression(n)))).Where(p=>p.Description!=null).Select(p=>new RuntimeSourceBinding(p.Node.Start,document.Expression(p.Node),p.Description!)).DistinctBy(b=>(b.Start,b.Expression)).ToArray();
var prepared=new BehaviorDocument(source,new PreparedBehaviorSchema(adapter,bindings,document.EncounterSources),document.Identities);
Check(prepared.Entities.Where(e=>e.Registration?.Shared==true).Select(e=>e.EntityId).SequenceEqual(expected)&&prepared.Identities.Select(i=>i.Id).SequenceEqual(document.Identities.Select(i=>i.Id)),"Prepared compiled-host facts restore every range projection and identity without re-running semantic compilation");
Check(adapter.NeedsCompiledRuntime(document,expected[0]),"Shared range templates select the original compiled factory path");
var phases=source.Remove(ranges[0].RootStart,ranges[0].RootLength).Insert(ranges[0].RootStart,"new State(new State(\"start\", new TKR.WorldServer.logic.transitions.TimedTransition(100, \"end\")), new State(\"end\", new Wander(.3)))");
var phaseDocument=new BehaviorDocument(phases,adapter,sourceFile:file);var phaseVariants=phaseDocument.Entities.Where(e=>e.Registration?.Shared==true).ToArray();var phaseIds=phaseDocument.Identities;
Check(phaseDocument.NormalizeSelection([phaseVariants[0].Root.Children[0].Id,phaseVariants[1].Root.Children[0].Children[0].Id]).Count==1,"Shared ancestor selection removes a descendant selected through another variant alias");
var end=phaseVariants[0].Root.Children[1];phaseDocument.RenameState(end.Id,"finish");
Check(phaseDocument.Entities.Where(e=>e.Registration?.Shared==true).All(e=>e.Root.Children[1].Label=="finish"&&e.Root.Children[0].Children.Single().Parameters.Any(p=>p.Name=="targetState"&&p.Expression=="\"finish\"")),"Renaming a shared phase rewrites its physical transition once and updates every variant");
Check(phaseDocument.Undo()&&phaseDocument.Source==phases&&phaseIds.All(i=>phaseDocument.Identities.Any(next=>next.Id==i.Id)),"Shared phase rename undo preserves every variant identity");
phaseDocument.MoveNode(end.Id,-1);
Check(phaseDocument.Entities.Where(e=>e.Registration?.Shared==true).All(e=>e.Root.Children[0].Label=="end")&&phaseIds.All(i=>phaseDocument.Identities.Any(next=>next.Id==i.Id)),"Shared phase reorder updates every projection and retains all existing identities");phaseDocument.Undo();
var copy=phaseDocument.DuplicateNode(end.Id);
Check(phaseDocument.Find(copy).Label=="end-copy"&&phaseDocument.Entities.Where(e=>e.Registration?.Shared==true).All(e=>e.Root.Children.Count==3)&&phaseDocument.Identities.Select(i=>i.Id).Distinct().Count()==phaseDocument.Identities.Count,"Duplicating a shared phase creates one physical copy and unique variant identities");phaseDocument.Undo();
phaseDocument.DeleteNode(end.Id);
Check(phaseDocument.Entities.Where(e=>e.Registration?.Shared==true).All(e=>e.Root.Children.Count==1&&e.Root.Children[0].Children.Count==0),"Deleting a shared phase removes its physical transition once across all projections");phaseDocument.Undo();
var actions=phaseDocument.Entities.First(e=>e.Registration?.Shared==true).Root.Children;var grouped=phaseDocument.GroupNodes(actions.Select(n=>n.Id),"group");
Check(phaseDocument.Entities.Where(e=>e.Registration?.Shared==true).All(e=>e.Root.Children.Single().Children.Count==2),"Shared grouping projects original child identities into every new phase");phaseDocument.UngroupState(grouped);
Check(phaseDocument.Entities.Where(e=>e.Registration?.Shared==true).All(e=>e.Root.Children.Count==2)&&phaseIds.All(i=>phaseDocument.Identities.Any(next=>next.Id==i.Id)),"Shared ungrouping restores all original variant child identities");
var admin=phaseDocument.Entities.Single(e=>e.EntityId=="Admin Orb").Root;
var orderedSource=phaseDocument.Source.Remove(admin.Start,admin.Length).Insert(admin.Start,"new State(new Order(10, \"Snowman\", \"end\"))");
var orderedDoc=new BehaviorDocument(orderedSource,adapter,sourceFile:file);var sharedEnd=orderedDoc.Entities.First(e=>e.Registration?.Shared==true).Root.Children[1];orderedDoc.RenameState(sharedEnd.Id,"finish");
Check(orderedDoc.Entities.Single(e=>e.EntityId=="Admin Orb").Root.Children.Single().Parameters.Any(p=>p.Name=="targetState"&&p.Expression=="\"finish\""),"Renaming a shared phase reconciles an external native order targeting a different range alias");orderedDoc.Undo();
var unsafeDelete=false;try{orderedDoc.DeleteNode(sharedEnd.Id);}catch(ArgumentException){unsafeDelete=true;}
Check(unsafeDelete&&orderedDoc.Source==orderedSource,"Deleting a shared phase rejects an external action reference to another variant atomically");
var wanderers=phaseDocument.Entities.Where(e=>e.Registration?.Shared==true).Take(2).Select(e=>e.Root.Children[1].Children.Single()).ToArray();
var parameterSource=phaseDocument.Source;
phaseDocument.Apply(new(phaseDocument.Revision,"Shared parameter",wanderers.Select(n=>new EditOperation(n.Id,"speed",".4")).ToArray()));
Check(phaseDocument.Entities.Where(e=>e.Registration?.Shared==true).All(e=>e.Root.Children[1].Children.Single().Parameters.Single().Expression==".4"),"Identical parameter proposals through two shared aliases commit one physical source edit");phaseDocument.Undo();
var conflict=false;try{phaseDocument.Apply(new(phaseDocument.Revision,"Conflicting shared values",[new(wanderers[0].Id,"speed",".4"),new(wanderers[1].Id,"speed",".5")]));}catch(ArgumentException){conflict=true;}
Check(conflict&&phaseDocument.Source==parameterSource,"Conflicting parameter proposals through shared aliases reject without changing the source");
var typed=source.Remove(ranges[0].RootStart,ranges[0].RootLength).Insert(ranges[0].RootStart,"StudioRangeSlots(new Wander(.3), new Wander(.5))")+"\nnamespace TKR.WorldServer.logic { partial class BehaviorDb { static State StudioRangeSlots(Behavior first, params Behavior[] tail) => new State(new TKR.WorldServer.logic.behaviors.Timed(1000, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Prepend(tail, first)))); } }";
var typedDocument=new BehaviorDocument(typed,adapter,sourceFile:file);var typedVariants=typedDocument.Entities.Where(e=>e.Registration?.Shared==true).Take(2).ToArray();
var firstSlot=typedVariants[1].Root.Children.Single(n=>n.Argument?.Name=="first");var tailSlot=typedVariants[0].Root.Children.Single(n=>n.Argument?.Name=="tail");typedDocument.LockedNodes.Add(firstSlot.Id);typedDocument.AddChild(tailSlot.Id,"new Wander(.4)");
Check(typedDocument.Entities.Where(e=>e.Registration?.Shared==true).All(e=>e.Root.Children.Single(n=>n.Argument?.Name=="tail").Children.Count==2)&&typedDocument.Find(firstSlot.Id).Children.Single().Parameters.Single().Expression==".3","A shared typed-slot lock protects its logical region while allowing an independent slot with the same call span");
typedDocument.LockedNodes.Clear();typedDocument.LockedNodes.Add(typedDocument.Entities.First(e=>e.EntityId==typedVariants[1].EntityId).Root.Children.Single(n=>n.Argument?.Name=="tail").Id);var typedLocked=false;var typedSource=typedDocument.Source;
try{typedDocument.AddChild(tailSlot.Id,"new Wander(.6)");}catch(InvalidOperationException){typedLocked=true;}
Check(typedLocked&&typedDocument.Source==typedSource,"A typed-slot lock through one range alias blocks editing the equivalent slot through another alias");
var block=named.Replace("_ => new State(new PetFollow())","_ => { return new State(new PetFollow()); }");var blockDocument=new BehaviorDocument(block,adapter,sourceFile:file);
Check(blockDocument.Entities.Where(e=>e.Registration?.Shared==true).All(e=>e.Root.Kind=="Opaque"&&e.Registration!.Boundary.Contains("delegate")),"Block factories retain an explicit opaque source boundary without executing the factory for discovery");
var enemyFile=adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile;var privateEnemySource=File.ReadAllText(enemyFile);
var members=adapter.Catalog().Where(e=>e.SourceFile==enemyFile).OrderBy(e=>e.ObjectType).ToArray();
var pair=members.Zip(members.Skip(1)).First(p=>p.Second.ObjectType==p.First.ObjectType+1);var minimum=pair.First.Id;var maximum=pair.Second.Id;
var fixture=$$"""
namespace TKR.WorldServer.logic {
    public partial class BehaviorDb {
        private _ StudioRangeFixture = () => Behav().InitMany("{{minimum}}", "{{maximum}}",
            id => new State(new TKR.WorldServer.logic.behaviors.Shoot(20, count: id == "{{minimum}}" ? 3 : 5, fixedAngle: 0, coolDown: 100)));
    }
}
""";
var rangeDocument=new BehaviorDocument(fixture,adapter,sourceFile:enemyFile);
Check(rangeDocument.Entities.Select(e=>e.EntityId).SequenceEqual(new[]{minimum,maximum}),"Enemy fixture discovers both adjacent actual XML members from its shared native factory");
var constants=fixture.Replace("private _ StudioRangeFixture",$"const string StudioRangeMin = \"{minimum}\", StudioRangeMax = \"{maximum}\";\n    private _ StudioRangeFixture").Replace($".InitMany(\"{minimum}\", \"{maximum}\",", ".InitMany(StudioRangeMin, StudioRangeMax,");
Check(adapter.ForSource(constants,enemyFile).AdditionalEncounters().Select(r=>r.EntityId).SequenceEqual(new[]{minimum,maximum}),"Range endpoint constants resolve through the original source semantic model");
var getter=fixture.Replace("private _ StudioRangeFixture","private static System.Func<string, State> StudioRangeGetter => throw new System.InvalidOperationException(\"Do not execute during discovery\");\n    private _ StudioRangeFixture");
var lambda=getter.IndexOf("id =>",StringComparison.Ordinal);var lambdaEnd=getter.IndexOf(";",lambda,StringComparison.Ordinal);getter=getter.Remove(lambda,lambdaEnd-lambda).Insert(lambda,"StudioRangeGetter)");
var getterDoc=new BehaviorDocument(getter,adapter,sourceFile:enemyFile);
Check(getterDoc.Entities.Count==2&&getterDoc.Entities.All(e=>e.Root.Kind=="Opaque"&&getterDoc.Expression(e.Root)=="StudioRangeGetter"&&e.Registration!.Boundary.Contains("delegate")),"Delegate property discovery retains the source boundary without invoking its throwing getter");
var foreign=fixture.Replace("Behav().InitMany","StudioForeign.InitMany").Replace("private _ StudioRangeFixture = () =>", "private object StudioForeignFixture =").Replace("public partial class BehaviorDb {","public partial class BehaviorDb {\n private static class StudioForeign { public static object InitMany(string first, string last, System.Func<string, State> factory) => throw new System.Exception(); }");
Check(adapter.ForSource(foreign,enemyFile).AdditionalEncounters().Count==0,"A same-name foreign InitMany API is excluded by its original compiler symbol");
var xmlData=new TKR.Shared.resources.XmlData();xmlData.LoadXmls(Path.Combine(server,"TKR.Shared/resources/xml"),"*.xml");
var canonical=resources.GroupBy(e=>e.ObjectType).Select(g=>g.First()).OrderBy(e=>e.ObjectType).ToArray();var hole=canonical.Zip(canonical.Skip(1)).First(p=>p.Second.ObjectType-p.First.ObjectType is >1 and <20&&Enumerable.Range(p.First.ObjectType+1,p.Second.ObjectType-p.First.ObjectType-1).Any(type=>!xmlData.ObjectTypeToId.ContainsKey((ushort)type)));
var gapSource=fixture.Replace($".InitMany(\"{minimum}\", \"{maximum}\",",$".InitMany(\"{hole.First.Id}\", \"{hole.Second.Id}\",");
var gapFacts=adapter.ForSource(gapSource,enemyFile).AdditionalEncounters();
Check(gapFacts.Count>=2&&gapFacts.All(r=>r.Boundary.Contains("native initialization fails")&&r.Boundary.Contains("source candidates")),"Missing numeric XML entries expose the actual native initialization failure boundary instead of implying skipped types");
foreach(var (id,count) in new[]{(minimum,3),(maximum,5)})
{
    var project=new StudioProject{Source=fixture,SourceFile=enemyFile,EntityId=id,Identities=rangeDocument.Identities};
    var runtime=await CompiledRuntime.PrepareAsync(server,project,artifactRoot:Path.GetFullPath(".artifacts/registrations/compiled"));
    await using var session=await runtime.StartAsync();await session.AdvanceAsync(new(),100);
    Check(session.Shots.Any(s=>s.Count==count),$"Compiled native InitMany invokes the original factory with canonical XML ID for {id}");
}
Check(File.ReadAllText(enemyFile)==privateEnemySource,"Range compilation and native execution preserve supplied enemy source");
var precedence=fixture.Replace("Behav().InitMany",$"Behav().Init(\"{minimum}\", new State(new TKR.WorldServer.logic.behaviors.Shoot(20, count: 2, fixedAngle: 0, coolDown: 100))).InitMany");
var precedenceDoc=new BehaviorDocument(precedence,adapter,sourceFile:enemyFile);
Check(precedenceDoc.Entities.Count==2&&precedenceDoc.Entities.Single(e=>e.EntityId==minimum).Registration==null&&precedenceDoc.Entities.Single(e=>e.EntityId==maximum).Registration?.Shared==true,"Local registration order retains an explicit first definition and projects only the applicable range variant");
var precedenceProject=new StudioProject{Source=precedence,SourceFile=enemyFile,EntityId=minimum,Identities=precedenceDoc.Identities};
var precedenceRuntime=await CompiledRuntime.PrepareAsync(server,precedenceProject,artifactRoot:Path.GetFullPath(".artifacts/registrations/compiled"));
await using(var native=await precedenceRuntime.StartAsync()){await native.AdvanceAsync(new(),100);Check(native.Shots.Any(s=>s.Count==2)&&native.Shots.All(s=>s.Count!=3),"Actual native InitMany leaves an existing explicit definition untouched");}
Check(adapter.Catalog().Any(e=>e.Id=="Black Cat")&&adapter.PreviewContextRequirement(document,"Black Cat")?.Contains("owner context")==true,"Actual pet range templates appear in the library with a truthful PetFollow owner/world playback requirement");
Console.WriteLine($"{checks} registration discovery checks passed");
