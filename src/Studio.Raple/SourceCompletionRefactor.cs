using Studio.Core;

namespace Studio.Raple;
public static class SourceCompletionRefactor
{
    public static SourcePhaseEdit Apply(RapleAdapter adapter,StudioProject project,string sourceFile,Guid selected,Guid movementReference,string? destination)
    {
        SourceVerification.SourceSet(adapter.ServerRoot,project);sourceFile=Path.GetFullPath(sourceFile);
        var records=project.AdditionalSources.Prepend(new(project.SourceFile,project.Source){Identities=project.Identities,LockedNodes=project.LockedNodes,CollapsedNodes=project.CollapsedNodes}).ToDictionary(s=>Path.GetFullPath(s.SourceFile),StringComparer.OrdinalIgnoreCase);
        if(!records.TryGetValue(sourceFile,out var original))throw new ArgumentException("The movement relationship source must be accepted in this project.");
        var scoped=adapter.ForProject(project);var target=new BehaviorDocument(original.Source,scoped,original.Identities,sourceFile);target.LockedNodes.UnionWith(original.LockedNodes);
        // Snapshot documents start at revision zero. The caller checks the live
        // revision; this plan is tied to the complete immutable Before snapshot.
        var node=target.Find(selected);var owner=target.AllRoots.Single(r=>r.Root.Descendants().Any(n=>n.Id==selected));var callers=owner.Definition?.Encounters??new[]{owner.EntityId};
        var callerDocuments=new List<BehaviorDocument>{target};
        foreach(var file in owner.Definition?.CallerFiles??new[]{sourceFile})
        {
            var path=Path.GetFullPath(file);if(!records.TryGetValue(path,out var record))records[path]=record=new(path,File.ReadAllText(path));
            var doc=path.Equals(sourceFile,StringComparison.OrdinalIgnoreCase)?target:new BehaviorDocument(record.Source,scoped,record.Identities,path);doc.LockedNodes.UnionWith(record.LockedNodes);if(!callerDocuments.Contains(doc))callerDocuments.Add(doc);
            if(record.LockedNodes.Any(id=>doc.AllRoots.Where(r=>r.Definition?.Encounters.Any(e=>callers.Any(c=>RapleAdapter.SameEntity(c,e)))==true||callers.Any(c=>RapleAdapter.SameEntity(c,r.EntityId))).SelectMany(r=>r.Root.Descendants()).Any(n=>n.Id==id)))throw new InvalidOperationException("A movement relationship's source caller or body is locked.");
        }
        var pending=new Queue<(BehaviorDocument Document,EntityBehavior Root)>(callerDocuments.SelectMany(doc=>doc.Entities.Where(e=>callers.Any(c=>RapleAdapter.SameEntity(c,e.EntityId))).Select(root=>(doc,root))));
        var visited=new HashSet<(string File,string Root)>();
        while(pending.TryDequeue(out var item))
        {
            var doc=item.Document;if(!visited.Add((doc.SourceFile!,item.Root.EntityId)))continue;
            if(item.Root.Root.Descendants().Any(n=>doc.LockedNodes.Contains(n.Id)))throw new InvalidOperationException("A movement completion caller or shared dependency is locked.");
            foreach(var reference in item.Root.Root.Descendants().Where(n=>n.Kind is "Factory" or "Reference" or "Spread"||n.Kind=="ArgumentGroup"&&n.ArgumentRequiresSource))
            {
                foreach(var body in doc.ReferencedDefinitions(reference))pending.Enqueue((doc,body));
                foreach(var foreign in doc.ExternalDefinitions(reference))
                {
                    var path=Path.GetFullPath(foreign.SourceFile);var dependency=callerDocuments.FirstOrDefault(d=>d.SourceFile!.Equals(path,StringComparison.OrdinalIgnoreCase));
                    if(dependency==null)
                    {if(!records.TryGetValue(path,out var record))records[path]=record=new(path,File.ReadAllText(path));dependency=new(record.Source,scoped,record.Identities,path);dependency.LockedNodes.UnionWith(record.LockedNodes);callerDocuments.Add(dependency);}
                    var body=dependency.Definitions.Single(d=>d.Root.Start==foreign.Start&&d.Root.Length==foreign.Length);pending.Enqueue((dependency,body));
                }
            }
        }
        if(destination!=null)
        {
            foreach(var caller in callers)
            {
                var sites=callerDocuments.SelectMany(doc=>doc.AllRoots.Where(r=>r.Definition?.Encounters.Any(e=>RapleAdapter.SameEntity(e,caller))==true||RapleAdapter.SameEntity(r.EntityId,caller)).SelectMany(r=>r.Root.Descendants().Where(n=>n.Kind=="State"&&doc.StateName(n.Id)==destination).Select(n=>(File:doc.SourceFile!.ToUpperInvariant(),n.Start)))).ToHashSet();
                if(sites.Count!=1)throw new ArgumentException("Choose a uniquely discovered destination phase for every known source caller.");
            }
        }
        var before=target.Source;var beforeIds=target.Identities;Guid selection;
        if(node.Kind=="State")selection=scoped.AddCompletionRoute(target,selected,movementReference,target.Revision,destination??throw new ArgumentException("Choose a destination phase."));
        else {scoped.RelinkCompletionRoute(target,selected,movementReference,target.Revision);selection=selected;}
        records[sourceFile]=original with{Source=target.Source,Identities=target.Identities};foreach(var doc in callerDocuments.Where(d=>d!=target))records[Path.GetFullPath(doc.SourceFile!)]=records[Path.GetFullPath(doc.SourceFile!)] with{Identities=doc.Identities};
        var primaryPath=Path.GetFullPath(project.SourceFile);var primary=records[primaryPath];var after=project with{Source=primary.Source,Identities=primary.Identities,AdditionalSources=records.Where(p=>!p.Key.Equals(primaryPath,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Value).ToList()};var final=adapter.ForProject(after);
        foreach(var record in records.Values)
        {
            var doc=new BehaviorDocument(record.Source,final,record.Identities,record.SourceFile);var errors=final.Validate(doc,null).Where(d=>d.IsError).ToArray();if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
        }
        var accepted=new BehaviorDocument(target.Source,final,target.Identities,sourceFile);if(final.ReadCompletionBinding(accepted,accepted.Find(selection))?.MovementReferenceId!=movementReference)throw new ArgumentException("The accepted completion route must retain its exact native movement instance relationship.");
        return new(project,after,[new(sourceFile,before,target.Source,beforeIds,[]){AfterIdentities=target.Identities}]){Selection=[selection],SelectionSourceFile=sourceFile};
    }
}
