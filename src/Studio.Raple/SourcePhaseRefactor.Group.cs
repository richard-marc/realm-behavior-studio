using Studio.Core;

namespace Studio.Raple;
public static partial class SourcePhaseRefactor
{
    public static SourcePhaseEdit Group(RapleAdapter adapter,StudioProject project,string sourceFile,IEnumerable<Guid> selected,string name,bool chooseUniqueName=false)
    {
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Enter a non-empty phase name.");
        SourceVerification.SourceSet(adapter.ServerRoot,project);sourceFile=Path.GetFullPath(sourceFile);
        var records=project.AdditionalSources.Prepend(new(project.SourceFile,project.Source){Identities=project.Identities,LockedNodes=project.LockedNodes,CollapsedNodes=project.CollapsedNodes})
            .ToDictionary(s=>Path.GetFullPath(s.SourceFile),StringComparer.OrdinalIgnoreCase);
        if(!records.ContainsKey(sourceFile))throw new ArgumentException("The grouping source is not in this project.");
        var scoped=adapter.ForProject(project);var documents=new Dictionary<string,BehaviorDocument>(StringComparer.OrdinalIgnoreCase);
        BehaviorDocument Document(string path)
        {
            path=Path.GetFullPath(path);if(documents.TryGetValue(path,out var retained))return retained;
            if(!records.TryGetValue(path,out var record))records[path]=record=new(path,File.ReadAllText(path));
            var doc=new BehaviorDocument(record.Source,scoped,record.Identities,path);doc.LockedNodes.UnionWith(record.LockedNodes);documents[path]=doc;return doc;
        }
        var target=Document(sourceFile);var nodes=target.NormalizeSelection(selected);
        var owner=target.AllRoots.Single(r=>r.Root.Descendants().Any(n=>n.Id==nodes[0].Id));
        var callers=owner.Definition?.Encounters??new[]{owner.EntityId};
        if(callers.Count==0||owner.Definition is {StructurallyComplete:false})throw new ArgumentException("This body has generated contents or unresolved caller inputs. Group its source directly.");
        foreach(var path in owner.Definition?.CallerFiles??new[]{sourceFile})Document(path);
        var queue=new Queue<(string File,EntityBehavior Root)>();
        foreach(var pair in documents.ToArray())foreach(var root in pair.Value.Entities.Where(e=>callers.Any(id=>RapleAdapter.SameEntity(id,e.EntityId))))queue.Enqueue((pair.Key,root));
        if(queue.Count==0)throw new ArgumentException("No complete source caller was found for this body.");
        var seen=new HashSet<(string File,string Root)>();var phaseNames=new HashSet<string>(StringComparer.Ordinal);
        while(queue.TryDequeue(out var pending))
        {
            if(!seen.Add((pending.File,pending.Root.EntityId)))continue;
            var doc=Document(pending.File);
            if(pending.Root.Definition is {StructurallyComplete:false})throw new ArgumentException("A caller body has generated contents or unresolved inputs. Group its source directly.");
            if(doc.LockedNodes.Any(id=>pending.Root.Root.Descendants().Any(n=>n.Id==id)))throw new InvalidOperationException("A phase caller or shared body is locked.");
            foreach(var node in pending.Root.Root.Descendants())
            {
                if(node.Kind=="Opaque"||node.Kind is not ("ArgumentGroup" or "Factory" or "Reference" or "Spread")&&!scoped.PhasePrimitive(doc,node))throw new ArgumentException("Custom/generated caller contents prevent complete phase discovery.");
                if(node.Kind=="State")
                {
                    var existing=doc.StateName(node.Id);
                    if(existing==null&&node.Parameters.Any(p=>p.Name=="name"))throw new ArgumentException("A generated phase name prevents collision checking.");
                    if(existing!=null)phaseNames.Add(existing);
                }
                if(node.Kind is not ("Factory" or "Reference" or "Spread")&&!(node.Kind=="ArgumentGroup"&&node.ArgumentRequiresSource))continue;
                var local=doc.ReferencedDefinitions(node);var foreign=doc.ExternalDefinitions(node);
                if(local.Count+foreign.Count==0)throw new ArgumentException("A caller's generated phase contents cannot be fully discovered.");
                foreach(var body in local)queue.Enqueue((pending.File,body));
                foreach(var reference in foreign)
                {
                    var path=Path.GetFullPath(reference.SourceFile);var dependency=Document(path);
                    var body=dependency.Definitions.Single(d=>d.Root.Start==reference.Start&&d.Root.Length==reference.Length);queue.Enqueue((path,body));
                }
            }
        }
        if(phaseNames.Contains(name))
        {
            if(!chooseUniqueName)throw new ArgumentException("A caller already contains that phase name.");
            var prefix=name;var suffix=2;while(phaseNames.Contains(name))name=prefix+"-"+suffix++;
        }
        var before=target.Source;var beforeIds=target.Identities;
        target.GroupNodes(nodes.Select(n=>n.Id),name);
        var ids=target.Identities;records[sourceFile]=records[sourceFile] with{Source=target.Source,Identities=ids};
        // Retain discovered identities even for unchanged callers, so later edits share the same workspace model.
        foreach(var pair in documents.Where(p=>p.Key!=sourceFile))records[pair.Key]=records[pair.Key] with{Identities=pair.Value.Identities};
        var primaryPath=Path.GetFullPath(project.SourceFile);var primary=records[primaryPath];
        var after=project with{Source=primary.Source,Identities=primary.Identities,AdditionalSources=records.Where(p=>!p.Key.Equals(primaryPath,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Value).ToList()};
        var final=adapter.ForProject(after);
        foreach(var record in records.Values)
        {
            var candidate=new BehaviorDocument(record.Source,final,record.Identities,record.SourceFile);
            var errors=final.Validate(candidate,project.EntityId).Where(d=>d.IsError).ToArray();
            if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
        }
        return new(project,after,[new(sourceFile,before,target.Source,beforeIds,[]){AfterIdentities=ids}]);
    }
}
