using Studio.Core;

namespace Studio.Raple;
public static partial class SourcePhaseRefactor
{
    public static SourcePhaseEdit Delete(RapleAdapter adapter,StudioProject project,string sourceFile,Guid phaseId)
        =>Delete(adapter,project,sourceFile,[phaseId]);
    public static SourcePhaseEdit Delete(RapleAdapter adapter,StudioProject project,string sourceFile,IEnumerable<Guid> selected)
    {
        SourceVerification.SourceSet(adapter.ServerRoot,project);sourceFile=Path.GetFullPath(sourceFile);
        var records=project.AdditionalSources.Prepend(new(project.SourceFile,project.Source){Identities=project.Identities,LockedNodes=project.LockedNodes,CollapsedNodes=project.CollapsedNodes})
            .ToDictionary(s=>Path.GetFullPath(s.SourceFile),StringComparer.OrdinalIgnoreCase);
        if(!records.TryGetValue(sourceFile,out var targetRecord))throw new ArgumentException("The phase source is not in this project.");
        var target=new BehaviorDocument(targetRecord.Source,adapter.ForProject(project),targetRecord.Identities,sourceFile);
        var branches=target.NormalizeSelection(selected);
        if(branches.Any(n=>target.Parent(n.Id)==null)||!branches.Any(n=>n.Descendants().Any(c=>c.Kind=="State")))throw new ArgumentException("Select child phases to delete; declaring bodies remain their source roots.");
        var phases=branches.SelectMany(n=>n.Descendants()).Where(n=>n.Kind=="State").ToArray();
        if(phases.Any(n=>target.StateName(n.Id)==null))throw new ArgumentException("Generated phase names require source reconciliation before deletion.");
        var names=phases.Select(n=>target.StateName(n.Id)!).ToHashSet(StringComparer.Ordinal);
        records[sourceFile]=targetRecord with{Identities=target.Identities};
        var primaryPath=Path.GetFullPath(project.SourceFile);
        StudioProject Snapshot()
        {
            var primary=records[primaryPath];
            return project with{Source=primary.Source,Identities=primary.Identities,LockedNodes=primary.LockedNodes,CollapsedNodes=primary.CollapsedNodes,
                AdditionalSources=records.Where(p=>!p.Key.Equals(primaryPath,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Value).ToList()};
        }
        // Rename already proves the complete known caller/reference closure, including native orders.
        // Its immutable probe is never installed; only original source spans are used for deletion.
        var probes=new List<SourcePhaseEdit>();
        foreach(var phase in phases)
        {
            var probe=PlanRename(adapter,Snapshot(),sourceFile,phase.Id,"__studio_delete_"+Guid.NewGuid().ToString("N"),false);probes.Add(probe);
            foreach(var record in probe.After.AdditionalSources.Prepend(new(probe.After.SourceFile,probe.After.Source){Identities=probe.After.Identities}))
            {
                var path=Path.GetFullPath(record.SourceFile);if(records.ContainsKey(path))continue;
                var before=probe.Changes.FirstOrDefault(c=>c.SourceFile.Equals(path,StringComparison.OrdinalIgnoreCase));
                records[path]=record with{Source=before?.Before??record.Source,Identities=before?.Identities??record.Identities};
            }
        }
        var scoped=adapter.ForProject(Snapshot());var documents=new Dictionary<string,BehaviorDocument>(StringComparer.OrdinalIgnoreCase);
        BehaviorDocument Document(string path)
        {
            path=Path.GetFullPath(path);if(documents.TryGetValue(path,out var retained))return retained;
            var record=records[path];var doc=new BehaviorDocument(record.Source,scoped,record.Identities,path);doc.LockedNodes.UnionWith(record.LockedNodes);documents[path]=doc;return doc;
        }
        var deletions=new Dictionary<string,HashSet<Guid>>(StringComparer.OrdinalIgnoreCase){[sourceFile]=branches.Select(n=>n.Id).ToHashSet()};
        foreach(var change in probes.SelectMany(p=>p.Changes))foreach(var edit in change.Edits)
        {
            if(change.SourceFile.Equals(sourceFile,StringComparison.OrdinalIgnoreCase)&&branches.Any(branch=>edit.Start>=branch.Start&&edit.Start+edit.Length<=branch.Start+branch.Length))continue;
            var doc=Document(change.SourceFile);
            var reference=doc.AllNodes.Where(n=>n.Start<=edit.Start&&n.Start+n.Length>=edit.Start+edit.Length).OrderBy(n=>n.Length).FirstOrDefault();
            if(reference==null||!reference.IsTransition&&reference.Kind is not ("Order" or "OrderOnce" or "OrderOnDeath"))throw new ArgumentException("A deleted phase reference lacks an editable transition/order slot. Reconcile it in Source.");
            if(!deletions.TryGetValue(change.SourceFile,out var ids))deletions[change.SourceFile]=ids=[];ids.Add(reference.Id);
        }
        var changes=new List<SourcePhaseChange>();
        foreach(var pair in deletions)
        {
            var doc=Document(pair.Key);var before=doc.Source;var oldIds=doc.Identities;
            doc.DeleteNodes(pair.Value,reconcilePhases:(_,affected)=>
            {
                if(affected.Any(n=>!names.Contains(n)))throw new InvalidOperationException("The deletion contains an unplanned phase.");
            });
            var ids=doc.Identities;var surviving=ids.Select(i=>i.Id).ToHashSet();
            records[pair.Key]=records[pair.Key] with{Source=doc.Source,Identities=ids,LockedNodes=doc.LockedNodes.Where(surviving.Contains).ToList(),CollapsedNodes=records[pair.Key].CollapsedNodes.Where(surviving.Contains).ToList()};
            changes.Add(new(pair.Key,before,doc.Source,oldIds,[]){AfterIdentities=ids});
        }
        var after=Snapshot();var final=adapter.ForProject(after);
        foreach(var record in records.Values)
        {
            var candidate=new BehaviorDocument(record.Source,final,record.Identities,record.SourceFile);
            var errors=final.Validate(candidate,project.EntityId).Where(d=>d.IsError).ToArray();
            if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
        }
        return new(project,after,changes);
    }
}
