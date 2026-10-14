using System.Reflection;
using Studio.Core;
using Studio.Raple;
using TKR.Shared.resources;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;
using TKR.WorldServer.networking.packets.outgoing;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
const string entity="Cave Pirate Brawler";
string Ledger(string extra="")=>"new TrailLedger(0,shardCount:4,fuseMs:300,intervalMs:100,startupMs:0"+(extra==""?"":","+extra)+")";
string Recount(string extra="")=>"new TrailRecount(0,shardCount:4,windowMs:500,spacing:1,minFuseMs:300,maxFuseMs:600,intervalMs:1000,startupMs:500"+(extra==""?"":","+extra)+")";
BehaviorDocument Doc(string body)=>new("class Fixture{void Build(){db.Init(\""+entity+"\",new State(\"trail\","+body+"));}}",adapter);
World World(EncounterSession session){var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session)!;return(World)engine.GetType().GetField("World")!.GetValue(engine)!;}
Enemy Boss(EncounterSession session)=>World(session).Enemies.Values.First(e=>e.ObjectDesc.IdName==entity);
Player Player(EncounterSession session)=>World(session).Players.Values.Single();
NativeEffect[] Marks(EncounterSession session)=>session.Effects.Where(e=>e.Kind==(byte)EffectType.LedgerMark&&e.DurationMs>0&&e.Y2==0).ToArray();
foreach(var kind in new[]{"TrailLedger","TrailRecount"})Check(adapter.Primitives.Single(p=>p.Name==kind) is{Category:"Attacks",Simulatable:true},kind+" exposes the original native attack constructor");
Check(!RapleAdapter.OutsideLedgerBounds(3,4,0,0,5)&&RapleAdapter.OutsideLedgerBounds(3,4.1,0,0,5)&&!RapleAdapter.OutsideLedgerBounds(100,100,0,0,0),"Jurisdiction uses strict spawn-centered distance with zero disabling the rule");
using(var native=new EncounterSession(adapter,Doc(Ledger()),entity,7,new(Invulnerable:true)))
{
    native.Advance(new(),300);Check(Marks(native).Length==1&&Marks(native)[0].X==70.5f&&Marks(native)[0].DurationMs==300,"Stationary targets merge into one live native glyph instead of stacking stamps");native.Advance(new(),100);Check(native.Shots.Single().Count==4&&native.Shots.Single().X==70.5f&&Marks(native).Length==2,"Due glyph bursts before the same tick stamps the newly free position");
}
using(var native=new EncounterSession(adapter,Doc(Ledger("boundsRadius:5,penaltyFuseMs:400")),entity,7,new(Invulnerable:true)))
{native.Advance(new(),100);Check(Marks(native).Single().DurationMs==400&&!native.Effects.Any(e=>e.Kind==(byte)EffectType.LedgerMark&&e.Y2==1),"Outside targets use penalty timing while the original minimum-long subtraction suppresses the boundary cue");}
using(var native=new EncounterSession(adapter,Doc(Ledger("mergeRadius:0,perPlayerCap:2,totalCap:2")),entity,7,new(Invulnerable:true)))
{native.Advance(new(),300);Check(Marks(native).Length==2,"Per-player and total native caps bound live stamps");}
using(var native=new EncounterSession(adapter,Doc(Ledger()),entity,7,new(Invulnerable:true)))
{native.Advance(new(),100);Boss(native).ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Stunned,-1));Player(native).Move(71.5f,64.5f);native.Advance(new(),300);Check(native.Shots.Count==1&&Marks(native).Length==1,"Stun suppresses stamping but armed glyphs still resolve through the original observer");}
using(var native=new EncounterSession(adapter,Doc(Ledger()),entity,7,new(Invulnerable:true)))
{native.Advance(new(),100);World(native).GetType().GetMethod("ClearHostileProjectiles",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(World(native),[Player(native)]);Boss(native).ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Stunned,-1));native.Advance(new(),400);Check(native.Shots.Count==0&&native.Effects.Any(e=>e.Kind==(byte)EffectType.LedgerMark&&e.DurationMs==0),"Original projectile generation clearing removes pending stamps and marks");}
using(var native=new EncounterSession(adapter,Doc(Ledger("interestIndex:0,interestCount:3,interestAimed:true,interestSpreadDeg:10,coreIndex:0")),entity,7,new(Invulnerable:true)))
{native.Advance(new(),100);Player(native).Move(70.5f,66.5f);native.Advance(new(),300);Check(native.Shots.Select(s=>s.Count).SequenceEqual(new[]{4,1,3}),"Native bursts preserve shard, core and aimed-interest packet order");Check(Math.Abs(native.Shots[2].Angle-(Math.PI/2-Math.PI/18))<.0001&&Math.Abs(native.Shots[2].AngleIncrement-Math.PI/18)<.0001,"Interest fan aims at the latest recorded owner position with original spacing");}
var recountDoc=Doc(Recount());var settings=adapter.ReadTrailLedger(recountDoc,recountDoc.AllNodes.Single(n=>n.Kind=="TrailRecount"))!;
var path=new List<(long T,float X,float Y)>{(0,0,0),(250,2,0),(500,4,0)};var points=RapleAdapter.RecountLayout(settings,path,500);
Check(points.Count==5&&points.First().FuseMs==300&&points.Last().FuseMs==600&&points.Last().X==4,"Original recount resampling retains the path endpoint and oldest/newest native fuses");var capped=RapleAdapter.RecountLayout(settings with{Values=settings.Values.ToDictionary(p=>p.Key,p=>p.Key=="perPlayerCap"?2:p.Value)},path,500);Check(capped.Count==2&&capped.Last().X==4,"Native adaptive spacing covers the full path under a two-point cap");
using(var native=new EncounterSession(adapter,recountDoc,entity,7,new(Invulnerable:true)))
{native.Advance(new(1,0),600);Check(Marks(native).Length>=3&&Marks(native).All(e=>e.DurationMs is >=300 and <=600)&&Marks(native).First().X<Marks(native).Last().X,"Actual recount tracks movement before startup and stamps resampled history with varying native fuses");native.Advance(new(),700);Check(native.Shots.Count==Marks(native).Length,"Every accepted recount point resolves through the native glyph engine");}
var phaseSource="class Fixture{void Build(){db.Init(\""+entity+"\",new State(new State(\"trail\","+Ledger()+",new TimedTransition(100,\"done\")),new State(\"done\")));}}";
using(var native=new EncounterSession(adapter,new BehaviorDocument(phaseSource,adapter),entity,7,new(Invulnerable:true))){native.Advance(new(),500);Check(native.Shots.Count==0&&native.Effects.Any(e=>e.Kind==(byte)EffectType.LedgerMark&&e.DurationMs==0),"Native phase exit removes pending trail glyphs before their burst");}
using(var native=new EncounterSession(adapter,Doc(Ledger().Replace("fuseMs:300","fuseMs:2000")),entity,7,new(Invulnerable:true))){native.Advance(new(),100);native.SetBossHpFraction(0);native.Advance(new(Attack:true),1000);Check(native.BossHp==0&&native.Shots.Count==0&&native.Effects.Any(e=>e.Kind==(byte)EffectType.LedgerMark&&e.DurationMs==0),"Actual weapon-triggered death clears pending trail data and markers");}
using(var native=new EncounterSession(adapter,Doc(Ledger()),entity,7,new(Invulnerable:true),arena:new(){Actors=[new(entity,67.5,64.5)]})){native.Advance(new(),200);Check(Marks(native).Length==2&&Marks(native).Select(e=>e.TargetObjectId).Distinct().Count()==2,"A shared native trail object retains independent glyph books for two actors");}
var doc=Doc(Ledger());var node=doc.AllNodes.Single(n=>n.Kind=="TrailLedger");var identities=doc.Identities;doc.Apply(new(doc.Revision,"Edit trail timing",[new(node.Id,"fuseMs","600")]),d=>adapter.Validate(d,entity));Check(adapter.ReadTrailLedger(doc,doc.Find(node.Id))!["fuseMs"]==600,"Native history timing edits retain original source fields");doc.LockedNodes.Add(node.Id);Reject(()=>doc.Apply(new(doc.Revision,"Locked trail",[new(node.Id,"fuseMs","900")]),d=>adapter.Validate(d,entity)),"Trail region locks protect timing edits");doc.LockedNodes.Clear();Check(doc.Undo()&&doc.Identities.SequenceEqual(identities),"Trail source history restores all identities");
Check(adapter.Validate(Doc(Ledger("perPlayerCap:25,totalCap:24")),entity).Any(d=>d.IsError)&&adapter.Validate(Doc(Recount().Replace("windowMs:500","windowMs:499")),entity).Any(d=>d.IsError),"Original cap and history-window constructor constraints reject before playback");
var file=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var supplied=File.ReadAllText(file);
foreach(var body in new[]{Ledger(),Recount()})
{var source="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioTrail=()=>Behav().Init(\""+entity+"\",new State(\"trail\","+body+"));}}";var project=new StudioProject{SourceFile=file,Source=source,EntityId=entity,Player=new(Invulnerable:true),Seed=7};using var literal=new EncounterSession(adapter,Doc(body),entity,7,project.Player);await using var compiled=await CompiledRuntime.StartAsync(adapter.ServerRoot,project);compiled.Recording=new(compiled);literal.Advance(new(1,0),600);await compiled.AdvanceAsync(new(1,0),600);literal.Advance(new(),700);await compiled.AdvanceAsync(new(),700);Check(compiled.Shots.SequenceEqual(literal.Shots)&&compiled.Effects.SequenceEqual(literal.Effects),"Literal and compiled "+body.Split('(')[0]+" preserve complete movement-history marks and burst packets");var tape=EncounterReplay.Capture(project,null,EncounterReplay.Identify(adapter,project,compiled.Stamp.BuildId),compiled);var cursor=await ReplayCursor.RestoreAsync(adapter,tape,1300);using(cursor.Session){Check(cursor.Session.Shots.SequenceEqual(compiled.Shots)&&cursor.Session.Effects.SequenceEqual(compiled.Effects),"Replay restores complete native "+body.Split('(')[0]+" history and pending-mark outcomes");}}
Check(File.ReadAllText(file)==supplied,"Trail history authoring and execution preserve supplied source");Console.WriteLine($"{checks} native trail-ledger checks passed.");
