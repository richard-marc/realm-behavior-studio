using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var primary=Path.GetFullPath(adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile);var helper=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=primary).SourceFile);var third=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=primary&&Path.GetFullPath(e.SourceFile)!=helper).SourceFile);
var originals=new[]{primary,helper,third}.ToDictionary(p=>p,File.ReadAllText);
const string source="""
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioReferenceClone=()=>Behav().Init("Cave Pirate Brawler",StudioReferenceCloneCaller());
    static State StudioReferenceCloneCaller()=>new State(StudioReferenceValue,new State("loop-copy"),new State("finish"));
} }
""";
const string field="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static readonly TKR.WorldServer.logic.behaviors.Shoot StudioReferenceAttack=new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100);
    static readonly State StudioReferenceValue=new State("loop",StudioReferenceAttack,new State("nested"));
} }
""";
const string attack="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static State StudioReferenceUnused=>new State("unused-reference-source");
} }
""";
var project=new StudioProject{SourceFile=primary,Source=source,EntityId="Cave Pirate Brawler",AdditionalSources=[new(helper,originals[helper]+field),new(third,originals[third]+attack)],Player=new(Invulnerable:true),Seed=7};
var scoped=adapter.ForProject(project);var doc=new BehaviorDocument(source,scoped,sourceFile:primary);var root=doc.Definitions.Single(d=>d.Definition!.Label=="StudioReferenceCloneCaller").Root;var reference=root.Children.First();project=project with{Identities=doc.Identities};
var cloned=SourcePhaseRefactor.CloneDefinition(adapter,project,primary,reference.Id,"StudioIndependentReference");
var accepted=new BehaviorDocument(cloned.After.Source,adapter.ForProject(cloned.After),cloned.After.Identities,primary);var newReference=accepted.Find(cloned.Selection.Single());
Check(cloned.Changes.Count==2&&newReference.Kind=="Reference"&&accepted.Expression(newReference)=="StudioIndependentReference", "A referenced State field acquires independent child-value declarations and a new reference");
Check(accepted.Find(reference.Id).Id==reference.Id&&accepted.Expression(accepted.Find(reference.Id))=="StudioReferenceValue"&&newReference.Id!=reference.Id,"Field cloning retains the original reference and allocates an independent callsite identity");
var helperRecord=cloned.After.AdditionalSources.Single(s=>s.SourceFile==helper);var fieldDoc=new BehaviorDocument(helperRecord.Source,adapter.ForProject(cloned.After),helperRecord.Identities,helper);var independent=fieldDoc.Definitions.Single(d=>d.Definition!.Label=="StudioIndependentReference");
Check(fieldDoc.StateName(independent.Root.Id)=="loop-copy-2"&&independent.Root.Children.Any(n=>fieldDoc.StateName(n.Id)=="nested-copy"),"Cloned field phase names avoid existing caller destinations and include independent nested names");
var clonedFields=Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(helperRecord.Source).GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>().Select(v=>v.Identifier.ValueText).ToList();
Check(clonedFields.IndexOf("StudioReferenceAttackCopy")>=0&&clonedFields.IndexOf("StudioReferenceAttackCopy")<clonedFields.IndexOf("StudioIndependentReference"),"A field clone initializes its independent Behavior value before the dependent State value");
foreach(var change in cloned.Changes)
{
    var editing=new BehaviorDocument(change.Before,scoped,change.Identities,change.SourceFile);editing.ApplySourceRevision(editing.Revision,change.Before,change.After,change.AfterIdentities!,adapter.ForProject(cloned.After));
    Check(editing.RestoreWorkspaceHistory(false,scoped)&&editing.RestoreWorkspaceHistory(true,adapter.ForProject(cloned.After))&&change.AfterIdentities!.All(i=>editing.Identities.Any(n=>n.Id==i.Id)),"Reference clone history preserves all original/new identities: "+Path.GetFileName(change.SourceFile));
}
var getter=project with{AdditionalSources=[project.AdditionalSources[0] with{Source=(originals[helper]+field).Replace("static readonly State StudioReferenceValue=","static State StudioReferenceValue=>")},project.AdditionalSources[1]]};
var getterClone=SourcePhaseRefactor.CloneDefinition(adapter,getter,primary,reference.Id,"StudioIndependentGetter");
Check(getterClone.After.AdditionalSources.Single(s=>s.SourceFile==helper).Source.Contains("StudioIndependentGetter")&&getterClone.After.Source.Contains("StudioIndependentGetter"),"Expression-bodied native getters clone as getters without evaluation or a fabricated factory call");
var blocked=project with{LockedNodes=[reference.Id]};Reject(()=>SourcePhaseRefactor.CloneDefinition(adapter,blocked,primary,reference.Id,"StudioBlockedReference"),"A locked reference prevents the entire independent value clone");
Reject(()=>SourcePhaseRefactor.CloneDefinition(adapter,project,primary,reference.Id,"StudioReferenceAttack"),"A cloned reference name cannot collide with another native source member");
var qualified=project with{Source=source.Replace("new State(StudioReferenceValue,","new State(BehaviorDb.StudioReferenceValue,")};var qualifiedDoc=new BehaviorDocument(qualified.Source,adapter.ForProject(qualified),sourceFile:primary);var qualifiedReference=qualifiedDoc.Definitions.Single(d=>d.Definition!.Label=="StudioReferenceCloneCaller").Root.Children.First();qualified=qualified with{Identities=qualifiedDoc.Identities};
var qualifiedClone=SourcePhaseRefactor.CloneDefinition(adapter,qualified,primary,qualifiedReference.Id,"StudioQualifiedIndependent");
Check(qualifiedClone.After.Source.Contains("BehaviorDb.StudioQualifiedIndependent"),"Qualified source member references preserve their actual access context when cloned");
var output=Path.GetFullPath(".artifacts/reference-clone");Directory.CreateDirectory(output);var path=Path.Combine(output,"reference-clone.rbs");BehaviorDocument.Save(path,cloned.After);var reopened=BehaviorDocument.Load(path);
Check(reopened.AdditionalSources.Select(s=>s.Source).SequenceEqual(cloned.After.AdditionalSources.Select(s=>s.Source)),"Saving a reference clone retains its complete independent source closure");
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,cloned.After,phase:"loop-copy-2"))
{await runtime.AdvanceAsync(new(),100);Check(runtime.Entities.Single().Phase=="nested-copy"&&runtime.Shots.Any(s=>s.Count==3),"The cloned field has independent native State/Behavior instances with its real child entry and attack");}
await using(var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,getterClone.After,phase:"loop-copy-2"))
{await runtime.AdvanceAsync(new(),100);Check(runtime.Entities.Single().Phase=="nested-copy"&&runtime.Shots.Any(s=>s.Count==3),"The cloned getter executes its actual constructor and independently cloned child Behavior value");}
Check(originals.All(p=>File.ReadAllText(p.Key)==p.Value),"Reference/getter cloning, history, persistence and native playback leave supplied files unchanged");
Console.WriteLine($"{checks} reference clone checks passed.");
