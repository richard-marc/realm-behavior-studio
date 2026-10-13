using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var primary=Path.GetFullPath(adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile);var destination=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=primary).SourceFile);
var originals=new[]{primary,destination}.ToDictionary(p=>p,File.ReadAllText);
const string template="""
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioCollectionExtract=()=>Behav().Init("Cave Pirate Brawler",StudioCollectionCaller(3));
    static State StudioCollectionCaller(int count)=>new State("loop",StudioCollectionWrap(CHILDREN));
    static Prioritize StudioCollectionWrap(System.Collections.Generic.IReadOnlyList<CycleBehavior> children)=>new Prioritize(System.Linq.Enumerable.ToArray(children));
} }
""";
const string items="new Shoot(20,count:count,fixedAngle:0,coolDown:100),new Shoot(20,count:5,fixedAngle:0,coolDown:100)";
var projects=new List<StudioProject>();
foreach(var (expression,label) in new[]{("new CycleBehavior[]{"+items+"}","array"),("["+items+"]","target-typed collection"),("new System.Collections.Generic.List<CycleBehavior>{"+items+"}","list initializer")})
{
    var source=template.Replace("CHILDREN",expression);
    var project=new StudioProject{SourceFile=primary,Source=source,EntityId="Cave Pirate Brawler",AdditionalSources=[new(destination,originals[destination])],Player=new(Invulnerable:true),Seed=7};
    var scoped=adapter.ForProject(project);var doc=new BehaviorDocument(source,scoped,sourceFile:primary);var selected=doc.Definitions.Single(d=>d.Definition!.Label=="StudioCollectionCaller").Root.Descendants().First(n=>n.Kind=="ArgumentGroup"&&!n.ArgumentFromCall&&!n.ArgumentRequiresSource);
    var ids=selected.Descendants().Select(n=>n.Id).ToArray();project=project with{Identities=doc.Identities,CollapsedNodes=[selected.Id]};
    var extracted=SourceDefinitionRefactor.Extract(adapter,project,primary,selected.Id,destination,"StudioExtractedChildren");var record=extracted.After.AdditionalSources.Single(s=>s.SourceFile==destination);
    var accepted=new BehaviorDocument(record.Source,adapter.ForProject(extracted.After),record.Identities,destination);var body=accepted.Definitions.Single(d=>d.Definition!.Label=="StudioExtractedChildren");
    Check(extracted.Changes.Count==2&&extracted.After.Source.Contains("StudioExtractedChildren(ref count)"),label+" extraction retains actual captured storage and source call order");
    Check(body.Root.Kind=="ArgumentGroup"&&body.Root.Id==selected.Id&&ids.All(id=>accepted.AllNodes.Any(n=>n.Id==id))&&body.Root.Children.Select(n=>n.Kind).SequenceEqual(new[]{"Shoot","Shoot"}),label+" child group and ordered native actions retain their identities");
    Check(record.CollapsedNodes.Contains(selected.Id)&&!extracted.After.CollapsedNodes.Contains(selected.Id),label+" collapse ownership moves with the helper body");
    foreach(var change in extracted.Changes)
    {
        var editing=new BehaviorDocument(change.Before,scoped,change.Identities,change.SourceFile);editing.ApplySourceRevision(editing.Revision,change.Before,change.After,change.AfterIdentities!,adapter.ForProject(extracted.After));
        Check(editing.RestoreWorkspaceHistory(false,scoped)&&editing.Source==change.Before&&editing.RestoreWorkspaceHistory(true,adapter.ForProject(extracted.After))&&change.AfterIdentities!.All(i=>editing.Identities.Any(n=>n.Id==i.Id)),label+" complete source history restores both ownership and identities: "+Path.GetFileName(change.SourceFile));
    }
    Reject(()=>SourceDefinitionRefactor.Extract(adapter,project with{LockedNodes=[selected.Id]},primary,selected.Id,destination,"StudioBlockedChildren"),label+" collection lock blocks complete extraction");
    projects.Add(extracted.After);
}
foreach(var project in projects)
{
    await using var runtime=await CompiledRuntime.StartAsync(adapter.ServerRoot,project);await runtime.AdvanceAsync(new(),100);
    Check(runtime.Shots.Select(s=>s.Count).SequenceEqual(new[]{3,5}),"Extracted collection retains native Prioritize dispatch and both ordered projectile counts");
}
var output=Path.GetFullPath(".artifacts/collection-extraction");Directory.CreateDirectory(output);var saved=Path.Combine(output,"collection.rbs");BehaviorDocument.Save(saved,projects[1]);
Check(BehaviorDocument.Load(saved).AdditionalSources.Single(s=>s.SourceFile==destination).Source==projects[1].AdditionalSources.Single(s=>s.SourceFile==destination).Source,"Saved collection extraction retains the complete accepted helper source");
Check(originals.All(p=>File.ReadAllText(p.Key)==p.Value),"Collection extraction, history and native playback leave supplied game files unchanged");
Console.WriteLine($"{checks} collection extraction checks passed.");
