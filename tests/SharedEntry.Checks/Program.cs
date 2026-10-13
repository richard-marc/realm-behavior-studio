using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var primary=Path.GetFullPath(adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile);var helper=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=primary).SourceFile);var originals=new[]{primary,helper}.ToDictionary(p=>p,File.ReadAllText);
const string source="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioSharedEntries=()=>Behav().Init("Cave Pirate Brawler",new State("loop",StudioSharedEntryWrap(StudioSharedEntryList()))).Init("Pirate Lieutenant",new State("loop",StudioSharedEntryWrap(StudioSharedEntryList())));
    static Prioritize StudioSharedEntryWrap(System.Collections.Generic.IEnumerable<CycleBehavior> children)=>new Prioritize(System.Linq.Enumerable.ToArray(children));
    sealed class StudioSharedEntryCollection:System.Collections.Generic.IEnumerable<CycleBehavior> {
        readonly System.Collections.Generic.SortedDictionary<int,CycleBehavior> values=new();
        public void Add(int key,CycleBehavior child)=>values.Add(key,child);
        public System.Collections.Generic.IEnumerator<CycleBehavior> GetEnumerator()=>values.Values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
} }
""";
const string declaration="""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static StudioSharedEntryCollection StudioSharedEntryList()=>new StudioSharedEntryCollection(){ {11,new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100)} };
} }
""";
var project=new StudioProject{SourceFile=primary,Source=source,EntityId="Cave Pirate Brawler",AdditionalSources=[new(helper,originals[helper]+declaration)],Player=new(Invulnerable:true),Seed=7};var scoped=adapter.ForProject(project);var primaryDoc=new BehaviorDocument(source,scoped,sourceFile:primary);var helperDoc=new BehaviorDocument(project.AdditionalSources.Single().Source,scoped,sourceFile:helper);var body=helperDoc.Definitions.Single(d=>d.Definition!.Label=="StudioSharedEntryList");var entry=body.Root.Children.Single();project=project with{Identities=primaryDoc.Identities,AdditionalSources=[project.AdditionalSources.Single() with{Identities=helperDoc.Identities}]};
var clipboard=helperDoc.CopyNodes([entry.Id]);var pasted=SourceEntryRefactor.Paste(adapter,project,helper,body.Root.Id,1,clipboard);
Check(pasted.Changes.Count==1&&pasted.Changes.Single().SourceFile==helper&&pasted.After.Source==source,"Shared keyed paste plans only the actual declaring file and retains both original callers");
var acceptedRecord=pasted.After.AdditionalSources.Single(s=>s.SourceFile==helper);var accepted=new BehaviorDocument(acceptedRecord.Source,adapter.ForProject(pasted.After),acceptedRecord.Identities,helper);var imported=accepted.Find(pasted.Selection.Single());
Check(imported.Kind=="InitializerEntry"&&imported.Id!=entry.Id&&accepted.Find(entry.Id).Id==entry.Id,"Shared paste retains original entry identity and assigns an independent imported pair");
Check(pasted.Before==project&&helperDoc.Source==project.AdditionalSources.Single().Source,"Immutable shared entry planning leaves all accepted source documents unchanged");
var change=pasted.Changes.Single();var history=new BehaviorDocument(change.Before,scoped,change.Identities,helper);history.ApplySourceRevision(history.Revision,change.Before,change.After,change.AfterIdentities!,adapter.ForProject(pasted.After));
Check(history.RestoreWorkspaceHistory(false,scoped)&&history.Source==change.Before&&history.RestoreWorkspaceHistory(true,adapter.ForProject(pasted.After))&&change.AfterIdentities!.All(i=>history.Identities.Any(n=>n.Id==i.Id)),"Shared entry history restores source and every saved identity against the complete workspace binding");
Reject(()=>SourceEntryRefactor.Paste(adapter,project with{LockedNodes=[primaryDoc.Entities.First().Root.Id]},helper,body.Root.Id,1,clipboard),"A lock on either known native caller protects the shared collection paste");
Reject(()=>SourceEntryRefactor.Paste(adapter,project with{AdditionalSources=[project.AdditionalSources.Single() with{LockedNodes=[body.Root.Id]}]},helper,body.Root.Id,1,clipboard),"A declaring collection lock protects workspace entry insertion");
var factorySource=project.AdditionalSources.Single().Source.Replace("new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100)","StudioEntryChild()")+"\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static TKR.WorldServer.logic.behaviors.Shoot StudioEntryChild()=>new TKR.WorldServer.logic.behaviors.Shoot(20,count:3,fixedAngle:0,coolDown:100); } }";
var factoryProject=project with{AdditionalSources=[new(helper,factorySource)]};var factoryDoc=new BehaviorDocument(factorySource,adapter.ForProject(factoryProject),sourceFile:helper);var factoryBody=factoryDoc.Definitions.Single(d=>d.Definition!.Label=="StudioSharedEntryList");factoryProject=factoryProject with{AdditionalSources=[factoryProject.AdditionalSources.Single() with{Identities=factoryDoc.Identities}]};
Reject(()=>SourceEntryRefactor.Paste(adapter,factoryProject,helper,factoryBody.Root.Id,1,factoryDoc.CopyNodes([factoryBody.Root.Children.Single().Id])),"Shared entry inputs with helper ownership retain an explicit import guard");
Check(factoryDoc.Source==factorySource&&factoryDoc.Revision==0,"Rejected shared input ownership leaves source and editor history untouched");
var modified=new BehaviorDocument(acceptedRecord.Source,adapter.ForProject(pasted.After),acceptedRecord.Identities,helper);modified.Apply(new(modified.Revision,"Set independent shared key",[new(imported.Id,"key","7")]),d=>adapter.ForProject(pasted.After).Validate(d,project.EntityId));var runnable=pasted.After with{AdditionalSources=[acceptedRecord with{Source=modified.Source,Identities=modified.Identities}]};
foreach(var entity in new[]{"Cave Pirate Brawler","Pirate Lieutenant"})
{
    await using var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,runnable with{EntityId=entity});await runtime.AdvanceAsync(new(),100);Check(runtime.Shots.Select(s=>s.Count).SequenceEqual(new[]{3,3}),"Every native caller executes the accepted shared keyed pair: "+entity);
}
var output=Path.GetFullPath(".artifacts/shared-entry");Directory.CreateDirectory(output);var path=Path.Combine(output,"shared-entry.rbs");BehaviorDocument.Save(path,runnable);Check(BehaviorDocument.Load(path).AdditionalSources.Single(s=>s.SourceFile==helper).Source==modified.Source,"Shared keyed source and new pair persist through save/reopen");
Check(originals.All(p=>File.ReadAllText(p.Key)==p.Value),"Shared entry planning, history and native execution leave supplied game files unchanged");Console.WriteLine($"{checks} shared entry checks passed.");
