using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var sourceFile=Path.GetFullPath(adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile);var destination=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=sourceFile).SourceFile);
var original=File.ReadAllText(sourceFile);var originalDestination=File.ReadAllText(destination);
const string source="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioExtractionFixture=()=>Behav().Init("Cave Pirate Brawler",StudioExtractionCaller(3)).Init("Pirate Lieutenant",StudioExtractionCaller(5));
    static State StudioExtractionCaller(int count)=>new State(new State("loop",new Shoot(20,count:count,fixedAngle:0,coolDown:100),new State("nested"),new TimedTransition(100,"finish")),new State("finish"));
} }
""";
var project=new StudioProject{SourceFile=sourceFile,Source=source,EntityId="Cave Pirate Brawler",AdditionalSources=[new(destination,originalDestination)],Player=new(Invulnerable:true),Seed=7};
var scoped=adapter.ForProject(project);var doc=new BehaviorDocument(source,scoped,sourceFile:sourceFile);var body=doc.Definitions.Single(d=>d.Definition!.Label=="StudioExtractionCaller");var selected=body.Root.Children.First();var moved=selected.Descendants().ToArray();
project=project with{Identities=doc.Identities,CollapsedNodes=[selected.Id]};
var extracted=SourceDefinitionRefactor.Extract(adapter,project,sourceFile,selected.Id,destination,"StudioExtractedLoop");
Check(extracted.Changes.Count==2&&extracted.After.Source.Contains("StudioExtractedLoop(ref count)"),"Cross-file extraction replaces the exact expression with a captured-storage helper call");
var record=extracted.After.AdditionalSources.Single(s=>s.SourceFile==destination);var accepted=new BehaviorDocument(record.Source,adapter.ForProject(extracted.After),record.Identities,destination);var helper=accepted.Definitions.Single(d=>d.Definition!.Label=="StudioExtractedLoop");
Check(helper.Root.Id==selected.Id&&moved.All(n=>accepted.AllNodes.Any(a=>a.Id==n.Id)),"Extracted native phase and descendants retain their original identities in the new declaring body");
Check(!extracted.After.CollapsedNodes.Contains(selected.Id)&&record.CollapsedNodes.Contains(selected.Id),"Extraction moves collapse metadata to the actual owning source document");
Check(extracted.Before==project&&doc.Source==source&&extracted.After.Source.Contains("\"finish\""),"Planning preserves the accepted project and incoming phase destinations");
foreach(var change in extracted.Changes)
{
    var editing=new BehaviorDocument(change.Before,scoped,change.Identities,change.SourceFile);editing.ApplySourceRevision(editing.Revision,change.Before,change.After,change.AfterIdentities!,adapter.ForProject(extracted.After));
    Check(editing.Source==change.After&&editing.Undo()&&editing.Source==change.Before&&editing.Redo()&&editing.Source==change.After,"Source installation and Undo/Redo retain the complete extraction: "+Path.GetFileName(change.SourceFile));
}
Reject(()=>SourceDefinitionRefactor.Extract(adapter,project,sourceFile,selected.Id,destination,"StudioExtractionCaller"),"Existing member names reject before any extraction mutation");
Reject(()=>SourceDefinitionRefactor.Extract(adapter,project with{LockedNodes=[selected.Id]},sourceFile,selected.Id,destination,"StudioBlocked"),"A locked selected region rejects complete extraction");
Reject(()=>SourceDefinitionRefactor.Extract(adapter,project with{LockedNodes=[doc.Entities.First().Root.Id]},sourceFile,selected.Id,destination,"StudioBlockedCaller"),"A locked source caller protects its extracted body");
var same=SourceDefinitionRefactor.Extract(adapter,project,sourceFile,selected.Id,sourceFile,"StudioExtractedSameFile");
Check(same.Changes.Count==1&&same.After.Source.Contains("StudioExtractedSameFile(ref count)")&&same.After.Identities.Any(i=>i.Id==selected.Id),"Same-file extraction preserves ownership identities in one source transaction");
var mutationSource=source.Replace("count:count","count:count++").Replace("new State(\"finish\"));","new State(\"finish\"),new Shoot(20,count:count,fixedAngle:0,coolDown:100));");
var mutationProject=project with{Source=mutationSource,Identities=[],CollapsedNodes=[]};var mutationDoc=new BehaviorDocument(mutationSource,adapter.ForProject(mutationProject),sourceFile:sourceFile);var mutationNode=mutationDoc.Definitions.Single(d=>d.Definition!.Label=="StudioExtractionCaller").Root.Children.First();mutationProject=mutationProject with{Identities=mutationDoc.Identities};
var mutation=SourceDefinitionRefactor.Extract(adapter,mutationProject,sourceFile,mutationNode.Id,destination,"StudioExtractMutation");
Check(mutation.After.Source.Contains("StudioExtractMutation(ref count)")&&mutation.After.AdditionalSources.Single().Source.Contains("count++"),"Captured storage remains by reference when constructor evaluation mutates its source value");
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,mutation.After))
{
    await runtime.AdvanceAsync(new(),100);
    Check(runtime.Shots.Take(2).Select(s=>s.Count).SequenceEqual(new[]{3,4}),"Native constructor mutation still updates the following original action's input in source order");
}
var invalidProject=project with{Source=source.Replace("\"finish\"","\"destination-that-does-not-exist\"")};
var invalidSource=invalidProject.Source.Replace("new State(\"destination-that-does-not-exist\")","new State(\"finish\")");var invalidDoc=new BehaviorDocument(invalidSource,adapter.ForProject(invalidProject with{Source=invalidSource}),sourceFile:sourceFile);
Check(adapter.ForProject(invalidProject with{Source=invalidSource}).Validate(invalidDoc,project.EntityId).Any(d=>d.IsError&&d.Message.Contains("Missing transition target")),"Complete caller-scope validation still rejects a genuinely missing transition target");
var constantSource=source.Replace("static State StudioExtractionCaller(int count)=>", "static State StudioExtractionCaller(int count) { const double range=20.25; const int volley=3; return ").Replace("new Shoot(20,count:count,", "new Shoot(range,count:volley,").Replace("new State(\"finish\"));", "new State(\"finish\")); }");
var constantProject=project with{Source=constantSource,Identities=[],CollapsedNodes=[]};var constantDoc=new BehaviorDocument(constantSource,adapter.ForProject(constantProject),sourceFile:sourceFile);var constantNode=constantDoc.Definitions.Single(d=>d.Definition!.Label=="StudioExtractionCaller").Root.Children.First();constantProject=constantProject with{Identities=constantDoc.Identities};
var constantExtracted=SourceDefinitionRefactor.Extract(adapter,constantProject,sourceFile,constantNode.Id,destination,"StudioExtractConstants");
Check(constantExtracted.After.Source.Contains("StudioExtractConstants()")&&constantExtracted.After.AdditionalSources.Single().Source.Contains("const double range"),"Captured local constants keep their native constant type and avoid artificial reference parameters");
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,constantExtracted.After))
{await runtime.AdvanceAsync(new(),100);Check(runtime.Shots.Any(s=>s.Count==3),"Extracted local constants execute their actual native attack values");}
var output=Path.GetFullPath(".artifacts/extraction");Directory.CreateDirectory(output);var path=Path.Combine(output,"extracted.rbs");BehaviorDocument.Save(path,extracted.After);var loaded=BehaviorDocument.Load(path);
Check(loaded.AdditionalSources.Single().Source==record.Source&&loaded.AdditionalSources.Single().Identities.Any(i=>i.Id==selected.Id),"Saved extraction reopens its actual helper source and moved identities");
foreach(var entity in new[]{"Cave Pirate Brawler","Pirate Lieutenant"})
{
    await using var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,extracted.After with{EntityId=entity});await runtime.AdvanceAsync(new(),300);
    Check(runtime.Entities.Single().Phase=="finish"&&runtime.Shots.Any(s=>s.Count==(entity=="Cave Pirate Brawler"?3:5)),"Native extraction retains the caller's captured count, child hierarchy and transition: "+entity);
}
Check(File.ReadAllText(sourceFile)==original&&File.ReadAllText(destination)==originalDestination,"Extraction, source history, persistence and native playback leave supplied game files unchanged");
Console.WriteLine($"{checks} extraction checks passed.");
