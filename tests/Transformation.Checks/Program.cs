using System.Reflection;
using Studio.Core;
using Studio.Raple;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
const string id="Cave Pirate Brawler";
BehaviorDocument Doc(string tree,string entity=id)=>new("class Fixture { void Build() { db.Init(\""+entity+"\", "+tree+"); } }",adapter);
EncounterSession Session(BehaviorDocument doc,string entity=id)=>new(adapter,doc,entity,7,new(Invulnerable:true));
Enemy Boss(EncounterSession session)
{
    var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session)!;
    return ((World)engine.GetType().GetField("World")!.GetValue(engine)!).Enemies.Values.Single();
}
Check(new[]{"SizeTransform","ChangeSize","SizeTransformComplete","SetAltTexture"}.All(k=>adapter.Primitives.Single(p=>p.Name==k).Simulatable),"Transformation authoring binds the four actual native constructors");
foreach(var argsText in new[]{"multiplier:0","multiplier:-1","growMs:-1","shrinkMs:-1","maxDurationMs:-1","hpThreshold:0","hpThreshold:1.01","glowColor:-1","glowColor:0x1000000","maxDurationMs:null,hpThreshold:null"})
    Reject(()=>adapter.BuildState(Doc("new State(new SizeTransform("+argsText+"))"),id),"Native constructor rejects invalid transform settings: "+argsText);
