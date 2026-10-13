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
BehaviorDocument Doc(string expression)=>new("class Fixture { void Build() { db.Init(\""+entity+"\", new State(\"loop\", "+expression+")); } }",adapter);
const string burst="new SequentialAimedSweep(0,triggerRange:12,count:7,intervalMs:150,sweepDegrees:60,recoveryMs:250,aimRange:16,shotsPerVolley:3)";
var doc=Doc(burst);var attack=doc.Entities.Single().Root.Children.Single();var pattern=adapter.ReadSequentialSweep(doc,attack)!;
Check(adapter.Primitives.Single(p=>p.Name=="SequentialAimedSweep") is {Category:"Attacks",Simulatable:true}&&attack.Kind=="SequentialAimedSweep","The actual sweep is an editable native attack primitive");
Check(adapter.Overloads("SequentialAimedSweep").Count==2&&adapter.ParametersFor(doc,attack).Single(p=>p.Name=="intervalMs").Unit=="ms","Both original constructors and native timing units remain available");
Check(pattern.VolleyCounts.SequenceEqual(new[]{3,3,1})&&pattern.NativeIntervalMs==200&&pattern.RecoveryMs==250,"Known burst metadata retains a partial final volley and native tick quantization");
Check(adapter.ProjectileParameter(doc,attack)=="projectile"&&adapter.ReadProjectileIndex(doc,attack)==0&&adapter.HasKnownProjectileOwner(doc,entity,attack),"Sweep XML selection binds the actual projectile parameter and native host");
Check(adapter.Callbacks("SequentialAimedSweep").Select(c=>c.Stage).SequenceEqual(new[]{"Entry","Ongoing"}),"Source lifecycle retains activation initialization and native ongoing dispatch");
var source=doc.Source;var originalIds=doc.Identities;doc.Apply(new(doc.Revision,"Edit burst",[new(attack.Id,"count","8"),new(attack.Id,"shotsPerVolley","2")]),d=>adapter.Validate(d,entity));
Check(adapter.ReadSequentialSweep(doc,doc.Find(attack.Id))!.VolleyCounts.SequenceEqual(new[]{2,2,2,2})&&originalIds.All(i=>doc.Identities.Any(n=>n.Id==i.Id)),"Atomic burst edits preserve every source identity");
Check(doc.Undo()&&doc.Source==source&&doc.Redo()&&adapter.ReadSequentialSweep(doc,doc.Find(attack.Id))!.Count==8,"One source Undo/Redo restores the complete burst edit");doc.Undo();
doc.LockedNodes.Add(attack.Id);Reject(()=>doc.Apply(new(doc.Revision,"Locked burst",[new(attack.Id,"count","9")]),d=>adapter.Validate(d,entity)),"A source-region lock protects burst timing and repetition");doc.LockedNodes.Clear();
foreach(var bad in new[]{"count:1","shotsPerVolley:13","intervalMs:99","recoveryMs:-1","triggerRange:0","aimRange:1","sweepDegrees:361","sweepDegrees:double.NaN","projectile:999","shotsPerVolley:8"})
{
    var name=bad.Split(':')[0];var value=bad[(name.Length+1)..];Reject(()=>doc.Apply(new(doc.Revision,"Reject native constraint",[new(attack.Id,name,value)]),d=>adapter.Validate(d,entity)),"Native sweep constraint rejects "+bad);
}
Check(doc.Source==source&&doc.Identities.SequenceEqual(originalIds),"Rejected burst inputs leave accepted source and identities untouched");
World NativeWorld(EncounterSession native){var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(native)!;return (World)engine.GetType().GetField("World")!.GetValue(engine)!;}
Enemy Boss(EncounterSession native)=>NativeWorld(native).Enemies.Values.Single(e=>e.ObjectDesc.IdName==entity);
using(var native=new EncounterSession(adapter,doc,entity,7,new(Invulnerable:true)))
{
    native.Advance(new(),1000);Check(native.Shots.Select(s=>s.Count).SequenceEqual(new[]{3,3,1,3})&&native.Shots.Select(s=>s.Ms).SequenceEqual(new long[]{0,200,400,800}),"Native bursts emit one packet per tick, finish recovery and reacquire with no backlog");
    var shots=native.Shots;Check(Math.Abs(shots[0].AngleIncrement-Math.PI/6)<1e-6&&Math.Abs(shots[1].AngleIncrement-Math.PI/9)<1e-6&&shots[2].AngleIncrement==0&&shots[3].AngleIncrement<0,"Native fan packets alternate full/inset widths, keep a partial last volley and reverse the next sweep");
}
using(var native=new EncounterSession(adapter,Doc(burst.Replace("triggerRange:12","triggerRange:1")),entity,7,new(Invulnerable:true)))
{native.Advance(new(),500);Check(native.Shots.Count==0,"Native activation waits for a player inside trigger range");}
using(var native=new EncounterSession(adapter,doc,entity,7,new(Invulnerable:true)))
{
    native.Step(new());native.Advance(new(0,1),200);Check(native.Shots.Count==2&&Math.Abs(native.Shots[1].Angle-native.Shots[0].Angle)>0.1,"The active native sweep tracks the player's changed direction between volleys");
}
using(var native=new EncounterSession(adapter,doc,entity,7,new(Invulnerable:true)))
{
    native.Step(new());Boss(native).ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Stunned,300));native.Advance(new(),400);
    Check(native.Shots.Select(s=>s.Ms).SequenceEqual(new long[]{0,300}),"Native stun expires on the original effect countdown and resumes one volley without backlog");
}
using(var native=new EncounterSession(adapter,Doc("new SequentialAimedSweep(0,triggerRange:12,count:4,intervalMs:100,sweepDegrees:60,recoveryMs:0)"),entity,7,new(Invulnerable:true)))
{
    native.Advance(new(),500);Check(native.Shots.Count==5&&native.Shots.All(s=>s.Count==1&&s.AngleIncrement==0),"The original single-shot constructor retains one projectile per sweep tick");
    Check(native.Shots.Select(s=>s.Angle*180/Math.PI).Zip(new[]{-30d,-10,10,30,30}).All(p=>Math.Abs(p.First-p.Second)<1e-4),"Single-shot source semantics trace the cone and reverse the next activation");
}
foreach(var locked in new[]{false,true})using(var native=new EncounterSession(adapter,Doc(burst.Replace("new SequentialAimedSweep(0","new SequentialAimedSweep(lockTarget:"+locked.ToString().ToLowerInvariant()+",projectile:0")),entity,7,new(Invulnerable:true)))
{
    native.Step(new());NativeWorld(native).Players.Values.Single().ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Invisible,5000));native.Advance(new(),400);
    Check(native.Shots.Count==(locked?1:3),"Native activation visibility follows the actual lockTarget overload: "+locked);
}
using(var native=new EncounterSession(adapter,Doc(burst+",new Shoot(20,projectileIndex:0,fixedAngle:0,coolDown:100)"),entity,7,new(Invulnerable:true)))
{
    native.Step(new());Check(native.Shots.Count==2&&native.Events.Where(e=>e.Kind=="attack").All(e=>e.NodeId==null),"A sweep sharing a packet index with Shoot never receives invented Shoot attribution");
}
var path=Path.GetFullPath(adapter.Catalog().Single(e=>e.Id==entity).SourceFile);var supplied=File.ReadAllText(path);
var compiledSource="using TKR.WorldServer.logic.behaviors; namespace TKR.WorldServer.logic { public partial class BehaviorDb { private _ StudioSweepFixture=()=>Behav().Init(\""+entity+"\",new State(\"loop\","+burst+")); } }";
var project=new StudioProject{SourceFile=path,Source=compiledSource,EntityId=entity,Seed=7,Player=new(Invulnerable:true)};
using(var native=new EncounterSession(adapter,doc,entity,7,project.Player))
{
    native.Advance(new(),1000);await using var compiled=await CompiledRuntime.StartAsync(adapter.ServerRoot,project);await compiled.AdvanceAsync(new(),1000);
    Check(compiled.Shots.SequenceEqual(native.Shots),"Compiled accepted source and literal native construction emit identical complete burst packets");
}
const string declarations=" const int StudioSweepCount=7; static int StudioSweepGetter { get { throw new System.Exception(\"authoring getter executed\"); } } ";
var knownSource=compiledSource.Replace("private _",declarations+"private _").Replace("count:7","count:StudioSweepCount");var knownDoc=new BehaviorDocument(knownSource,adapter,sourceFile:path);var knownAttack=knownDoc.Entities.Single().Root.Children.Single();
var shadowSource=compiledSource.Replace(burst,"new SequentialAimedSweep(0)").Replace("private _","sealed class SequentialAimedSweep:global::TKR.WorldServer.logic.Behavior { public SequentialAimedSweep(int projectile){} } private _");var shadowDoc=new BehaviorDocument(shadowSource,adapter,sourceFile:path);var shadowAttack=shadowDoc.Entities.Single().Root.Children.Single();
Check(adapter.ProjectileParameter(shadowDoc,shadowAttack)==null&&adapter.ReadSequentialSweep(shadowDoc,shadowAttack)==null,"A same-named source controller never borrows the native sweep's XML controls or burst semantics");
Check(adapter.ReadSequentialSweep(knownDoc,knownAttack)!.Count==7&&!adapter.Validate(knownDoc,entity).Any(d=>d.IsError),"Compiler-known constants retain burst controls without evaluating game/user code");
var unknownDoc=new BehaviorDocument(knownSource.Replace("count:StudioSweepCount","count:StudioSweepGetter"),adapter,sourceFile:path);
Check(adapter.ReadSequentialSweep(unknownDoc,unknownDoc.Entities.Single().Root.Children.Single())==null&&!adapter.Validate(unknownDoc,entity).Any(d=>d.IsError),"Unknown getters remain source inputs without execution or invented burst metadata");
var output=Path.GetFullPath(".artifacts/sequential-sweep");Directory.CreateDirectory(output);var saved=Path.Combine(output,"sweep.rbs");BehaviorDocument.Save(saved,project with{Identities=new BehaviorDocument(compiledSource,adapter,sourceFile:path).Identities});var reopened=BehaviorDocument.Load(saved);
Check(reopened.Source==compiledSource&&File.ReadAllText(path)==supplied,"Sweep persistence and native execution preserve supplied game source");Console.WriteLine($"{checks} sequential sweep checks passed.");
