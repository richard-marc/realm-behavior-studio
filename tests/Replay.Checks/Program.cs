using System.Text.Json;
using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));
var checks=new List<string>(); var output=Path.GetFullPath(".artifacts/replay"); Directory.CreateDirectory(output);
void Check(bool value,string label){if(!value)throw new Exception(label); checks.Add(label); Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(ArgumentException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
async Task RejectReplay(EncounterReplay tape,string label){try{var run=await ReplayCursor.RestoreAsync(adapter,tape,tape.DurationMs);run.Session.Dispose();}catch(InvalidOperationException){Check(true,label);return;}throw new Exception("Expected replay rejection: "+label);}
var entry=adapter.Catalog().First(e=>e.Id=="Cave Pirate Brawler"); var original=File.ReadAllText(entry.SourceFile);
var sourceRoot=new BehaviorDocument(original,adapter,sourceFile:entry.SourceFile).Entities.First(e=>e.EntityId==entry.Id).Root;
var tree="new State(new State(\"opening\", new Follow(.4), new Shoot(20, count: 3, coolDown: new Cooldown(200, 50)), new TimedTransition(400, \"gate\")), new State(\"gate\", new HpLessTransition(.7, \"finish\")), new State(\"finish\", new Shoot(20, count: 7, coolDown: 100)))";
var source=original.Remove(sourceRoot.Start,sourceRoot.Length).Insert(sourceRoot.Start,tree);
var document=new BehaviorDocument(source,adapter,sourceFile:entry.SourceFile);
var project=new StudioProject{Source=source,SourceFile=entry.SourceFile,EntityId=entry.Id,Identities=document.Identities,Seed=7,Player=new(Defense:0)};
var identity=EncounterReplay.Identify(adapter,project);
var fingerprintRoot=Path.Combine(output,"fingerprint-fixture");var fingerprintDir=Path.Combine(fingerprintRoot,"TKR.WorldServer");Directory.CreateDirectory(fingerprintDir);
File.WriteAllText(Path.Combine(fingerprintDir,"Fixture.cs"),"class Fixture {}");
File.WriteAllText(Path.Combine(fingerprintDir,"TKR.WorldServer.csproj"),"<Project><ItemGroup><EmbeddedResource Include=\"fixture.js\" LogicalName=\"Fixture.Script\"/></ItemGroup></Project>");
var scriptPath=Path.Combine(fingerprintDir,"fixture.js");File.WriteAllText(scriptPath,"return 1;");
var firstFingerprint=EncounterReplay.NativeInputFingerprint(fingerprintRoot);File.WriteAllText(scriptPath,"return 2;");
Check(EncounterReplay.NativeInputFingerprint(fingerprintRoot)!=firstFingerprint,"Native compatibility fingerprints include actual declared embedded-script inputs, without relying on a reusable compiled bundle");
File.Delete(scriptPath);var missingScript=false;
try{EncounterReplay.NativeInputFingerprint(fingerprintRoot);}catch(IOException){missingScript=true;}
Check(missingScript,"Missing declared native script inputs fail compatibility inspection explicitly");
EncounterReplay tape;
using(var native=new EncounterSession(adapter,document,entry.Id,project.Seed,project.Player))
{
    native.Recording=new(native);
    native.Advance(new(1,0,true,120,64.5),155); native.Advance(new(0,1),345);
    native.SetBossHpFraction(.6); native.RemoveMinionsForTest(); native.Advance(new(-1,0),500);
    tape=EncounterReplay.Capture(project,null,identity,native);
    File.WriteAllText(Path.Combine(output,"fixture-events.json"),JsonSerializer.Serialize(new{native.Events,native.Shots,native.BossHp},BehaviorDocument.JsonOptions));
    Check(tape.Commands.Length==5 && tape.DurationMs==1000 && tape.Commands[2].Kind=="boss-hp" && tape.Commands[3].Kind=="remove-minions","Native journal retains ordered movement/firing and zero-time test overrides");
    Check(tape.Movements.Any(m=>m.Player&&m.DurationMs==5) && tape.Movements.Any(m=>!m.Player&&m.DurationMs==100&&m.PhaseNodeId!=null),"Recording retains actual player and enemy motion samples with native tick durations and known phase origins");
    Check(tape.Events.Any(e=>e.Kind=="transition"&&e.Message.Contains("finish"))&&native.Shots.Any(s=>s.Count==7),"The original recorded native encounter reaches its HP-gated phase and fires its real seven-shot fan");
}
var path=Path.Combine(output,"native.rbsreplay"); tape.Save(path); tape=EncounterReplay.Load(path);
Check(tape.Project.Identities.Count==document.Identities.Count && tape.Identity==identity,"Readable replay files retain exact source, identities, settings and compatibility stamps");
var restored=await ReplayCursor.RestoreAsync(adapter,tape,1000);
using(restored.Session)
{
    Check(restored.Session.Recording!.ObserveCurrent(restored.Session)==tape.Commands.Last().Result,"Full native replay reproduces its independent observable-state witness, including randomized cooldowns");
    Check(restored.Session.Events.SequenceEqual(tape.Events),"Native replay reproduces exact event order, actor IDs and source provenance");
    Check(restored.Session.Movements.SequenceEqual(tape.Movements),"Native replay reproduces all recorded player steps and enemy movement ticks exactly");
}
var partial=await ReplayCursor.RestoreAsync(adapter,tape,75);
using(partial.Session)
{
    Check(partial.Session.Ms==75 && partial.Session.Inputs.Single().DurationMs==75,"Seeking inside a recorded advance executes only the requested native five-millisecond prefix");
    await partial.AdvanceToAsync(1000);
    Check(partial.Session.Recording!.ObserveCurrent(partial.Session)==tape.Commands.Last().Result,"Continuing a split input reproduces the original final witness independently of command chunking");
    partial.Session.Advance(new(0,-1),100);
    var branch=EncounterReplay.Capture(project,null,identity,partial.Session);
    var replayBranch=await ReplayCursor.RestoreAsync(adapter,branch,branch.DurationMs);
    using(replayBranch.Session)Check(replayBranch.Session.Recording!.ObserveCurrent(replayBranch.Session)==branch.Commands.Last().Result,"A live branch from a replay position creates a genuinely replayable native input history");
}
var mismatchedShots=tape.Shots.ToArray(); mismatchedShots[0]=mismatchedShots[0] with{Count=999};
var prefixRejected=false;
try{var bad=await ReplayCursor.RestoreAsync(adapter,tape with{Shots=mismatchedShots},75);bad.Session.Dispose();}catch(InvalidOperationException){prefixRejected=true;}
Check(prefixRejected,"A divergent emission prefix rejects even when seeking before the next recorded state witness");
var brokenMovement=tape.Movements.ToArray();brokenMovement[0]=brokenMovement[0] with{ToX=brokenMovement[0].ToX+1};
var movementRejected=false;
try{var bad=await ReplayCursor.RestoreAsync(adapter,tape with{Movements=brokenMovement},75);bad.Session.Dispose();}catch(InvalidOperationException){movementRejected=true;}
Check(movementRejected,"A divergent native motion prefix rejects before the next recorded state witness");
var atGate=await ReplayCursor.RestoreAsync(adapter,tape,500);
using(atGate.Session)Check(atGate.Session.BossHp<atGate.Session.BossMaxHp && atGate.Session.Events.Count(e=>e.Kind=="override")==2,"Seeking to a boundary applies same-time HP and minion overrides in their original order");
var start=await ReplayCursor.RestoreAsync(adapter,tape,0);
using(start.Session)Check(start.Session.Ms==0 && start.Session.Events.SequenceEqual(tape.Events.Take(tape.Initial.Events)),"Seeking to zero restores actual initialization without running future inputs");
Reject(()=> (tape with{Version=2}).Validate(),"Unknown replay versions reject");
Reject(()=> (tape with{Project=tape.Project with{Version=2}}).Validate(),"Unknown embedded project versions reject");
Reject(()=> (tape with{Commands=[tape.Commands[0] with{Ms=5}]}).Validate(),"Gapped input histories reject before native initialization");
Reject(()=> (tape with{Commands=[tape.Commands[0] with{DurationMs=3}]}).Validate(),"Non-sample input durations reject");
Reject(()=> (tape with{Project=tape.Project with{Seed=8}}).Validate(),"Modified scenario settings invalidate their exact recording stamp");
Reject(()=> (tape with{Commands=[tape.Commands[0] with{Input=new(double.NaN)}]}).Validate(),"Non-finite replay inputs reject");
await RejectReplay(tape with{Identity=identity with{ResourceHash=new('0',64)}},"Resource fingerprint disagreement rejects native replay");
await RejectReplay(tape with{Identity=identity with{NativeSourcesHash=new('0',64)}},"Native source fingerprint disagreement rejects replay");
await RejectReplay(tape with{Identity=identity with{RuntimeHash=new('0',64)}},"Runtime fingerprint disagreement rejects replay");
await RejectReplay(tape with{Initial=tape.Initial with{Signature=new('0',64)}},"Initial native-state divergence rejects before seeking");
var broken=tape.Commands.ToArray(); broken[0]=broken[0] with{Result=broken[0].Result! with{Signature=new('0',64)}};
await RejectReplay(tape with{Commands=broken},"Per-command native-state divergence rejects instead of showing a certified replay");
using(var cancellation=new CancellationTokenSource())
{
    cancellation.Cancel();var cancelled=false;
    try{await ReplayCursor.RestoreAsync(adapter,tape,1000,cancellation:cancellation.Token);}catch(OperationCanceledException){cancelled=true;}
    Check(cancelled,"Cancellation before replay initialization leaves no run installed");
}
var forcedDocument=new BehaviorDocument(source,adapter,project.Identities,entry.SourceFile);
EncounterReplay forced;
using(var native=new EncounterSession(adapter,forcedDocument,entry.Id,7,project.Player,phase:"finish")){native.Recording=new(native);native.Advance(new(),200);forced=EncounterReplay.Capture(project,"finish",identity,native);}
var forcedRun=await ReplayCursor.RestoreAsync(adapter,forced,200);
using(forcedRun.Session)Check(forcedRun.Session.Events.Any(e=>e.Kind=="forced-phase")&&forcedRun.Session.Recording!.ObserveCurrent(forcedRun.Session)==forced.Commands.Last().Result,"Forced-phase replay preserves its genuine initialization and bypass provenance");
var helper="\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static State StudioReplayRoot(int count) => new State(new Follow(.4), new Shoot(20, count: count, coolDown: 100)); } }";
var compiledSource=original.Remove(sourceRoot.Start,sourceRoot.Length).Insert(sourceRoot.Start,"StudioReplayRoot(6)")+helper;
var compiledDocument=new BehaviorDocument(compiledSource,adapter,sourceFile:entry.SourceFile);
var compiledProject=project with{Source=compiledSource,Identities=compiledDocument.Identities};
var prepared=await CompiledRuntime.PrepareAsync(adapter.ServerRoot,compiledProject);
EncounterReplay compiledTape;int originalPid;
await using(var runtime=await prepared.StartAsync()){originalPid=runtime.ProcessId;runtime.Recording=new(runtime);await runtime.AdvanceAsync(new(1,0,true),200);await runtime.SetBossHpAsync(.8);await runtime.AdvanceAsync(new(),300);compiledTape=EncounterReplay.Capture(compiledProject,null,EncounterReplay.Identify(adapter,compiledProject,runtime.Stamp.BuildId),runtime);}
var compiledReplay=await ReplayCursor.RestoreAsync(adapter,compiledTape,75,prepared);
using(compiledReplay.Session)
{
    Check(compiledReplay.Session is CompiledRuntime worker && worker.ProcessId!=originalPid && worker.Stamp.BuildId==compiledTape.Identity.CompiledBuildId,"Compiled seeking uses a fresh native process with the exact recorded build");
    await compiledReplay.AdvanceToAsync(500);
    Check(compiledReplay.Session.Recording!.ObserveCurrent(compiledReplay.Session)==compiledTape.Commands.Last().Result && compiledReplay.Session.Shots.Any(s=>s.Count==6),"Compiled replay reproduces helper execution, player firing and recorded HP overrides");
    Check(compiledReplay.Session.Movements.SequenceEqual(compiledTape.Movements)&&compiledTape.Movements.Any(m=>!m.Player)&&compiledTape.Movements.Any(m=>m.Player),"Compiled replay transports and reproduces native actor movement independently of parent rendering");
}
using(var cancellation=new CancellationTokenSource())
{
    var cancelled=false;
    try{await ReplayCursor.RestoreAsync(adapter,compiledTape,500,prepared,new ImmediateProgress<long>(ms=>{if(ms>=200)cancellation.Cancel();}),cancellation.Token);}
    catch(OperationCanceledException){cancelled=true;}
    Check(cancelled,"Cancellation during compiled native replay interrupts an already executing recording");
}
Check(File.ReadAllText(entry.SourceFile)==original,"Replay recording, seeking, branches and file persistence leave supplied game source untouched");
File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{checks},BehaviorDocument.JsonOptions));
Console.WriteLine($"{checks.Count} native replay checks passed.");
sealed class ImmediateProgress<T>(Action<T> action):IProgress<T>{public void Report(T value)=>action(value);}
