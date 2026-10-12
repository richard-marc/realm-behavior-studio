using System.Text.Json;
using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label,string? message=null){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){if(message!=null&&!e.Message.Contains(message))throw;Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var primary=Path.GetFullPath(adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile);
var origin=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=primary).SourceFile);
var destination=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=primary&&Path.GetFullPath(e.SourceFile)!=origin).SourceFile);
var originals=new[]{primary,origin,destination}.ToDictionary(p=>p,File.ReadAllText);
const string source="""
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    const int StudioCopyCount=3;
    private _ StudioCopyFixture=()=>Behav().Init("Cave Pirate Brawler",StudioCopyCaller()).Init("Pirate Lieutenant",StudioCopyCaller());
    static State StudioCopyCaller()=>new State(StudioCopyShared(),StudioCopyDestination(),new State("loop-copy"),new State("nested-copy"),new State("finish"));
} }
""";
const string shared="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioCopyShared()=>new State(new State("loop",new TKR.WorldServer.logic.behaviors.Shoot(20,count:StudioCopyCount,fixedAngle:0,coolDown:100),new TKR.WorldServer.logic.transitions.TimedTransition(100,"nested"),new State("nested",new TKR.WorldServer.logic.transitions.TimedRandomTransition(100,false,new[]{"loop",/* weighted */"loop","finish"}))),new State("keep"));
} }
""";
const string target="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioCopyDestination()=>new State("destination");
} }
""";
var project=new StudioProject{SourceFile=primary,Source=source,EntityId="Cave Pirate Brawler",AdditionalSources=[new(origin,originals[origin]+shared),new(destination,originals[destination]+target)],Seed=7,Player=new(Invulnerable:true)};
var scoped=adapter.ForProject(project);var caller=new BehaviorDocument(source,scoped,sourceFile:primary);var from=new BehaviorDocument(originals[origin]+shared,scoped,sourceFile:origin);var to=new BehaviorDocument(originals[destination]+target,scoped,sourceFile:destination);
var body=from.Definitions.Single(d=>d.Definition!.Label=="StudioCopyShared");var branch=body.Root.Children.Single(n=>from.StateName(n.Id)=="loop");var root=to.Definitions.Single(d=>d.Definition!.Label=="StudioCopyDestination").Root;
project=project with{Identities=caller.Identities,AdditionalSources=[project.AdditionalSources[0] with{Identities=from.Identities,CollapsedNodes=[branch.Id]},project.AdditionalSources[1] with{Identities=to.Identities}]};
var clipboard=from.CopyNodes([branch.Id,branch.Children.Last().Id]);
Check(clipboard.Expressions.Count==1&&clipboard.Symbols.Any(s=>s.Signature.Contains("StudioCopyCount")),"Clipboard normalizes nested selection and retains actual source symbol bindings");
clipboard=JsonSerializer.Deserialize<BehaviorClipboard>(JsonSerializer.Serialize(clipboard,BehaviorDocument.JsonOptions),BehaviorDocument.JsonOptions)!;
var pasted=SourcePhaseRefactor.Paste(adapter,project,destination,root.Id,0,clipboard);var record=pasted.After.AdditionalSources.Single(s=>s.SourceFile==destination);var accepted=new BehaviorDocument(record.Source,adapter.ForProject(pasted.After),record.Identities,destination);var copy=accepted.Find(pasted.Selection.Single());
Check(pasted.Changes.Count==1&&pasted.After.Source==source&&pasted.After.AdditionalSources.Single(s=>s.SourceFile==origin).Source==from.Source,"Cross-file copy changes only its actual destination body and preserves every original caller/source");
Check(accepted.StateName(copy.Id)=="loop-copy-2"&&copy.Children.Any(n=>accepted.StateName(n.Id)=="nested-copy-2"),"Copied nested phase names avoid collisions anywhere in the destination callers");
Check(copy.Children.Single(n=>n.Kind=="TimedTransition").Parameters.Any(p=>p.Expression=="\"nested-copy-2\"")&&copy.Descendants().Single(n=>n.Kind=="TimedRandomTransition").Parameters.Any(p=>p.Expression.Contains("loop-copy-2")),"Copy remaps nested and repeated internal references within the new fragment");
var choice=copy.Descendants().Single(n=>n.Kind=="TimedRandomTransition");var targets=choice.Parameters.Single(p=>p.Name.StartsWith("states")).Expression;
Check(BehaviorDocument.PhaseLiterals(targets)!.Select(v=>v.Name).SequenceEqual(new[]{"loop-copy-2","loop-copy-2","finish"}),"Repeated random-choice weighting and external destinations retain native meaning");
Check(copy.Descendants().All(n=>!from.AllNodes.Any(o=>o.Id==n.Id))&&accepted.Find(root.Id).Id==root.Id&&to.Identities.All(i=>accepted.Identities.Any(n=>n.Id==i.Id)),"Copied nodes receive fresh identities while all destination identities remain stable");
Check(pasted.Before==project&&from.Source==originals[origin]+shared&&to.Source==originals[destination]+target&&pasted.After.AdditionalSources.Single(s=>s.SourceFile==origin).CollapsedNodes.Contains(branch.Id),"Planning is immutable and does not transfer origin collapse state to independent copies");
var change=pasted.Changes.Single();to.ApplySourceRevision(to.Revision,change.Before,change.After,change.AfterIdentities!,adapter.ForProject(pasted.After));
Check(to.Undo()&&to.Source==change.Before&&to.Redo()&&to.Source==change.After,"Accepted cross-file paste retains exact per-document structural history");
Reject(()=>SourcePhaseRefactor.Paste(adapter,project with{LockedNodes=[caller.Entities.First().Root.Id]},destination,root.Id,0,clipboard),"A locked destination caller blocks the complete copy transaction");
Reject(()=>SourcePhaseRefactor.Paste(adapter,project with{AdditionalSources=[project.AdditionalSources[0],project.AdditionalSources[1] with{LockedNodes=[root.Id]}]},destination,root.Id,0,clipboard),"A locked destination body blocks copying before any source changes");
Reject(()=>SourcePhaseRefactor.Paste(adapter,project,destination,root.Id,0,clipboard with{Expressions=clipboard.Expressions.Select(e=>e.Replace("\"finish\"","\"missing\"")).ToArray(),Symbols=[]}),"A copied external transition cannot target a missing destination caller phase");
var shadowSource=(originals[destination]+target).Replace("StudioCopyDestination()=>","StudioCopyDestination(int StudioCopyCount=11)=>");var shadowProject=project with{AdditionalSources=[project.AdditionalSources[0],new(destination,shadowSource)]};var shadow=new BehaviorDocument(shadowSource,adapter.ForProject(shadowProject),sourceFile:destination);var shadowRoot=shadow.Definitions.Single(d=>d.Definition!.Label=="StudioCopyDestination").Root;
Reject(()=>shadow.PasteNodes(shadowRoot.Id,0,clipboard),"A copied constant cannot silently rebind to a same-named destination parameter","bind differently");
Check(shadow.Source==shadowSource,"Rejected lexical rebinding leaves destination source and history unchanged");
Reject(()=>SourcePhaseRefactor.Paste(adapter,project,destination,root.Id,0,new(1,["StudioCopyShared()"])),"A shared phase factory requires independent definition ownership before duplication");
var linkedProject=project with{Source=source+"\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static State StudioCopyLink=>throw new System.Exception(\"must not evaluate while copying\"); } }"};
var linked=SourcePhaseRefactor.Paste(adapter,linkedProject,destination,root.Id,0,new(1,["StudioCopyLink"]));
var linkedRecord=linked.After.AdditionalSources.Single(s=>s.SourceFile==destination);var linkedDocument=new BehaviorDocument(linkedRecord.Source,adapter.ForProject(linked.After),linkedRecord.Identities,destination);
Check(linkedDocument.Find(linked.Selection.Single()).Kind=="Reference"&&linkedDocument.Expression(linkedDocument.Find(linked.Selection.Single()))=="StudioCopyLink","Explicit linked-reference copying retains its source value without executing a getter or inventing independent phases");
Reject(()=>SourcePhaseRefactor.Paste(adapter,project,destination,root.Id,0,clipboard with{Symbols=[new(0,-1,1,"bad")]}),"Malformed clipboard binding ranges reject before source mutation");
var output=Path.GetFullPath(".artifacts/workspace-copy");Directory.CreateDirectory(output);var path=Path.Combine(output,"copied.rbs");BehaviorDocument.Save(path,pasted.After);var loaded=BehaviorDocument.Load(path);
Check(loaded.AdditionalSources.Select(s=>s.Source).SequenceEqual(pasted.After.AdditionalSources.Select(s=>s.Source)),"Saved workspace retains the complete cross-file copy snapshot");
foreach(var entity in new[]{"Cave Pirate Brawler","Pirate Lieutenant"})
{
    await using var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,pasted.After with{EntityId=entity},phase:"nested-copy-2");
    Check(runtime.Entities.Single().Phase=="nested-copy-2","Native phase test enters the independent copied child: "+entity);
    await runtime.AdvanceAsync(new(),300);
    Check(runtime.Entities.Single().Phase is "nested-copy-2" or "finish"&&runtime.Shots.Any(s=>s.Count==3),"Native copied choice stays in the copied hierarchy or its retained external target and uses the original bound attack count: "+entity);
}
Check(originals.All(p=>File.ReadAllText(p.Key)==p.Value),"Cross-file copy, history, persistence and native playback leave supplied game files untouched");
Console.WriteLine($"{checks} workspace copy checks passed.");
