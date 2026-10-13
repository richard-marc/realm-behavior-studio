using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
const string entity="Cave Pirate Brawler";var primary=adapter.Catalog().Single(e=>e.Id==entity).SourceFile;var helper=adapter.Catalog().First(e=>e.SourceFile!=primary).SourceFile;var supplied=new[]{primary,helper}.ToDictionary(p=>p,File.ReadAllText);
const string source="using TKR.WorldServer.logic.behaviors; namespace TKR.WorldServer.logic { public partial class BehaviorDb { private _ StudioRemovalFixture=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"live\",new Shoot(20,count:3,fixedAngle:0,coolDown:100))); } }";
const string declarations="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    // retained declaration note
    static TKR.WorldServer.logic.State StudioUnusedMethod()=>new State("unused",StudioRetainedChild);
    static readonly TKR.WorldServer.logic.behaviors.Shoot StudioRetainedChild=new TKR.WorldServer.logic.behaviors.Shoot(20,count:2);
    static TKR.WorldServer.logic.State StudioUnusedGetter=>new State("unused-getter");
    static readonly TKR.WorldServer.logic.State StudioUnusedField=new State("unused-field"), StudioOtherField=new State("other-field");
} }
""";
StudioProject Project(string helperSource)=>new(){SourceFile=primary,Source=source,EntityId=entity,AdditionalSources=[new(helper,helperSource)],Player=new(Invulnerable:true),Seed=7};
var project=Project(supplied[helper]+declarations);var scoped=adapter.ForProject(project);var doc=new BehaviorDocument(project.AdditionalSources.Single().Source,scoped,sourceFile:helper);var primaryDoc=new BehaviorDocument(source,scoped,sourceFile:primary);project=project with{Identities=primaryDoc.Identities,AdditionalSources=[project.AdditionalSources.Single() with{Identities=doc.Identities}]};
EntityBehavior Definition(BehaviorDocument d,string label)=>d.Definitions.Single(d=>d.Definition!.Label==label);
var unused=Definition(doc,"StudioUnusedMethod");var retained=Definition(doc,"StudioRetainedChild");var plan=SourceDefinitionRemoval.Remove(adapter,project,helper,unused.Root.Id);var afterRecord=plan.After.AdditionalSources.Single();var afterDoc=new BehaviorDocument(afterRecord.Source,adapter.ForProject(plan.After),afterRecord.Identities,helper);
Check(plan.Changes.Count==1&&plan.After.Source==source&&!afterDoc.Definitions.Any(d=>d.Definition!.Label=="StudioUnusedMethod")&&afterRecord.Source.Contains("retained declaration note"),"Unused method removal plans only its declaring file and retains exterior source comments");
Check(Definition(afterDoc,"StudioRetainedChild").Root.Id==retained.Root.Id&&doc.Source==project.AdditionalSources.Single().Source&&plan.OriginSelection!.Single()==unused.Root.Id,"Dependencies and their identities remain intact while immutable planning records the removed view");
var history=new BehaviorDocument(doc.Source,scoped,doc.Identities,helper);var change=plan.Changes.Single();history.ApplySourceRevision(history.Revision,change.Before,change.After,change.AfterIdentities!,adapter.ForProject(plan.After));
Check(history.RestoreWorkspaceHistory(false,scoped)&&Definition(history,"StudioUnusedMethod").Root.Id==unused.Root.Id&&history.RestoreWorkspaceHistory(true,adapter.ForProject(plan.After))&&history.Source==change.After,"Full workspace Undo/Redo restores exact removed definition identities against complete source binding");
Reject(()=>SourceDefinitionRemoval.Remove(adapter,project with{AdditionalSources=[project.AdditionalSources.Single() with{LockedNodes=[unused.Root.Id]}]},helper,unused.Root.Id),"A declaring-body region lock blocks removal atomically");
Reject(()=>SourceDefinitionRemoval.Remove(adapter,project,helper,retained.Root.Id),"A bound reference from an otherwise unused declaration still protects its dependency");
var referencedProject=project with{Source=source.Replace("new State(\"live\",new Shoot(20,count:3,fixedAngle:0,coolDown:100))","StudioUnusedMethod()")};
Reject(()=>SourceDefinitionRemoval.Remove(adapter,referencedProject,helper,unused.Root.Id),"A compiler-bound encounter reference in another source file prevents declaration removal");
foreach(var label in new[]{"StudioUnusedGetter","StudioUnusedField"})
{
    var target=Definition(doc,label);var removed=SourceDefinitionRemoval.Remove(adapter,project,helper,target.Root.Id);var changed=removed.After.AdditionalSources.Single();var parsed=new BehaviorDocument(changed.Source,adapter.ForProject(removed.After),changed.Identities,helper);
    Check(!parsed.Definitions.Any(d=>d.Definition!.Label==label)&&Definition(parsed,"StudioOtherField").Root.Id==Definition(doc,"StudioOtherField").Root.Id,"Private getter/field removal preserves independently declared siblings: "+label);
}
var publicProject=Project((supplied[helper]+declarations).Replace("static TKR.WorldServer.logic.State StudioUnusedMethod","public static TKR.WorldServer.logic.State StudioUnusedMethod"));var publicDoc=new BehaviorDocument(publicProject.AdditionalSources.Single().Source,adapter.ForProject(publicProject),sourceFile:helper);
Reject(()=>SourceDefinitionRemoval.Remove(adapter,publicProject,helper,Definition(publicDoc,"StudioUnusedMethod").Root.Id),"Externally callable declarations retain explicit source ownership");
var nameofProject=Project(supplied[helper]+declarations+"\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { const string StudioUnusedName=nameof(StudioUnusedMethod); } }");var nameofDoc=new BehaviorDocument(nameofProject.AdditionalSources.Single().Source,adapter.ForProject(nameofProject),sourceFile:helper);
Reject(()=>SourceDefinitionRemoval.Remove(adapter,nameofProject,helper,Definition(nameofDoc,"StudioUnusedMethod").Root.Id),"Bound nameof references protect a definition even without encounter callers");
var reflectionProject=Project(supplied[helper]+declarations+"\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static System.Reflection.MethodInfo StudioReflection()=>typeof(BehaviorDb).GetMethod(\"StudioUnusedMethod\",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static); } }");var reflectionDoc=new BehaviorDocument(reflectionProject.AdditionalSources.Single().Source,adapter.ForProject(reflectionProject),sourceFile:helper);
Reject(()=>SourceDefinitionRemoval.Remove(adapter,reflectionProject,helper,Definition(reflectionDoc,"StudioUnusedMethod").Root.Id),"Named reflection lookups retain an explicit declaration ownership guard");
var singleProject=new StudioProject{SourceFile=primary,Source=source+declarations,EntityId=entity};var singleDoc=new BehaviorDocument(singleProject.Source,adapter,sourceFile:primary);singleProject=singleProject with{Identities=singleDoc.Identities};var singleRemoval=SourceDefinitionRemoval.Remove(adapter,singleProject,primary,Definition(singleDoc,"StudioUnusedMethod").Root.Id);
Check(singleRemoval.Changes.Count==1&&singleRemoval.After.AdditionalSources.Count==0&&!singleRemoval.After.Source.Contains("static TKR.WorldServer.logic.State StudioUnusedMethod")&&singleRemoval.Selection.Single()==singleDoc.Entities.Single().Root.Id,"Single-file declaration removal retains the live encounter selection without inventing a dependency document");
var dynamicProject=Project(supplied[helper]+declarations+"\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static object StudioDynamicReference(dynamic receiver)=>receiver.StudioUnusedMethod(); } }");var dynamicDoc=new BehaviorDocument(dynamicProject.AdditionalSources.Single().Source,adapter.ForProject(dynamicProject),sourceFile:helper);
Reject(()=>SourceDefinitionRemoval.Remove(adapter,dynamicProject,helper,Definition(dynamicDoc,"StudioUnusedMethod").Root.Id),"Unbound dynamic member ownership blocks declaration removal");
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,plan.After)){await native.AdvanceAsync(new(),100);Check(native.Shots.Single().Count==3,"Native playback compiles the accepted removal while retaining the independent live encounter");}
var output=Path.GetFullPath(".artifacts/definition-removal");Directory.CreateDirectory(output);var path=Path.Combine(output,"removed.rbs");BehaviorDocument.Save(path,plan.After);Check(BehaviorDocument.Load(path).AdditionalSources.Single().Source==afterRecord.Source&&supplied.All(p=>File.ReadAllText(p.Key)==p.Value),"Removal persistence and native execution leave supplied game source unchanged");Console.WriteLine($"{checks} definition removal checks passed.");
