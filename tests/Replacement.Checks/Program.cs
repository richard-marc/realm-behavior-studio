using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
const string primaryId="Cave Pirate Brawler",replacementId="Pirate Lieutenant";
var primary=adapter.Catalog().Single(e=>e.Id==primaryId).SourceFile;var original=File.ReadAllText(primary);
string Source(string action)=>"""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioReplacementFixture=()=>Behav().Init("Cave Pirate Brawler",new State(new State("original",ACTION))).Init("Pirate Lieutenant",new State(new State("replacement",new ChangeSize(30,130),new Shoot(20,count:7,fixedAngle:0,coolDown:100))));
} }
""".Replace("ACTION",action);
StudioProject Project(string action)=>new(){EntityId=primaryId,SourceFile=primary,Source=Source(action),Seed=7,Player=new(Invulnerable:true,WeaponId:"Staff of Diabolic Secrets",Attack:0,Dexterity:0,StartX:66.5,StartY:64.5)};
var project=Project("new Transform(\"Pirate Lieutenant\")");var doc=new BehaviorDocument(project.Source,adapter,sourceFile:primary);
Check(adapter.Primitives.Single(p=>p.Name=="Transform").Simulatable&&adapter.Primitives.Single(p=>p.Name=="TransformOnDeath").Simulatable,"Replacement authoring binds actual native transform constructors");
Check(adapter.NeedsCompiledRuntime(doc,primaryId),"Native replacement forces the edited complete behavior database instead of a one-actor override");
Check(adapter.EntityParameterFor(doc,doc.AllNodes.Single(n=>n.Kind=="Transform")) is {Name:"target",Scope:EntityResourceScope.Resolvable},"Transformation picker exposes native resolvable XML objects");
EncounterReplay tape;
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,project))
{
    var originalActor=runtime.Entities.Single();runtime.Recording=new(runtime);await runtime.AdvanceAsync(new(),100);
    var replacement=runtime.Entities.Single();
    Check(replacement.Id!=originalActor.Id&&replacement.Name==replacementId&&replacement.X==originalActor.X&&replacement.Y==originalActor.Y,"Native Transform removes its host and resolves a distinct replacement at the original position");
    Check(runtime.BossHp==0&&runtime.InspectTimers().Contains("no longer active"),"Removed original actor no longer reports live health or timers");
    var rejected=false;try{await runtime.SetBossHpAsync(.5);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Health override rejects a removed original actor without guessing a replacement");
}
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,project))
{
    runtime.Recording=new(runtime);await runtime.AdvanceAsync(new(),500);
    Check(runtime.Entities.Single() is {Name:replacementId,Phase:"replacement",Size:130},"Replacement executes its own edited native behavior database entry");
    Check(runtime.Shots.Count>0&&runtime.Shots.All(s=>s.Count==7&&s.OwnerId==runtime.Entities.Single().Id),"Replacement attacks use its native actor identity and edited volley");
    Check(runtime.Events.Any(e=>e.Kind=="despawn"&&e.Message.Contains(primaryId))&&runtime.Events.Any(e=>e.Kind=="spawn"&&e.Message.Contains(replacementId)),"Native actor turnover is recorded as removal and creation without fabricated ownership");
    Check(runtime.Entities.Single().ParentId==null&&!runtime.Entities.Single().SpawnedByBehavior,"Transform preserves the game's unassigned parent and behavior-spawn distinction");
    tape=EncounterReplay.Capture(project,null,EncounterReplay.Identify(adapter,project,runtime.Stamp.BuildId),runtime);
}
var replay=await ReplayCursor.RestoreAsync(adapter,tape,500);Check(replay.Session.Entities.Single() is {Name:replacementId,Phase:"replacement",Size:130}&&replay.Session.BossHp==0,"Native replay reproduces actor replacement and the removed original health state");replay.Session.Dispose();
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,Project("new TransformOnDeath(\"Pirate Lieutenant\",min:2,max:2,probability:1)")))
{
    for(var i=0;i<50&&runtime.Entities.Any(e=>e.Name==primaryId);i++)await runtime.AdvanceAsync(new(Attack:true,AimX:64.5,AimY:64.5),100);
    Check(runtime.Entities.Count(e=>e.Name==replacementId)==2&&runtime.Entities.All(e=>e.Name!=primaryId),"Actual XML weapon death executes native TransformOnDeath and its inclusive replacement count");
    Check(runtime.Events.Any(e=>e.Kind=="death")&&runtime.BossHp==0,"Weapon-driven death retains native event evidence and removes the original health target");
}
var portal=EntityResources.Catalog(EntityResourceScope.Resolvable).First(e=>e.RuntimeKind=="Portal"&&e.Id.Contains("Pirate Cave",StringComparison.Ordinal)).Id;
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,Project("new Transform(\""+portal+"\")")))
{
    await runtime.AdvanceAsync(new(),100);Check(runtime.Entities.Count==0&&runtime.WorldObjects.Any(e=>e.Name==portal),"Native portal replacement enters its actual world collection and appears in protocol snapshots");
    Check(runtime.WorldObjects.Single(e=>e.Name==portal) is {X:64.5f,Y:64.5f}&&runtime.BossHp==0,"Portal replacement retains native position without inventing enemy health");
}
Check(File.ReadAllText(primary)==original,"Replacement authoring, compilation, weapon death and replay leave original game source unchanged");
Console.WriteLine($"{checks} replacement checks passed.");
