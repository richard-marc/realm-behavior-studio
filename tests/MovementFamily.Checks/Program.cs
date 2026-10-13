using System.Reflection;
using Studio.Core;
using Studio.Raple;
using TKR.Shared.resources;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
const string entity="Cave Pirate Brawler";
BehaviorDocument Doc(string body)=>new("class Fixture { void Build() { db.Init(\""+entity+"\",new State(\"movement\","+body+")); } }",adapter);
World World(EncounterSession session){var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session)!;return (World)engine.GetType().GetField("World")!.GetValue(engine)!;}
Enemy Boss(EncounterSession session)=>World(session).Enemies.Values.First(e=>e.ObjectDesc.IdName==entity);
foreach(var kind in new[]{"ReturnToSpawn","StayBack","Charge"})Check(adapter.Primitives.Single(p=>p.Name==kind) is {Category:"Movement",Simulatable:true},"Original "+kind+" has native constructor-backed movement authoring");
foreach(var (body,parameter,value) in new[]{("new ReturnToSpawn(1)","returnWithinRadius",1d),("new StayBack(1)","distance",8d),("new Charge()","range",10d)})
{
    var doc=Doc(body);var node=doc.Entities.Single().Root.Children.Single();var model=adapter.ReadMovementPattern(doc,node)!;Check(model.PrimaryParameter==parameter&&model.Range==value&&!model.IntegerRange,"Native "+node.Kind+" exposes its actual default distance input and type");
}
var returnDoc=Doc("new ReturnToSpawn(1,returnWithinRadius:1)");
using(var native=new EncounterSession(adapter,returnDoc,entity,7,new(Invulnerable:true)))
{
    native.Step(new());var boss=Boss(native);var spawn=boss.SpawnPoint;boss.Move(spawn.X+4,spawn.Y);native.Step(new());Check(Math.Abs(boss.X-(spawn.X+3.5))<1e-5&&boss.SpawnPoint.Equals(spawn),"Native return uses the original SpawnPoint and BehaviourTickTime step");
    boss.Move(spawn.X+1,spawn.Y);native.Step(new());Check(Math.Abs(boss.X-(spawn.X+1))<1e-5,"The exact native return-radius boundary completes without another move");
    boss.Move(spawn.X+4,spawn.Y);boss.ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Paralyzed,1000));native.Step(new());Check(Math.Abs(boss.X-(spawn.X+4))<1e-5,"Native paralysis suspends return movement");
}
using(var native=new EncounterSession(adapter,Doc("new ReturnToSpawn(1,returnWithinRadius:.1)"),entity,7,new(Invulnerable:true)))
{native.Step(new());var boss=Boss(native);var spawn=boss.SpawnPoint;boss.Move(spawn.X+.2f,spawn.Y);native.Step(new());Check(boss.X<spawn.X,"Native return retains overshoot rather than an invented destination clamp");}
using(var native=new EncounterSession(adapter,Doc("new StayBack(1,distance:8)"),entity,7,new(Invulnerable:true)))
{native.Step(new());Check(Math.Abs(Boss(native).X-64)<1e-5&&Math.Abs(Boss(native).Y-64.5)<1e-5,"Native retreat moves away from the nearby player through its own movement path");}
using(var native=new EncounterSession(adapter,Doc("new StayBack(1,distance:6)"),entity,7,new(Invulnerable:true)))
{native.Step(new());Check(Boss(native).X==64.5&&Boss(native).StateStorage.Values.Contains(1000),"The exact retreat distance boundary does not acquire or advance the active cycle timer");}
using(var native=new EncounterSession(adapter,Doc("new StayBack(1,distance:20)"),entity,7,new(Invulnerable:true)))
{native.Advance(new(),1000);Check(Boss(native).StateStorage.Values.Contains(0),"The native one-second retreat cycle countdown advances only while moving away");native.Step(new());Check(Boss(native).StateStorage.Values.Contains(1000),"The original retreat cycle resets on its later completion tick");}
var targetDoc=Doc("new StayBack(1,distance:8,entity:\"Ghost Ship\")");var targetNode=targetDoc.Entities.Single().Root.Children.Single();
Check(adapter.EntityParameterFor(targetDoc,targetNode) is {Name:"entity",Scope:EntityResourceScope.Enemy,AllowsPlayer:true,NameFragment:true},"Named retreat targeting exposes the actual enemy-name query and native null-player mode");
using(var native=new EncounterSession(adapter,targetDoc,entity,7,new(Invulnerable:true),arena:new ArenaPreset{Actors=[new("Ghost Ship",66.5,64.5)]}))
{native.Step(new());Check(Boss(native).X<64.5,"The native named retreat resolves the actual nearby enemy instead of the player prototype");}
var chargeDoc=Doc("new Charge(speed:4,range:10f,coolDown:new Cooldown(100,0))");
using(var native=new EncounterSession(adapter,chargeDoc,entity,7,new(Invulnerable:true)))
{native.Advance(new(),500);Check(Boss(native).X==64.5&&Boss(native).Y==64.5,"The actual charge rejects an axis-aligned player despite being within acquisition range");}
using(var native=new EncounterSession(adapter,chargeDoc,entity,7,new(Invulnerable:true,StartY:66.5)))
{
    native.Step(new());var boss=Boss(native);var dx=boss.X-64.5;var dy=boss.Y-64.5;Check(Math.Abs(double.Hypot(dx,dy)-.4)<1e-5&&Math.Abs(dx/dy-3)<1e-4,"Native charge acquires a diagonal player and uses unscaled DeltaTime movement");
    native.Advance(new(0,1),300);Check(Math.Abs((boss.X-64.5)/(boss.Y-64.5)-3)<1e-4,"Native charge retains its initial direction when the player changes position");
}
using(var native=new EncounterSession(adapter,chargeDoc,entity,7,new(Invulnerable:true,StartY:66.5)))
{typeof(Entity).GetProperty("DebugMovementSpeedMultiplier")!.SetValue(Boss(native),3f);native.Step(new());Check(Math.Abs(double.Hypot(Boss(native).X-64.5,Boss(native).Y-64.5)-.4)<1e-5,"Charge preserves the source's unscaled speed despite an ordinary movement multiplier");}
using(var native=new EncounterSession(adapter,chargeDoc,entity,7,new(Invulnerable:true,StartY:66.5)))
{Boss(native).ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Slowed,1000));native.Step(new());Check(Math.Abs(double.Hypot(Boss(native).X-64.5,Boss(native).Y-64.5)-.2)<1e-5,"Native Slowed halves charge movement and participates in travel-time initialization");}
var editDoc=Doc("new ReturnToSpawn(1)");var edited=editDoc.Entities.Single().Root.Children.Single();var before=editDoc.Source;editDoc.Apply(new(editDoc.Revision,"Edit native return radius",[new(edited.Id,"returnWithinRadius","2.5")]),d=>adapter.Validate(d,entity));
Check(adapter.ReadMovementPattern(editDoc,editDoc.Find(edited.Id))!.Range==2.5&&editDoc.Undo()&&editDoc.Source==before&&editDoc.Redo()&&editDoc.Find(edited.Id).Id==edited.Id,"Native movement inputs retain source identity and complete parameter history");editDoc.LockedNodes.Add(edited.Id);
Reject(()=>editDoc.Apply(new(editDoc.Revision,"Locked movement",[new(edited.Id,"returnWithinRadius","3")]),d=>adapter.Validate(d,entity)),"A movement region lock protects the new source inputs");
var sourceFile=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var supplied=File.ReadAllText(sourceFile);var source="using TKR.WorldServer.logic.behaviors; namespace TKR.WorldServer.logic { public partial class BehaviorDb { private _ StudioMovementFixture=()=>Behav().Init(\""+entity+"\",new State(\"movement\",new Charge(speed:4,range:10f,coolDown:new Cooldown(100,0)))); } }";var project=new StudioProject{SourceFile=sourceFile,Source=source,EntityId=entity,Seed=7,Player=new(Invulnerable:true,StartY:66.5)};
using(var native=new EncounterSession(adapter,chargeDoc,entity,7,project.Player)){native.Advance(new(),500);await using var compiled=await CompiledRuntime.StartAsync(adapter.ServerRoot,project);await compiled.AdvanceAsync(new(),500);Check(native.Movements.Select(m=>(m.Ms,m.DurationMs,m.ActorId,m.Entity,m.Player,m.FromX,m.FromY,m.ToX,m.ToY,m.Phase)).SequenceEqual(compiled.Movements.Select(m=>(m.Ms,m.DurationMs,m.ActorId,m.Entity,m.Player,m.FromX,m.FromY,m.ToX,m.ToY,m.Phase)))&&native.Entities.Select(e=>(e.X,e.Y)).SequenceEqual(compiled.Entities.Select(e=>(e.X,e.Y))),"Literal and compiled accepted source retain identical native charge samples and positions");}
var output=Path.GetFullPath(".artifacts/movement-family");Directory.CreateDirectory(output);var saved=Path.Combine(output,"movement.rbs");BehaviorDocument.Save(saved,project);Check(BehaviorDocument.Load(saved).Source==source&&File.ReadAllText(sourceFile)==supplied,"Movement family persistence and execution leave supplied game source unchanged");Console.WriteLine($"{checks} movement family checks passed.");
