using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var primary=Path.GetFullPath(adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile);var helper=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=primary).SourceFile);var third=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=primary&&Path.GetFullPath(e.SourceFile)!=helper).SourceFile);
var originals=new[]{primary,helper,third}.ToDictionary(p=>p,File.ReadAllText);
const string source="""
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioCloneFixture=()=>Behav().Init("Cave Pirate Brawler",StudioCloneCaller()).Init("Pirate Lieutenant",StudioCloneCaller());
    static State StudioCloneCaller()=>new State(StudioCloneShared(3),new State("loop-copy"),new State("finish"));
} }
""";
const string shared="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioCloneShared(int count)=>new State("loop",StudioCloneAttack(count),StudioCloneChild,new TKR.WorldServer.logic.transitions.TimedRandomTransition(100,false,new[]{"loop",/* weight */"loop","finish"}));
    static TKR.WorldServer.logic.behaviors.Shoot StudioCloneAttack(int count)=>new TKR.WorldServer.logic.behaviors.Shoot(20,count:count,fixedAngle:0,coolDown:100);
} }
""";
const string child="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static readonly State StudioCloneChild=new State("nested");
} }
""";
var project=new StudioProject{SourceFile=primary,Source=source,EntityId="Cave Pirate Brawler",AdditionalSources=[new(helper,originals[helper]+shared),new(third,originals[third]+child)],Player=new(Invulnerable:true),Seed=7};
var scoped=adapter.ForProject(project);var doc=new BehaviorDocument(source,scoped,sourceFile:primary);var call=doc.Definitions.Single(d=>d.Definition!.Label=="StudioCloneCaller").Root.Children.First();
project=project with{Identities=doc.Identities};
var clone=SourcePhaseRefactor.CloneFactory(adapter,project,primary,call.Id,"StudioIndependentClone");
Check(clone.Changes.Count==3&&clone.After.Source.Contains("StudioCloneShared(3)")&&clone.After.Source.Contains("StudioIndependentClone(3)"),"Cloning adds an independent call beside the original and coordinates all declaring source files");
Check(clone.Before==project&&doc.Source==source,"Clone planning retains the complete accepted input snapshot");
var declaring=clone.After.AdditionalSources.Single(s=>s.SourceFile==helper);var fields=clone.After.AdditionalSources.Single(s=>s.SourceFile==third);
Check(declaring.Source.Contains("StudioIndependentClone")&&declaring.Source.Contains("StudioCloneAttackCopy")&&fields.Source.Contains("StudioCloneChildCopy"),"Cloned factory inputs, pure action helpers and shared child fields acquire independent declarations");
var bodyDoc=new BehaviorDocument(declaring.Source,adapter.ForProject(clone.After),declaring.Identities,helper);var body=bodyDoc.Definitions.Single(d=>d.Definition!.Label=="StudioIndependentClone");
Check(bodyDoc.StateName(body.Root.Id)=="loop-copy-2"&&fields.Source.Contains("\"nested-copy\""),"Complete cloned phase closure avoids caller collisions with independent phase names");
var choice=body.Root.Children.Single(n=>n.Kind=="TimedRandomTransition");
Check(BehaviorDocument.PhaseLiterals(choice.Parameters.Single(p=>p.Name.StartsWith("states")).Expression)!.Select(t=>t.Name).SequenceEqual(new[]{"loop-copy-2","loop-copy-2","finish"}),"Cloned internal random choices retain repetition and remap only owned destinations");
var acceptedCaller=new BehaviorDocument(clone.After.Source,adapter.ForProject(clone.After),clone.After.Identities,primary);var selected=acceptedCaller.Find(clone.Selection.Single());
Check(selected.Kind=="Factory"&&selected.Id!=call.Id&&acceptedCaller.Find(call.Id).Parameters.Any(p=>p.Expression=="3"),"New factory call has independent identity while the original call and inputs remain stable");
foreach(var change in clone.Changes)
{
    var editing=new BehaviorDocument(change.Before,scoped,change.Identities,change.SourceFile);editing.ApplySourceRevision(editing.Revision,change.Before,change.After,change.AfterIdentities!,adapter.ForProject(clone.After));
    Check(editing.RestoreWorkspaceHistory(false,scoped)&&editing.Source==change.Before&&editing.RestoreWorkspaceHistory(true,adapter.ForProject(clone.After))&&editing.Source==change.After&&change.AfterIdentities!.All(i=>editing.Identities.Any(n=>n.Id==i.Id)),"Clone workspace history restores exact declarations and every identity against the full source snapshot: "+Path.GetFileName(change.SourceFile));
}
Reject(()=>SourcePhaseRefactor.CloneFactory(adapter,project,primary,call.Id,"StudioCloneShared"),"An existing member name blocks the entire clone transaction");
Reject(()=>SourcePhaseRefactor.CloneFactory(adapter,project with{LockedNodes=[call.Id]},primary,call.Id,"StudioBlockedClone"),"Locked selected calls protect independent definition cloning");
var helperDoc=new BehaviorDocument(originals[helper]+shared,scoped,sourceFile:helper);var root=helperDoc.Definitions.Single(d=>d.Definition!.Label=="StudioCloneShared").Root;
Reject(()=>SourcePhaseRefactor.CloneFactory(adapter,project with{AdditionalSources=[project.AdditionalSources[0] with{Identities=helperDoc.Identities,LockedNodes=[root.Id]},project.AdditionalSources[1]]},primary,call.Id,"StudioBlockedBodyClone"),"Locks on foreign shared bodies protect the whole clone closure");
var changedName=project with{AdditionalSources=[project.AdditionalSources[0] with{Source=(originals[helper]+shared).Replace("new State(\"loop\",","new State(\"loop\"+count,")},project.AdditionalSources[1]]};
Reject(()=>SourcePhaseRefactor.CloneFactory(adapter,changedName,primary,call.Id,"StudioGeneratedClone"),"Generated phase names reject independent cloning without invoking their factory");
var changedTarget=project with{AdditionalSources=[project.AdditionalSources[0] with{Source=(originals[helper]+shared).Replace("new[]{\"loop\",/* weight */\"loop\",\"finish\"}","StudioCloneTargets()")+"\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static string[] StudioCloneTargets()=>new[]{\"loop\",\"finish\"}; } }"},project.AdditionalSources[1]]};
Reject(()=>SourcePhaseRefactor.CloneFactory(adapter,changedTarget,primary,call.Id,"StudioDynamicClone"),"Dynamic phase destinations preserve the complete source-reconciliation boundary");
var retained=bodyDoc.Definitions.Single(d=>d.Definition!.Label=="StudioCloneShared");
Check(bodyDoc.StateName(retained.Root.Id)=="loop"&&retained.Root.Children.Any(n=>n.Kind=="Factory"&&n.Label.Contains("StudioCloneAttack")),"Original phase names and original helper references remain unchanged beside their clones");
var output=Path.GetFullPath(".artifacts/clone-workspace");Directory.CreateDirectory(output);var path=Path.Combine(output,"clone.rbs");BehaviorDocument.Save(path,clone.After);var loaded=BehaviorDocument.Load(path);
Check(loaded.Source==clone.After.Source&&loaded.AdditionalSources.Select(s=>s.Source).SequenceEqual(clone.After.AdditionalSources.Select(s=>s.Source)),"Save/reopen retains the new call and all independently cloned declarations");
foreach(var entity in new[]{"Cave Pirate Brawler","Pirate Lieutenant"})
{
    await using var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,clone.After with{EntityId=entity},phase:"loop-copy-2");await runtime.AdvanceAsync(new(),100);
    Check(runtime.Entities.Single().Phase is "nested-copy" or "finish"&&runtime.Shots.Any(s=>s.Count==3),"Native independent clone loads its own child value and retained bound attack input: "+entity);
}
Check(originals.All(p=>File.ReadAllText(p.Key)==p.Value),"Clone declarations, source history and native playback leave supplied game files unchanged");
Console.WriteLine($"{checks} clone checks passed.");
