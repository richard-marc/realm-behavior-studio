using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var file=adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile;var other=adapter.Catalog().First(e=>e.SourceFile!=file).SourceFile;
var supplied=File.ReadAllText(file);var suppliedOther=File.ReadAllText(other);
const string source="""
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioExternalFixture=()=>Behav().Init("Cave Pirate Brawler",StudioExternalRoot()).Init("Pirate Lieutenant",StudioExternalRoot());
} }
""";
const string helper="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioExternalRoot()=>new State("loop",StudioExternalShot,new TKR.WorldServer.logic.behaviors.Timed(1000,StudioExternalList));
    static readonly TKR.WorldServer.logic.behaviors.Shoot StudioExternalShot=new TKR.WorldServer.logic.behaviors.Shoot(20,count:2,fixedAngle:0,coolDown:100);
    static Behavior[] StudioExternalList=[new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100),new TKR.WorldServer.logic.behaviors.Shoot(20,count:5,fixedAngle:0,coolDown:100)];
} }
""";
var project=new StudioProject{SourceFile=file,Source=source,EntityId="Cave Pirate Brawler",AdditionalSources=[new(other,suppliedOther+helper)],Player=new(Invulnerable:true)};
var scoped=adapter.ForProject(project);var primary=new BehaviorDocument(source,scoped,sourceFile:file);
var caller=primary.Entities.Single(e=>e.EntityId==project.EntityId).Root;var references=primary.ExternalDefinitions(caller);
Check(references.Count==1&&Path.GetFullPath(references[0].SourceFile)==Path.GetFullPath(other)&&references[0].Label=="StudioExternalRoot","Native helper navigation locates the actual declaring file and body");
Check((suppliedOther+helper).Substring(references[0].Start,references[0].Length).StartsWith("new State(\"loop\""),"Cross-file navigation points to the retained source body rather than copying the call expression");
var dependency=new BehaviorDocument(project.AdditionalSources.Single().Source,scoped,sourceFile:other);
var root=dependency.Definitions.Single(d=>d.Definition!.Label=="StudioExternalRoot");var shot=dependency.Definitions.Single(d=>d.Definition!.Label=="StudioExternalShot");var list=dependency.Definitions.Single(d=>d.Definition!.Label=="StudioExternalList");
Check(root.Root.Start==references[0].Start&&root.Root.Length==references[0].Length,"Opening the target document binds the exact native definition identity");
Check(new[]{root,shot,list}.All(d=>d.Definition!.Encounters.SequenceEqual(new[]{"Cave Pirate Brawler","Pirate Lieutenant"})),"Cross-file ownership follows both native registrations through transitive helper and field references");
Check(dependency.ReferencedDefinitions(root.Root).Select(d=>d.EntityId).ToHashSet().SetEquals(new[]{shot.EntityId,list.EntityId}),"The external helper hierarchy links its real shared shot and collection definitions");
var third=adapter.Catalog().First(e=>e.SourceFile!=file&&e.SourceFile!=other).SourceFile;
var relayProject=project with{Source=source.Replace("StudioExternalRoot()","StudioExternalRelay()"),AdditionalSources=[..project.AdditionalSources,new(third,File.ReadAllText(third)+"\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static State StudioExternalRelay()=>StudioExternalRoot(); } }")]};
var relayAdapter=adapter.ForProject(relayProject);var relayBody=new BehaviorDocument(project.AdditionalSources[0].Source,relayAdapter,sourceFile:other);
Check(relayBody.Definitions.Single(d=>d.Definition!.Label=="StudioExternalRoot").Definition!.Encounters.SequenceEqual(new[]{"Cave Pirate Brawler","Pirate Lieutenant"}),"Reverse symbol discovery retains transitive native callers through a third source file");
var relayPrimary=new BehaviorDocument(relayProject.Source,relayAdapter,sourceFile:file);var relayReference=relayPrimary.ExternalDefinitions(relayPrimary.Entities.First().Root).Single();
Check(Path.GetFullPath(relayReference.SourceFile)==Path.GetFullPath(third)&&relayReference.Label=="StudioExternalRelay","Cross-file navigation follows the immediate native relay declaration rather than skipping source layers");
Check(!root.Definition!.CompletePhaseContents&&!shot.Definition!.CompletePhaseContents,"Known foreign references retain the explicit phase-reconciliation guard");
Reject(()=>dependency.RenameState(root.Root.Id,"renamed"),"A single-file phase rename cannot silently leave foreign native references stale");
var ids=dependency.Identities;dependency.Apply(new(dependency.Revision,"Shared projectile count",[new(shot.Root.Id,"count","7")]));
Check(dependency.Source.Contains("count:7")&&ids.All(i=>dependency.Identities.Any(next=>next.Id==i.Id)),"Typed external-body editing updates its original source and retains every body identity");
dependency.Undo();Check(dependency.Source==project.AdditionalSources.Single().Source,"External body Undo restores the exact complete dependency file");dependency.Redo();
dependency.LockedNodes.Add(shot.Root.Id);Reject(()=>dependency.Apply(new(dependency.Revision,"Locked helper",[new(shot.Root.Id,"count","9")])),"External definition locks reject shared-body edits atomically");dependency.LockedNodes.Clear();
var before=dependency.Source;Reject(()=>dependency.Apply(new(dependency.Revision,"Wrong native type",[new(shot.Root.Id,"count","\"wrong\"")])),"Native compiler typing rejects incompatible helper-body values");Check(dependency.Source==before,"Rejected helper edits retain the last valid source");
dependency.MoveNode(list.Root.Children[1].Id,-1);Check(dependency.Find(list.Root.Id).Children[0].Parameters.Single(p=>p.Name=="count").Expression=="5","Structural editing reorders the actual shared collection initializer");
var bad=dependency.Source.Replace("count:7","count:0");var badDoc=new BehaviorDocument(bad,scoped,sourceFile:other);
Check(scoped.Validate(badDoc,project.EntityId).Any(d=>d.IsError&&d.Message.Contains("Invalid value for count")),"Native parameter limits are reviewed in helper-only documents with known external callers");
project=project with{AdditionalSources=[new(other,dependency.Source){Identities=dependency.Identities,LockedNodes=[shot.Root.Id],CollapsedNodes=[root.Root.Id]}]};
var output=Path.GetFullPath(".artifacts/external-definitions");Directory.CreateDirectory(output);var path=Path.Combine(output,"external.rbs");BehaviorDocument.Save(path,project);var reopened=BehaviorDocument.Load(path);
var restored=new BehaviorDocument(reopened.AdditionalSources[0].Source,adapter.ForProject(reopened),reopened.AdditionalSources[0].Identities,other);
Check(ids.All(i=>restored.Identities.Any(n=>n.Id==i.Id))&&reopened.AdditionalSources[0].LockedNodes.Contains(shot.Root.Id)&&reopened.AdditionalSources[0].CollapsedNodes.Contains(root.Root.Id),"Save/reopen preserves external body identities, locks and collapse metadata");
foreach(var entity in new[]{"Cave Pirate Brawler","Pirate Lieutenant"})
{
    await using var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,project with{EntityId=entity});await runtime.AdvanceAsync(new(),100);
    Check(runtime.Shots.Select(s=>s.Count).SequenceEqual(new[]{7,5,3}),"The original native caller executes the edited external field and reordered collection for "+entity);
}
var throwing=project with{AdditionalSources=[new(other,(suppliedOther+helper).Replace("=>new State(\"loop\",StudioExternalShot,new TKR.WorldServer.logic.behaviors.Timed(1000,StudioExternalList))","=>throw new System.Exception(\"Do not execute discovery\")"))]};
var throwAdapter=adapter.ForProject(throwing);var throwDoc=new BehaviorDocument(throwing.AdditionalSources[0].Source,throwAdapter,sourceFile:other);
Check(throwDoc.Definitions.Single(d=>d.Definition!.Label=="StudioExternalRoot").Root.Kind=="Opaque","Cross-file discovery preserves a throwing helper body without executing it");
Check(File.ReadAllText(file)==supplied&&File.ReadAllText(other)==suppliedOther,"Visual discovery, edits, persistence and native playback leave both supplied files untouched");
Console.WriteLine($"{checks} external definition checks passed.");
