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
BehaviorDocument Doc(string body)=>new("class Fixture{void Build(){db.Init(\""+entity+"\",new State(\"groups\","+body+"));}}",adapter);
World World(EncounterSession session){var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session)!;return (World)engine.GetType().GetField("World")!.GetValue(engine)!;}
Enemy Boss(EncounterSession session)=>World(session).Enemies.Values.First(e=>e.ObjectDesc.IdName==entity);
Enemy[] Children(EncounterSession session)=>World(session).Enemies.Values.Where(e=>e!=Boss(session)).ToArray();
var dwarves=NativeGroups.Catalog().Single(g=>g.Name=="Dwarves");Check(dwarves.Members.Count==3&&dwarves.Members.Select(m=>m.ObjectType).Distinct().Count()==3&&dwarves.Members.All(m=>m.RuntimeKind=="Enemy"),"Native group discovery uses actual descriptor membership without alias duplication");
foreach(var kind in new[]{"SpawnGroup","Reproduce","ReproduceGroup","GroupNotExistTransition"})Check(adapter.Primitives.Single(p=>p.Name==kind).Simulatable,"Original "+kind+" has constructor-backed native authoring");
using(var native=new EncounterSession(adapter,Doc("new SpawnGroup(\"Dwarves\",maxChildren:3,initialSpawn:1,radius:2)"),entity,7,new(Invulnerable:true)))
{
    native.Step(new());var children=Children(native);Check(children.Length==3&&children.All(e=>dwarves.Members.Any(m=>m.ObjectType==e.ObjectType)),"Native entry selects actual group member object types");
    Check(children.All(e=>e.X>=64.5&&e.X<66.5&&e.Y>=64.5&&e.Y<66.5),"Positive spawn radius retains the source's positive-quadrant square offsets");
    Check(children.All(e=>e.ParentEntity==null)&&native.Entities.Where(e=>e.Id!=Boss(native).Id).All(e=>e.ParentId==null),"Group-created children retain native unowned relationships rather than invented parent links");
}
using(var native=new EncounterSession(adapter,Doc("new SpawnGroup(\"Dwarves\",maxChildren:3,initialSpawn:.5,coolDown:new Cooldown(100,0))"),entity,7,new(Invulnerable:true)))
{
    native.Step(new());Check(Children(native).Length==1,"Initial native group fraction truncates three times one half to one attempt");native.Advance(new(),400);Check(Children(native).Length==3,"Native ongoing attempts fill the remaining per-entry budget");native.RemoveMinionsForTest();native.Advance(new(),500);Check(Children(native).Length==0,"Removing group children never replenishes the original attempt budget");
}
using(var native=new EncounterSession(adapter,Doc("new SpawnGroup(\"Dwarves\",maxChildren:2,initialSpawn:1.5)"),entity,7,new(Invulnerable:true)))
{native.Step(new());Check(Children(native).Length==3,"Native initial fractions can exceed the nominal ongoing spawn cap");}
using(var native=new EncounterSession(adapter,Doc("new SpawnGroup(\"Dwarves\",maxChildren:2,initialSpawn:1,radius:-2)"),entity,7,new(Invulnerable:true)))
{native.Step(new());Check(Children(native).Length==2&&Children(native).All(e=>e.X<=64.5&&e.X>62.5&&e.Y<=64.5&&e.Y>62.5),"Negative native spawn radius retains signed square offsets without an invented range restriction");}
var wall=NativeArena.Obstacles().First(o=>o.EnemyOccupySquare&&!o.Connects);
using(var native=new EncounterSession(adapter,Doc("new SpawnGroup(\"Dwarves\",maxChildren:2,initialSpawn:1)"),entity,7,new(Invulnerable:true)))
{var boss=Boss(native);boss.Spawned=true;native.Step(new());Check(Children(native).All(e=>e.Spawned&&e.HasConditionEffect(ConditionEffectIndex.Invisible)),"Native group spawning inherits Spawned and permanent Invisible from a spawned host");}
using(var native=new EncounterSession(adapter,Doc("new SpawnGroup(\"Dwarves\",maxChildren:2,initialSpawn:0,coolDown:new Cooldown(100,0))"),entity,7,new(Invulnerable:true),arena:new ArenaPreset{Tiles=[new(68,64,"Arena Dirt",wall.Id)]}))
{
    native.Step(new());Boss(native).Move(68.5f,64.5f);native.Advance(new(),300);var state=Boss(native).StateStorage.Single(p=>p.Key.GetType().Name=="SpawnGroup").Value;
    Check(Children(native).Length==0&&(int)state.GetType().GetField("CurrentNumber")!.GetValue(state)! ==2,"Native blocked group placements consume both attempts without creating children");Boss(native).Move(64.5f,64.5f);native.Advance(new(),400);Check(Children(native).Length==0,"Returning to passable ground cannot recover consumed native group attempts");
}
using(var native=new EncounterSession(adapter,Doc("new ReproduceGroup(\"Dwarves\",densityMax:2,coolDown:new Cooldown(100,0))"),entity,7,new(Invulnerable:true)))
{
    native.Advance(new(),400);Check(Children(native).Length==2&&Children(native).All(e=>e.GivesNoXp&&e.ParentEntity==null),"Native group reproduction ignores nearby players and creates unowned no-XP children below density");native.Advance(new(),400);Check(Children(native).Length==2,"Native local density stops group reproduction at its configured count");native.RemoveMinionsForTest();native.Advance(new(),300);Check(Children(native).Length>0,"Density-based group reproduction can replenish removed population");
}
using(var native=new EncounterSession(adapter,Doc("new ReproduceGroup(\"Dwarves\")"),entity,7,new(Invulnerable:true)))
{native.Step(new());Check(Children(native).Length==0&&Boss(native).StateStorage.Values.Contains(59900),"Default reproduction cooldown retains native 60000 ms initialization and first-tick decrement");}
foreach(var (effect,expected) in new[]{(ConditionEffectIndex.Invisible,0),(ConditionEffectIndex.Hidden,1)})
using(var native=new EncounterSession(adapter,Doc("new Reproduce("+Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Literal(dwarves.Members.First().Id).Text+",densityMax:1,coolDown:new Cooldown(100,0))"),entity,7,new(Invulnerable:true)))
{World(native).Players.Values.Single().ApplyConditionEffect(effect,1000);native.Advance(new(),200);Check(Children(native).Length==expected,"Native reproduction nearby-player gate preserves "+effect+" semantics");}
using(var native=new EncounterSession(adapter,Doc("new Reproduce("+Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Literal(dwarves.Members.First().Id).Text+",densityMax:1,coolDown:new Cooldown(100,0))"),entity,7,new(Invulnerable:true,StartX:79.5)))
{native.Advance(new(),200);Check(Children(native).Length==1,"The exact 15-tile native nearby-player boundary permits reproduction");}
using(var native=new EncounterSession(adapter,Doc("new Reproduce(densityMax:2,coolDown:new Cooldown(100,0))"),entity,7,new(Invulnerable:true,StartX:82)))
{native.Advance(new(),500);Check(native.Entities.Count==2&&native.Entities.All(e=>e.Name==entity),"Null child reproduction uses the host's XML type and density includes that host");}
using(var native=new EncounterSession(adapter,Doc("new ReproduceGroup(\"Dwarves\",densityMax:1,coolDown:new Cooldown(100,0),region:(TileRegion)1,regionRange:1)"),entity,7,new(Invulnerable:true),arena:new ArenaPreset{Tiles=[new(64,64,"Arena Dirt",Region:0),new(70,70,"Arena Dirt",Region:1)]}))
{native.Advance(new(),200);Check(Children(native) is[{X:70,Y:70}],"Group region selection preserves the native host-fraction filter even for a distant marked tile");}
using(var native=new EncounterSession(adapter,Doc("new State(\"waiting\",new GroupNotExistTransition(10,\"done\",\"dWaRvEs\")),new State(\"done\")"),entity,7,new(Invulnerable:true),arena:new ArenaPreset{Actors=[new(dwarves.Members.First().Id,66.5,64.5)]}))
{native.Step(new());Check(Boss(native).CurrentState.Name=="waiting","Native group absence matches group names without case sensitivity");native.RemoveMinionsForTest();native.Advance(new(),200);Check(Boss(native).CurrentState.Name=="done","Native group absence changes phase after the actual matching population is removed");}
using(var native=new EncounterSession(adapter,Doc("new State(\"waiting\",new GroupNotExistTransition(10,\"done\",\"\")),new State(\"done\")"),entity,7,new(Invulnerable:true)))
{native.Advance(new(),300);Check(Boss(native).CurrentState.Name=="waiting","A blank group never triggers the native absence transition");}
var member=dwarves.Members.First().Id;var memberBody=new BehaviorDocument(Doc("new ReproduceGroup(\"Dwarves\",densityMax:1,coolDown:new Cooldown(100,0))").Source.Replace(entity,member),adapter);
using(var native=new EncounterSession(adapter,memberBody,member,7,new(Invulnerable:true)))
{native.Advance(new(),400);Check(native.Entities.Count==1,"A matching native group host itself fills a density limit of one");}
var selfAbsence=new BehaviorDocument(Doc("new State(\"waiting\",new GroupNotExistTransition(10,\"done\",\"dwarves\")),new State(\"done\")").Source.Replace(entity,member),adapter);
using(var native=new EncounterSession(adapter,selfAbsence,member,7,new(Invulnerable:true)))
{native.Advance(new(),300);Check(native.Entities.Single().Phase=="waiting","A matching host prevents its own native group-absence transition");}
var doc=Doc("new SpawnGroup(\"Dwarves\")");var node=doc.Entities.Single().Root.Children.Single();var before=doc.Source;adapter.ApplyGroupReference(doc,entity,node.Id,doc.Revision,"Wargs");Check(adapter.ReadGroupReference(doc,doc.Find(node.Id)) is{Known:true,Name:"Wargs"}&&doc.Undo()&&doc.Source==before&&doc.Redo(),"Exact group selection retains source identity and complete history");doc.LockedNodes.Add(node.Id);Reject(()=>adapter.ApplyGroupReference(doc,entity,node.Id,doc.Revision,"Dwarves"),"Group region locks protect native resource selection");doc.LockedNodes.Clear();Reject(()=>adapter.ApplyGroupReference(doc,entity,node.Id,doc.Revision-1,"Dwarves"),"Stale group selections reject atomically");Reject(()=>adapter.ApplyGroupReference(doc,entity,node.Id,doc.Revision,"dwarves"),"Spawn group resource selection retains native case-sensitive membership");
Check(RapleAdapter.EntityParameterFor("Reproduce") is{NullTitle:"This host's XML type"}&&adapter.ParametersFor(doc,node).Single(p=>p.Name=="radius").Explanation.Contains("square"),"Resource controls and tooltips expose native self-reproduction and square spawning semantics");
var sourceFile=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var supplied=File.ReadAllText(sourceFile);var source="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioGroupFixture=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"groups\",new SpawnGroup(\"Dwarves\",maxChildren:3,initialSpawn:1,radius:2)));}}";var project=new StudioProject{SourceFile=sourceFile,Source=source,EntityId=entity,Seed=7,Player=new(Invulnerable:true)};
using(var native=new EncounterSession(adapter,Doc("new SpawnGroup(\"Dwarves\",maxChildren:3,initialSpawn:1,radius:2)"),entity,7,project.Player)){native.Step(new());await using var compiled=await CompiledRuntime.StartAsync(adapter.ServerRoot,project);await compiled.AdvanceAsync(new(),100);Check(native.Entities.Select(e=>(e.Id,e.Name,e.X,e.Y,e.ParentId)).SequenceEqual(compiled.Entities.Select(e=>(e.Id,e.Name,e.X,e.Y,e.ParentId))),"Literal and compiled group source select identical native types, offsets and ownership");}
Check(File.ReadAllText(sourceFile)==supplied,"Group authoring and native execution preserve supplied source");Console.WriteLine($"{checks} group checks passed.");
