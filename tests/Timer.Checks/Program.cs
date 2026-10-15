using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool passed,string label){if(!passed)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
const string entity="Cave Pirate Brawler";var file=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var supplied=File.ReadAllText(file);
const string grenade="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioTimerGrenade=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"timer\",new Grenade(2,50,range:20,coolDown:10000)));}}";
var doc=new BehaviorDocument(grenade,adapter,sourceFile:file);var grenadeId=doc.AllNodes.Single(n=>n.Kind=="Grenade").Id;
var project=new StudioProject{SourceFile=file,Source=grenade,EntityId=entity,Identities=doc.Identities,Player=new(Defense:0),Seed=7};
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,project))
await using(var plain=await CompiledRuntime.StartAsync(adapter.ServerRoot,project,captureStateOrigins:false))
{
    native.Recording=new(native);await native.AdvanceAsync(new(),1000);await native.AdvanceAsync(new(),700);await plain.AdvanceAsync(new(),1000);await plain.AdvanceAsync(new(),700);
    var impact=native.Events.Single(e=>e.Kind=="area");var binding=impact.PacketOrigin?.Timer;
    Check(binding is{DelayMs:1500,Invocation:1,EntityOwned:true,SchedulingSource.NodeId:not null}&&binding.SchedulingSource.NodeId==grenadeId,"Native grenade timer capture retains actual delay, entity ownership and accepted scheduling construction");
    Check(native.Stamp.Source.TimerInstrumentation&&RuntimeProtocol.Version==20,"Source provenance and protocol explicitly identify native timer observation");
    var call=native.Callbacks.Single(c=>c.Sequence==impact.PacketOrigin!.CallbackSequence);
    Check(call is{Stage:"Timer action",Origin:null,Outcome:"completed",Timer.Returned:true}&&call.Timer.TimerId==binding!.TimerId,"Area packet links the actual native timer delegate invocation rather than a fabricated Grenade tick");
    Check(native.Callbacks.Any(c=>c.Sequence==binding!.SchedulingCallbackSequence&&c.Origin?.NodeId==grenadeId&&c.Stage=="Tick"),"Scheduling callback remains a separate recorded native dispatch");
    Check(impact.NodeId==null&&impact.PacketOrigin!.Source==null&&impact.ActorId==binding!.OwnerActorId,"Timer packet source construction stays distinct from scheduling source and native entity owner");
    Check(binding!.Returned==null&&call.Timer!.Returned==true,"Packet-time provenance does not claim a function result before the invocation returns");
    Check(native.Effects.SequenceEqual(plain.Effects)&&native.PlayerHp==plain.PlayerHp&&native.PlayerHp==950,"Timer observation preserves complete uninstrumented native impact payloads and actual HP damage");
    var tape=EncounterReplay.Capture(project,null,EncounterReplay.Identify(adapter,project,native.Stamp.BuildId),native);var seek=await ReplayCursor.RestoreAsync(adapter,tape,1495);using var seekSession=seek.Session;
    Check(seek.Session.Callbacks.All(c=>c.Timer==null),"Pre-expiry seek records no timer invocation before the native callback boundary");
    await seek.AdvanceToAsync(1505);Check(seek.Session.Events.Where(e=>e.Kind=="area").SequenceEqual(tape.Events.Where(e=>e.Kind=="area"))&&seek.Session.Callbacks.Any(c=>c.Timer?.SchedulingSource?.NodeId==grenadeId),"Partial replay independently reconstructs actual timer registration and packet linkage");
    await seek.AdvanceToAsync(1700);Check(seek.Session.Callbacks.SequenceEqual(native.Callbacks),"Complete replay verifies the full native timer transcript including results and scheduling metadata");
    var altered=tape with{Callbacks=tape.Callbacks.Select(c=>c.Timer==null?c:c with{Timer=c.Timer with{DelayMs=1}}).ToArray()};var rejected=false;try{var bad=await ReplayCursor.RestoreAsync(adapter,altered,1505);bad.Session.Dispose();}catch(InvalidOperationException){rejected=true;}Check(rejected,"Replay rejects changed timer registration evidence independently of unchanged impact packets");
    var invalid=tape with{Callbacks=tape.Callbacks.Select(c=>c.Timer==null?c:c with{Timer=c.Timer with{SchedulingCallbackSequence=c.Sequence+1}}).ToArray()};var malformed=false;try{invalid.Validate();}catch(ArgumentException){malformed=true;}Check(malformed,"Forward timer scheduling links reject before replay starts");
}
const string source="""
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using TKR.Shared.resources;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;
using TKR.WorldServer.networking.packets.outgoing;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static readonly WorldTimer StudioPrecreatedTimer=new WorldTimer(100,(world,time)=>StudioTimerProbe.Emit(world,901));
    private _ StudioTimerCases=()=>Behav().Init("Cave Pirate Brawler",new State("cases",new StudioTimerProbe()));
    sealed class StudioTimerProbe:Behavior {
        public static void Emit(World world,int value){var pos=new TKR.WorldServer.core.structures.Position(65,65);world.BroadcastIfVisible(new AoeMessage(pos,1,value,0,0,0,new(0xff123456)),ref pos);}
        protected override void OnStateEntry(Entity host,TickTime time,ref object state){
            if(host.X>65)return;
            int evaluations=0;int Delay(){evaluations++;return 100;}
            host.World.StartNewTimer(Delay(),(world,tick)=>Emit(world,71));
            host.World.StartNewTimer(Delay(),(world,tick)=>Emit(world,70+evaluations));
            int repeats=0;host.World.StartNewTimer(100,(world,tick)=>{Emit(world,200+ ++repeats);return repeats==3;});
            host.World.StartEntityTimer(host,100,(world,tick)=>{Emit(world,101);world.StartEntityTimer(host,0,(nested,t)=>Emit(nested,301));});
            var other=host.World.Enemies.Values.First(e=>e.Id!=host.Id);host.World.StartEntityTimer(other,100,(world,tick)=>Emit(world,401));
            int resets=0;WorldTimer reset=null;reset=host.World.StartNewTimer(100,(world,tick)=>{Emit(world,500+ ++resets);if(resets==1){reset.Reset();return false;}return true;});
            host.World.StartNewTimer(100,(world,tick)=>{Emit(world,701);throw new InvalidOperationException("native timer fault");});
            ((List<WorldTimer>)typeof(World).GetField("Timers",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host.World)).Add(StudioPrecreatedTimer);
            Action<World,TickTime> chain=(world,tick)=>Emit(world,801);chain+=(world,tick)=>Emit(world,802);host.World.StartNewTimer(100,chain);
        }
        protected override void TickCore(Entity host,TickTime time,ref object state){}
    }
} }
""";
var caseDoc=new BehaviorDocument(source,adapter,sourceFile:file);var probe=caseDoc.AllNodes.Single(n=>n.Kind.EndsWith("StudioTimerProbe"));
var cases=project with{Source=source,Identities=caseDoc.Identities,Player=new(Invulnerable:true),Arena=new(){Actors=[new(entity,68.5,68.5)]}};
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,cases))
await using(var plain=await CompiledRuntime.StartAsync(adapter.ServerRoot,cases,captureStateOrigins:false))
{
    await native.AdvanceAsync(new(),700);await plain.AdvanceAsync(new(),700);
    Check(native.Effects.SequenceEqual(plain.Effects),"Native action/function/reset/nested/multicast/fault timers retain complete original packet order and timing");
    var calls=native.Callbacks.Where(c=>c.Timer!=null).ToArray();var impacts=native.Events.Where(e=>e.Kind=="area").ToArray();
    NativeCallbackRecord Call(int damage)=>calls.Single(c=>c.Sequence==impacts.Single(e=>native.Effects[e.PacketIndex!.Value].Area!.Damage==damage).PacketOrigin!.CallbackSequence);
    Check(native.Effects.Count(e=>e.Area?.Damage is 71 or 72)==2&&native.Effects.Any(e=>e.Area?.Damage==72),"Original timer argument side effects execute once in native evaluation order");
    Check(impacts.Take(2).Select(e=>native.Effects[e.PacketIndex!.Value].Area!.Damage).SequenceEqual(new ushort[]{801,802}),"Original reverse timer order and multicast handler order remain unchanged");
    Check(Call(801).Sequence==Call(802).Sequence&&Call(801).Timer!.Handler.Contains(';'),"Multicast packets retain one actual native delegate invocation with its ordered handler description");
    var repeat=new[]{Call(201),Call(202),Call(203)};
    Check(repeat.Select(c=>c.Ms).SequenceEqual(new long[]{100,200,300})&&repeat.Select(c=>c.Timer!.Invocation).SequenceEqual(new[]{1,2,3}),"Native false-returning function timer repeats on later ticks without resetting its countdown");
    Check(repeat.Select(c=>c.Timer!.Returned).SequenceEqual(new bool?[]{false,false,true})&&repeat.Select(c=>c.Timer!.TimerId).Distinct().Count()==1,"Actual function results and stable timer identity follow native removal semantics");
    var reset=new[]{Call(501),Call(502)};Check(reset.Select(c=>c.Ms).SequenceEqual(new long[]{100,300})&&reset.Select(c=>c.Timer!.TimerId).Distinct().Count()==1,"Original Reset changes the pending interval while retaining timer identity and invocation count");
    var nested=Call(301);var outer=Call(101);Check(nested.Timer!.SchedulingCallbackSequence==outer.Sequence&&nested.Timer.SchedulingSource==null&&nested.ParentSequence==null,"Nested timer scheduling links the actual earlier invocation without inventing synchronous nesting or a constructor source");
    Check(Call(401).Timer is{EntityOwned:true,OwnerActorId:not null,SchedulingActorId:not null} foreign&&foreign.OwnerActorId!=foreign.SchedulingActorId&&Call(401).ActorId==foreign.OwnerActorId,"Entity timer owner stays distinct from the action's scheduling actor");
    Check(Call(701) is{Outcome:"faulted",Timer.Returned:null}&&calls.Any(c=>c.Sequence>Call(701).Sequence&&c.Outcome=="completed"),"Original timer faults retain native caller handling and restore callback context for later invocations");
    Check(Call(901).Timer is{SchedulingCallbackSequence:null,SchedulingSource:null,SchedulingActorId:null,OwnerActorId:null}&&impacts.Single(e=>native.Effects[e.PacketIndex!.Value].Area!.Damage==901).ActorId==null,"Untracked timer construction remains explicitly unattributed rather than inheriting the action that later installed it");
    Check(Call(71).Timer!.SchedulingSource?.NodeId==probe.Id&&Call(71).Timer.OwnerActorId==null,"Ordinary world timers retain observed scheduling construction without inventing entity ownership");
    Check(calls.All(c=>c.Origin==null)&&impacts.All(e=>e.PacketOrigin?.Behavior=="TKR.WorldServer.core.worlds.WorldTimer"),"Timer dispatch is represented separately from its scheduler's behavior construction");
}
Check(File.ReadAllText(file)==supplied,"Native timer observation, replay and failure handling preserve supplied source");
Console.WriteLine($"{checks} native timer checks passed.");
