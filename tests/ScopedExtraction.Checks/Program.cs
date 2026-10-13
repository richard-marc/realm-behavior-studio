using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var primary=Path.GetFullPath(adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile);var destination=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=primary).SourceFile);var originals=new[]{primary,destination}.ToDictionary(p=>p,File.ReadAllText);
string Scoped(string source)
{
    var root=CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();var ns=root.Members.OfType<NamespaceDeclarationSyntax>().Single();
    return root.ReplaceNode(ns,SyntaxFactory.FileScopedNamespaceDeclaration(ns.Name).WithUsings(ns.Usings).WithExterns(ns.Externs).WithMembers(ns.Members)).NormalizeWhitespace().ToFullString();
}
const string source="""
using Fire=TKR.WorldServer.logic.behaviors.Shoot;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioScopedExtract=()=>Behav().Init("Cave Pirate Brawler",StudioScopedCaller(3));
    static State StudioScopedCaller(int count)=>new State("loop",new Fire(20,count:count,fixedAngle:0,coolDown:100));
} }
""";
var destinationSource="using Fire=TKR.WorldServer.logic.behaviors.Shoot;\n"+Scoped(originals[destination]).Replace("namespace TKR.WorldServer.logic;","namespace TKR./* scoped namespace */WorldServer.logic;");
var project=new StudioProject{SourceFile=primary,Source=source,EntityId="Cave Pirate Brawler",AdditionalSources=[new(destination,destinationSource)],Player=new(Invulnerable:true),Seed=7};var scoped=adapter.ForProject(project);var doc=new BehaviorDocument(source,scoped,sourceFile:primary);var selected=doc.Definitions.Single(d=>d.Definition!.Label=="StudioScopedCaller").Root.Children.Single();project=project with{Identities=doc.Identities,CollapsedNodes=[selected.Id]};
var extracted=SourceDefinitionRefactor.Extract(adapter,project,primary,selected.Id,destination,"StudioScopedFire");var result=extracted.After.AdditionalSources.Single(s=>s.SourceFile==destination);
Check(extracted.Changes.Count==2&&extracted.After.Source.Contains("StudioScopedFire(ref count)"),"Cross-file extraction introduces a captured-storage helper into a file-scoped destination");
Check(result.Source.StartsWith(destinationSource,StringComparison.Ordinal)&&CSharpSyntaxTree.ParseText(result.Source).GetRoot().DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().Count()==1&&!result.Source.Contains("namespace TKR.WorldServer.logic {"),"The original scoped file and imports stay exact while the same-type helper is appended");
var accepted=new BehaviorDocument(result.Source,adapter.ForProject(extracted.After),result.Identities,destination);var helper=accepted.Definitions.Single(d=>d.Definition!.Label=="StudioScopedFire");
Check(helper.Root.Id==selected.Id&&helper.Root.Kind=="Shoot"&&accepted.Expression(helper.Root).StartsWith("new Fire("),"Scoped helper alias binding and original native action identity remain intact");
Check(result.CollapsedNodes.Contains(selected.Id)&&!extracted.After.CollapsedNodes.Contains(selected.Id),"Scoped extraction moves collapse ownership into the actual helper source");
foreach(var change in extracted.Changes)
{
    var editing=new BehaviorDocument(change.Before,scoped,change.Identities,change.SourceFile);editing.ApplySourceRevision(editing.Revision,change.Before,change.After,change.AfterIdentities!,adapter.ForProject(extracted.After));
    Check(editing.RestoreWorkspaceHistory(false,scoped)&&editing.Source==change.Before&&editing.RestoreWorkspaceHistory(true,adapter.ForProject(extracted.After))&&change.AfterIdentities!.All(i=>editing.Identities.Any(n=>n.Id==i.Id)),"Scoped extraction history restores source and every identity: "+Path.GetFileName(change.SourceFile));
}
var own=project with{Source=Scoped(source),Identities=[],CollapsedNodes=[]};var ownDoc=new BehaviorDocument(own.Source,adapter.ForProject(own),sourceFile:primary);var ownNode=ownDoc.Definitions.Single(d=>d.Definition!.Label=="StudioScopedCaller").Root.Children.Single();own=own with{Identities=ownDoc.Identities};
var same=SourceDefinitionRefactor.Extract(adapter,own,primary,ownNode.Id,primary,"StudioSameScopedFire");
Check(same.Changes.Count==1&&CSharpSyntaxTree.ParseText(same.After.Source).GetRoot().DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().Count()==1&&same.After.Source.Contains("using Fire = TKR.WorldServer.logic.behaviors.Shoot;")&&same.After.Source.Contains("StudioSameScopedFire(ref count)")&&same.After.Identities.Any(i=>i.Id==ownNode.Id),"Same-file scoped extraction preserves alias scope, captured storage and the moved identity");
Check(!adapter.ForProject(same.After).Validate(new BehaviorDocument(same.After.Source,adapter.ForProject(same.After),same.After.Identities,primary),project.EntityId).Any(d=>d.IsError),"The same-file scoped partial helper compiles against the complete native source set");
Reject(()=>SourceDefinitionRefactor.Extract(adapter,project with{AdditionalSources=[new(destination,destinationSource.Replace("namespace TKR./* scoped namespace */WorldServer.logic;","namespace StudioWrongScope;"))]},primary,selected.Id,destination,"StudioWrongNamespace"),"A different file-scoped namespace rejects extraction without creating a different source type");
Reject(()=>SourceDefinitionRefactor.Extract(adapter,project with{AdditionalSources=[new(destination,destinationSource.Replace("using Fire=TKR.WorldServer.logic.behaviors.Shoot;","using Fire=TKR.WorldServer.logic.behaviors.Wander;"))]},primary,selected.Id,destination,"StudioWrongAlias"),"A destination alias with different native binding rejects rather than altering existing imports");
Reject(()=>SourceDefinitionRefactor.Extract(adapter,project with{AdditionalSources=[project.AdditionalSources[0] with{LockedNodes=[Guid.NewGuid()]}]},primary,selected.Id,destination,"StudioLockedScope"),"Destination locks protect introducing a scoped helper declaration");
foreach(var playback in new[]{extracted.After,same.After})
{
    await using var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,playback);await runtime.AdvanceAsync(new(),100);
    Check(runtime.Shots.Any(s=>s.Count==3),"Native scoped-helper execution retains the actual aliased Shoot constructor and captured count");
}
var output=Path.GetFullPath(".artifacts/scoped-extraction");Directory.CreateDirectory(output);var path=Path.Combine(output,"scoped-extraction.rbs");BehaviorDocument.Save(path,extracted.After);
Check(BehaviorDocument.Load(path).AdditionalSources.Single(s=>s.SourceFile==destination).Source==result.Source,"Scoped destination source and helper persist exactly through save/reopen");
Check(originals.All(p=>File.ReadAllText(p.Key)==p.Value),"Scoped extraction, locks, history and native execution leave supplied files unchanged");Console.WriteLine($"{checks} scoped extraction checks passed.");
