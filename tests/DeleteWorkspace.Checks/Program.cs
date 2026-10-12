using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var primary=Path.GetFullPath(adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile);
var helper=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=primary).SourceFile);
var third=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=primary&&Path.GetFullPath(e.SourceFile)!=helper).SourceFile);
var original=File.ReadAllText(primary);var originalHelper=File.ReadAllText(helper);var originalThird=File.ReadAllText(third);
const string source="""
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDeleteFixture=()=>Behav().Init("Cave Pirate Brawler",StudioDeleteCaller()).Init("Pirate Lieutenant",StudioDeleteCaller());
    static State StudioDeleteCaller()=>new State(new State("entry",new TimedTransition(100,"loop")),StudioDeleteShared(),new State("finish"),new State("choice",new TimedRandomTransition(100,false,new[]{"loop",/* repeated */"loop","finish"})),new State("nested-choice",new TimedTransition(100,"nested")));
} }
""";
const string shared="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioDeleteShared()=>new State(new State("loop",new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100),new State("nested")),new State("keep",new TKR.WorldServer.logic.behaviors.Shoot(20,count:5,fixedAngle:0,coolDown:100)));
} }
""";
const string orders="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static Behavior[] StudioDeleteOrders()=>[new TKR.WorldServer.logic.behaviors.Order(10,"Cave Pirate Brawler","loop"),new TKR.WorldServer.logic.behaviors.OrderOnDeath(10,"Pirate Lieutenant","nested"),new TKR.WorldServer.logic.behaviors.Order(10,"Cave Pirate Captain","loop")];
    static TKR.WorldServer.logic.behaviors.Order[] StudioDeleteImplicit()=>[new(10,"Cave Pirate Brawler","loop")];
} }
""";
var project=new StudioProject{SourceFile=primary,Source=source,EntityId="Cave Pirate Brawler",AdditionalSources=[new(helper,originalHelper+shared),new(third,originalThird+orders)],Seed=7,Player=new(Invulnerable:true)};
var scoped=adapter.ForProject(project);var primaryDoc=new BehaviorDocument(source,scoped,sourceFile:primary);var dependency=new BehaviorDocument(project.AdditionalSources[0].Source,scoped,sourceFile:helper);
var body=dependency.Definitions.Single(d=>d.Definition!.Label=="StudioDeleteShared");var branch=body.Root.Children.Single(n=>dependency.StateName(n.Id)=="loop");
project=project with{Identities=primaryDoc.Identities,AdditionalSources=[project.AdditionalSources[0] with{Identities=dependency.Identities,CollapsedNodes=[branch.Id]},project.AdditionalSources[1]]};
var deleted=SourcePhaseRefactor.Delete(adapter,project,helper,branch.Id);
Check(deleted.Changes.Count==3,"One deletion plans the actual shared branch, both callers and native incoming orders");
Check(!deleted.After.AdditionalSources[0].Source.Contains("new State(\"loop\"")&&!deleted.After.AdditionalSources[0].Source.Contains("new State(\"nested\"")&&deleted.After.AdditionalSources[0].Source.Contains("new State(\"keep\""),"Shared deletion removes the selected nested branch and retains neighboring native phases");
Check(!deleted.After.Source.Contains("TimedTransition(100,\"loop\")")&&!deleted.After.Source.Contains("TimedRandomTransition")&&!deleted.After.Source.Contains("TimedTransition(100,\"nested\")")&&deleted.After.Source.Contains("new State(\"choice\")"),"Caller transitions and repeated-choice transitions are removed while their phases remain");
var orderSource=deleted.After.AdditionalSources.Single(s=>s.SourceFile==third).Source;
Check(!orderSource.Contains("\"Cave Pirate Brawler\",\"loop\"")&&!orderSource.Contains("\"Pirate Lieutenant\",\"nested\"")&&orderSource.Contains("\"Cave Pirate Captain\",\"loop\""),"Incoming self/shared orders delete while foreign actor destinations retain their native source");
Check(orderSource.Contains("StudioDeleteImplicit()=>[]"),"Target-typed native orders delete through their actual collection slot");
Check(deleted.Before==project&&project.Source==source&&project.AdditionalSources[0].Source==originalHelper+shared,"Preparing deletion leaves accepted project snapshots unchanged");
Check(!deleted.After.AdditionalSources[0].CollapsedNodes.Contains(branch.Id),"Removed branch collapse metadata does not survive into the accepted project");
foreach(var change in deleted.Changes)
{
    var editing=new BehaviorDocument(change.Before,scoped,change.Identities,change.SourceFile);var beforeIds=editing.Identities;
    editing.ApplySourceRevision(editing.Revision,change.Before,change.After,change.AfterIdentities!,adapter.ForProject(deleted.After));
    Check(editing.Source==change.After&&editing.Identities.Select(i=>i.Id).SequenceEqual(change.AfterIdentities!.Select(i=>i.Id)),"Accepted structural revision installs exact planned surviving identities: "+Path.GetFileName(change.SourceFile));
    Check(editing.Undo()&&editing.Source==change.Before&&beforeIds.All(i=>editing.Identities.Any(n=>n.Id==i.Id))&&editing.Redo()&&editing.Source==change.After,"Deletion preserves exact per-document Undo/Redo and restored identities: "+Path.GetFileName(change.SourceFile));
}
var stale=new BehaviorDocument(source,scoped,primaryDoc.Identities,primary);var primaryChange=deleted.Changes.Single(c=>c.SourceFile==primary);
Reject(()=>stale.ApplySourceRevision(stale.Revision+1,source,primaryChange.After,primaryChange.AfterIdentities!,adapter.ForProject(deleted.After)),"Stale coordinated structural revisions reject before mutation");
Check(stale.Source==source,"Rejected stale revision retains the accepted source");
Reject(()=>SourcePhaseRefactor.Delete(adapter,project with{LockedNodes=[primaryDoc.Entities.First().Root.Id]},helper,branch.Id),"A locked caller blocks every file in the shared deletion");
Reject(()=>SourcePhaseRefactor.Delete(adapter,project with{AdditionalSources=[project.AdditionalSources[0] with{LockedNodes=[branch.Id]},project.AdditionalSources[1]]},helper,branch.Id),"A locked branch blocks coordinated deletion before planning any mutation");
Reject(()=>SourcePhaseRefactor.Delete(adapter,project with{Source=source.Replace("new[]{\"loop\",/* repeated */\"loop\",\"finish\"}","StudioDeleteNames")+"\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static string[] StudioDeleteNames=>new[]{\"loop\",\"finish\"}; } }"},helper,branch.Id),"Dynamic phase choices retain their source reconciliation boundary");
Reject(()=>SourcePhaseRefactor.Delete(adapter,project,helper,body.Root.Id),"A declaring source body cannot be deleted as a child phase");
var keep=body.Root.Children.Single(n=>dependency.StateName(n.Id)=="keep");
var bulk=SourcePhaseRefactor.Delete(adapter,project,helper,[branch.Id,branch.Children.Single(n=>dependency.StateName(n.Id)=="nested").Id,keep.Id]);
Check(!bulk.After.AdditionalSources[0].Source.Contains("new State(\"loop\"")&&!bulk.After.AdditionalSources[0].Source.Contains("new State(\"keep\"")&&bulk.Changes.Count==3,"Bulk deletion normalizes ancestor/descendant selection and reconciles multiple shared phases in one plan");
Reject(()=>SourcePhaseRefactor.Delete(adapter,project with{AdditionalSources=[project.AdditionalSources[0],project.AdditionalSources[1] with{Source=(originalThird+orders).Replace("\"Cave Pirate Brawler\"","StudioDeleteTarget()")+"\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static string StudioDeleteTarget()=>\"Cave Pirate Brawler\"; } }"}]},helper,branch.Id),"Unresolved incoming order ownership blocks the whole deletion without executing its target getter");
var orderDocument=new BehaviorDocument(project.AdditionalSources[1].Source,scoped,sourceFile:third);
var order=orderDocument.AllNodes.First(n=>n.Kind=="Order"&&n.Parameters.Any(p=>p.Expression=="\"Cave Pirate Brawler\""));
Reject(()=>SourcePhaseRefactor.Delete(adapter,project with{AdditionalSources=[project.AdditionalSources[0],project.AdditionalSources[1] with{Identities=orderDocument.Identities,LockedNodes=[order.Id]}]},helper,branch.Id),"A locked incoming native order prevents all coordinated source changes");
var protectedDoc=new BehaviorDocument(primaryChange.Before,scoped,primaryChange.Identities,primary);protectedDoc.LockedNodes.Add(protectedDoc.Entities.First().Root.Id);
Reject(()=>protectedDoc.ApplySourceRevision(protectedDoc.Revision,primaryChange.Before,primaryChange.After,primaryChange.AfterIdentities!,adapter.ForProject(deleted.After)),"Applying an accepted structural plan still verifies the current document's locks");
Check(protectedDoc.Source==source,"Rejected locked structural installation leaves source and history unchanged");
foreach(var entity in new[]{"Cave Pirate Brawler","Pirate Lieutenant"})
{
    await using var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,deleted.After with{EntityId=entity});await runtime.AdvanceAsync(new(),300);
    Check(runtime.Entities.Single().Phase=="entry"&&runtime.Shots.Count==0,"Native caller loads the complete deletion and has no stale transition into its removed helper phase: "+entity);
}
Check(File.ReadAllText(primary)==original&&File.ReadAllText(helper)==originalHelper&&File.ReadAllText(third)==originalThird,"Shared deletion, history and native playback leave supplied game files unchanged");
Console.WriteLine($"{checks} workspace deletion checks passed.");
