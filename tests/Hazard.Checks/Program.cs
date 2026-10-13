using System.Reflection;
using Studio.Core;
using Studio.Raple;
using TKR.Shared.resources;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
const string entity="Gordo, the Unwound";
BehaviorDocument Doc(string body)=>new("class Fixture { void Build() { db.Init(\""+entity+"\",new State(\"hazards\","+body+")); } }",adapter);
const string lattice="new DelayedOriginHazards(40,HazardLayout.Lattice,250,startupMs:150,volleyLimit:2)";
const string trail="new DelayedOriginHazards(40,HazardLayout.Trail,100,volleyLimit:2)";
World World(EncounterSession session){var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session)!;return (World)engine.GetType().GetField("World")!.GetValue(engine)!;}
Enemy Boss(EncounterSession session)=>World(session).Enemies.Values.First(e=>e.ObjectDesc.IdName==entity);
object State(Enemy boss)=>boss.StateStorage.Single(p=>p.Key.GetType().Name=="DelayedOriginHazards").Value;
Check(adapter.Primitives.Single(p=>p.Name=="DelayedOriginHazards") is{Category:"Attacks",Simulatable:true},"Hazards expose the original native attack constructor");
var doc=Doc(lattice);var node=doc.Entities.Single().Root.Children.Single();
Check(adapter.ReadHazardPattern(doc,node) is{Layout:"Lattice",Projectile:40,NativeIntervalMs:300,NativeStartupMs:200,VolleyLimit:2,OutwardFirst:false},"Hazard metadata retains native layout, defaults and 100 ms attempt quantization");
Check(new HazardPattern("Trail",40,int.MaxValue,int.MaxValue,1,false) is{NativeIntervalMs:2147483700L,NativeStartupMs:2147483700L},"Native tick quantization retains long timing beyond the largest integer input without saturation");
Check(adapter.ProjectileParameter(doc,node)=="projectileIndex"&&adapter.HasKnownProjectileOwner(doc,entity,node)&&adapter.HazardProjectiles(entity).Any(p=>p.Index==40),"Compatible dormant projectile choices come from the actual owner XML");
Check(adapter.Callbacks("DelayedOriginHazards").Select(c=>c.Stage).SequenceEqual(new[]{"Entry","Ongoing","Exit","Death"})&&adapter.Callbacks("DelayedOriginHazards").All(c=>c.DeclaringType=="BossPatternAttack"),"Inherited hazard lifecycle points to actual base entry, tick, exit and death callbacks");
using(var native=new EncounterSession(adapter,doc,entity,7,new(Invulnerable:true)))
{
    native.Advance(new(),800);Check(native.Shots.GroupBy(s=>s.Ms).Select(g=>(g.Key,g.Count())).SequenceEqual(new[]{(200L,4),(500L,4)}),"Native lattice sends four packets per attempt after quantized startup and interval");
    Check(native.Shots.All(s=>s.Count==2&&Math.Abs(s.AngleIncrement-Math.PI/9)<1e-6)&&native.Shots.Take(4).Select(s=>(s.X,s.Y)).ToHashSet().SetEquals(new[]{(59.5f,59.5f),(59.5f,69.5f),(69.5f,59.5f),(69.5f,69.5f)}),"Native lattice origins are original spawn ±5 tiles with centered two-ray fans");
    Check(Math.Abs(Math.Cos(native.Shots[0].Angle)-Math.Cos(native.Shots[4].Angle+Math.PI))<1e-5&&Math.Abs(Math.Sin(native.Shots[0].Angle)-Math.Sin(native.Shots[4].Angle+Math.PI))<1e-5,"Native consecutive lattice attempts alternate inward and outward");
    Check(native.Effects.Count==8&&native.Effects.All(e=>e.Kind==19&&Math.Abs(e.X2-.65)<1e-6&&e.DurationMs==adapter.HazardProjectiles(entity).Single(p=>p.Index==40).ArmingDelayMs),"Native sanctuary warnings carry actual per-site radius and XML arming duration");
    Check(TelegraphGeometry.Sample(native.Effects,250).Take(4).All(m=>m.Shape==TelegraphShape.Circle&&Math.Abs(m.Radius-.65)<1e-6),"Warning projection follows actual received circles rather than inferred collision bounds");
    Check(native.Events.Where(e=>e.Kind=="attack").All(e=>e.NodeId==null),"Hazard packets do not invent Shoot callback attribution");
}
using(var native=new EncounterSession(adapter,Doc("new DelayedOriginHazards(40,HazardLayout.Lattice,100,volleyLimit:1,outwardFirst:true)"),entity,7,new(Invulnerable:true)))
{native.Step(new());var shot=native.Shots.First();var angle=Math.Atan2(shot.Y-64.5,shot.X-64.5)-Math.PI/18;Check(Math.Abs(Math.Cos(shot.Angle)-Math.Cos(angle))<1e-5&&Math.Abs(Math.Sin(shot.Angle)-Math.Sin(angle))<1e-5,"Outward-first uses the native first lattice orientation");}
using(var native=new EncounterSession(adapter,Doc(trail),entity,7,new(Invulnerable:true)))
{native.Step(new());Check(native.Shots.Count==0&&(int)State(Boss(native)).GetType().GetField("Volley")!.GetValue(State(Boss(native)))! ==1,"A stationary trail attempt emits no packets but consumes one unstunned volley");Boss(native).Move(65,64.5f);native.Step(new());Check(native.Shots is[{Count:3,X:65,Y:64.5f}]&&Math.Abs(Math.Cos(native.Shots[0].Angle+Math.PI/9)+1)<1e-5,"Native trail emits at the moved host with a backward centered three-ray fan");Boss(native).Move(66,64.5f);native.Step(new());Check(native.Shots.Count==1,"The trail attempt limit includes its earlier suppressed stationary attempt");}
using(var native=new EncounterSession(adapter,Doc("new DelayedOriginHazards(40,HazardLayout.Trail,100,volleyLimit:1),new FollowDistance(1,10,2,stopRange:0)"),entity,7,new(Invulnerable:true)))
{native.Step(new());Check(native.Shots.Count==0,"Original source order runs the hazard before following movement when written first");}
using(var native=new EncounterSession(adapter,Doc("new FollowDistance(1,10,2,stopRange:0),new DelayedOriginHazards(40,HazardLayout.Trail,100,volleyLimit:1)"),entity,7,new(Invulnerable:true)))
{native.Step(new());Check(native.Shots.Single() is{X:65,Count:3},"Original source order exposes same-tick movement to a later trail hazard");}
using(var native=new EncounterSession(adapter,Doc("new DelayedOriginHazards(40,HazardLayout.Lattice,100,volleyLimit:1)"),entity,7,new(Invulnerable:true)))
{Boss(native).RemoveCondition(ConditionEffectIndex.StunImmune);Boss(native).ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Stunned,300));native.Advance(new(),500);Check(native.Shots.Count==4&&native.Shots.All(s=>s.Ms==200),"Stunned attempts consume the interval without consuming the native volley budget or a backlog");}
using(var native=new EncounterSession(adapter,Doc("new DelayedOriginHazards(40,HazardLayout.Lattice,100,volleyLimit:1)"),entity,7,new(Invulnerable:true)))
{native.Step(new());var boss=Boss(native);var action=boss.StateStorage.Single(p=>p.Key.GetType().Name=="DelayedOriginHazards").Key;World(native).LeaveWorld(boss);Check(!boss.StateStorage.ContainsKey(action),"Encounter removal releases native per-actor hazard state");}
foreach(var (parameter,value) in new[]{("projectileIndex","38"),("projectileIndex","999"),("intervalMs","99"),("startupMs","-1"),("volleyLimit","0"),("layout","(HazardLayout)99")})
{var rejectedDoc=Doc(lattice);var rejectedNode=rejectedDoc.Entities.Single().Root.Children.Single();var before=rejectedDoc.Source;Reject(()=>rejectedDoc.Apply(new(rejectedDoc.Revision,"Invalid hazard",[new(rejectedNode.Id,parameter,value)]),d=>adapter.Validate(d,entity)),"Native hazard validation rejects "+parameter+"="+value);Check(rejectedDoc.Source==before,"Rejected "+parameter+" retains accepted source");}
var original=doc.Source;doc.Apply(new(doc.Revision,"Switch hazard",[new(node.Id,"layout","HazardLayout.Trail")]),d=>adapter.Validate(d,entity));Check(adapter.ReadHazardPattern(doc,doc.Find(node.Id))!.Layout=="Trail"&&doc.Undo()&&doc.Source==original&&doc.Redo()&&doc.Find(node.Id).Id==node.Id,"Native hazard layout retains exact source, identity and complete history");doc.LockedNodes.Add(node.Id);Reject(()=>doc.Apply(new(doc.Revision,"Locked hazard",[new(node.Id,"outwardFirst","true")]),d=>adapter.Validate(d,entity)),"Hazard region locks protect all layout inputs");
using(var native=new EncounterSession(adapter,Doc(lattice),entity,7,new(Invulnerable:true)))
{native.Step(new());Boss(native).Move(67.5f,64.5f);native.Advance(new(),300);Check(native.Shots.Take(4).Select(s=>(s.X,s.Y)).ToHashSet().SetEquals(new[]{(59.5f,59.5f),(59.5f,69.5f),(69.5f,59.5f),(69.5f,69.5f)}),"Lattice retains the original spawn anchor after host relocation");}
using(var native=new EncounterSession(adapter,Doc(lattice),entity,7,new(Invulnerable:true)))
{
    native.Step(new());var p=Boss(native).ObjectDesc.Projectiles[40];var path=new Player.ValidatedProjectile(0,false,40,64.5f,64.5f,0,Boss(native).ObjectType,1,true,false);
    foreach(var age in new[]{0,499,500,700,1000,1500,2500})
    {
        // Independent scalar formula from supplied Projectile.as getPositionAt:
        // total elapsed multiplies accelerated speed; it is not a physical integral.
        var distance=age<p.AccelerationDelay?age*p.Speed/10000d:Math.Min(age*(p.Speed+p.Acceleration*(age-p.AccelerationDelay)/1000d)/10000d,age*p.SpeedCap/10000d);
        var sample=ClientTrajectory.Sample(path,age,0,p);Check(Math.Abs(sample.X-(64.5+distance))<1e-4&&sample.Y==64.5,"Dormant hazard trajectory matches supplied client acceleration/cap formula at "+age+" ms");
    }
}
using(var native=new EncounterSession(adapter,Doc("new State(\"armed\",new DelayedOriginHazards(40,HazardLayout.Lattice,100),new TimedTransition(200,\"quiet\")),new State(\"quiet\")"),entity,7,new(Invulnerable:true)))
{native.Advance(new(),600);Check(Boss(native).CurrentState.Name=="quiet"&&native.Shots.All(s=>s.Ms<=200)&&native.Shots.Count==12&&!Boss(native).StateStorage.Keys.Any(k=>k.GetType().Name=="DelayedOriginHazards"),"Native phase exit stops future attempts and releases hazard state without inventing delayed callbacks");}
var sourceFile=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var supplied=File.ReadAllText(sourceFile);var source="using TKR.WorldServer.logic.behaviors; namespace TKR.WorldServer.logic { public partial class BehaviorDb { private _ StudioHazardFixture=()=>Behav().Init(\""+entity+"\",new State(\"hazards\","+lattice+")); } }";var project=new StudioProject{SourceFile=sourceFile,Source=source,EntityId=entity,Seed=7,Player=new(Invulnerable:true)};var scoped=new BehaviorDocument(source,adapter,sourceFile:sourceFile);var scopedNode=scoped.Entities.Single().Root.Children.Single();
Check(adapter.ReadHazardPattern(scoped,scopedNode)?.Layout=="Lattice","Compiler-bound native enum constants resolve without executing source getters");
using(var native=new EncounterSession(adapter,Doc(lattice),entity,7,project.Player)){native.Advance(new(),800);await using var compiled=await CompiledRuntime.StartAsync(adapter.ServerRoot,project);await compiled.AdvanceAsync(new(),800);Check(native.Shots.SequenceEqual(compiled.Shots)&&native.Effects.SequenceEqual(compiled.Effects),"Literal and compiled hazard source emit identical native packets and warning records");}
Check(File.ReadAllText(sourceFile)==supplied,"Hazard edits and compiled playback leave original game source unchanged");Console.WriteLine($"{checks} hazard checks passed.");
