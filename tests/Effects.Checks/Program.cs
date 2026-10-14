using System.Reflection;
using System.Text.Json;
using Studio.Core;
using Studio.Raple;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;
using TKR.WorldServer.core.net.datas;
using TKR.WorldServer.core.structures;
using TKR.WorldServer.networking.packets.outgoing;

var server=Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server");var adapter=new RapleAdapter(server);var checks=0;
void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
const string owner="Cave Pirate Brawler";
BehaviorDocument Doc(string state)=>new("class Fixture { void Build() { db.Init(\""+owner+"\", "+state+"); } }",adapter);
const string lanes="new State(new OwnedLaneEmitters(\"Gordo Procession Emitter\", LaneAxes.Cross, intervalMs:200, startupMs:200))";
using(var native=new EncounterSession(adapter,Doc(lanes),owner,7,new(Invulnerable:true)))
{
    native.Advance(new(),1000);Check(native.Effects.Count>0&&native.Effects.All(e=>e.Kind==19&&e.DurationMs==350&&e.X2==.45f&&e.Color==0xff66ccff),"Actual lane controller delivers original warning type, duration, radius and ARGB through the native queue");
    Check(native.Effects.All(e=>native.Shots.Any(s=>s.Ms==e.Ms&&s.OwnerId==e.TargetObjectId&&s.X==e.X&&s.Y==e.Y)),"Native warnings retain actual helper ownership, emission times and remote shot origins");
    var marks=TelegraphGeometry.Sample(native.Effects,native.Ms);Check(marks.Length>0&&marks.All(m=>m.Shape==TelegraphShape.Circle&&m.Color==0xffffff&&Math.Abs(m.Radius-.45)<.0001),"Sanctuary warning projection follows client holy-circle color and native ground radius");
    var e=0;var s=0;var p=0;var m=0;var fx=0;var cb=0;long arena=-1;var first=RuntimeProtocol.Capture(native,ref e,ref s,ref p,ref m,ref arena,ref fx,ref cb);var second=RuntimeProtocol.Capture(native,ref e,ref s,ref p,ref m,ref arena,ref fx,ref cb);
    Check(first.EffectDelta.SequenceEqual(native.Effects)&&second.EffectDelta.Length==0&&RuntimeProtocol.Version==17,"Versioned frames transport actual effect deltas exactly once");
    var path=Path.GetFullPath(".artifacts/effects/literal-trace.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);native.ExportTrace(path);using var trace=JsonDocument.Parse(File.ReadAllText(path));Check(trace.RootElement.GetProperty("effects").GetArrayLength()==native.Effects.Count,"Literal trace exports every raw native effect packet");
}
using(var native=new EncounterSession(adapter,Doc("new State()"),owner,7,new(Invulnerable:true)))
{
    var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(native)!;var world=(World)engine.GetType().GetField("World")!.GetValue(engine)!;var host=world.Enemies.Values.Single();
    var effect=new ShowEffect{EffectType=EffectType.LedgerMark,TargetObjectId=host.Id,Pos1=new(65,66),Pos2=new(3,2),Color=new ARGB(0x80a1b2c3),Duration=425,ObjectType=123};world.BroadcastIfVisible(effect,host);
    var far=new Position(0,0);world.BroadcastIfVisible(new ShowEffect{EffectType=EffectType.SanctuaryWarning,TargetObjectId=host.Id,Duration=100},ref far);native.Advance(new(),5);
    Check(native.Effects is [{Kind:22,X:65,Y:66,X2:3,Y2:2,Color:0x80a1b2c3,DurationMs:425,ObjectType:123,Ms:0}],"Native broadcast visibility filters distant packets while preserving every received payload field");
    Check(world.Players.Values.Single().Client.Socket==null,"Offline native sender capture opens no socket");
    native.RemoveMinionsForTest();Check(native.Effects.Count==1,"Test overrides drain their native queue without duplicating prior packets");
    native.Dispose();native.Dispose();var restored=world.Players.Values.Single().Client.State.ToString()=="Disconnected";Check(restored,"Idempotent disposal restores the original disconnected transport");
}
NativeEffect Effect(byte kind,int duration=1000,int ownerId=1,float x2=2,float y2=0,ushort count=0)=>new(0,kind,ownerId,64,64,x2,y2,0xff123456,duration,count);
Check(TelegraphGeometry.Sample([Effect(19)],-5).Length==0&&TelegraphGeometry.Sample([Effect(19)],1000).Length==0,"Warning lifetime excludes future packets and its exact expiry boundary");
Check(TelegraphGeometry.Sample([Effect(19)],500).Single().Progress==.5,"Warning fuse uses the client's thirty-frame progress quantization");
var ring=TelegraphGeometry.Sample([Effect(18,x2:0,y2:(float)(Math.Tau/12),count:10)],100);
Check(ring.Count(m=>!m.Opening)==100&&ring.Count(m=>m.Opening)==4&&Math.Abs(ring.First().X-66.5)<.0001,"Ring warnings use native first-angle spacing, radial markers and the client's conditional opening");
Check(TelegraphGeometry.Sample([Effect(18,count:100)],350).Length==640&&TelegraphGeometry.Sample([Effect(18,count:1)],350).All(m=>m.Color==0xffef99),"Ring count caps and pulse colors follow client warning behavior");
Check(TelegraphGeometry.Sample([Effect(18,x2:(float)Math.PI,count:1) with {X=0}],100).Length==0,"Warning particle positions outside the actual map dimensions are omitted like the client's square lookup");
Check(TelegraphGeometry.Sample([Effect(17,count:10000)],100).Length==31&&TelegraphGeometry.Sample([Effect(21,count:10000)],100).Length==49,"Cellar warning families retain landing circles and ten or sixteen native burst spokes");
Check(TelegraphGeometry.Sample([Effect(22,x2:50,y2:99)],100).Single() is {Shape:TelegraphShape.Diamond,Color:0x123456}&&Math.Abs(TelegraphGeometry.Sample([Effect(22,x2:50)],100).Single().Radius-14.75)<.0001,"Ledger glyphs retain client style fallback, RGB and radius clamp");
var clear=Effect(22,duration:0) with {Ms=400};var later=Effect(22) with {Ms=450};
Check(TelegraphGeometry.Sample([Effect(22),Effect(22,ownerId:2),clear,later],500).Length==2,"Ledger clear packets remove earlier marks for exactly their native owner while retaining later marks");
Check(TelegraphGeometry.Sample([Effect(250)],0).Length==0,"Unknown native effect types remain raw packets rather than invented warning geometry");
var file=adapter.Catalog().Single(e=>e.Id==owner).SourceFile;var original=File.ReadAllText(file);var root=new BehaviorDocument(original,adapter,sourceFile:file).Entities.Single(e=>e.EntityId==owner).Root;
var source=original.Remove(root.Start,root.Length).Insert(root.Start,lanes);var doc=new BehaviorDocument(source,adapter,sourceFile:file);var project=new StudioProject{Source=source,SourceFile=file,EntityId=owner,Identities=doc.Identities,Seed=7,Player=new(Invulnerable:true)};
EncounterReplay recording;
using(var native=new EncounterSession(adapter,doc,owner,7,project.Player))
{
    native.Recording=new(native);native.Advance(new(),1000);recording=EncounterReplay.Capture(project,null,EncounterReplay.Identify(adapter,project),native);
    Check(recording.Version==2&&recording.Effects.SequenceEqual(native.Effects)&&recording.Commands.Last().Result!.Effects==native.Effects.Count,"Replay version two persists native effects and includes them in command witnesses");
}
var replay=await ReplayCursor.RestoreAsync(adapter,recording,505);using(replay.Session){Check(replay.Session.Effects.SequenceEqual(recording.Effects.Where(e=>e.Ms<505)),"Partial replay independently reproduces the actual warning prefix");await replay.AdvanceToAsync(1000);Check(replay.Session.Effects.SequenceEqual(recording.Effects),"Full replay reproduces native warnings with complete witness agreement");}
var modified=recording with {Effects=recording.Effects.Select((e,i)=>i==0?e with{Color=0}:e).ToArray()};var rejected=false;try{var bad=await ReplayCursor.RestoreAsync(adapter,modified,505);bad.Session.Dispose();}catch(InvalidOperationException){rejected=true;}Check(rejected,"Partial replay rejects a modified native warning payload");
rejected=false;try{var bad=await ReplayCursor.RestoreAsync(adapter,modified,1000);bad.Session.Dispose();}catch(InvalidOperationException){rejected=true;}Check(rejected,"Completed replay rejects stored warning payload divergence independently of the state digest");
await using(var compiled=await CompiledRuntime.StartAsync(server,project,artifactRoot:Path.GetFullPath(".artifacts/effects/compiled")))
{
    await compiled.AdvanceAsync(new(),1000);Check(compiled.Effects.SequenceEqual(recording.Effects),"Compiled original encounter transports exactly the same native warning packets as literal playback");
    compiled.ExportTrace(Path.GetFullPath(".artifacts/effects/compiled-trace.json"));using var trace=JsonDocument.Parse(File.ReadAllText(".artifacts/effects/compiled-trace.json"));Check(trace.RootElement.GetProperty("effects").GetArrayLength()==compiled.Effects.Count,"Compiled traces retain raw native effects");
}
Check(File.ReadAllText(file)==original,"Native warning execution and capture leave supplied source unchanged");
const string forcedTree="new State(new State(\"opening\", new StudioWarning()), new State(\"finish\"))";
var forcedSource=original.Remove(root.Start,root.Length).Insert(root.Start,forcedTree)+"""

namespace TKR.WorldServer.logic.behaviors {
    internal sealed class StudioWarning : Behavior {
        protected override void OnStateEntry(TKR.WorldServer.core.objects.Entity host,TKR.WorldServer.core.worlds.TickTime time,ref object state) => Send(host,1000);
        protected override void OnStateExit(TKR.WorldServer.core.objects.Entity host,TKR.WorldServer.core.worlds.TickTime time,ref object state) => Send(host,0);
        static void Send(TKR.WorldServer.core.objects.Entity host,int duration) => host.World.BroadcastIfVisible(new TKR.WorldServer.networking.packets.outgoing.ShowEffect {
            EffectType=TKR.WorldServer.networking.packets.outgoing.EffectType.LedgerMark,TargetObjectId=host.Id,
            Pos1=new TKR.WorldServer.core.structures.Position(host.X,host.Y),Pos2=new TKR.WorldServer.core.structures.Position(2,0),Duration=duration
        },host);
    }
}
""";
await using(var forced=await CompiledRuntime.StartAsync(server,project with{Source=forcedSource,Identities=[]},phase:"finish",artifactRoot:Path.GetFullPath(".artifacts/effects/forced")))
{
    Check(forced.Effects is [{Kind:22,DurationMs:1000,Ms:0},{Kind:22,DurationMs:0,Ms:0}],"Forced-phase initialization captures actual entry and exit packets at time zero before the initial frame");
    Check(TelegraphGeometry.Sample(forced.Effects,0).Length==0,"Native forced-phase clear cannot leave an obsolete initial ledger marker on screen");
}
Console.WriteLine($"{checks} native effect checks passed");
