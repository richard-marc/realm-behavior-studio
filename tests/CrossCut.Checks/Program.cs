using System.Reflection;
using Studio.Core;
using Studio.Raple;
using TKR.Shared.resources;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;
var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
const string entity="Cave Pirate Brawler";const string expression="new EdgeCrossCut(0,radius:4,count:3,spacing:2,intervalMs:100,startupMs:0)";
BehaviorDocument Doc(string body)=>new("class Fixture{void Build(){db.Init(\""+entity+"\",new State(\"crosscut\","+body+"));}}",adapter);
World World(EncounterSession session){var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session)!;return (World)engine.GetType().GetField("World")!.GetValue(engine)!;}
Enemy Boss(EncounterSession session)=>World(session).Enemies.Values.Single();
var doc=Doc(expression);var node=doc.Entities.Single().Root.Children.Single();var pattern=adapter.ReadCrossCut(doc,node)!;
Check(adapter.Primitives.Single(p=>p.Name=="EdgeCrossCut") is{Category:"Attacks",Simulatable:true}&&adapter.ProjectileParameter(doc,node)=="projectileIndex","Crosscut exposes its original attack constructor and owner XML projectile parameter");
Check(pattern is{Radius:4,Count:3,Spacing:2,IntervalMs:100,StartupMs:0}&&adapter.ParametersFor(doc,node).Single(p=>p.Name=="spacing").Unit=="tiles","Crosscut metadata retains exact origin geometry and source units");
Check(adapter.Callbacks("EdgeCrossCut").Select(c=>c.Stage).SequenceEqual(new[]{"Entry","Ongoing","Exit","Death"}),"Crosscut lifecycle follows native BossPatternAttack callbacks");
using(var native=new EncounterSession(adapter,doc,entity,7,new(Invulnerable:true)))
{
    native.Advance(new(),800);Check(native.Shots.Count==24&&native.Shots.GroupBy(s=>s.Ms).All(g=>g.Count()==3)&&native.Shots.All(s=>s.Count==1&&s.AngleIncrement==0),"Native crosscut emits three independent single-shot packets on each edge attempt");
    for(var volley=0;volley<8;volley++){var actual=native.Shots.Where(s=>s.Ms==volley*100).ToArray();var expected=pattern.Origins(64.5,64.5,volley);Check(actual.Zip(expected).All(p=>Math.Abs(p.First.X-p.Second.X)<1e-5&&Math.Abs(p.First.Y-p.Second.Y)<1e-5&&Math.Abs(Math.Cos(p.First.Angle)-Math.Cos(p.Second.Heading))<1e-5),"Native edge "+volley+" retains captured-anchor origin and inward heading geometry");}
    Check(native.Events.Where(e=>e.Kind=="attack").All(e=>e.NodeId==null),"Independent crosscut packets never invent Shoot callback ownership");
}
using(var native=new EncounterSession(adapter,doc,entity,7,new(Invulnerable:true)))
{native.Step(new());Boss(native).Move(66.5f,64.5f);native.Step(new());Check(native.Shots.Where(s=>s.Ms==100).All(s=>Math.Abs(s.X-68.5)<1e-5),"Crosscut origins retain the original state-entry anchor after host movement");}
using(var native=new EncounterSession(adapter,Doc(expression.Replace("intervalMs:100,startupMs:0","intervalMs:250,startupMs:150")),entity,7,new(Invulnerable:true)))
{native.Advance(new(),800);Check(native.Shots.GroupBy(s=>s.Ms).Select(g=>g.Key).SequenceEqual(new long[]{200,500}),"Native startup and attempts retain 100 ms quantization without a backlog");}
using(var native=new EncounterSession(adapter,doc,entity,7,new(Invulnerable:true)))
{native.Step(new());Boss(native).ApplyConditionEffect(ConditionEffectIndex.Stunned,300);native.Advance(new(),400);Check(native.Shots.GroupBy(s=>s.Ms).Select(g=>g.Key).SequenceEqual(new long[]{0,300,400})&&native.Shots.Where(s=>s.Ms==300).All(s=>Math.Abs(s.X-68.5)<1e-5),"Stunned attempts retain interval consumption without advancing the edge sequence");}
var wall=NativeArena.Obstacles().First(o=>o.EnemyOccupySquare&&!o.Connects);
using(var native=new EncounterSession(adapter,Doc(expression.Replace("radius:4","radius:4.5")),entity,7,new(Invulnerable:true),arena:new ArenaPreset{Tiles=[new(60,64,"Arena Dirt",wall.Id)]}))
{native.Step(new());Check(native.Shots.Count==2&&native.Shots.All(s=>Math.Abs(s.Y-64.5)>1),"Native map passability suppresses only the blocked origin lane");}
using(var native=new EncounterSession(adapter,Doc(expression),entity,7,new(Invulnerable:true)))
{native.Step(new());Boss(native).Move(45.5f,64.5f);native.Step(new());Check(native.Shots.All(s=>s.Ms==0),"Live host distance suppresses captured origins beyond the native 14-tile envelope");}
foreach(var (name,value) in new[]{("count","0"),("count","9"),("radius","0"),("radius","15"),("radius","double.NaN"),("spacing","0"),("spacing","30"),("spacing","double.PositiveInfinity"),("intervalMs","99"),("startupMs","-1"),("projectileIndex","999")})
{var before=doc.Source;Reject(()=>doc.Apply(new(doc.Revision,"Invalid crosscut",[new(node.Id,name,value)]),d=>adapter.Validate(d,entity)),"Native crosscut bounds reject "+name+"="+value);Check(doc.Source==before,"Rejected "+name+" preserves accepted source");}
var original=doc.Source;doc.Apply(new(doc.Revision,"Change origin radius",[new(node.Id,"radius","5.25")]),d=>adapter.Validate(d,entity));Check(adapter.ReadCrossCut(doc,doc.Find(node.Id))!.Radius==5.25&&doc.Undo()&&doc.Source==original&&doc.Redo()&&doc.Find(node.Id).Id==node.Id,"Native origin edits preserve action identity and complete source history");doc.LockedNodes.Add(node.Id);Reject(()=>doc.Apply(new(doc.Revision,"Locked origin",[new(node.Id,"spacing","1")]),d=>adapter.Validate(d,entity)),"Crosscut region locks protect its source geometry");doc.LockedNodes.Clear();
var sourceFile=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var supplied=File.ReadAllText(sourceFile);var source="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioCrossCutFixture=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"crosscut\","+expression+"));}}";var project=new StudioProject{SourceFile=sourceFile,Source=source,EntityId=entity,Seed=7,Player=new(Invulnerable:true)};
using(var native=new EncounterSession(adapter,Doc(expression),entity,7,project.Player)){native.Advance(new(),800);await using var compiled=await CompiledRuntime.StartAsync(adapter.ServerRoot,project);await compiled.AdvanceAsync(new(),800);Check(native.Shots.SequenceEqual(compiled.Shots),"Literal and compiled crosscut source retain identical full native packet records");}
Check(File.ReadAllText(sourceFile)==supplied,"Crosscut authoring and native execution preserve supplied source");Console.WriteLine($"{checks} crosscut checks passed.");
