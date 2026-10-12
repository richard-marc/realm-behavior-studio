using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(ArgumentException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var entry=adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler");var supplied=File.ReadAllText(entry.SourceFile);
var second=adapter.Catalog().First(e=>e.SourceFile!=entry.SourceFile).SourceFile;var suppliedSecond=File.ReadAllText(second);
var original=new BehaviorDocument(supplied,adapter,sourceFile:entry.SourceFile);var root=original.Entities.Single(e=>e.EntityId==entry.Id).Root;
var source=supplied.Remove(root.Start,root.Length).Insert(root.Start,"StudioSourceSetRoot()");
const string helper="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioSourceSetRoot() => new State(new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100));
} }
""";
var dependency=new StudioSourceDocument(second,suppliedSecond+helper);
var project=new StudioProject{SourceFile=entry.SourceFile,Source=source,EntityId=entry.Id,AdditionalSources=[dependency],Player=new(Invulnerable:true),Seed=7};
var scoped=adapter.ForProject(project);var document=new BehaviorDocument(source,scoped,sourceFile:entry.SourceFile);
Check(!document.SourceDiagnostics().Any(d=>d.IsError),"A native caller resolves its edited helper from another original source file");
Check(scoped.NeedsCompiledRuntime(document,entry.Id),"A multi-file project always uses the complete original compiler");
Check(!document.Definitions.Any(d=>d.Definition!.Label=="StudioSourceSetRoot"),"External helper bodies retain their explicit visual-authoring boundary");
var literal=project with{Source=supplied};var literalAdapter=adapter.ForProject(literal);
Check(literalAdapter.NeedsCompiledRuntime(new(supplied,literalAdapter,sourceFile:entry.SourceFile),entry.Id),"Even a literal host compiles edited dependencies that spawned actors may consume");
Check(!scoped.ForProject(literal with{AdditionalSources=[]}).HasAdditionalSources,"Opening a single-file project removes previous dependency overlays");
var invalid=project with{AdditionalSources=[dependency with{Source=dependency.Source.Replace("count:3","count:unknownStudioCount")}]};
Check(new BehaviorDocument(source,adapter.ForProject(invalid),sourceFile:entry.SourceFile).SourceDiagnostics().Any(d=>d.IsError&&d.Message.Contains(Path.GetFileName(second))),"Compiler errors in dependency files are reported at their own source locations");
Reject(()=>SourceVerification.SourceSet(adapter.ServerRoot,project with{AdditionalSources=[dependency,dependency with{SourceFile=second.ToUpperInvariant()}]}),"Case-insensitive duplicate source paths are rejected");
Reject(()=>SourceVerification.SourceSet(adapter.ServerRoot,project with{AdditionalSources=[dependency with{SourceFile=entry.SourceFile}]}),"A dependency cannot replace the primary source twice");
Reject(()=>SourceVerification.SourceSet(adapter.ServerRoot,project with{AdditionalSources=[dependency with{SourceFile=Path.Combine(adapter.ServerRoot,"../outside.cs")}]}),"Sources outside the original native compilation are rejected");
Reject(()=>SourceVerification.SourceSet(adapter.ServerRoot,project with{AdditionalSources=[dependency with{SourceFile=Path.Combine(adapter.ServerRoot,"TKR.WorldServer/not-original.cs")}]}),"A nonexistent server file cannot masquerade as an original dependency");
Reject(()=>(project with{AdditionalSources=null!}).ValidateSourceSet(),"Null source document collections are rejected");
Reject(()=>(project with{AdditionalSources=[dependency with{Source=new string('x',4_000_001)}]}).ValidateSourceSet(),"Oversized dependency source is rejected before compilation");
Reject(()=>(project with{AdditionalSources=Enumerable.Repeat(dependency,129).ToList()}).ValidateSourceSet(),"Oversized source document counts are rejected");
var output=Path.GetFullPath(".artifacts/source-set");Directory.CreateDirectory(output);
var id=Guid.NewGuid();var metadata=dependency with{Identities=[new(id,"retained","Opaque")],LockedNodes=[id],CollapsedNodes=[id]};
BehaviorDocument.Save(Path.Combine(output,"multi.rbs"),project with{AdditionalSources=[metadata]});var loaded=BehaviorDocument.Load(Path.Combine(output,"multi.rbs"));
Check(loaded.Source==source&&loaded.AdditionalSources.Single().Source==metadata.Source&&loaded.AdditionalSources.Single().Identities.Single().Id==id&&loaded.AdditionalSources.Single().LockedNodes.SequenceEqual(new[]{id})&&loaded.AdditionalSources.Single().CollapsedNodes.SequenceEqual(new[]{id}),"Project reopen retains both complete files and dependency identity/lock/collapse metadata");
BehaviorDocument.Save(Path.Combine(output,"legacy.rbs"),new StudioProject{Source="class Standalone {}"});
Check(BehaviorDocument.Load(Path.Combine(output,"legacy.rbs")).AdditionalSources.Count==0,"Older single-file projects with no original path still round-trip");
var edited=project with{AdditionalSources=[dependency with{Source=dependency.Source.Replace("count:3","count:7")}]};
Check(SourceVerification.EditedSourceHash(project)!=SourceVerification.EditedSourceHash(edited),"Artifact provenance changes when only a dependency changes");
Check(SourceVerification.EditedSourceHash(project)==SourceVerification.EditedSourceHash(project with{AdditionalSources=[dependency with{Identities=[new(id,"unused","Opaque")]}]}),"Editor-only metadata does not change source provenance");
var prepared=await CompiledRuntime.PrepareAsync(adapter.ServerRoot,project);
await using var runtime=await prepared.StartAsync();runtime.Recording=new(runtime);await runtime.AdvanceAsync(new(),100);
Check(runtime.Shots.Single().Count==3,"The native loader executes the edited cross-file helper's three-projectile volley");
var identity=EncounterReplay.Identify(adapter,project,prepared.Stamp.BuildId);var tape=EncounterReplay.Capture(project,null,identity,runtime);
var tapePath=Path.Combine(output,"multi.replay.json");tape.Save(tapePath);var savedTape=EncounterReplay.Load(tapePath);
Check(savedTape.Project.AdditionalSources.Single().Source==dependency.Source,"Encounter recordings carry the exact dependency source snapshot");
var replay=await ReplayCursor.RestoreAsync(adapter,savedTape,100,prepared);
try{Check(replay.Session.Shots.Single().Count==3,"Replay restores the same compiled multi-file volley and verified witness");}finally{replay.Session.Dispose();}
var next=await CompiledRuntime.PrepareAsync(adapter.ServerRoot,edited);
Check(prepared.Stamp.BuildId!=next.Stamp.BuildId&&prepared.Stamp.Source.SourceHash!=next.Stamp.Source.SourceHash&&prepared.Stamp.Source.EditedSourceHash!=next.Stamp.Source.EditedSourceHash,"Dependency edits invalidate the emitted assembly, full source stamp and compiled build identity");
await using var editedRuntime=await next.StartAsync();await editedRuntime.AdvanceAsync(new(),100);
Check(editedRuntime.Shots.Single().Count==7,"Editing only the dependency produces the native seven-projectile volley");
Check(File.ReadAllText(entry.SourceFile)==supplied&&File.ReadAllText(second)==suppliedSecond,"Multi-file authoring, compilation and playback never modify supplied game files");
Console.WriteLine($"Passed {checks} source-set checks.");
