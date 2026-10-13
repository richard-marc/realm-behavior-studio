using Studio.Core;
using Studio.Raple;

var adapter=new RapleAdapter(Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server"));var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var primary=Path.GetFullPath(adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler").SourceFile);var destination=Path.GetFullPath(adapter.Catalog().First(e=>Path.GetFullPath(e.SourceFile)!=primary).SourceFile);var originals=new[]{primary,destination}.ToDictionary(p=>p,File.ReadAllText);
const string template="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioSelectionExtract=()=>Behav().Init("Cave Pirate Brawler",StudioSelectionCaller(2));
    static State StudioSelectionCaller(int count)=>new State(new State("loop",CHILDREN),new State("finish"));
} }
""";
const string actions="new Shoot(20,count:++count,fixedAngle:0,coolDown:100),/* retain selected note */new Shoot(20,count:count+1,fixedAngle:0,coolDown:100),/* retain following note */new Shoot(20,count:count+2,fixedAngle:0,coolDown:100),new TimedTransition(100,\"finish\")";
var playbacks=new List<(StudioProject Before,StudioProject After,int[] Counts,int Duration)>();
foreach(var (inputs,label) in new[]{(actions,"expanded params"),("children:new IStateChildren[]{"+actions+"}","explicit array"),("children:["+actions+"]","bracket collection")})
{
    var source=template.Replace("CHILDREN",inputs);var project=new StudioProject{SourceFile=primary,Source=source,EntityId="Cave Pirate Brawler",AdditionalSources=[new(destination,originals[destination])],Player=new(Invulnerable:true),Seed=7};var scoped=adapter.ForProject(project);var doc=new BehaviorDocument(source,scoped,sourceFile:primary);
    var loop=doc.Definitions.Single(d=>d.Definition!.Label=="StudioSelectionCaller").Root.Children.First();var container=loop.Children.SingleOrDefault(n=>n.Kind=="ArgumentGroup")??loop;var selected=container.Children.Take(2).ToArray();var remaining=container.Children.Skip(2).Select(n=>n.Id).ToArray();project=project with{Identities=doc.Identities,CollapsedNodes=[selected[0].Id]};
    var extracted=SourceDefinitionRefactor.Extract(adapter,project,primary,selected.Select(n=>n.Id),destination,"StudioSelectedActions");var result=extracted.After.AdditionalSources.Single(s=>s.SourceFile==destination);var binding=adapter.ForProject(extracted.After);var helperDoc=new BehaviorDocument(result.Source,binding,result.Identities,destination);var helper=helperDoc.Definitions.Single(d=>d.Definition!.Label=="StudioSelectedActions");var caller=new BehaviorDocument(extracted.After.Source,binding,extracted.After.Identities,primary);
    Check(extracted.Changes.Count==2&&extracted.After.Source.Contains("..StudioSelectedActions(ref count)"),label+" selection uses the actual native child array and a source-order spread call");
    Check(helper.Root.Kind=="ArgumentGroup"&&helper.Root.Children.Select(n=>n.Id).SequenceEqual(selected.Select(n=>n.Id))&&helperDoc.Source.Contains("/* retain selected note */"),label+" ordered selected identities and internal source comments move together");
    Check(caller.Find(loop.Id).Id==loop.Id&&remaining.All(id=>caller.AllNodes.Any(n=>n.Id==id))&&!caller.AllNodes.Any(n=>selected.Any(s=>s.Id==n.Id))&&caller.Source.Contains("/* retain following note */"),label+" parent, following comments and all unselected action/transition identities survive the new collection layout");
    Check(result.CollapsedNodes.Contains(selected[0].Id)&&!extracted.After.CollapsedNodes.Contains(selected[0].Id),label+" collapse metadata moves with selected action ownership");
    foreach(var change in extracted.Changes)
    {
        var editing=new BehaviorDocument(change.Before,scoped,change.Identities,change.SourceFile);editing.ApplySourceRevision(editing.Revision,change.Before,change.After,change.AfterIdentities!,binding);
        Check(editing.RestoreWorkspaceHistory(false,scoped)&&editing.Source==change.Before&&editing.RestoreWorkspaceHistory(true,binding)&&change.AfterIdentities!.All(i=>editing.Identities.Any(n=>n.Id==i.Id)),label+" complete source history restores all selection identities: "+Path.GetFileName(change.SourceFile));
    }
    Reject(()=>SourceDefinitionRefactor.Extract(adapter,project with{LockedNodes=[selected[1].Id]},primary,selected.Select(n=>n.Id),destination,"StudioLockedSelection"),label+" a lock on any selected action protects the whole extraction");
    Reject(()=>SourceDefinitionRefactor.Extract(adapter,project,primary,new[]{selected[0].Id,container.Children[2].Id},destination,"StudioNonAdjacent"),label+" skipped source inputs cannot be silently moved across evaluation order");
    playbacks.Add((project,extracted.After,[3,4,5],100));
}
const string phaseSource="""
using TKR.WorldServer.logic.behaviors;
using TKR.WorldServer.logic.transitions;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioSelectedPhases=()=>Behav().Init("Cave Pirate Brawler",StudioPhaseCaller());
    static State StudioPhaseCaller()=>new State(new State("a",new Shoot(20,count:3,fixedAngle:0,coolDown:100),new TimedTransition(100,"b")),new State("b",new Shoot(20,count:5,fixedAngle:0,coolDown:100),new TimedTransition(100,"finish")),new State("finish"));
} }
""";
foreach(var (source,label,callerName) in new[]{(phaseSource,"phase branches","StudioPhaseCaller"),(template.Replace("CHILDREN",actions.Replace("++count","count=3")).Replace("StudioSelectionCaller(2)","StudioSelectionCaller(out var count)").Replace("StudioSelectionCaller(int count)","StudioSelectionCaller(out int count)"),"unassigned output storage","StudioSelectionCaller")})
{
    var project=new StudioProject{SourceFile=primary,Source=source,EntityId="Cave Pirate Brawler",AdditionalSources=[new(destination,originals[destination])],Player=new(Invulnerable:true),Seed=7};var doc=new BehaviorDocument(source,adapter.ForProject(project),sourceFile:primary);var root=doc.Definitions.Single(d=>d.Definition!.Label==callerName).Root;
    var parent=label=="phase branches"?root:root.Children.First();var selected=parent.Children.Take(2).ToArray();var moved=selected.SelectMany(n=>n.Descendants()).Select(n=>n.Id).ToArray();project=project with{Identities=doc.Identities};
    var extracted=SourceDefinitionRefactor.Extract(adapter,project,primary,selected.Select(n=>n.Id),destination,"StudioSelectedSpecial");var record=extracted.After.AdditionalSources.Single(s=>s.SourceFile==destination);var helperDoc=new BehaviorDocument(record.Source,adapter.ForProject(extracted.After),record.Identities,destination);var helper=helperDoc.Definitions.Single(d=>d.Definition!.Label=="StudioSelectedSpecial");
    Check(moved.All(id=>helperDoc.AllNodes.Any(n=>n.Id==id))&&helper.Root.Children.Select(n=>n.Kind).SequenceEqual(selected.Select(n=>n.Kind)),label+" selection retains all nested native identities and branch order without a wrapper State");
    Check(label=="phase branches"?helper.Root.Children.Select(n=>helperDoc.StateName(n.Id)).SequenceEqual(new[]{"a","b"}):extracted.After.Source.Contains("StudioSelectedSpecial(out count)"),label+" retains real phase names or definite-assignment output storage");
    playbacks.Add((project,extracted.After,label=="phase branches"?[3,5]:[3,4,5],label=="phase branches"?300:100));
}
foreach(var pair in playbacks)
{
    await using var before=await CompiledRuntime.StartAsync(adapter.ServerRoot,pair.Before);await before.AdvanceAsync(new(),pair.Duration);
    await using var after=await CompiledRuntime.StartAsync(adapter.ServerRoot,pair.After);await after.AdvanceAsync(new(),pair.Duration);
    Check(before.Shots.Select(s=>s.Count).SequenceEqual(pair.Counts)&&after.Shots.Select(s=>s.Count).SequenceEqual(before.Shots.Select(s=>s.Count))&&after.Entities.Single().Phase==before.Entities.Single().Phase,"Original and extracted native execution retain mutation, ordered volleys and transition timing");
}
var output=Path.GetFullPath(".artifacts/selection-extraction");Directory.CreateDirectory(output);var path=Path.Combine(output,"selection.rbs");BehaviorDocument.Save(path,playbacks[0].After);
Check(BehaviorDocument.Load(path).AdditionalSources.Single(s=>s.SourceFile==destination).Source==playbacks[0].After.AdditionalSources.Single(s=>s.SourceFile==destination).Source,"Saved multi-expression extraction retains its complete native helper source");
Check(originals.All(p=>File.ReadAllText(p.Key)==p.Value),"Selection extraction, history and native differential checks leave supplied files unchanged");Console.WriteLine($"{checks} selection extraction checks passed.");
