using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var primary=Path.GetFullPath(adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile);
var helper=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=primary).SourceFile);
var original=File.ReadAllText(primary);var originalHelper=File.ReadAllText(helper);
const string source="""
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioGroupFixture=()=>Behav().Init("Cave Pirate Brawler",StudioGroupCaller()).Init("Pirate Lieutenant",StudioGroupCaller());
    static State StudioGroupCaller()=>new State(StudioGroupShared(),new State("phase-1"),new State("phase-1-2"));
} }
""";
const string shared="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioGroupShared()=>new State(new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100),new TKR.WorldServer.logic.behaviors.Shoot(20,count:5,fixedAngle:0,coolDown:100),new State("keep"));
} }
""";
var project=new StudioProject{SourceFile=primary,Source=source,EntityId="Cave Pirate Brawler",AdditionalSources=[new(helper,originalHelper+shared)],Seed=7,Player=new(Invulnerable:true)};
var scoped=adapter.ForProject(project);var caller=new BehaviorDocument(source,scoped,sourceFile:primary);var dependency=new BehaviorDocument(originalHelper+shared,scoped,sourceFile:helper);
var body=dependency.Definitions.Single(d=>d.Definition!.Label=="StudioGroupShared");var actions=body.Root.Children.Take(2).Select(n=>n.Id).ToArray();var keep=body.Root.Children.Last().Id;
project=project with{Identities=caller.Identities,AdditionalSources=[project.AdditionalSources[0] with{Identities=dependency.Identities,CollapsedNodes=[keep]}]};
Reject(()=>SourcePhaseRefactor.Group(adapter,project,helper,actions,"phase-1"),"Shared grouping rejects a new phase name already used by its external callers");
var grouped=SourcePhaseRefactor.Group(adapter,project,helper,actions.Reverse().Concat(actions),"phase-1",chooseUniqueName:true);
var after=grouped.After.AdditionalSources.Single(s=>s.SourceFile==helper);var accepted=new BehaviorDocument(after.Source,adapter.ForProject(grouped.After),after.Identities,helper);
var root=accepted.Find(body.Root.Id);var group=root.Children.Single(n=>accepted.StateName(n.Id)=="phase-1-3");
Check(grouped.Changes.Count==1&&grouped.After.Source==source,"Grouping changes only the actual shared body while retaining both callers");
Check(group.Children.Select(n=>n.Id).SequenceEqual(actions)&&root.Children.Last().Id==keep,"Grouping normalizes duplicate selection and preserves exact action order and neighboring IDs");
Check(grouped.Before==project&&dependency.Source==originalHelper+shared,"Grouping plans an immutable complete source snapshot");
Check(after.CollapsedNodes.SequenceEqual(new[]{keep})&&project.AdditionalSources[0].Identities.All(i=>after.Identities.Any(n=>n.Id==i.Id)),"Grouping retains existing collapse metadata and every surviving identity");
Check(!project.AdditionalSources[0].Identities.Any(i=>i.Id==group.Id),"New shared group receives an independent source identity");
var change=grouped.Changes.Single();dependency.ApplySourceRevision(dependency.Revision,change.Before,change.After,change.AfterIdentities!,adapter.ForProject(grouped.After));
Check(dependency.Source==after.Source&&dependency.Undo()&&dependency.Source==originalHelper+shared&&dependency.Redo()&&dependency.Source==after.Source,"Accepted grouping retains per-document structural Undo/Redo");
Reject(()=>SourcePhaseRefactor.Group(adapter,project with{LockedNodes=[caller.Entities.First().Root.Id]},helper,actions,"attacks"),"A locked external caller blocks shared grouping before any source mutation");
Reject(()=>SourcePhaseRefactor.Group(adapter,project with{AdditionalSources=[project.AdditionalSources[0] with{LockedNodes=[actions[0]]}]},helper,actions,"attacks"),"A locked selected action blocks the complete grouping transaction");
Reject(()=>SourcePhaseRefactor.Group(adapter,project,helper,[actions[0],keep],"attacks"),"Nonadjacent source selections reject instead of changing action order");
Reject(()=>SourcePhaseRefactor.Group(adapter,project,helper,[body.Root.Id],"attacks"),"Declaring source roots remain outside child grouping");
Reject(()=>SourcePhaseRefactor.Group(adapter,project with{Source=source.Replace("new State(\"phase-1\")","new State(StudioGroupName())")+"\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static string StudioGroupName()=>\"phase-1\"; } }"},helper,actions,"attacks"),"Generated caller phase names prevent unproven uniqueness without evaluating their factory");
var phaseSource=(originalHelper+shared).Replace("new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100)","new State(\"left\",new TKR.WorldServer.logic.transitions.TimedTransition(100,\"keep\"))").Replace("new TKR.WorldServer.logic.behaviors.Shoot(20,count:5,fixedAngle:0,coolDown:100)","new State(\"right\")");
var phaseProject=project with{AdditionalSources=[new(helper,phaseSource)]};var phaseDoc=new BehaviorDocument(phaseSource,adapter.ForProject(phaseProject),sourceFile:helper);
var phaseRoot=phaseDoc.Definitions.Single(d=>d.Definition!.Label=="StudioGroupShared").Root;var phaseIds=phaseRoot.Children.Take(2).Select(n=>n.Id).ToArray();
phaseProject=phaseProject with{AdditionalSources=[phaseProject.AdditionalSources[0] with{Identities=phaseDoc.Identities,CollapsedNodes=[phaseIds[0]]}]};
var phaseGrouped=SourcePhaseRefactor.Group(adapter,phaseProject,helper,phaseIds,"branches");var phaseRecord=phaseGrouped.After.AdditionalSources.Single(s=>s.SourceFile==helper);
var phaseAccepted=new BehaviorDocument(phaseRecord.Source,adapter.ForProject(phaseGrouped.After),phaseRecord.Identities,helper);var phaseGroup=phaseAccepted.Find(phaseRoot.Id).Children.Single(n=>phaseAccepted.StateName(n.Id)=="branches");
Check(phaseGroup.Children.Select(n=>n.Id).SequenceEqual(phaseIds)&&phaseGroup.Children.Select(n=>phaseAccepted.StateName(n.Id)).SequenceEqual(new[]{"left","right"}),"Grouping existing shared phases retains their literal names and editor identities");
Check(phaseGrouped.After.Source==source&&phaseAccepted.Find(phaseIds[0]).Children.Single(n=>n.Kind=="TimedTransition").Parameters.Any(p=>p.Expression=="\"keep\"")&&phaseRecord.CollapsedNodes.Contains(phaseIds[0]),"Phase grouping retains existing transition destinations and nested collapse metadata");
Reject(()=>SourcePhaseRefactor.Group(adapter,phaseProject,helper,phaseIds,"keep"),"Grouping rejects collisions in the shared body as well as external callers");
var untracked=project with{AdditionalSources=[project.AdditionalSources[0] with{Source=originalHelper+shared+"\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static State StudioGroupUnused()=>new State(\"phase-1-3\"); } }"}]};
var unrelated=SourcePhaseRefactor.Group(adapter,untracked,helper,actions,"phase-1",chooseUniqueName:true);
var unrelatedRecord=unrelated.After.AdditionalSources.Single(s=>s.SourceFile==helper);var unrelatedDocument=new BehaviorDocument(unrelatedRecord.Source,adapter.ForProject(unrelated.After),unrelatedRecord.Identities,helper);
Check(unrelatedDocument.Find(body.Root.Id).Children.Any(n=>unrelatedDocument.StateName(n.Id)=="phase-1-3"),"Unrelated source definitions do not reserve encounter phase names");
var oneFile=project with{Source=source+shared,Identities=[],AdditionalSources=[]};var oneDocument=new BehaviorDocument(oneFile.Source,adapter.ForProject(oneFile),sourceFile:primary);
var oneBody=oneDocument.Definitions.Single(d=>d.Definition!.Label=="StudioGroupShared");var oneActions=oneBody.Root.Children.Take(2).Select(n=>n.Id).ToArray();oneFile=oneFile with{Identities=oneDocument.Identities};
Reject(()=>SourcePhaseRefactor.Group(adapter,oneFile,primary,oneActions,"phase-1"),"Same-file helper grouping checks every caller rather than only the helper's local names");
var oneGrouped=SourcePhaseRefactor.Group(adapter,oneFile,primary,oneActions,"phase-1",chooseUniqueName:true);
var oneAccepted=new BehaviorDocument(oneGrouped.After.Source,adapter.ForProject(oneGrouped.After),oneGrouped.After.Identities,primary);
Check(oneGrouped.After.AdditionalSources.Count==0&&oneGrouped.Changes.Single().SourceFile==primary&&oneAccepted.Find(oneBody.Root.Id).Children.Any(n=>oneAccepted.StateName(n.Id)=="phase-1-3"),"Same-file shared bodies use coordinated naming without creating artificial dependency files");
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,phaseGrouped.After))
{
    Check(runtime.Entities.Single().Phase=="left","Native grouping enters the original first child phase instead of its new parent label");
    await runtime.AdvanceAsync(new(),300);Check(runtime.Entities.Single().Phase=="keep","Native grouped phase retains its transition to an existing phase outside the new group");
}
foreach(var entity in new[]{"Cave Pirate Brawler","Pirate Lieutenant"})
{
    await using var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,grouped.After with{EntityId=entity});await runtime.AdvanceAsync(new(),300);
    Check(runtime.Entities.Single().Phase=="phase-1-3"&&runtime.Shots.Count>=2&&runtime.Shots.Select((shot,index)=>shot.Count==(index%2==0?3:5)).All(v=>v),"Native caller enters the shared group and emits ordered three/five-projectile volleys: "+entity);
}
Check(File.ReadAllText(primary)==original&&File.ReadAllText(helper)==originalHelper,"Grouping, history and both native callers leave supplied game files unchanged");
Console.WriteLine($"{checks} workspace grouping checks passed.");
