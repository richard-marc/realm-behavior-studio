using System.Reflection;
using System.Text.Json;
using Studio.Core;
using Studio.Raple;
using TKR.WorldServer.core.objects;
var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
const string entity="Cave Pirate Brawler";var file=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var supplied=File.ReadAllText(file);
const string loopSource="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioLoopState=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"loop\",new Timed(250,new Shoot(20,count:2,fixedAngle:0,coolDown:1000),new Shoot(20,count:3,fixedAngle:0,coolDown:1000)),new Timed(250)));}}";
var doc=new BehaviorDocument(loopSource,adapter,sourceFile:file);var timed=doc.AllNodes.First(n=>n.Kind=="Timed");var originalIds=doc.Identities;var contract=adapter.ReadLoop(doc,timed)!;
Check(contract.Period==250&&contract.Children.Count==2&&contract.Description.Contains("once per child"),"Native Timed authoring exposes actual period and per-child counter semantics");
doc.Apply(new(doc.Revision,"Edit loop period",[new(timed.Id,"period","350")]),d=>adapter.Validate(d,entity));Check(adapter.ReadLoop(doc,doc.Find(timed.Id))!.Period==350&&doc.Find(timed.Id).Children.Select(n=>n.Id).SequenceEqual(timed.Children.Select(n=>n.Id)),"Typed native period edits preserve loop children and identity");doc.Undo();Check(doc.Source==loopSource&&doc.Identities.SequenceEqual(originalIds),"Loop period Undo restores exact source and identities");
var project=new StudioProject{SourceFile=file,Source=loopSource,EntityId=entity,Identities=doc.Identities,Seed=7,Player=new(Invulnerable:true)};
using(var literal=new EncounterSession(adapter,doc,entity,7,project.Player))
await using(var compiled=await CompiledRuntime.StartAsync(adapter.ServerRoot,project))
{
    var expected=new[]{"50","150","250"};
    for(var i=0;i<3;i++){literal.Advance(new(),100);await compiled.AdvanceAsync(new(),100);var native=compiled.NativeState.Where(s=>s.Owner.EndsWith(".Timed")).ToArray();Check(native.Length==2&&native[0].Value==expected[i]&&native[1].Value=="250","Actual loop counter at tick "+i+" decrements per child while an empty loop keeps its entry counter");Check(literal.NativeState.Where(s=>s.Owner.EndsWith(".Timed")).Select(s=>s.Value).SequenceEqual(native.Select(s=>s.Value)),"Literal and compiled native loop counters agree at tick "+i);}
    Check(compiled.NativeState.Where(s=>s.Owner.EndsWith(".Timed")).All(s=>s.Meaning.Contains("milliseconds")&&s.Origin?.TryResolve(project,out _)==true),"Native stored loop counters carry exact accepted construction source and native units");
    Check(compiled.NativeState.First(s=>s.Owner.EndsWith(".Timed")).CycleStatus=="Completed"&&compiled.NativeState.Last(s=>s.Owner.EndsWith(".Timed")).CycleStatus=="NotStarted","Observed cycle status comes from the original backing field rather than a predicted timer model");
    Check(literal.Shots.SequenceEqual(compiled.Shots),"Native state observation preserves complete original loop emissions");
    var engine=typeof(EncounterSession).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(literal)!;var world=(TKR.WorldServer.core.worlds.World)engine.GetType().GetField("World")!.GetValue(engine)!;var boss=world.Enemies.Values.Single();boss.StateStorage[new DangerousOwner()]=new DangerousState();
    var observed=literal.NativeState.Single(s=>s.Owner.EndsWith("DangerousOwner"));Check(observed.Origin==null&&observed.Fields.Any(f=>f.Name=="Counter"&&f.Value=="17")&&observed.Fields.Any(f=>f.Name=="Samples"&&f.Value=="Array · 3 elements"),"Unknown native state reads direct scalar fields and array length without properties or custom formatting");
    Check(observed.Fields.Single(f=>f.Name=="Text").Value.Length<=128&&observed.Fields.All(f=>f.Name!="Danger"),"State observation bounds strings and excludes throwing getters");
    for(var i=0;i<1100;i++)boss.StateStorage[new object()]=i;Check(literal.NativeState.Count<=1025&&literal.NativeState.Any(s=>s.Truncated),"Native state observation has explicit actor/global storage limits");
}
const string counterSource="""
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static readonly StudioObservedCounter StudioSharedCounter=new(1);
    private _ StudioCounterFixture=()=>Behav().Init("Cave Pirate Brawler",new State("counter",StudioSharedCounter));
    sealed class StudioObservedCounter:Behavior {
        readonly int step; public StudioObservedCounter(int step)=>this.step=step;
        sealed class CounterState {public int Count;public bool Enabled=true;public object Parent;public int Danger=>throw new System.Exception("Do not execute state getters.");}
        protected override void TickCore(Entity host,TickTime time,ref object state){var value=state as CounterState??new CounterState();value.Count+=host.Y>65?step*2:step;value.Parent=host;state=value;}
    }
} }
""";
var counters=new BehaviorDocument(counterSource,adapter,sourceFile:file);var counterProject=project with{Source=counterSource,Identities=counters.Identities,Arena=new(){Actors=[new(entity,64.5,67.5)]}};
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,counterProject))
{
    native.Recording=new(native);await native.AdvanceAsync(new(),300);var values=native.NativeState.Where(s=>s.Owner.EndsWith("StudioObservedCounter")).ToArray();Check(values.Length==2&&values.Select(s=>s.ActorId).Distinct().Count()==2&&values.Select(s=>s.Fields.Single(f=>f.Name=="Count").Value).Order().SequenceEqual(new[]{"3","6"}),"Shared native counter behavior retains independent actual actor storage and source-defined increments");
    Check(values.All(s=>s.Origin?.TryResolve(counterProject,out _)==true)&&values.Select(s=>s.Origin!.Start).Distinct().Count()==1,"Shared counter owners link the same executed constructor while preserving separate actor values");
    Check(values.All(s=>s.Fields.Any(f=>f.Name=="Parent"&&f.Value.StartsWith("Reference"))&&!s.Fields.Any(f=>f.Name=="Danger")),"Counter observation leaves actor references opaque and never evaluates state accessors");
    var output=Path.GetFullPath(".artifacts/native-state");Directory.CreateDirectory(output);var trace=Path.Combine(output,"trace.json");native.ExportTrace(trace);Check(JsonDocument.Parse(File.ReadAllText(trace)).RootElement.GetProperty("nativeState").GetArrayLength()>=2,"Native traces export actual per-actor stored counter snapshots");
    var tape=EncounterReplay.Capture(counterProject,null,EncounterReplay.Identify(adapter,counterProject,native.Stamp.BuildId),native);var saved=Path.Combine(output,"counter.replay.json");tape.Save(saved);tape=EncounterReplay.Load(saved);var restored=await ReplayCursor.RestoreAsync(adapter,tape,300);using(restored.Session){Check(JsonSerializer.Serialize(restored.Session.NativeState)==JsonSerializer.Serialize(native.NativeState),"Saved native replay reproduces complete source-linked counter state at its recorded boundary");}
    var partial=await ReplayCursor.RestoreAsync(adapter,tape,75);using(partial.Session){await partial.AdvanceToAsync(300);Check(partial.Session.Recording!.ObserveCurrent(partial.Session)==tape.Commands.Last().Result,"Partial native seek resumes to the exact observed counter-state witness");}
    var stored=native.Frame.NativeState!;var position=Array.FindIndex(stored,s=>s.Owner.EndsWith("StudioObservedCounter"));var original=stored[position];stored[position]=original with{Fields=original.Fields.Select(f=>f.Name=="Count"?f with{Value="999"}:f).ToArray()};Check(native.Recording.ObserveCurrent(native).Signature!=tape.Commands.Last().Result!.Signature,"Replay witnesses detect changed stored counter values even when physical actor snapshots remain equal");stored[position]=original;
}
var prioritySource=loopSource.Replace("new Timed(250,new Shoot(20,count:2,fixedAngle:0,coolDown:1000),new Shoot(20,count:3,fixedAngle:0,coolDown:1000)),new Timed(250)","new Prioritize(new Timed(100,new Shoot(20,count:2,fixedAngle:0,coolDown:1000)),new Timed(300,new Shoot(20,count:3,fixedAngle:0,coolDown:1000)))");var priorityDoc=new BehaviorDocument(prioritySource,adapter,sourceFile:file);var priority=priorityDoc.AllNodes.Single(n=>n.Kind=="Prioritize");Check(adapter.ReadLoop(priorityDoc,priority) is{Children.Count:2}&&adapter.ReadLoop(priorityDoc,priority)!.Description.Contains("following tick"),"Prioritize authoring exposes actual child selection and deferred reselection semantics");
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,project with{Source=prioritySource,Identities=priorityDoc.Identities}))
{
    foreach(var expected in new[]{"1","1","-1","1"}){await native.AdvanceAsync(new(),100);Check(native.NativeState.Single(s=>s.Owner.EndsWith(".Prioritize")).Value==expected,"Native Prioritize selection retains current child until completion then reselects: "+expected);}
}
var resetSource=loopSource.Replace("new Timed(250,new Shoot(20,count:2,fixedAngle:0,coolDown:1000),new Shoot(20,count:3,fixedAngle:0,coolDown:1000)),new Timed(250)","new Timed(150,new Prioritize(new Timed(500,new Shoot(20,count:2,fixedAngle:0,coolDown:1000))),new Prioritize(new Timed(500,new Shoot(20,count:3,fixedAngle:0,coolDown:1000))))");var resetDoc=new BehaviorDocument(resetSource,adapter,sourceFile:file);await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,project with{Source=resetSource,Identities=resetDoc.Identities})){await native.AdvanceAsync(new(),100);Check(native.NativeState.Where(s=>s.Owner.EndsWith(".Prioritize")).Select(s=>s.Value).SequenceEqual(new[]{"0","-1"}),"Timed expiry resets only the Prioritize child visited at that expiry rather than resetting every child");}
Check(File.ReadAllText(file)==supplied,"Loop authoring, native state observation and replay preserve supplied game source");Console.WriteLine($"{checks} native stored-state checks passed.");
sealed class DangerousOwner {public override string ToString()=>throw new Exception("Do not execute owner formatting.");}
sealed class DangerousState {public int Counter=17;public string Text=new('x',300);public int[] Samples=[1,2,3];public int Danger=>throw new Exception("Do not execute state getters.");public override string ToString()=>throw new Exception("Do not execute state formatting.");}
