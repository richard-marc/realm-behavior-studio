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
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioWorkspacePhaseFixture=()=>Behav()
        .Init("Cave Pirate Brawler",StudioWorkspaceCaller())
        .Init("Pirate Lieutenant",StudioWorkspaceCaller());
    static State StudioWorkspaceCaller()=>new State(new State("entry",new TimedTransition(100,"loop")),StudioWorkspaceShared(),new State("finish"),new State("choice",new TimedRandomTransition(100,false,new[]{"loop", /* repeated */ "loop","finish"})));
} }
""";
const string shared="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioWorkspaceShared()=>new State("loop",new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100),new TKR.WorldServer.logic.transitions.TimedTransition(200,"finish"));
} }
""";
const string orders="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static Behavior[] StudioWorkspaceIncoming()=>[new TKR.WorldServer.logic.behaviors.Order(10,"Cave Pirate Brawler","loop"),new TKR.WorldServer.logic.behaviors.OrderOnDeath(10,"Pirate Lieutenant","loop"),new TKR.WorldServer.logic.behaviors.Order(10,"Cave Pirate Captain","loop")];
    static TKR.WorldServer.logic.behaviors.Order[] StudioWorkspaceImplicitOrders()=>[new(10,"Cave Pirate Brawler","loop")];
} }
""";
var project=new StudioProject{SourceFile=primary,Source=source,AdditionalSources=[new(helper,originalHelper+shared),new(third,originalThird+orders)],Player=new(Invulnerable:true),Seed=7};
var scoped=adapter.ForProject(project);var primaryDoc=new BehaviorDocument(source,scoped,sourceFile:primary);var helperDoc=new BehaviorDocument(project.AdditionalSources[0].Source,scoped,sourceFile:helper);
var phase=helperDoc.Definitions.Single(d=>d.Definition!.Label=="StudioWorkspaceShared");
project=project with{Identities=primaryDoc.Identities,AdditionalSources=[project.AdditionalSources[0] with{Identities=helperDoc.Identities},project.AdditionalSources[1]]};
Check(phase.Definition!.StructurallyComplete&&!phase.Definition.CompletePhaseContents&&phase.Definition.CallerFiles.Any(p=>Path.GetFullPath(p)==primary),"Phase ownership distinguishes complete source bodies from cross-file coordination requirements");
var rename=SourcePhaseRefactor.Rename(adapter,project,helper,phase.Root.Id,"cycle");
Check(rename.Changes.Count==3,"One native phase rename plans changes to the shared body, callers and incoming-order file");
Check(rename.After.Source.Contains("TimedTransition(100,\"cycle\")")&&rename.After.Source.Contains("new[]{\"cycle\", /* repeated */ \"cycle\",\"finish\"}"),"Caller transitions and repeated native random-choice slots rename while retaining comments");
Check(rename.After.AdditionalSources.Single(s=>s.SourceFile==helper).Source.Contains("new State(\"cycle\""),"The phase declaration changes only in its actual shared source body");
var updatedOrders=rename.After.AdditionalSources.Single(s=>s.SourceFile==third).Source;
Check(updatedOrders.Contains("\"Cave Pirate Brawler\",\"cycle\"")&&updatedOrders.Contains("\"Pirate Lieutenant\",\"cycle\"")&&updatedOrders.Contains("\"Cave Pirate Captain\",\"loop\""),"Incoming native orders reconcile both affected callers and retain foreign entity destinations");
Check(updatedOrders.Contains("[new(10,\"Cave Pirate Brawler\",\"cycle\")]"),"Target-typed incoming native orders retain their original syntax and reconcile the real destination");
var aliased=project with{Source=source.Replace("\n} }","\n    static StudioWorkspaceCommand[] StudioWorkspaceCommands()=>[new(10,\"Cave Pirate Brawler\",\"loop\")];\n} }"),AdditionalSources=[project.AdditionalSources[0],project.AdditionalSources[1] with{Source="global using StudioWorkspaceCommand=TKR.WorldServer.logic.behaviors.Order;\n"+project.AdditionalSources[1].Source}]};
var aliasedRename=SourcePhaseRefactor.Rename(adapter,aliased,helper,phase.Root.Id,"cycle");
Check(!aliased.Source.Contains("Order",StringComparison.Ordinal)&&aliasedRename.After.Source.Contains("[new(10,\"Cave Pirate Brawler\",\"cycle\")]"),"Native order discovery follows global aliases even in files without the original type name");
Check(rename.Before==project&&project.Source==source&&project.AdditionalSources[0].Source==originalHelper+shared,"Planning a coordinated edit does not mutate the original workspace snapshot");
foreach(var change in rename.Changes)
{
    var doc=new BehaviorDocument(change.After,adapter.ForProject(rename.After),change.Identities,change.SourceFile);
    Check(change.Identities.All(i=>doc.Identities.Any(n=>n.Id==i.Id)),"A renamed source file retains every native authoring identity: "+Path.GetFileName(change.SourceFile));
    var editing=new BehaviorDocument(change.Before,scoped,change.Identities,change.SourceFile);
    editing.ApplyLiteralEdits(editing.Revision,change.Edits,adapter.ForProject(rename.After));
    Check(editing.Source==change.After&&editing.Undo()&&editing.Source==change.Before&&editing.Redo()&&editing.Source==change.After,"Accepted literal patches preserve the document's existing Undo/Redo contract: "+Path.GetFileName(change.SourceFile));
}
var staleDoc=new BehaviorDocument(source,scoped,primaryDoc.Identities,primary);var firstChange=rename.Changes.Single(c=>c.SourceFile==primary);
Reject(()=>staleDoc.ApplyLiteralEdits(staleDoc.Revision+1,firstChange.Edits,adapter.ForProject(rename.After)),"Stale coordinated literal edits reject before mutation");
Check(staleDoc.Source==source,"A rejected stale patch retains the exact caller source");
Reject(()=>SourcePhaseRefactor.Rename(adapter,project,helper,phase.Root.Id,"finish"),"A shared rename rejects a duplicate name in any native caller");
Reject(()=>SourcePhaseRefactor.Rename(adapter,project with{LockedNodes=[primaryDoc.Entities.First().Root.Id]},helper,phase.Root.Id,"cycle"),"A locked caller prevents a coordinated helper rename atomically");
Reject(()=>SourcePhaseRefactor.Rename(adapter,project with{AdditionalSources=[project.AdditionalSources[0] with{LockedNodes=[phase.Root.Id]},project.AdditionalSources[1]]},helper,phase.Root.Id,"cycle"),"A locked shared body prevents the entire multi-file rename");
var dynamic=project with{Source=source.Replace("new[]{\"loop\", /* repeated */ \"loop\",\"finish\"}","StudioWorkspaceNames")+"\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static string[] StudioWorkspaceNames=>new[]{\"loop\",\"finish\"}; } }"};
Reject(()=>SourcePhaseRefactor.Rename(adapter,dynamic,helper,phase.Root.Id,"cycle"),"Dynamic native destination arrays cannot silently leave hidden phase references stale");
var dynamicCaller=project with{Source=source.Replace(".Init(\"Pirate Lieutenant\",StudioWorkspaceCaller())",".Init(StudioWorkspaceEntity(),StudioWorkspaceCaller())")+"\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static string StudioWorkspaceEntity()=>\"Pirate Lieutenant\"; } }"};
Reject(()=>SourcePhaseRefactor.Rename(adapter,dynamicCaller,helper,phase.Root.Id,"cycle"),"A shared body used by an unresolved native registration rejects coordinated rename without executing its entity getter");
var unknownOrder=project with{AdditionalSources=[project.AdditionalSources[0],project.AdditionalSources[1] with{Source=(originalThird+orders).Replace("\"Cave Pirate Brawler\"","StudioWorkspaceUnknownTarget()")+"\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static string StudioWorkspaceUnknownTarget()=>\"Cave Pirate Brawler\"; } }"}]};
Reject(()=>SourcePhaseRefactor.Rename(adapter,unknownOrder,helper,phase.Root.Id,"cycle"),"An incoming order with unresolved entity ownership rejects the complete transaction");
var branch=project with{AdditionalSources=[project.AdditionalSources[0] with{Source=(originalHelper+shared).Replace("=>new State(\"loop\"","=>System.DateTime.Now.Millisecond>=0?new State(\"loop\"").Replace("(200,\"finish\"));","(200,\"finish\")):new State(\"other\");")},project.AdditionalSources[1]]};
Reject(()=>SourcePhaseRefactor.Rename(adapter,branch,helper,phase.Root.Id,"cycle"),"Generated conditional phase bodies retain their explicit coordination boundary");
var output=Path.GetFullPath(".artifacts/phase-workspace");Directory.CreateDirectory(output);var path=Path.Combine(output,"renamed.rbs");BehaviorDocument.Save(path,rename.After);var loaded=BehaviorDocument.Load(path);
Check(loaded.Source==rename.After.Source&&loaded.AdditionalSources.Select(s=>s.Source).SequenceEqual(rename.After.AdditionalSources.Select(s=>s.Source)),"Save/reopen retains the complete coordinated source set");
foreach(var id in new[]{"Cave Pirate Brawler","Pirate Lieutenant"})
{
    await using var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,rename.After with{EntityId=id});await runtime.AdvanceAsync(new(),200);
    Check(runtime.Entities.Single().Phase=="cycle","The original native caller resolves and enters the renamed shared phase: "+id);
}
Check(File.ReadAllText(primary)==original&&File.ReadAllText(helper)==originalHelper&&File.ReadAllText(third)==originalThird,"Coordinated source planning, persistence and native playback leave supplied game files untouched");
Console.WriteLine($"{checks} workspace phase checks passed.");
