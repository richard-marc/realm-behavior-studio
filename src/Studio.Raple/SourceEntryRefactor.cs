using Studio.Core;

namespace Studio.Raple;
public static class SourceEntryRefactor
{
    public static SourcePhaseEdit Paste(RapleAdapter adapter,StudioProject project,string sourceFile,Guid parentId,int index,BehaviorClipboard clipboard)
    {
        SourceVerification.SourceSet(adapter.ServerRoot,project);sourceFile=Path.GetFullPath(sourceFile);
        var records=project.AdditionalSources.Prepend(new(project.SourceFile,project.Source){Identities=project.Identities,LockedNodes=project.LockedNodes,CollapsedNodes=project.CollapsedNodes}).ToDictionary(s=>Path.GetFullPath(s.SourceFile),StringComparer.OrdinalIgnoreCase);
        if(!records.TryGetValue(sourceFile,out var original))throw new ArgumentException("The entry collection source is not accepted in this project.");
        var scoped=adapter.ForProject(project);var target=new BehaviorDocument(original.Source,scoped,original.Identities,sourceFile);target.LockedNodes.UnionWith(original.LockedNodes);var parent=target.Find(parentId);
        var owner=target.AllRoots.Single(r=>r.Root.Descendants().Any(n=>n.Id==parentId));
        if(owner.Definition is {StructurallyComplete:false})throw new ArgumentException("Resolve generated collection ownership before shared entry import.");
        var callers=owner.Definition?.Encounters??new[]{owner.EntityId};
        foreach(var file in owner.Definition?.CallerFiles??new[]{sourceFile})
        {
            var path=Path.GetFullPath(file);if(!records.TryGetValue(path,out var record))records[path]=record=new(path,File.ReadAllText(path));
            var caller=new BehaviorDocument(record.Source,scoped,record.Identities,path);
            if(record.LockedNodes.Any(id=>caller.AllRoots.Where(r=>r.Definition?.Encounters.Any(e=>callers.Any(c=>RapleAdapter.SameEntity(c,e)))==true||callers.Any(c=>RapleAdapter.SameEntity(c,r.EntityId))).SelectMany(r=>r.Root.Descendants()).Any(n=>n.Id==id)))throw new InvalidOperationException("A shared collection caller is locked.");
        }
        var before=target.Source;var beforeIds=target.Identities;var pasted=target.PasteNodes(parent.Id,index,clipboard);
        if(pasted.Any(id=>target.Find(id).Kind!="InitializerEntry"))throw new ArgumentException("Paste complete keyed entries into the shared collection.");
        foreach(var node in pasted.SelectMany(id=>target.Find(id).Descendants()))
        {
            if(node.Kind is "Factory" or "Reference" or "Spread" or "State" or "Opaque")throw new ArgumentException("Shared/generated entry inputs need declaration ownership before workspace paste.");
            foreach(var parameter in node.Parameters)
                if(callers.Any(c=>target.SchemaAt(node)!.TargetsPhaseOf(target.Expression(node),parameter.Name,c,c)!=false))throw new ArgumentException("Entry-owned phase destinations need complete caller reconciliation before import.");
        }
        records[sourceFile]=original with{Source=target.Source,Identities=target.Identities};var primaryPath=Path.GetFullPath(project.SourceFile);var primary=records[primaryPath];
        var after=project with{Source=primary.Source,Identities=primary.Identities,AdditionalSources=records.Where(p=>!p.Key.Equals(primaryPath,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Value).ToList()};var final=adapter.ForProject(after);
        foreach(var record in records.Values)
        {
            var doc=new BehaviorDocument(record.Source,final,record.Identities,record.SourceFile);var errors=final.Validate(doc,project.EntityId).Where(d=>d.IsError).ToArray();if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
        }
        return new(project,after,[new(sourceFile,before,target.Source,beforeIds,[]){AfterIdentities=target.Identities}]){Selection=pasted};
    }
}
