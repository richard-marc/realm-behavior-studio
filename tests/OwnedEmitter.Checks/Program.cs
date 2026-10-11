using System.Reflection;
using Studio.Core;
using Studio.Raple;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;

var server=Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server");var adapter=new RapleAdapter(server);var checks=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception error)when(error is InvalidOperationException or ArgumentException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
const string helper="Gordo Procession Emitter",owner="Cave Pirate Brawler";
string Emitter(string axis="Horizontal",string entity=helper)=>"new OwnedLaneEmitters(\""+entity+"\", LaneAxes."+axis+", intervalMs: 200, startupMs: 200)";
BehaviorDocument Doc(string expression)=>new("class Fixture { void Build() { db.Init(\""+owner+"\", "+expression+"); } }",adapter);
var doc=Doc("new State("+Emitter()+")");var node=doc.Entities.Single().Root.Children.Single();var ids=doc.Identities;
Check(adapter.Primitives.Single(p=>p.Name=="OwnedLaneEmitters") is {Simulatable:true,Category:"Attacks"}&&adapter.ParametersFor(doc,node).Where(p=>p.Name is "intervalMs" or "startupMs").All(p=>p.Unit=="ms"),"Native owned lane constructor exposes genuine attack parameters and millisecond units");
var parameter=adapter.EntityParameterFor(doc,node)!;var choices=EntityResources.Catalog(parameter);
Check(parameter is {Scope:EntityResourceScope.Enemy,RequiredProjectileIndex:0,AllowsPlayer:false}&&choices.Any(e=>e.Id==helper)&&choices.All(e=>EnemyProjectiles.ForEntity(e.Id).Any(p=>p.Index==0)),"Emitter entity catalog requires actual Enemy packet owners with XML projectile zero");
var resource=adapter.ReadOwnedEmitterResource(doc,node);
Check(resource is {EntityId:helper,Projectile.Index:0}&&resource.Projectile==EnemyProjectiles.ForEntity(helper).Single(p=>p.Index==0),"Resource preview reads the child owner's actual projectile rather than the host enemy's XML");
Check(adapter.LaneAxes(doc,node).Select(c=>c.Name).SequenceEqual(new[]{"Horizontal","Vertical","Cross"})&&adapter.ReadLaneAxis(doc,node)=="Horizontal","Axis choices and selection come from the actual native enum");
adapter.ApplyLaneAxis(doc,owner,node.Id,doc.Revision,"Cross");Check(adapter.ReadLaneAxis(doc,doc.Find(node.Id))=="Cross"&&ids.All(i=>doc.Identities.Any(next=>next.Id==i.Id)),"Axis selection commits one typed source edit with stable identities");Check(doc.Undo()&&adapter.ReadLaneAxis(doc,doc.Find(node.Id))=="Horizontal","Axis selection has one atomic undo");
doc.LockedNodes.Add(node.Id);Reject(()=>adapter.ApplyLaneAxis(doc,owner,node.Id,doc.Revision,"Vertical"),"Axis edits respect source locks");doc.LockedNodes.Clear();Reject(()=>adapter.ApplyLaneAxis(doc,owner,node.Id,doc.Revision-1,"Vertical"),"Stale axis edits reject before mutation");
Reject(()=>adapter.ApplyEntityReference(doc,owner,node.Id,doc.Revision,"childName","Wizard"),"Native player XML cannot become an enemy lane packet owner");
var enemyWithoutProjectile=EntityResources.Catalog(EntityResourceScope.Enemy).First(e=>EnemyProjectiles.ForEntity(e.Id).All(p=>p.Index!=0));Reject(()=>adapter.ApplyEntityReference(doc,owner,node.Id,doc.Revision,"childName",enemyWithoutProjectile.Id),"An enemy without required projectile zero cannot enter the emitter picker");
Check(adapter.Validate(Doc("new State("+Emitter(entity:enemyWithoutProjectile.Id)+")"),owner).Any(d=>d.IsError)&&adapter.Validate(Doc("new State("+Emitter().Replace("intervalMs: 200","intervalMs: 99")+")"),owner).Any(d=>d.IsError)&&adapter.Validate(Doc("new State("+Emitter().Replace("startupMs: 200","startupMs: -1")+")"),owner).Any(d=>d.IsError),"Authoring validates native helper/projectile prerequisites and constructor timing bounds");
Check(adapter.Validate(Doc("new State("+Emitter().Replace("LaneAxes.Horizontal","(LaneAxes)99")+")"),owner).Any(d=>d.IsError),"Undefined cast enum values cannot bypass the native lane axis constraint");
Check(adapter.Callbacks("OwnedLaneEmitters").Select(c=>c.Stage).SequenceEqual(new[]{"Entry","Exit","Death"}),"Lifecycle inspection reports real native entry/exit/death callbacks without an invented ongoing action");
var nestedGuard=false;try{using var incompatible=new EncounterSession(adapter,Doc("new State("+Emitter(entity:"Ghost Ship")+")"),owner,7,new(Invulnerable:true));}catch(InvalidOperationException error){nestedGuard=error.Message.Contains("flat starting state");}
Check(nestedGuard,"Native playback guards a nested helper's unsupported state-tree replacement before any entry callback");
foreach(var axis in new[]{"Horizontal","Vertical","Cross"})
{
    using var native=new EncounterSession(adapter,Doc("new State("+Emitter(axis)+")"),owner,7,new(Invulnerable:true));native.Step(new());var primary=native.Entities.Single(e=>e.Name==owner);var children=native.Entities.Where(e=>e.Name==helper).OrderBy(e=>e.Id).ToArray();
    Check(children.Length==2&&children.All(e=>e.ParentId==primary.Id&&e.SpawnedByBehavior&&e.Phase=="lane-emission")&&(axis!="Horizontal"||children.All(e=>e.Y==primary.Y))&&(axis!="Vertical"||children.All(e=>e.X==primary.X))&&(axis!="Cross"||children.Count(e=>e.X==primary.X)==1),"Native "+axis+" arrangement creates two genuine owned helper entities");
    Check(native.Shots.Count==0,"Native "+axis+" helpers do not emit in the pending-spawn entry tick");for(var tick=0;tick<10;tick++)native.Step(new());
    Check(native.Shots.Count>0&&native.Shots.All(s=>children.Any(e=>e.Id==s.OwnerId)&&s.Index==0&&s.Count==1)&&children.All(e=>native.Shots.Any(s=>s.OwnerId==e.Id)),"Native "+axis+" emission uses each real helper's XML projectile and packet owner");
    Check(native.Shots.Any(s=>children.Any(e=>e.Id==s.OwnerId&&Math.Sqrt(Math.Pow(s.X-e.X,2)+Math.Pow(s.Y-e.Y,2))>3))&&native.Projectiles.Any(p=>p.TextureId==resource!.Projectile.TextureId),"Native "+axis+" launch points retain remote source origins and real projectile art");
}
using(var native=new EncounterSession(adapter,Doc("new State(new OwnedLaneEmitters(\""+helper+"\", LaneAxes.Horizontal))"),owner,7,new(Invulnerable:true)))
{
    for(var tick=0;tick<11;tick++)native.Step(new());var warmedWithoutShots=native.Shots.Count==0;for(var tick=0;tick<10;tick++)native.Step(new());var firstVolleys=native.Shots.GroupBy(s=>s.OwnerId).Select(g=>g.Min(s=>s.Ms)).Order().ToArray();
    Check(warmedWithoutShots&&firstVolleys.Length==2&&firstVolleys[0]>=1200&&firstVolleys[1]-firstVolleys[0] is >=400 and <=500,"Actual default startup and half-interval helper staggering retain native 100 ms tick quantization");
}
using(var native=new EncounterSession(adapter,Doc("new State(new State(\"lanes\", "+Emitter()+", new TimedTransition(800, \"idle\")), new State(\"idle\"))"),owner,7,new(Invulnerable:true)))
{
    native.Step(new());Check(native.Entities.Count==3,"Lane entry installs actual helpers before phase cleanup");for(var tick=0;tick<12;tick++)native.Step(new());
    Check(native.Entities is [{Name:owner,Phase:"idle"}]&&native.Events.Count(e=>e.Kind=="despawn"&&e.ParentId!=null)==2,"Native phase exit removes both helper enemies and records their real parent links");
    var emitted=native.Shots.Count;for(var tick=0;tick<5;tick++)native.Step(new());Check(native.Shots.Count==emitted,"Helpers stop native emission after their owner's lane phase exits");
}
using(var native=new EncounterSession(adapter,Doc("new State("+Emitter()+")"),owner,7,new(Invulnerable:true)))
{
    native.Step(new());var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(native)!;var world=(World)engine.GetType().GetField("World")!.GetValue(engine)!;world.Enemies.Values.Single(e=>e.ObjectDesc.IdName==owner).Expunge();native.Step(new());
    Check(native.Entities.Count==0,"Native owner removal callback clears helpers independently of phase/death callbacks");
}
using(var native=new EncounterSession(adapter,Doc("new State("+Emitter()+")"),owner,7,new(Invulnerable:true)))
{
    native.Step(new());native.SetBossHpFraction(.001);for(var tick=0;tick<20&&native.Entities.Any(e=>e.Name==owner);tick++)native.Step(new(Attack:true,AimX:64.5,AimY:64.5));
    Check(native.Entities.Count==0&&native.Events.Any(e=>e.Kind=="death"&&e.ParentId==null),"Actual weapon-triggered owner death clears both lane helpers through the native death callback");
}
var file=adapter.Catalog().Single(e=>e.Id==owner).SourceFile;var supplied=File.ReadAllText(file);const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    const string StudioEmitter = "Gordo Procession Emitter";
    private _ StudioOwnedFixture = () => Behav().Init("Cave Pirate Brawler", new State(new OwnedLaneEmitters(StudioEmitter, LaneAxes.Cross, intervalMs: 200, startupMs: 200)));
} }
""";
var contextual=new BehaviorDocument(source,adapter,sourceFile:file);var contextualNode=contextual.Entities.Single().Root.Children.Single();Check(adapter.ReadOwnedEmitterResource(contextual,contextualNode)?.EntityId==helper&&adapter.ReadLaneAxis(contextual,contextualNode)=="Cross","Compiler-known native child constants and enum values retain their real emitter resource binding");
var getterSource=source.Replace("const string StudioEmitter = \"Gordo Procession Emitter\";","static string StudioEmitter => throw new System.Exception(\"Authoring must not invoke getters\");");var getterDoc=new BehaviorDocument(getterSource,adapter,sourceFile:file);
Check(adapter.ReadOwnedEmitterResource(getterDoc,getterDoc.Entities.Single().Root.Children.Single())==null&&!adapter.Validate(getterDoc,owner).Any(d=>d.IsError),"Unknown emitter getters retain an explicit resource boundary without executing source or guessing the host owner");
var compiledGuard=false;
try
{
    await using var incompatible=await CompiledRuntime.StartAsync(server,new(){SourceFile=file,Source=source.Replace(helper,"Ghost Ship"),EntityId=owner,Seed=7,Player=new(Invulnerable:true)},artifactRoot:Path.GetFullPath(".artifacts/owned-emitters/guard"));
}
catch(InvalidOperationException error){compiledGuard=error.Message.Contains("flat starting state");}
Check(compiledGuard,"Compiled original-source playback reports the actual nested helper state prerequisite before entry");
await using(var native=await CompiledRuntime.StartAsync(server,new(){SourceFile=file,Source=source,EntityId=owner,Identities=contextual.Identities,Seed=7,Player=new(Invulnerable:true)},artifactRoot:Path.GetFullPath(".artifacts/owned-emitters/compiled")))
{
    await native.AdvanceAsync(new(),1000);await native.AdvanceAsync(new(),200);Check(native.Entities.Count==3&&native.Shots.Count>0&&native.Shots.All(s=>native.Entities.Any(e=>e.Id==s.OwnerId&&e.Name==helper&&e.ParentId!=null)),"Compiled native source preserves actual owned helpers, projectile packets and parent links");
}
Check(File.ReadAllText(file)==supplied,"Owned emitter authoring and compiled playback preserve supplied game source");
Console.WriteLine($"{checks} owned emitter checks passed");
