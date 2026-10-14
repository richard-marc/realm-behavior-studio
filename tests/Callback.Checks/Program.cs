using Studio.Core;
using Studio.Raple;
var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
Emission[] observedShots=[];
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
const string entity="Cave Pirate Brawler";var file=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var original=File.ReadAllText(file);
const string source="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
using TKR.Shared.resources;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;
namespace TKR.WorldServer.logic {public partial class BehaviorDb {
    private _ StudioCallbackFixture=()=>Behav().Init("Cave Pirate Brawler",new State(new State("go",new StudioCallbackWrapper(new ConditionEffectBehavior(ConditionEffectIndex.Slowed),new Shoot(20,count:3,fixedAngle:0,coolDown:100),new ConditionEffectBehavior(ConditionEffectIndex.Armored),new ConditionEffectBehavior(ConditionEffectIndex.Speedy)),new TimedTransition(100,"wait")),new State("wait")));
    sealed class StudioCallbackWrapper:Behavior {
        readonly Behavior entry,tick,exit,death;
        public StudioCallbackWrapper(Behavior entry,Behavior tick,Behavior exit,Behavior death){this.entry=entry;this.tick=tick;this.exit=exit;this.death=death;}
        protected override void OnStateEntry(Entity host,TickTime time,ref object state)=>entry.OnStateEntry(host,time);
        protected override void TickCore(Entity host,TickTime time,ref object state)=>tick.Tick(host,time);
        protected override void OnStateExit(Entity host,TickTime time,ref object state)=>exit.OnStateEntry(host,time);
        public override void OnDeath(Entity host,ref TickTime time)=>death.OnStateEntry(host,time);
    }
} }
""";
var document=new BehaviorDocument(source,adapter,sourceFile:file);var project=new StudioProject{SourceFile=file,Source=source,EntityId=entity,Identities=document.Identities,Player=new(Invulnerable:true),Seed=7};
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,project))
{
    native.Recording=new(native);await native.AdvanceAsync(new(),200);
    Check(native.Stamp.Source.CallbackInstrumentation,"Runtime provenance explicitly identifies native callback instrumentation");
    var records=native.Callbacks.Where(c=>c.Actor==entity).ToArray();Check(records.Length>0&&records.All(c=>c.Outcome=="completed"),"Compiled execution exposes completed actual native callback dispatches");
    var wrapper=records.Where(c=>c.Behavior.EndsWith("StudioCallbackWrapper")).ToArray();Check(wrapper.Select(c=>c.Stage).SequenceEqual(new[]{"Entry","Tick","Tick","Exit"}),"Custom source controller transcript retains actual entry/tick/exit order");
    foreach(var parent in wrapper){var child=records.Single(c=>c.ParentSequence==parent.Sequence);Check(child.Stage==(parent.Stage=="Tick"?"Tick":"Entry"),"Observed "+parent.Stage+" records the child's actual dispatched stage");}
    Check(records.Where(c=>c.ParentSequence!=null).All(c=>records.Any(p=>p.Sequence==c.ParentSequence&&p.Sequence<c.Sequence)),"Nested callbacks retain real parent sequence order");
    Check(records.Where(c=>c.Behavior.EndsWith("Shoot")).All(c=>c.Origin?.TryResolve(project,out _)==true&&c.Origin.NodeId==document.AllNodes.Single(n=>n.Kind=="Shoot").Id),"Native action callbacks retain accepted constructor source and saved editor identity");
    Check(wrapper.All(c=>c.Origin?.TryResolve(project,out _)==true&&source.Substring(c.Origin.Start,c.Origin.Length).StartsWith("new StudioCallbackWrapper")),"Custom controller callbacks retain their exact executed constructor span");
    observedShots=native.Shots.ToArray();Check(native.Shots.Count==1&&native.Shots.All(s=>s.Count==3),"Callback observation retains original custom-controller attack behavior");
    var trace=Path.GetFullPath(".artifacts/callback-trace.json");native.ExportTrace(trace);Check(File.ReadAllText(trace).Contains("callbacks"),"Exported traces retain callback transcripts separately from console events");
    var tape=EncounterReplay.Capture(project,null,EncounterReplay.Identify(adapter,project,native.Stamp.BuildId),native);Check(tape.Callbacks.SequenceEqual(native.Callbacks)&&tape.Commands.Last().Result!.Callbacks==native.Callbacks.Count,"Replay captures complete callback order and its witness count");
    var tapePath=Path.GetFullPath(".artifacts/callback-replay.rbsr");tape.Save(tapePath);Check(EncounterReplay.Load(tapePath).Callbacks.SequenceEqual(tape.Callbacks),"Saved replay retains every callback's nested order and constructor origin");
    var changed=tape.Callbacks.ToArray();changed[0]=changed[0] with{Behavior="modified"};var rejected=false;try{var bad=await ReplayCursor.RestoreAsync(adapter,tape with{Callbacks=changed},200);bad.Session.Dispose();}catch(InvalidOperationException){rejected=true;}Check(rejected,"Replay rejects altered callback evidence at completed boundaries");

    var replay=await ReplayCursor.RestoreAsync(adapter,tape,200);Check(replay.Session.Callbacks.SequenceEqual(tape.Callbacks),"Fresh compiled replay verifies the full callback transcript");
    replay.Session.Dispose();var partial=await ReplayCursor.RestoreAsync(adapter,tape,105);Check(partial.Session.Callbacks.SequenceEqual(tape.Callbacks.Where(c=>c.Ms<105)),"Partial replay seek verifies callback prefixes at native tick boundaries");partial.Session.Dispose();
}
await using(var plain=await CompiledRuntime.StartAsync(adapter.ServerRoot,project,captureStateOrigins:false)){await plain.AdvanceAsync(new(),200);Check(plain.Callbacks.Count==0&&plain.Shots.SequenceEqual(observedShots),"Uninstrumented native execution retains packets and does not invent callback evidence");}
var deathSource=source.Replace("new TimedTransition(100,\"wait\")","new TimedTransition(100000,\"wait\")");var deathDoc=new BehaviorDocument(deathSource,adapter,sourceFile:file);
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,project with{Source=deathSource,Identities=deathDoc.Identities})){await native.AdvanceAsync(new(),100);await native.SetBossHpAsync(0);await native.AdvanceAsync(new(Attack:true,AimX:64.5,AimY:64.5),1000);var death=native.Callbacks.Single(c=>c.Stage=="Death"&&c.Behavior.EndsWith("StudioCallbackWrapper"));Check(native.Callbacks.Any(c=>c.ParentSequence==death.Sequence&&c.Stage=="Entry"&&c.Behavior.EndsWith("ConditionEffectBehavior")),"Actual weapon death captures a custom override and its nested entry dispatch");Check(native.BossHp==0&&death.Origin?.TryResolve(project with{Source=deathSource},out _)==true,"Death callback retains the removed actor and accepted custom constructor origin");}
const string orderedSource="using TKR.WorldServer.logic.behaviors;using TKR.WorldServer.logic.behaviors.@new;using TKR.WorldServer.logic.behaviors.@new.movements;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioCallbackOrdered=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"motion\",new OrderedBehavior(new Shoot(20),new NewWander(1))));}}";
var orderedDoc=new BehaviorDocument(orderedSource,adapter,sourceFile:file);await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,project with{Source=orderedSource,Identities=orderedDoc.Identities})){await native.AdvanceAsync(new(),100);var parent=native.Callbacks.Single(c=>c.Stage=="Tick"&&c.Behavior.EndsWith("OrderedBehavior"));Check(native.Callbacks.Any(c=>c.ParentSequence==parent.Sequence&&c.Stage=="Ordered tick"&&c.Behavior.EndsWith("Shoot")),"Ordered wrapper transcript records the actual TickOrdered dispatcher");Check(native.Shots.Count==0&&!native.Callbacks.Any(c=>c.Stage=="Ordered tick"&&c.Behavior.EndsWith("NewWander")),"Inherited ordered no-op stops later children without inventing ordinary attacks");}
const string callerSource="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic{public partial class BehaviorDb{
    private _ StudioCallerArgument=()=>Behav().Init("Cave Pirate Brawler",new State("motion",StudioCallerChoose(new Shoot(20,count:4,fixedAngle:0,coolDown:100))));
    static Behavior StudioCallerChoose(Behavior child,[System.Runtime.CompilerServices.CallerArgumentExpression("child")]string text=null)=>text.StartsWith("new Shoot(")?child:new Shoot(20,count:9,fixedAngle:0,coolDown:100);
}}
""";
var callerDoc=new BehaviorDocument(callerSource,adapter,sourceFile:file);await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,project with{Source=callerSource,Identities=callerDoc.Identities})){await native.AdvanceAsync(new(),100);Check(native.Shots.Single().Count==4,"CallerArgumentExpression retains original constructor text and native helper branching");Check(native.Callbacks.Where(c=>c.Behavior.EndsWith("Shoot")).All(c=>c.Origin==null),"Text-sensitive constructor arguments retain an explicit untracked construction boundary");}
Check(File.ReadAllText(file)==original,"Callback observation and replay preserve supplied source");Console.WriteLine($"{checks} native callback checks passed.");
