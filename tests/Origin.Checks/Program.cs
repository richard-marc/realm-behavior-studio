using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var primary=Path.GetFullPath(adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile);
var helper=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=primary).SourceFile);
var original=File.ReadAllText(primary);var originalHelper=File.ReadAllText(helper);
const string source="""
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioOriginFixture=()=>Behav().Init("Cave Pirate Brawler",StudioOriginRoot(false)).Init("Pirate Lieutenant",StudioOriginRoot(true));
} }
""";
const string helpers="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioOriginRoot(bool second)=>second
        ?new State("same",new TKR.WorldServer.logic.behaviors.Follow(.2,range:1),new TKR.WorldServer.logic.behaviors.Shoot(20,count:5,fixedAngle:0,coolDown:100))
        :new State("same",new TKR.WorldServer.logic.behaviors.Follow(.2,range:1),new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100));
} }
""";
var project=new StudioProject{SourceFile=primary,Source=source,EntityId="Cave Pirate Brawler",AdditionalSources=[new(helper,originalHelper+helpers)],Seed=7,Player=new(Invulnerable:true),Arena=new(){Actors=[new("Pirate Lieutenant",66.5,64.5)]}};
var scoped=adapter.ForProject(project);var dependency=new BehaviorDocument(project.AdditionalSources[0].Source,scoped,sourceFile:helper);
project=project with{AdditionalSources=[project.AdditionalSources[0] with{Identities=dependency.Identities}]};
var prepared=await CompiledRuntime.PrepareAsync(adapter.ServerRoot,project);
var plain=await CompiledRuntime.PrepareAsync(adapter.ServerRoot,project,captureStateOrigins:false);
Check(prepared.Stamp.Source.StateOriginInstrumentation&&!plain.Stamp.Source.StateOriginInstrumentation,"Runtime stamps distinguish State-object provenance from untouched native export execution");
Check(prepared.Stamp.AssemblyHash!=plain.Stamp.AssemblyHash&&prepared.Stamp.Source.EditedSourceHash==plain.Stamp.Source.EditedSourceHash,"Instrumented and original native assemblies retain the same accepted source provenance with distinct verified builds");
EncounterReplay tape;
await using(var runtime=await prepared.StartAsync())
await using(var reference=await plain.StartAsync())
{
    runtime.Recording=new(runtime);await runtime.AdvanceAsync(new(),1000);await reference.AdvanceAsync(new(),1000);
    Check(runtime.Shots.SequenceEqual(reference.Shots)&&runtime.Entities.SequenceEqual(reference.Entities)&&runtime.InspectTimers()==reference.InspectTimers(),"Tracking preserves actual native attacks, movement, actor state and timers against uninstrumented execution");
    var events=runtime.Events.Where(e=>e.Kind=="transition"&&e.PhaseOrigin!=null).ToArray();
    File.WriteAllText(Path.GetFullPath(".artifacts/origin-events.json"),System.Text.Json.JsonSerializer.Serialize(new{runtime.Events,runtime.Entities,runtime.Movements},BehaviorDocument.JsonOptions));
    Check(events.Length==2&&events.All(e=>e.PhaseOrigin!.SourceFile==helper),"Both native actors report their actual foreign State construction sites");
    Check(events.Select(e=>e.PhaseOrigin!.Start).Distinct().Count()==2,"Equal phase names from different generated branches retain distinct object provenance");
    Check(events.All(e=>e.PhaseOrigin!.TryResolve(project,out var text)&&text.Substring(e.PhaseOrigin.Start,e.PhaseOrigin.Length).StartsWith("new State(\"same\"")),"Native construction spans and hashes resolve exact accepted helper expressions");
    Check(events.All(e=>e.NodeId!=null&&e.NodeId==e.PhaseOrigin!.NodeId&&dependency.AllNodes.Any(n=>n.Id==e.NodeId)),"Saved foreign identities are carried by actual native State instances");
    Check(runtime.Movements.Any(m=>!m.Player)&&runtime.Movements.Where(m=>!m.Player).All(m=>m.PhaseOrigin!=null&&m.PhaseNodeId==m.PhaseOrigin.NodeId),"Native movement records retain observed State construction without attributing a movement callback");
    Check(runtime.Events.Where(e=>e.Kind=="attack").All(e=>e.NodeId==null&&e.PhaseOrigin!=null),"Compiled attack records identify observed phase construction while leaving exact emission callback unknown");
    Check(!events[0].PhaseOrigin!.TryResolve(project with{AdditionalSources=[project.AdditionalSources[0] with{Source=project.AdditionalSources[0].Source+"\n// changed"}]},out _),"Source-origin resolution rejects changed accepted text before navigation");
    Check(!(events[0].PhaseOrigin! with{Start=-1}).TryResolve(project,out _)&&!(events[0].PhaseOrigin! with{Length=int.MaxValue}).TryResolve(project,out _),"Source-origin resolution rejects invalid spans without reading arbitrary files");
    tape=EncounterReplay.Capture(project,null,EncounterReplay.Identify(adapter,project,prepared.Stamp.BuildId),runtime);
}
var restored=await ReplayCursor.RestoreAsync(adapter,tape,1000,prepared);
Check(restored.Session.Events.SequenceEqual(tape.Events)&&restored.Session.Movements.SequenceEqual(tape.Movements),"Replay reproduces object-linked foreign phase events and movement origins exactly");restored.Session.Dispose();
var badEvents=tape.Events.ToArray();var foreign=Array.FindIndex(badEvents,e=>e.PhaseOrigin!=null);badEvents[foreign]=badEvents[foreign] with{PhaseOrigin=badEvents[foreign].PhaseOrigin! with{Start=0}};
var rejected=false;try{var bad=await ReplayCursor.RestoreAsync(adapter,tape with{Events=badEvents},1000,prepared);bad.Session.Dispose();}catch(InvalidOperationException){rejected=true;}
Check(rejected,"Replay rejects changed provenance instead of presenting a falsely attributed source event");
const string generated="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioOriginRoot(bool second)=>new State(System.Linq.Enumerable.ToArray(StudioOriginGenerated(second)));
    static System.Collections.Generic.IEnumerable<State> StudioOriginGenerated(bool second) {
        for(var i=0;i<1;i++)yield return new State(second?"generated-two":"generated-one",new TKR.WorldServer.logic.behaviors.Follow(.2,range:1),new TKR.WorldServer.logic.behaviors.Shoot(20,count:4,fixedAngle:0,coolDown:100));
    }
} }
""";
var generatedProject=project with{AdditionalSources=[new(helper,originalHelper+generated)]};
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,generatedProject))
{
    await runtime.AdvanceAsync(new(),100);var events=runtime.Events.Where(e=>e.Kind=="transition").ToArray();
    Check(events.Length==2&&events.All(e=>e.PhaseOrigin!=null&&e.PhaseOrigin.TryResolve(generatedProject,out _)),"Iterator-generated phases retain their executed construction site without static name inference");
    Check(events.All(e=>e.PhaseOrigin!.NodeId==null)&&runtime.Shots.All(s=>s.Count==4),"Generated phases keep source spans without inventing editor nodes or altering native iteration");
}
const string expression="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioOriginRoot(bool second) {
        if(StudioOriginExpression.Body.NodeType!=System.Linq.Expressions.ExpressionType.New)throw new System.Exception("Expression shape changed");
        var state=StudioOriginExpression.Compile()();state.Behaviors.Add(new TKR.WorldServer.logic.behaviors.Shoot(20,count:6,fixedAngle:0,coolDown:100));return state;
    }
    static System.Linq.Expressions.Expression<System.Func<State>> StudioOriginExpression=()=>new State("expression",new IStateChildren[0]);
} }
""";
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,project with{AdditionalSources=[new(helper,originalHelper+expression)]}))
{
    await runtime.AdvanceAsync(new(),100);
    Check(runtime.Entities.All(e=>e.Phase=="expression")&&runtime.Shots.All(s=>s.Count==6)&&runtime.Events.Where(e=>e.Kind=="transition").All(e=>e.PhaseOrigin==null),"Native expression-tree constructors keep their exact uninstrumented expression shape and explicit provenance boundary");
}
const string implicitSource="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioOriginRoot(bool second)=>new("implicit",new TKR.WorldServer.logic.behaviors.Shoot(20,count:7,fixedAngle:0,coolDown:100));
} }
""";
var implicitProject=project with{AdditionalSources=[new(helper,originalHelper+implicitSource)]};
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,implicitProject))
{
    await runtime.AdvanceAsync(new(),100);
    Check(runtime.Shots.Count>0&&runtime.Shots.All(s=>s.Count==7)&&runtime.Events.Where(e=>e.Kind=="transition").All(e=>e.PhaseOrigin!=null&&e.PhaseOrigin.TryResolve(implicitProject,out var text)&&text.Substring(e.PhaseOrigin.Start,e.PhaseOrigin.Length).StartsWith("new(")),"Target-typed native State constructors retain actual construction spans and execution");
}
Check(File.ReadAllText(primary)==original&&File.ReadAllText(helper)==originalHelper,"Runtime provenance, generated playback and differential checks leave game checkout files unchanged");
Console.WriteLine($"{checks} native origin checks passed.");
