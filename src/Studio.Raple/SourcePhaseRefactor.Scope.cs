using Studio.Core;

namespace Studio.Raple;
public static partial class SourcePhaseRefactor
{
    sealed record StructureScope(string SourceFile,BehaviorDocument Target,IReadOnlyList<string> Callers,
        Dictionary<string,StudioSourceDocument> Records,Dictionary<string,BehaviorDocument> Documents,
        Dictionary<string,Dictionary<string,HashSet<(string File,int Start)>>> Names)
    {
        public HashSet<string> PhaseNames=>Names.Values.SelectMany(n=>n.Keys).ToHashSet(StringComparer.Ordinal);
        public bool UniqueTarget(string name)=>Callers.All(c=>Names[c].TryGetValue(name,out var sites)&&sites.Count==1);
        public bool UniqueTarget(string name,IEnumerable<string> callers)=>callers.All(c=>Names[c].TryGetValue(name,out var sites)&&sites.Count==1);
    }
    static StructureScope PrepareStructureScope(RapleAdapter adapter,StudioProject project,string sourceFile,Guid selected)
    {
        SourceVerification.SourceSet(adapter.ServerRoot,project);sourceFile=Path.GetFullPath(sourceFile);
        var records=project.AdditionalSources.Prepend(new(project.SourceFile,project.Source){Identities=project.Identities,LockedNodes=project.LockedNodes,CollapsedNodes=project.CollapsedNodes})
            .ToDictionary(s=>Path.GetFullPath(s.SourceFile),StringComparer.OrdinalIgnoreCase);
        if(!records.ContainsKey(sourceFile))throw new ArgumentException("The authoring source is not in this project.");
        var scoped=adapter.ForProject(project);var documents=new Dictionary<string,BehaviorDocument>(StringComparer.OrdinalIgnoreCase);
        BehaviorDocument Document(string path)
        {
            path=Path.GetFullPath(path);if(documents.TryGetValue(path,out var retained))return retained;
            if(!records.TryGetValue(path,out var record))records[path]=record=new(path,File.ReadAllText(path));
            var doc=new BehaviorDocument(record.Source,scoped,record.Identities,path);doc.LockedNodes.UnionWith(record.LockedNodes);documents[path]=doc;return doc;
        }
        var target=Document(sourceFile);target.Find(selected);
        var owner=target.AllRoots.Single(r=>r.Root.Descendants().Any(n=>n.Id==selected));
        var callers=owner.Definition?.Encounters??new[]{owner.EntityId};
        if(callers.Count==0||owner.Definition is {StructurallyComplete:false})throw new ArgumentException("This body has generated contents or unresolved caller inputs. Edit its source directly.");
        foreach(var path in owner.Definition?.CallerFiles??new[]{sourceFile})Document(path);
        var queue=new Queue<(string File,EntityBehavior Root)>();
        foreach(var pair in documents.ToArray())foreach(var root in pair.Value.Entities.Where(e=>callers.Any(id=>RapleAdapter.SameEntity(id,e.EntityId))))queue.Enqueue((pair.Key,root));
        if(queue.Count==0)throw new ArgumentException("No complete source caller was found for this body.");
        var names=callers.ToDictionary(c=>c,_=>new Dictionary<string,HashSet<(string File,int Start)>>(StringComparer.Ordinal),StringComparer.Ordinal);
        var seen=new HashSet<(string File,string Root)>();
        while(queue.TryDequeue(out var pending))
        {
            if(!seen.Add((pending.File,pending.Root.EntityId)))continue;
            var doc=Document(pending.File);
            if(pending.Root.Definition is {StructurallyComplete:false})throw new ArgumentException("A caller body has generated contents or unresolved inputs. Edit its source directly.");
            if(doc.LockedNodes.Any(id=>pending.Root.Root.Descendants().Any(n=>n.Id==id)))throw new InvalidOperationException("A phase caller or shared body is locked.");
            var users=pending.Root.Definition?.Encounters??new[]{pending.Root.EntityId};
            foreach(var node in pending.Root.Root.Descendants())
            {
                if(node.Kind=="Opaque"||node.Kind is not ("ArgumentGroup" or "Factory" or "Reference" or "Spread")&&!scoped.PhasePrimitive(doc,node))throw new ArgumentException("Custom/generated caller contents prevent complete phase discovery.");
                if(node.Kind=="State")
                {
                    var existing=doc.StateName(node.Id);
                    if(existing==null&&node.Parameters.Any(p=>p.Name=="name"))throw new ArgumentException("A generated phase name prevents collision checking.");
                    if(existing!=null)foreach(var caller in callers.Where(c=>users.Any(u=>RapleAdapter.SameEntity(c,u))))
                    {
                        if(!names[caller].TryGetValue(existing,out var sites))names[caller][existing]=sites=[];
                        sites.Add((pending.File.ToUpperInvariant(),node.Start));
                    }
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
        return new(sourceFile,target,callers,records,documents,names);
    }
    static SourcePhaseEdit FinishStructure(RapleAdapter adapter,StudioProject project,StructureScope scope,string before,List<NodeIdentity> beforeIds,IReadOnlyList<Guid> selection)
    {
        var target=scope.Target;var records=scope.Records;
        records[scope.SourceFile]=records[scope.SourceFile] with{Source=target.Source,Identities=target.Identities};
        foreach(var pair in scope.Documents.Where(p=>!p.Key.Equals(scope.SourceFile,StringComparison.OrdinalIgnoreCase)))records[pair.Key]=records[pair.Key] with{Identities=pair.Value.Identities};
        var primaryPath=Path.GetFullPath(project.SourceFile);var primary=records[primaryPath];
        var after=project with{Source=primary.Source,Identities=primary.Identities,AdditionalSources=records.Where(p=>!p.Key.Equals(primaryPath,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Value).ToList()};
        var final=adapter.ForProject(after);
        foreach(var record in records.Values)
        {
            var candidate=new BehaviorDocument(record.Source,final,record.Identities,record.SourceFile);
            var errors=final.Validate(candidate,project.EntityId).Where(d=>d.IsError).ToArray();
            if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
        }
        return new(project,after,[new(scope.SourceFile,before,target.Source,beforeIds,[]){AfterIdentities=target.Identities}]){Selection=selection};
    }
}
