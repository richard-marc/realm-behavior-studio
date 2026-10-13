using System.Reflection;
using Studio.Core;
using Studio.Raple;
using TKR.Shared.resources;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
const string entity="Cave Pirate Brawler";
BehaviorDocument Doc(string body)=>new("class Fixture { void Build() { db.Init(\""+entity+"\",new State(\"segment\","+body+")); } }",adapter);
World World(EncounterSession session){var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session)!;return (World)engine.GetType().GetField("World")!.GetValue(engine)!;}
Enemy Boss(EncounterSession session)=>World(session).Enemies.Values.First(e=>e.ObjectDesc.IdName==entity);
bool Complete(Enemy boss)=>boss.StateStorage.Values.Any(s=>s?.GetType().GetField("Complete")?.GetValue(s) is true);
foreach(var kind in new[]{"FollowDistance","LockedFollowDistance"})
{
    Check(adapter.Primitives.Single(p=>p.Name==kind) is {Category:"Movement",Simulatable:true},kind+" uses its actual native constructor and movement category");
    var doc=Doc("new "+kind+"(1,10,2)");var node=doc.Entities.Single().Root.Children.Single();
    Check(adapter.ReadMovementPattern(doc,node) is {PrimaryParameter:"stopRange",AcquireRange:10,TravelDistance:2,ResumeRange:null} pattern&&pattern.Range==(kind=="FollowDistance"?.5:4),kind+" exposes separate native stop, acquisition and cumulative travel bounds");
    using(var native=new EncounterSession(adapter,doc,entity,7,new(Invulnerable:true)))
    {native.Advance(new(),1000);var boss=Boss(native);Check(Math.Abs(boss.X-66.5)<1e-5&&Complete(boss),kind+" clamps its native movement to the two-tile travel budget");native.Advance(new(),500);Check(Math.Abs(boss.X-66.5)<1e-5,kind+" stays complete for the remainder of this state entry");}
    using(var native=new EncounterSession(adapter,Doc("new "+kind+"(1,10,20,stopRange:2)"),entity,7,new(Invulnerable:true)))
    {native.Advance(new(),1000);native.Advance(new(),200);Check(Math.Abs(Boss(native).X-68.5)<1e-5&&Complete(Boss(native)),kind+" clamps the final native step to the target stop boundary");}
    using(var native=new EncounterSession(adapter,Doc("new "+kind+"(1,10,.2,stopRange:1)"),entity,7,new(Invulnerable:true)))
    {native.Step(new());Check(Math.Abs(Boss(native).X-64.7)<1e-5&&Complete(Boss(native)),kind+" retains fractional travel limits below one normal movement step");}
}
using(var native=new EncounterSession(adapter,Doc("new FollowDistance(1,2,10)"),entity,7,new(Invulnerable:true)))
{native.Step(new());Check(Boss(native).X==64.5&&!Complete(Boss(native)),"Reacquiring follow waits without completing when no target is available");World(native).Players.Values.Single().Move(66,64.5f);native.Step(new());Check(Boss(native).X==65,"Reacquiring follow finds a player arriving on a later tick");}
using(var native=new EncounterSession(adapter,Doc("new LockedFollowDistance(1,2,10,stopRange:1)"),entity,7,new(Invulnerable:true)))
{native.Step(new());Check(Complete(Boss(native)),"Locked follow completes immediately when entry has no valid target");World(native).Players.Values.Single().Move(66,64.5f);native.Step(new());Check(Boss(native).X==64.5,"Locked follow never acquires a later arriving player after completion");}
foreach(var kind in new[]{"FollowDistance","LockedFollowDistance"})
using(var native=new EncounterSession(adapter,Doc("new "+kind+"(1,10,20,stopRange:1)"),entity,7,new(Invulnerable:true)))
{native.Step(new());World(native).Players.Values.Single().Move(80,64.5f);native.Step(new());Check(Math.Abs(Boss(native).X-(kind=="FollowDistance"?65:65.5))<1e-5,kind+" preserves its own target behavior outside the acquisition radius");}
using(var native=new EncounterSession(adapter,Doc("new LockedFollowDistance(1,10,20,stopRange:1,timeoutMs:200)"),entity,7,new(Invulnerable:true)))
{native.Step(new());Boss(native).ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Paralyzed,1000));native.Step(new());Check(Boss(native).X==65&&Complete(Boss(native)),"Locked target timeout advances during paralysis and completes without movement");}
using(var native=new EncounterSession(adapter,Doc("new FollowDistance(1,10,2)"),entity,7,new(Invulnerable:true)))
{Boss(native).ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Paralyzed,1000));native.Advance(new(),300);Check(Boss(native).X==64.5&&!Complete(Boss(native)),"Reacquiring follow paralysis preserves the unconsumed travel budget");}
using(var native=new EncounterSession(adapter,Doc("new LockedFollowDistance(1,10,20,stopRange:1)"),entity,7,new(Invulnerable:true)))
{native.Step(new());World(native).LeaveWorld(World(native).Players.Values.Single());native.Step(new());Check(Complete(Boss(native)),"Locked follow ends when its retained native player becomes invalid");}
var editedDoc=Doc("new LockedFollowDistance(1,10,2)");var edited=editedDoc.Entities.Single().Root.Children.Single();var original=editedDoc.Source;
foreach(var kind in new[]{"FollowDistance","LockedFollowDistance"})
using(var native=new EncounterSession(adapter,Doc("new "+kind+"(1,10,20,stopRange:1)"),entity,7,new(Invulnerable:true)))
{
    typeof(Entity).GetProperty("DebugMovementSpeedMultiplier")!.SetValue(Boss(native),0f);native.Advance(new(),400);
    Check(!Complete(Boss(native))&&Boss(native).X==64.5,kind+" records blocked time without consuming travel distance");native.Step(new());
    Check(Complete(Boss(native))&&Boss(native).X==64.5,kind+" completes after the native 500 ms blocked-movement threshold");
}
editedDoc.Apply(new(editedDoc.Revision,"Travel budget",[new(edited.Id,"distance","3.25")]),d=>adapter.Validate(d,entity));
Check(adapter.ReadMovementPattern(editedDoc,editedDoc.Find(edited.Id))!.TravelDistance==3.25&&editedDoc.Undo()&&editedDoc.Source==original&&editedDoc.Redo(),"Bounded follow travel edits keep complete source history and native units");
foreach(var body in new[]{"new FollowDistance(1,10,0)","new LockedFollowDistance(1,4,2)","new LockedFollowDistance(1,10,2,timeoutMs:0)"})
{var rejected=false;try{using var session=new EncounterSession(adapter,Doc(body),entity,7,new(Invulnerable:true));}catch(Exception){rejected=true;}Check(rejected,"Actual native constructor rejects invalid segment: "+body);}
var sourceFile=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var supplied=File.ReadAllText(sourceFile);
foreach(var kind in new[]{"FollowDistance","LockedFollowDistance"})
{
    var source="using TKR.WorldServer.logic.behaviors; namespace TKR.WorldServer.logic { public partial class BehaviorDb { private _ StudioBoundedFollow=()=>Behav().Init(\""+entity+"\",new State(\"segment\",new "+kind+"(1,10,2,stopRange:1))); } }";
    var project=new StudioProject{SourceFile=sourceFile,Source=source,EntityId=entity,Seed=7,Player=new(Invulnerable:true)};
    using var native=new EncounterSession(adapter,Doc("new "+kind+"(1,10,2,stopRange:1)"),entity,7,project.Player);native.Advance(new(),700);
    await using var compiled=await CompiledRuntime.StartAsync(adapter.ServerRoot,project);await compiled.AdvanceAsync(new(),700);
    Check(native.Movements.Select(m=>(m.Ms,m.ActorId,m.FromX,m.FromY,m.ToX,m.ToY)).SequenceEqual(compiled.Movements.Select(m=>(m.Ms,m.ActorId,m.FromX,m.FromY,m.ToX,m.ToY)))&&native.Entities.Select(e=>(e.X,e.Y)).SequenceEqual(compiled.Entities.Select(e=>(e.X,e.Y))),kind+" literal and compiled source execute identical native movement");
}
Check(File.ReadAllText(sourceFile)==supplied,"Bounded follow authoring and execution leave supplied source unchanged");
Console.WriteLine($"{checks} bounded follow checks passed.");
