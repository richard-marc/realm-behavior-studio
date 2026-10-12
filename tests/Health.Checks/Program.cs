using System.Reflection;
using System.Text.Json;
using Studio.Core;
using Studio.Raple;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;
using TKR.WorldServer.logic;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception error)when(error is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
const string id="Cave Pirate Brawler";const string helper="Cave Pirate Cabin Boy";
BehaviorDocument Doc(string tree)=>new("class Fixture { void Build() { db.Init(\""+id+"\", "+tree+"); } }",adapter);
(World World,Player Player,Enemy Boss) Engine(EncounterSession native)
{
    var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(native)!;
    var world=(World)engine.GetType().GetField("World")!.GetValue(engine)!;return(world,world.Players.Values.Single(),world.Enemies.Values.Single(e=>e.ObjectDesc.IdName==id));
}
void Damage(EncounterSession native,int amount){var parts=Engine(native);var time=new TickTime{TotalElapsedMs=native.Ms,ElapsedMsDelta=100};parts.Boss.Damage(parts.Player,ref time,amount,true);}
// Controlled mechanism fixture: change the real actor's live HP fields, never XML.
EncounterSession Session(BehaviorDocument doc,PlayerPreset player,ArenaPreset? arena=null){var native=new EncounterSession(adapter,doc,id,7,player,arena:arena);var boss=Engine(native).Boss;boss.MaximumHP=300;boss.HP=300;return native;}
foreach(var kind in new[]{"DamageTakenTransition","EntityHpLessTransition","HealSelf","GiveTemporaryDefense"})Check(adapter.Primitives.Single(p=>p.Name==kind).Simulatable,"Actual "+kind+" has native constructor-backed playback");
var damageDoc=Doc("new State(new State(\"first\",new DamageTakenTransition(100,\"second\",wipeProgress:true)),new State(\"second\",new DamageTakenTransition(100,\"third\",wipeProgress:true)),new State(\"third\"))");
using(var native=Session(damageDoc,new(Invulnerable:true)))
{
    native.Step(new());Damage(native,99);native.Step(new());Check(native.Entities.Single().Phase=="first"&&native.Entities.Single().Damage is {RecordedDamage:99,PresentPlayerDamage:99,Players.Length:1},"Native credited damage remains below an inclusive threshold while snapshots expose its real contributor");
    Damage(native,1);native.Step(new());Check(native.Entities.Single().Phase=="second"&&native.Entities.Single().Damage!.Players.Single().PlayerId==Engine(native).Player.Id,"Exact native damage threshold selects its destination and retains actual player identity");
    native.Step(new());Check(native.Entities.Single().Phase=="third"&&native.Entities.Single().Damage!.PresentPlayerDamage==100,"The actual wipeProgress flag does not reset credit on entering another damage-gated phase");
}
using(var native=Session(damageDoc,new(Invulnerable:true))){native.SetBossHpFraction(.2);native.Step(new());Check(native.Entities.Single().Phase=="first"&&native.Entities.Single().Damage is {RecordedDamage:0,PresentPlayerDamage:0},"Forcing HP does not invent native damage credit");}
using(var native=Session(Doc("new State(new DamageTakenTransition(100,\"done\"),new State(\"done\"))"),new(Invulnerable:true)))
{
    Damage(native,50);var parts=Engine(native);parts.World.Players.Remove(parts.Player.Id);
    Check(native.Entities.Single().Damage is {RecordedDamage:50,PresentPlayerDamage:0,Players.Length:0},"Snapshots distinguish recorded damage from credit belonging to players still present in their world");parts.World.Players[parts.Player.Id]=parts.Player;
    Check(native.Entities.Single().Damage!.PresentPlayerDamage==50,"Restoring the same native player restores the counter's own eligibility result");
}
using(var native=Session(Doc("new State()"),new(Invulnerable:true)))
{
    var parts=Engine(native);var parent=new DamageCounter(parts.Boss);parent.HitBy(parts.Player,12);parts.Boss.DamageCounter.Parent=parent;
    Check(native.Entities.Single().Damage is {RecordedDamage:0,PresentPlayerDamage:12,ParentActorId:not null},"Native parent counters remain distinct from the child's recorded total");
}
using(var native=Session(Doc("new State(new State(\"gate\",new DamageTakenTransition(1,\"hit\")),new State(\"hit\"))"),new(Invulnerable:true,StartX:66.5,StartY:64.5)))
{
    for(var tick=0;tick<20&&native.Entities.Single().Phase!="hit";tick++)native.Step(new(Attack:true,AimX:64.5,AimY:64.5));
    Check(native.Entities.Single().Phase=="hit"&&native.Events.Any(e=>e.Kind=="weapon-hit")&&native.Entities.Single().Damage!.PresentPlayerDamage>0,"Actual XML weapon hits populate the native counter and trigger damage-based phase transitions");
}
var hpDoc=Doc("new State(new State(\"gate\",new EntityHpLessTransition(10,\"cabIN\",.5,\"done\")),new State(\"done\"))");
using(var native=Session(hpDoc,new(Invulnerable:true),arena:new(){Actors=[new(helper,66.5,64.5)]}))
{
    var target=Engine(native).World.Enemies.Values.Single(e=>e.ObjectDesc.IdName==helper);target.MaximumHP=200;target.HP=100;native.Step(new());Check(native.Entities.Single(e=>e.Name==id).Phase=="gate","Native nearby-health comparison retains its strict equality boundary");target.HP--;native.Step(new());Check(native.Entities.Single(e=>e.Name==id).Phase=="done","Case-insensitive native ID-name substrings select a nearby Enemy for health gating");
}
using(var native=Session(Doc("new State(new State(\"gate\",new EntityHpLessTransition(10,\"BRAWLER\",.5,\"done\")),new State(\"done\"))"),new(Invulnerable:true))){native.SetBossHpFraction(.4);native.Step(new());Check(native.Entities.Single().Phase=="done","Native name lookup includes the host itself rather than excluding it as a helper");}
using(var native=Session(Doc("new State(new State(\"gate\",new EntityHpLessTransition(10,\"Missing name fragment\",.5,\"done\")),new State(\"done\"))"),new(Invulnerable:true))){native.Step(new());Check(native.Entities.Single().Phase=="gate","An absent native name fragment leaves the health condition false");}
var parameter=adapter.EntityParameterFor(hpDoc,hpDoc.AllNodes.Single(n=>n.Kind=="EntityHpLessTransition"))!;Check(parameter is {Scope:EntityResourceScope.Enemy,NameFragment:true,AllowsPlayer:false}&&EntityResources.Catalog(parameter).Any(e=>e.SpriteId==helper),"Nearby-health authoring offers Enemy sprites with explicit substring semantics");
var hpNode=hpDoc.AllNodes.Single(n=>n.Kind=="EntityHpLessTransition");var before=hpDoc.Source;adapter.ApplyEntityReference(hpDoc,id,hpNode.Id,hpDoc.Revision,"entity",helper);Check(hpDoc.Undo()&&hpDoc.Source==before,"Canonical native name selection has one exact source undo");Reject(()=>adapter.ApplyEntityReference(hpDoc,id,hpNode.Id,hpDoc.Revision,"entity","Wizard"),"Player XML cannot be selected for the native Enemy health cast");
foreach(var sample in new[]{(Amount:"50",Percentage:false,Fraction:.5,Expected:200),(Amount:"50",Percentage:true,Fraction:.4,Expected:180),(Amount:"100",Percentage:true,Fraction:.4,Expected:120),(Amount:"null",Percentage:false,Fraction:.4,Expected:120),(Amount:"100",Percentage:false,Fraction:1d/3,Expected:100)})
{
    using var native=Session(Doc("new State(new HealSelf(coolDown:200,amount:"+sample.Amount+",percentage:"+(sample.Percentage?"true":"false")+"))"),new(Invulnerable:true));native.SetBossHpFraction(sample.Fraction);native.Step(new());Check(native.BossHp==sample.Expected,"Native HealSelf retains actual additive/current-HP/null/equal-increment behavior");
}
using(var native=Session(Doc("new State(new State(\"defend\",new GiveTemporaryDefense(20),new TimedTransition(200,\"plain\")),new State(\"plain\"))"),new(Invulnerable:true)))
{
    var initial=native.Entities.Single().Defense;native.Step(new());Check(native.Entities.Single().Defense==initial+20,"Native entry adds temporary defense to the live actor snapshot");native.Advance(new(),300);Check(native.Entities.Single().Phase=="plain"&&native.Entities.Single().Defense==initial,"Native phase exit restores defense without changing its XML definition");
}
var analysis=Doc("new State(new State(\"gate\",new DamageTakenTransition(10,\"a\"),new DamageTakenTransition(20,\"b\",true)),new State(\"a\"),new State(\"b\"))");var review=BehaviorAnalysis.Analyze(analysis.Entities.Single().Root);
Check(review.Transitions[0].Condition=="Credited damage ≥ 10"&&review.Diagnostics.Any(d=>d.Message.StartsWith("Priority conflict")),"Damage review uses inclusive thresholds and native lower-threshold sibling precedence");
Check(adapter.Callbacks("GiveTemporaryDefense").Select(c=>c.Stage).SequenceEqual(new[]{"Entry","Exit"})&&adapter.Callbacks("HealSelf").Select(c=>c.Stage).SequenceEqual(new[]{"Entry","Ongoing"}),"Lifecycle provenance retains genuine defense entry/exit and healing entry/tick callbacks");
var file=adapter.Catalog().Single(e=>e.Id==id).SourceFile;var supplied=File.ReadAllText(file);var root=new BehaviorDocument(supplied,adapter,sourceFile:file).Entities.Single(e=>e.EntityId==id).Root;
var source=supplied.Remove(root.Start,root.Length).Insert(root.Start,"new State(new HealthFixture(),new State(\"heal\",new GiveTemporaryDefense(15),new HealSelf(coolDown:200,amount:10),new DamageTakenTransition(1,\"done\")),new State(\"done\"))")+"\nnamespace TKR.WorldServer.logic.behaviors { internal sealed class HealthFixture : Behavior { protected override void OnStateEntry(TKR.WorldServer.core.objects.Entity host,TKR.WorldServer.core.worlds.TickTime time,ref object state) { var enemy=(TKR.WorldServer.core.objects.Enemy)host;enemy.MaximumHP=300;enemy.HP=300; } } }";var contextual=new BehaviorDocument(source,adapter,sourceFile:file);var project=new StudioProject{SourceFile=file,Source=source,EntityId=id,Identities=contextual.Identities,Seed=7,Player=new(Invulnerable:true,StartX:66.5,StartY:64.5)};
EncounterReplay? healthRecording=null; DamageCounterView? recordedCounter=null;
await using(var compiled=await CompiledRuntime.StartAsync(adapter.ServerRoot,project,artifactRoot:Path.GetFullPath(".artifacts/health/compiled"))){compiled.Recording=new(compiled);await compiled.AdvanceAsync(new(),100);Check(compiled.Entities.Single().Defense==15&&compiled.Entities.Single().Damage is {PresentPlayerDamage:0},"Compiled original-source frames carry live defense and native counters through protocol nine");for(var tick=0;tick<20&&compiled.Entities.Single().Phase!="done";tick++)await compiled.AdvanceAsync(new(Attack:true,AimX:64.5,AimY:64.5),100);Check(compiled.Entities.Single().Phase=="done"&&compiled.Entities.Single().Damage!.PresentPlayerDamage>0,"Compiled native weapon damage selects the original damage gate");var path=Path.GetFullPath(".artifacts/health/trace.json");compiled.ExportTrace(path);using var trace=JsonDocument.Parse(File.ReadAllText(path));Check(trace.RootElement.GetProperty("actors")[0].GetProperty("Damage").GetProperty("PresentPlayerDamage").GetInt32()>0,"Trace actor snapshots retain native damage eligibility and defense");recordedCounter=compiled.Entities.Single().Damage;healthRecording=EncounterReplay.Capture(project,null,EncounterReplay.Identify(adapter,project,compiled.Stamp.BuildId),compiled);}
var replay=await ReplayCursor.RestoreAsync(adapter,healthRecording!,healthRecording!.DurationMs);using(replay.Session){Check(replay.Session.Entities.Single().Defense==0&&replay.Session.Entities.Single().Damage!.PresentPlayerDamage==recordedCounter!.PresentPlayerDamage&&replay.Session.Entities.Single().Damage!.Players.SequenceEqual(recordedCounter.Players),"Compiled replay witnesses reproduce native defense and contributor credit snapshots");}
Check(File.ReadAllText(file)==supplied,"Health authoring and native playback leave supplied source unchanged");Console.WriteLine($"{checks} native health checks passed");
