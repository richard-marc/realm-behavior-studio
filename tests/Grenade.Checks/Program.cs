using System.Reflection;
using System.Text.Json;
using Studio.Core;
using Studio.Raple;
using TKR.Shared.resources;
using TKR.WorldServer.core.net.datas;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.structures;
using TKR.WorldServer.core.worlds;
using TKR.WorldServer.core.worlds.impl;
using TKR.WorldServer.networking.packets.outgoing;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool passed,string label){if(!passed)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
void Advance(IEncounterSession session,EncounterInput input,int ms){while(ms>0){var amount=Math.Min(1000,ms);session.Advance(input,amount);ms-=amount;}}
async Task AdvanceAsync(IEncounterSession session,EncounterInput input,int ms){while(ms>0){var amount=Math.Min(1000,ms);await session.AdvanceAsync(input,amount);ms-=amount;}}
const string entity="Cave Pirate Brawler";
BehaviorDocument Doc(string body)=>new("class Fixture{void Build(){db.Init(\""+entity+"\",new State(\"attack\","+body+"));}}",adapter);
World World(EncounterSession s){var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(s)!;return(World)engine.GetType().GetField("World")!.GetValue(engine)!;}
Enemy Boss(EncounterSession s)=>World(s).Enemies.Values.OrderBy(e=>e.Id).First();
NativeEffect[] Throws(IEncounterSession s)=>s.Effects.Where(e=>e.Area==null&&e.Kind==(byte)EffectType.Throw).ToArray();
var doc=Doc("new Grenade(2,50)");var node=doc.AllNodes.Single(n=>n.Kind=="Grenade");
Check(adapter.Primitives.Single(p=>p.Name=="Grenade") is{Category:"Attacks",Simulatable:true}&&adapter.ProjectileParameter(doc,node)==null,"Grenade exposes the actual native area-attack constructor without inventing an XML projectile index");
Check(adapter.ReadGrenade(doc,node) is{Radius:2,Damage:50,Range:5,FixedAngle:null,Count:1,NativeSpacing:0,Color:0xffff0000},"Grenade authoring retains native optional geometry and color defaults");
Check(adapter.ParametersFor(doc,node).Where(p=>p.Name is "shootAngle" or "fixedAngle" or "rotateAngle" or "angleOffset").All(p=>p.Unit=="degrees")&&adapter.ParametersFor(doc,node).Single(p=>p.Name=="effectDuration").Unit=="ms","Original angle and condition/countdown units are explicit");
var ids=doc.Identities;var originalSource=doc.Source;
doc.Apply(new(doc.Revision,"Fixed multi-throw",[new(node.Id,"fixedAngle","0"),new(node.Id,"count","7")]),d=>adapter.Validate(d,entity));
Check(adapter.ReadGrenade(doc,doc.Find(node.Id)) is{Count:7,NativeSpacing:51,FixedAngle:0}&&ids.All(i=>doc.Identities.Any(n=>n.Id==i.Id)),"Typed edits preserve original integer default spacing and construction identities");
Check(doc.Undo()&&doc.Source==originalSource&&doc.Redo(),"Coordinated grenade geometry changes retain complete one-step history");
doc.LockedNodes.Add(node.Id);Reject(()=>doc.Apply(new(doc.Revision,"Locked",[new(node.Id,"range","8")]),d=>adapter.Validate(d,entity)),"Grenade edits honor region locks");doc.LockedNodes.Clear();
Reject(()=>doc.Apply(new(doc.Revision-1,"Stale",[new(node.Id,"range","8")]),d=>adapter.Validate(d,entity)),"Stale grenade changes reject without mutation");
Check(adapter.Validate(Doc("new Grenade(2,50,range:double.MaxValue)"),entity).Any(d=>d.IsError),"Non-finite native landing geometry rejects before preview");
Check(adapter.Validate(Doc("new Grenade(2,50,range:double.PositiveInfinity,fixedAngle:double.NaN)"),entity).Count(d=>d.IsError)>=2,"Named non-finite constants cannot bypass native grenade geometry validation");
foreach(var body in new[]{"new Grenade(2,50,range:6,fixedAngle:0,count:7,coolDown:10000)","new Grenade(2,50,range:-6,fixedAngle:45,count:3,shootAngle:22.5,angleOffset:12.5,coolDown:10000)"})
{
    var layoutDoc=Doc(body);var layout=adapter.ReadGrenade(layoutDoc,layoutDoc.AllNodes.Single(n=>n.Kind=="Grenade"))!;
    using var native=new EncounterSession(adapter,layoutDoc,entity,7,new(Invulnerable:true));Advance(native,new(),100);
    var expected=layout.FixedTargets(64.5f,64.5f);var actual=Throws(native);
    Check(actual.Select(e=>(e.X,e.Y)).SequenceEqual(expected.Select(p=>(p.X,p.Y))),"First fixed grenade layout matches every native float landing point: "+body);
    Check(actual.All(e=>e.X2==222&&e.DurationMs==0)&&native.Shots.Count==0,"Grenade launch retains original Throw payload and emits no enemy projectile packets");
}
using(var native=new EncounterSession(adapter,Doc("new Grenade(2,50,range:20,count:3,shootAngle:12,rotateAngle:90,coolDown:10000)"),entity,7,new(Invulnerable:true)))
{
    Advance(native,new(),100);var throws=Throws(native);Check(throws.Length==3&&throws.All(e=>e.X==native.PlayerX&&e.Y==native.PlayerY),"Aimed multi-throws capture one player location instead of applying fixed-angle spread");
    var captured=(throws[0].X,throws[0].Y);Advance(native,new(0,1),200);Check(Throws(native).All(e=>(e.X,e.Y)==captured)&&native.PlayerY!=captured.Y,"Throw destinations remain fixed after player movement");
}
using(var native=new EncounterSession(adapter,Doc("new Grenade(2,50,range:20,coolDown:10000)"),entity,7,new(Defense:0)))
{
    Advance(native,new(),1500);Check(native.Effects.All(e=>e.Area==null)&&native.PlayerHp==1000,"Native grenade timer has no impact before its expiry boundary");
    Advance(native,new(),200);var impact=native.Effects.Single(e=>e.Area!=null);
    Check(impact.Ms==1500&&impact.Area is{Radius:2,Damage:50,Condition:0,ConditionDuration:0}&&impact.X==70.5f&&impact.Y==64.5f,"Original timer dispatch emits the complete native area packet at its observed tick");
    Check(native.PlayerHp==950&&native.Events.Any(e=>e.Kind=="hit"),"Native delayed area damage reaches the actual prototype Player.Damage path");
    Check(native.Events.Single(e=>e.Kind=="area") is{PacketOrigin:null,NodeId:null,ActorId:null},"Delayed impacts retain an explicit source boundary rather than inheriting the latest callback");
    var marks=TelegraphGeometry.Sample(native.Effects,impact.Ms+100);Check(marks.Length==8&&marks.All(m=>m.Shape==TelegraphShape.Dot&&Math.Abs(Math.Sqrt(Math.Pow(m.X-impact.X,2)+Math.Pow(m.Y-impact.Y,2))-1)<1e-5),"Area impact ground projection uses the original client's point count and linear 200 ms radial travel");
    Check(TelegraphGeometry.Sample(native.Effects,impact.Ms+200).Length==0,"Area impact particles expire at the client's exact lifetime boundary");
}
using(var native=new EncounterSession(adapter,Doc("new Grenade(2,50,range:20,coolDown:10000)"),entity,7,new(Defense:0)))
{Advance(native,new(),100);Advance(native,new(0,1),1600);Check(native.PlayerHp==1000&&native.Effects.Any(e=>e.Area!=null),"Moving outside the captured native blast location avoids actual area damage");}
using(var native=new EncounterSession(adapter,Doc("new Grenade(2,50,range:20,coolDown:10000,effect:ConditionEffectIndex.Slowed,effectDuration:1000)"),entity,7,new(Invulnerable:true)))
{Advance(native,new(),1700);Check(native.PlayerHp==1000&&World(native).Players.Values.Single().HasConditionEffect(ConditionEffectIndex.Slowed),"Native invulnerability protects HP while the original server still applies the configured non-Stasis condition");}
using(var native=new EncounterSession(adapter,Doc("new Grenade(2,50,range:20,coolDown:10000)"),entity,7,new(Invulnerable:true)))
{Boss(native).ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Stunned,-1));Advance(native,new(),400);Check(Throws(native).Length==0&&native.NativeState.Single(s=>s.Owner.EndsWith(".Grenade")).Value=="0","Stun holds a ready native grenade launch without resetting its countdown");}
using(var native=new EncounterSession(adapter,Doc("new Grenade(2,50,range:20,coolDown:10000,coolDownOffset:100)"),entity,7,new(Invulnerable:true)))
{Advance(native,new(),100);Check(Throws(native).Length==0,"Initial launch countdown decrements before a later ready tick");Advance(native,new(),100);Check(Throws(native).Single().Ms==100,"Grenade launches on the native tick after countdown reaches zero");}
using(var native=new EncounterSession(adapter,Doc("new Grenade(2,50,range:20,coolDown:10000)"),entity,7,new(Invulnerable:true)))
{Advance(native,new(),100);Boss(native).Expunge();Advance(native,new(),1700);Check(native.Entities.Count==0&&native.Effects.Any(e=>e.Area!=null),"Native Expunge retains the host world reference, so its pending grenade still impacts after removal");}
using(var native=new EncounterSession(adapter,Doc("new Grenade(2,50,range:20,coolDown:10000)"),entity,7,new(Invulnerable:true)))
{Advance(native,new(),100);typeof(BossLabWorld).GetMethod("ClearHostileProjectiles",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(World(native),[World(native).Players.Values.Single()]);Advance(native,new(),1700);Check(native.Effects.All(e=>e.Area==null),"Original hostile-projectile clear generation invalidates pending grenade impacts");}
using(var native=new EncounterSession(adapter,Doc("new Grenade(2,50,range:4,fixedAngle:0,rotateAngle:90,coolDown:100)"),entity,7,new(Invulnerable:true),arena:new(){Actors=[new(entity,68.5,68.5)]}))
{
    Advance(native,new(),600);var throws=Throws(native);var hosts=native.Entities.ToDictionary(e=>e.Id);
    var layout=new GrenadeSettings(2,50,4,0,1,null,0,90,0xffff0000);
    Check(throws.Select(e=>e.TargetObjectId).Distinct().Count()==2&&throws.Select((e,i)=>(e,i)).All(p=>layout.FixedTargets(hosts[p.e.TargetObjectId].X,hosts[p.e.TargetObjectId].Y,p.i).Single() is{} point&&point.X==p.e.X&&point.Y==p.e.Y),"One native Grenade object shares its rotation counter while two actors keep independent cooldown storage");
}
using(var native=new EncounterSession(adapter,new BehaviorDocument("class Fixture{void Build(){db.Init(\"Cave Pirate Brawler\",new State(new State(\"launch\",new Grenade(2,50,range:20,coolDown:10000),new TimedTransition(100,\"idle\")),new State(\"idle\")));}}",adapter),entity,7,new(Invulnerable:true)))
{Advance(native,new(),1700);Check(native.Entities.Single().Phase=="idle"&&native.Effects.Any(e=>e.Area!=null),"Original pending grenade timer survives an ordinary phase exit");}
using(var native=new EncounterSession(adapter,Doc("new Grenade(2,50,range:100,fixedAngle:0,coolDown:10000)"),entity,7,new(Invulnerable:true)))
{Advance(native,new(),1700);Check(Throws(native).Length==1&&native.Effects.All(e=>e.Area==null),"Native prototype arena boundary suppresses an out-of-arena landing at impact time");}
using(var native=new EncounterSession(adapter,new BehaviorDocument("class Fixture{void Build(){db.Init(\"Cave Pirate Brawler\",new State());}}",adapter),entity,7,new(Invulnerable:true)))
{
    var world=World(native);var host=Boss(native);world.BroadcastIfVisible(new AoeMessage(new(65,66),3,70000,(ConditionEffectIndex)99,.75f,123,new ARGB(0x80a1b2c3)),host);
    var far=new Position(0,0);world.BroadcastIfVisible(new AoeMessage(new(65,66),3,50,0,0,123,new ARGB(0xffffffff)),ref far);Advance(native,new(),5);
    Check(native.Effects is[{Area:{Radius:3,Damage:4464,Condition:99,ConditionDuration:.75f},X:65,Y:66,ObjectType:123,Color:0x80a1b2c3}],"Area capture follows native visibility and exact unsigned wire fields without claiming packet damage was applied");
}
var file=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var supplied=File.ReadAllText(file);
const string source="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioGrenade=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"grenades\",new Grenade(2,50,range:20,coolDown:10000)));}}";
var sourceDoc=new BehaviorDocument(source,adapter,sourceFile:file);var project=new StudioProject{SourceFile=file,Source=source,EntityId=entity,Identities=sourceDoc.Identities,Player=new(Defense:0)};
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,project))
await using(var plain=await CompiledRuntime.StartAsync(adapter.ServerRoot,project,captureStateOrigins:false))
{
    native.Recording=new(native);await AdvanceAsync(native,new(),1700);await AdvanceAsync(plain,new(),1700);
    Check(native.Effects.SequenceEqual(plain.Effects)&&native.PlayerHp==plain.PlayerHp&&native.PlayerHp==950,"Native callback/packet instrumentation preserves complete delayed area payloads and actual player damage");
    var grenadeId=sourceDoc.AllNodes.Single(n=>n.Kind=="Grenade").Id;
    Check(native.Events.Single(e=>e.Kind=="effect").PacketOrigin?.Source?.NodeId==grenadeId&&native.Events.Single(e=>e.Kind=="area").PacketOrigin?.Timer?.SchedulingSource?.NodeId==grenadeId,"Compiled launch links its actual Grenade constructor and timer impact retains the distinct recorded scheduling source");
    var tape=EncounterReplay.Capture(project,null,EncounterReplay.Identify(adapter,project,native.Stamp.BuildId),native);var seek=await ReplayCursor.RestoreAsync(adapter,tape,1495);using var seekSession=seek.Session;
    Check(seek.Session.PlayerHp==1000&&seek.Session.Effects.All(e=>e.Area==null),"Pre-impact seek retains a genuine pending timer without advancing its damage");await seek.AdvanceToAsync(1505);
    Check(seek.Session.Effects.SequenceEqual(native.Effects)&&seek.Session.PlayerHp==950,"Partial replay reconstructs the original pending native timer and verifies its area impact prefix");
    await seek.AdvanceToAsync(1700);Check(seek.Session.Effects.SequenceEqual(native.Effects)&&seek.Session.PlayerHp==native.PlayerHp,"Complete grenade replay retains native packets and actual damage witnesses");
    var changed=tape with{Effects=tape.Effects.Select(e=>e.Area==null?e:e with{Area=e.Area with{Damage=1}}).ToArray()};var rejected=false;try{var bad=await ReplayCursor.RestoreAsync(adapter,changed,1505);bad.Session.Dispose();}catch(InvalidOperationException){rejected=true;}Check(rejected,"Replay rejects altered native area packet damage independently of the observed HP witness");
    var invalid=tape with{Effects=tape.Effects.Select(e=>e.Area==null?e:e with{Area=e.Area with{Radius=float.NaN}}).ToArray()};Reject(invalid.Validate,"Replay rejects non-finite native area metadata before restoration");
    var output=Path.GetFullPath(".artifacts/grenade");Directory.CreateDirectory(output);native.ExportTrace(Path.Combine(output,"trace.json"));Check(File.ReadAllText(Path.Combine(output,"trace.json")).Contains("\"Area\""),"Compiled trace exports the discriminated native area packet payload");
}
const string areaSource="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioAreaOrigins=()=>Behav().Init("Cave Pirate Brawler",new State("area",new StudioAreaProbe(1),new StudioAreaProbe(2)));
} }
namespace TKR.WorldServer.logic.behaviors {
    internal sealed class StudioAreaProbe:Behavior {
        public StudioAreaProbe(int occurrence) {}
        static readonly TKR.WorldServer.networking.packets.outgoing.AoeMessage Shared=new(new(65,66),3,70,0,0,123,new(0xff123456));
        protected override void TickCore(TKR.WorldServer.core.objects.Entity host,TKR.WorldServer.core.worlds.TickTime time,ref object state)=>host.World.BroadcastIfVisible(Shared,host);
    }
}
""";
var areaDoc=new BehaviorDocument(areaSource,adapter,sourceFile:file);var areaIds=areaDoc.AllNodes.Where(n=>n.Kind.EndsWith("StudioAreaProbe",StringComparison.Ordinal)).Select(n=>n.Id).ToArray();
await using(var areas=await CompiledRuntime.StartAsync(adapter.ServerRoot,project with{Source=areaSource,Identities=areaDoc.Identities}))
{
    await areas.AdvanceAsync(new(),100);var records=areas.Events.Where(e=>e.Kind=="area").ToArray();
    File.WriteAllText(".artifacts/grenade-area-origins.json",JsonSerializer.Serialize(new{areaIds,nodes=areaDoc.AllNodes.Select(n=>new{n.Id,n.Kind,n.Start}),records},BehaviorDocument.JsonOptions));
    Check(records.Length==2&&records.Select(e=>e.PacketOrigin?.Source?.NodeId).SequenceEqual(areaIds.Select(id=>(Guid?)id)),"Reusing one synchronous native area packet preserves FIFO source attribution for both actual emitters");
    Check(records.Select(e=>e.PacketOrigin?.CallbackSequence).Distinct().Count()==2&&areas.Effects[0]==areas.Effects[1],"Identical area payloads retain distinct original callback occurrences");
}
Check(File.ReadAllText(file)==supplied,"Grenade authoring, timers and compiled playback preserve supplied game source");
Console.WriteLine($"{checks} grenade checks passed.");