Reject(()=>adapter.BuildState(Doc("new State(new SizeTransformComplete(new SizeTransform(),\"done\"),new State(\"done\"))"),id),"A completion transition cannot invent an unattached transform instance");
using(var session=Session(Doc("new State(new SizeTransform(2,growMs:200,maxDurationMs:300,shrinkMs:200,hpThreshold:null,glowColor:0x123456))")))
{
    var boss=Boss(session);var normal=boss.Size;boss.GlowEnemy=0x010203;boss.ProjectileSizeBase=7;
    var sizes=new List<int>();for(var i=0;i<5;i++){session.Step(new());sizes.Add(session.Entities.Single().Size!.Value);}
    Check(sizes.SequenceEqual(new[]{normal*3/2,normal*2,normal*2,normal*3/2,normal}),"Original smooth-step growth, hold and shrinking follow native 100 ms callbacks");
    Check(session.Entities.Single() is {GlowColor:0x010203,ProjectileSizeBase:7},"Completion restores native entry glow and projectile-size settings");
    Check(session.Events.Count(e=>e.Kind=="appearance")>=4,"Native appearance changes are observed with actor identity");
}
using(var session=Session(Doc("new State(new SizeTransform(2,growMs:200,maxDurationMs:null,shrinkMs:200,hpThreshold:.75))")))
{
    var normal=Boss(session).Size;session.Step(new());session.SetBossHpFraction(.75);session.Step(new());
    Check(session.Entities.Single().Size==normal*5/4,"Inclusive HP interruption shrinks from the actual intermediate native size");session.Step(new());
    Check(session.Entities.Single().Size==normal,"HP-only transformation restores entry size after shrinking");
}
using(var session=Session(Doc("new State(new State(\"grow\",new SizeTransform(2,growMs:200,maxDurationMs:1000,shrinkMs:200,hpThreshold:null,scaleProjectiles:false),new TimedTransition(100,\"done\")),new State(\"done\"))")))
{
    var normal=Boss(session).Size;Boss(session).ProjectileSizeBase=9;session.Step(new());
    Check(session.Entities.Single().ProjectileSizeBase==0,"Disabling projectile scaling uses the native zero base during transformation");session.Step(new());
    Check(session.Entities.Single() is {Phase:"done",ProjectileSizeBase:9}&&session.Entities.Single().Size==normal,"Early native phase exit restores size and projectile settings");
}
using(var session=Session(Doc("new State(new ChangeSize(15,130))")))
{
    var sizes=new List<int>();for(var i=0;i<4;i++){session.Step(new());sizes.Add(session.Entities.Single().Size!.Value);}
    Check(sizes.SequenceEqual(new[]{115,115,115,130}),"ChangeSize retains native 150 ms countdown quantization and target clamping");
}
using(var session=Session(Doc("new State(new ChangeSize(-5,130))"))){session.Step(new());Check(session.Entities.Single().Size==130,"A wrong-signed native rate clamps directly to the target instead of being corrected by Studio");}
using(var session=Session(Doc("new State(new ChangeSize(0,130))"))){session.Advance(new(),1000);Check(session.Entities.Single().Size==100,"Zero native size rate remains stationary");}
using(var session=Session(Doc("new State(new SizeTransform(2,growMs:0,maxDurationMs:0,shrinkMs:0,hpThreshold:null))"))){session.Step(new());Check(session.Entities.Single() is {Size:100,GlowColor:0,ProjectileSizeBase:0},"Zero-duration native transforms complete and restore their settings in the first callback");}
Check(AlternateTextures.Indices("Treasure Rat").SequenceEqual(new[]{0,1}),"Alternate texture IDs come from the supplied XML, including the base texture");
Check(AlternateTextures.Resolve("Treasure Rat",2,1)==1&&AlternateTextures.Resolve("Treasure Rat",0,1)==0,"Undefined texture IDs retain the previous client texture and zero restores the base");
using(var session=Session(Doc("new State(new SetAltTexture(1,3,cooldown:100,loop:false))","Treasure Rat"),"Treasure Rat"))
{
    session.Advance(new(),1000);Check(session.Entities.Single() is {AltTexture:3,TextureIndex:1},"A batched advance retains valid intermediate textures through later undefined native IDs");
}
var primary=adapter.Catalog().Single(e=>e.Id==id).SourceFile;var original=File.ReadAllText(primary);
const string source="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static readonly SizeTransform StudioVerifiedTransform=new(2,growMs:200,maxDurationMs:300,shrinkMs:200,hpThreshold:null);
    private _ StudioTransformFixture=()=>Behav().Init("Cave Pirate Brawler",new State(new State("grow",StudioVerifiedTransform,new SizeTransformComplete(StudioVerifiedTransform,"done")),new State("done")));
} }
""";
var bound=new BehaviorDocument(source,adapter,sourceFile:primary);var root=bound.Entities.Single().Root;
var sharedTransform=bound.Definitions.Single(d=>d.Definition!.Label=="StudioVerifiedTransform").Root;
Check(sharedTransform.Kind=="SizeTransform"&&sharedTransform.Parameters.Single(p=>p.Name=="multiplier").Expression=="2","Target-typed shared transform construction exposes its real native parameters");
bound.Apply(new(bound.Revision,"Edit native shared transform",[new(sharedTransform.Id,"multiplier","3")]),d=>adapter.Validate(d,id));
Check(bound.Find(sharedTransform.Id).Parameters.Single(p=>p.Name=="multiplier").Expression=="3"&&bound.Undo()&&bound.Find(sharedTransform.Id).Parameters.Single(p=>p.Name=="multiplier").Expression=="2","Target-typed transform editing preserves native binding and Undo identity");
Reject(()=>bound.Apply(new(bound.Revision,"Invalid shared transform",[new(sharedTransform.Id,"multiplier","0")]),d=>adapter.Validate(d,id)),"Target-typed shared transforms retain native constructor range checks before mutation");
bound.Apply(new(bound.Revision,"Change optional scaling",[new(sharedTransform.Id,"scaleProjectiles","false")]),d=>adapter.Validate(d,id));
Check(bound.Find(sharedTransform.Id).Parameters.Single(p=>p.Name=="scaleProjectiles").Expression=="false"&&bound.Source.Contains("new(2,")&&bound.Undo(),"Optional native parameters append to target-typed constructors without changing their source form or history");
Check(BehaviorAnalysis.Analyze(root).Transitions.Single().Node.Kind=="SizeTransformComplete","Completion appears as a real ordered transition despite its native name lacking the Transition suffix");
bound.RenameState(root.StateChildren.Single(n=>bound.StateName(n.Id)=="done").Id,"restored",d=>adapter.Validate(d,id));
Check(bound.Source.Contains("StudioVerifiedTransform,\"restored\""),"Native completion targets participate in phase rename");
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,new StudioProject{SourceFile=primary,Source=bound.Source,Identities=bound.Identities,Player=new(Invulnerable:true)}))
{
    await runtime.AdvanceAsync(new(),600);Check(runtime.Entities.Single() is {Phase:"restored",Size:100,GlowColor:0,ProjectileSizeBase:0},"Compiled original native completion reads the same shared transform and restores appearance on phase entry");
}
Check(File.ReadAllText(primary)==original,"Transformation discovery, authoring and native playback leave the supplied source unchanged");
var twoActorSource=source.Replace("hpThreshold:null","hpThreshold:.75").Replace("new State(\"done\")));","new State(\"done\"))).Init(\"Pirate Lieutenant\",new State(new State(\"grow\",StudioVerifiedTransform,new SizeTransformComplete(StudioVerifiedTransform,\"done\")),new State(\"done\")));");
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,new StudioProject{SourceFile=primary,Source=twoActorSource,Player=new(Invulnerable:true),Arena=new(){Actors=[new("Pirate Lieutenant",70,64)]}}))
{
    var initialSizes=runtime.Entities.ToDictionary(e=>e.Name,e=>e.Size!.Value);
    await runtime.AdvanceAsync(new(),100);Check(runtime.Entities.Count==2&&runtime.Entities.All(e=>e.Size==Math.Round(initialSizes[e.Name]*1.5)),"One original SizeTransform instance stores separate animation state on two native actors: "+System.Text.Json.JsonSerializer.Serialize(runtime.Entities.Select(e=>new{e.Name,e.Size,e.Phase})));
    await runtime.SetBossHpAsync(.75);await runtime.AdvanceAsync(new(),100);
    Check(runtime.Entities.Single(e=>e.Name==id).Size==Math.Round(initialSizes[id]*1.25)&&runtime.Entities.Single(e=>e.Name=="Pirate Lieutenant").Size==initialSizes["Pirate Lieutenant"]*2,"HP interruption affects only its host even when both actors share the same native transform");
}
var originalDoc=new BehaviorDocument(original,adapter,sourceFile:primary);var originalRoot=originalDoc.Entities.Single(e=>e.EntityId==id).Root;
var replaySource=original.Remove(originalRoot.Start,originalRoot.Length).Insert(originalRoot.Start,"new State(new SizeTransform(2,growMs:200,maxDurationMs:300,shrinkMs:200,hpThreshold:null))");
var replayDoc=new BehaviorDocument(replaySource,adapter,sourceFile:primary);var replayProject=new StudioProject{SourceFile=primary,Source=replaySource,Identities=replayDoc.Identities,Player=new(Invulnerable:true),Seed=7};
EncounterReplay tape;
using(var native=Session(replayDoc)){native.Recording=new(native);native.Advance(new(),500);tape=EncounterReplay.Capture(replayProject,null,EncounterReplay.Identify(adapter,replayProject),native);}
var restored=await ReplayCursor.RestoreAsync(adapter,tape,500);
Check(restored.Session.Entities.Single() is {Size:100,GlowColor:0,ProjectileSizeBase:0}&&restored.Session.Events.Any(e=>e.Kind=="appearance"),"Replay reproduces native appearance fields and ordered changes");restored.Session.Dispose();
using(var native=Session(replayDoc)){native.Recording=new(native);Boss(native).GlowEnemy=123;native.Advance(new(),500);tape=EncounterReplay.Capture(replayProject,null,EncounterReplay.Identify(adapter,replayProject),native);}
var rejected=false;try{var divergent=await ReplayCursor.RestoreAsync(adapter,tape,500);divergent.Session.Dispose();}catch(InvalidOperationException){rejected=true;}
Check(rejected,"Replay witnesses reject an unrecorded native glow change after the transform restores its entry value");
Console.WriteLine($"{checks} transformation checks passed.");
