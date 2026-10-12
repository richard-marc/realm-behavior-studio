using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;

public sealed record SourcePhaseChange(string SourceFile,string Before,string After,List<NodeIdentity> Identities,IReadOnlyList<SourceLiteralEdit> Edits)
{
    public List<NodeIdentity>? AfterIdentities {get;init;}
}
public sealed record SourcePhaseEdit(StudioProject Before,StudioProject After,IReadOnlyList<SourcePhaseChange> Changes);

public static partial class SourcePhaseRefactor
{
    public static SourcePhaseEdit Rename(RapleAdapter adapter,StudioProject project,string sourceFile,Guid phaseId,string name)
        =>PlanRename(adapter,project,sourceFile,phaseId,name,true);
    static SourcePhaseEdit PlanRename(RapleAdapter adapter,StudioProject project,string sourceFile,Guid phaseId,string name,bool validateCandidate)
    {
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Enter a non-empty phase name.");
        var originals=SourceVerification.SourceSet(adapter.ServerRoot,project);
        sourceFile=Path.GetFullPath(sourceFile);
        if(!originals.ContainsKey(sourceFile))throw new ArgumentException("The phase's source file is not in this project.");
        var scoped=adapter.ForProject(project);
        var records=project.AdditionalSources.Prepend(new(project.SourceFile,project.Source){Identities=project.Identities,LockedNodes=project.LockedNodes,CollapsedNodes=project.CollapsedNodes})
            .ToDictionary(s=>Path.GetFullPath(s.SourceFile),StringComparer.OrdinalIgnoreCase);
        var documents=new Dictionary<string,BehaviorDocument>(StringComparer.OrdinalIgnoreCase);
        BehaviorDocument Document(string path)
        {
            path=Path.GetFullPath(path);
            if(documents.TryGetValue(path,out var existing))return existing;
            if(!records.TryGetValue(path,out var record))records[path]=record=new(path,File.ReadAllText(path));
            var doc=new BehaviorDocument(record.Source,scoped,record.Identities,path);doc.LockedNodes.UnionWith(record.LockedNodes);
            documents.Add(path,doc);return doc;
        }
        var targetDoc=Document(sourceFile);var target=targetDoc.Find(phaseId);var old=targetDoc.StateName(phaseId)??throw new ArgumentException("Select a named phase.");
        if(old==name)return new(project,project,[]);
        var owner=targetDoc.AllRoots.Single(r=>r.Root.Descendants().Any(n=>n.Id==phaseId));
        var callers=owner.Definition?.Encounters??new[]{owner.EntityId};
        if(callers.Count==0||owner.Definition is {StructurallyComplete:false})throw new ArgumentException("This phase has generated contents or unresolved caller inputs. Reconcile it in Source.");
        foreach(var callerFile in owner.Definition?.CallerFiles??new[]{sourceFile})Document(callerFile);
        var queue=new Queue<(string File,EntityBehavior Root)>();
        foreach(var pair in documents.ToArray())foreach(var root in pair.Value.Entities.Where(e=>callers.Any(id=>RapleAdapter.SameEntity(id,e.EntityId))))queue.Enqueue((pair.Key,root));
        if(queue.Count==0)throw new ArgumentException("No complete source caller was found for this phase.");
        var relevant=new HashSet<(string File,string Root)>();
        while(queue.TryDequeue(out var pending))
        {
            if(!relevant.Add((pending.File,pending.Root.EntityId)))continue;
            var doc=Document(pending.File);
            if(pending.Root.Definition is {StructurallyComplete:false})throw new ArgumentException("A shared phase body has generated contents or unresolved inputs. Reconcile its references in Source.");
            if(doc.LockedNodes.Any(id=>pending.Root.Root.Descendants().Any(n=>n.Id==id)))throw new InvalidOperationException("A phase caller or shared body is locked.");
            foreach(var node in pending.Root.Root.Descendants())
            {
                if(node.Kind=="Opaque")throw new ArgumentException("Opaque caller contents prevent complete phase reconciliation.");
                if(node.Kind is not ("ArgumentGroup" or "Factory" or "Reference" or "Spread")&&!scoped.PhasePrimitive(doc,node))throw new ArgumentException("A custom controller prevents complete phase-reference discovery. Reconcile it in Source.");
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
        var edits=new Dictionary<string,List<(int Start,int Length,string Text)>>(StringComparer.OrdinalIgnoreCase);
        var literal=SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,SyntaxFactory.Literal(name)).ToFullString();
        void Edit(string file,int start,int length)
        {
            file=Path.GetFullPath(file);var doc=Document(file);
            var node=doc.AllNodes.Where(n=>n.Start<=start&&n.Start+n.Length>=start+length).OrderBy(n=>n.Length).FirstOrDefault();
            if(node==null?doc.LockedNodes.Count>0:!doc.CanEditRegion(node.Id))throw new InvalidOperationException("A phase reference is locked or lacks an editable source region.");
            if(!edits.TryGetValue(file,out var changes))edits[file]=changes=[];
            changes.Add((start,length,literal));
        }
        var nameParameter=target.Parameters.First(p=>BehaviorDocument.LiteralString(p.Expression)==old);Edit(sourceFile,nameParameter.Start,nameParameter.Length);
        foreach(var scope in relevant)
        {
            var doc=Document(scope.File);var root=doc.AllRoots.Single(r=>r.EntityId==scope.Root);
            foreach(var node in root.Root.Descendants())
            {
                if(node.Kind=="State")
                {
                    var phase=doc.StateName(node.Id);
                    if(phase==null&&node.Parameters.Any(p=>p.Name=="name"))throw new ArgumentException("A generated phase name prevents complete reconciliation.");
                    if(node.Start!=target.Start||!scope.File.Equals(sourceFile,StringComparison.OrdinalIgnoreCase))
                        if(phase==old||phase==name)throw new ArgumentException("A caller already contains that phase name or an ambiguous original name.");
                }
                foreach(var parameter in node.Parameters)
                {
                    if(node.Kind is "Factory" or "Reference" or "Spread")continue;
                    var binding=doc.SchemaAt(node)!;
                    if(!callers.Any(id=>binding.TargetsPhaseOf(doc.Expression(node),parameter.Name,id,id)==true))continue;
                    if(binding.PhaseEntityParameter(doc.Expression(node),parameter.Name)!=null)continue; // Whole-compilation order scan below establishes target ownership.
                    var values=BehaviorDocument.PhaseLiterals(parameter.Expression)??throw new ArgumentException("A dynamic phase destination prevents complete reconciliation.");
                    foreach(var value in values.Where(v=>v.Name==old))Edit(scope.File,parameter.Start+value.Start,value.Length);
                }
            }
        }
        var compiler=scoped.SourceCompilation(project);
        foreach(var tree in compiler.SyntaxTrees)
        {
            var model=compiler.GetSemanticModel(tree);
            foreach(var creation in tree.GetRoot().DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            {
                if(model.GetSymbolInfo(creation).Symbol is not IMethodSymbol method||!RapleAdapter.NativeOrderType(method.ContainingType.ToDisplayString()))continue;
                var arguments=new Dictionary<string,ExpressionSyntax>(StringComparer.Ordinal);var positional=0;
                foreach(var arg in creation.ArgumentList?.Arguments??default)
                {
                    var key=arg.NameColon?.Name.Identifier.ValueText??method.Parameters[positional].Name;arguments[key]=arg.Expression;positional=Array.FindIndex(method.Parameters.ToArray(),p=>p.Name==key)+1;
                }
                var death=method.ContainingType.Name=="OrderOnDeath";
                if(!arguments.TryGetValue(death?"state":"targetState",out var destination))continue;
                var values=BehaviorDocument.PhaseLiterals(destination.ToString());
                if(values!=null&&!values.Any(v=>v.Name==old))continue;
                var entity=arguments.TryGetValue(death?"target":"children",out var entityExpression)?model.GetConstantValue(entityExpression):default;
                if(!entity.HasValue||entity.Value is not string entityId)throw new ArgumentException("An incoming order has unresolved target ownership. Reconcile it in Source.");
                if(!callers.Any(id=>RapleAdapter.SameEntity(id,entityId)))continue;
                if(values==null)throw new ArgumentException("An incoming order has a dynamic phase destination. Reconcile it in Source.");
                foreach(var value in values.Where(v=>v.Name==old))Edit(tree.FilePath,destination.SpanStart+value.Start,value.Length);
            }
        }
        var changesList=new List<SourcePhaseChange>();
        foreach(var pair in edits)
        {
            var doc=Document(pair.Key);var changed=doc.Source;
            foreach(var edit in pair.Value.Distinct().OrderByDescending(e=>e.Start))changed=changed.Remove(edit.Start,edit.Length).Insert(edit.Start,edit.Text);
            var ids=doc.Identities;changesList.Add(new(pair.Key,doc.Source,changed,ids,pair.Value.Distinct().Select(e=>new SourceLiteralEdit(e.Start,e.Length,e.Text)).ToArray()));records[pair.Key]=records[pair.Key] with{Source=changed,Identities=ids};
        }
        var primaryPath=Path.GetFullPath(project.SourceFile);var primary=records[primaryPath];
        var after=project with{Source=primary.Source,Identities=primary.Identities,AdditionalSources=records.Where(p=>!p.Key.Equals(primaryPath,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Value).ToList()};
        if(validateCandidate)
        {
            var candidateAdapter=adapter.ForProject(after);
            foreach(var record in records.Values)
            {
                var candidate=new BehaviorDocument(record.Source,candidateAdapter,record.Identities,record.SourceFile);
                var errors=candidate.SourceDiagnostics().Where(d=>d.IsError).ToArray();
                if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
                if(record.Identities.Any(id=>!candidate.Identities.Any(next=>next.Id==id.Id)))throw new InvalidOperationException("Phase reconciliation changed a surviving source identity.");
            }
        }
        return new(project,after,changesList);
    }
}
