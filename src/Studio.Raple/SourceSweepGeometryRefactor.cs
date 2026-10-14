using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;
namespace Studio.Raple;
public static class SourceSweepGeometryRefactor
{
    public static SourcePhaseEdit Apply(RapleAdapter adapter,StudioProject project,string sourceFile,Guid controller,IReadOnlyDictionary<string,string> values)
    {
        if(values.Count==0||values.Keys.Any(k=>k is not("armCount" or "startAngle" or "refreshMs" or "startupMs")))throw new ArgumentException("Choose native sweep arms, angle, refresh or startup fields. XML-bound radius/rate/lifetime changes require a matching XML set.");
        SourceVerification.SourceSet(adapter.ServerRoot,project);sourceFile=Path.GetFullPath(sourceFile);var records=project.AdditionalSources.Prepend(new(project.SourceFile,project.Source){Identities=project.Identities,LockedNodes=project.LockedNodes,CollapsedNodes=project.CollapsedNodes}).ToDictionary(s=>Path.GetFullPath(s.SourceFile),StringComparer.OrdinalIgnoreCase);if(!records.TryGetValue(sourceFile,out var controllerRecord))throw new ArgumentException("The controller source must be accepted in this project.");
        var scoped=adapter.ForProject(project);var controllerDoc=new BehaviorDocument(controllerRecord.Source,scoped,controllerRecord.Identities,sourceFile);controllerDoc.LockedNodes.UnionWith(controllerRecord.LockedNodes);var selected=controllerDoc.Find(controller);if(!controllerDoc.CanEditRegion(controller))throw new InvalidOperationException("The sweep controller is locked.");
        var found=scoped.GeometryCreation(controllerDoc,selected)??throw new ArgumentException("Resolve this geometry's source constructor before changing shared fields.");if(RapleAdapter.ReadGeometry(found.Creation,found.Model)==null)throw new ArgumentException("Geometry inputs need known source values.");var declaringFile=found.Creation.SyntaxTree.FilePath.Length==0?sourceFile:Path.GetFullPath(found.Creation.SyntaxTree.FilePath);
        if(!records.TryGetValue(declaringFile,out var geometryRecord))records[declaringFile]=geometryRecord=new(declaringFile,File.ReadAllText(declaringFile));
        // Discover native consumers in the complete supplied source set before
        // editing a shared scalar definition, including not-yet-opened files.
        var compiler=scoped.SourceCompilation(project);
        foreach(var tree in compiler.SyntaxTrees)
        {
            var path=Path.GetFullPath(tree.FilePath);if(records.ContainsKey(path))continue;
            if(!tree.GetRoot().DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().Any(c=>c.ToString().Contains("ClockSweep",StringComparison.Ordinal)||c.ToString().Contains("CorridorPulse",StringComparison.Ordinal)||c.ToString().Contains("OrientationEcho",StringComparison.Ordinal)))continue;
            var doc=new BehaviorDocument(tree.GetText().ToString(),scoped,sourceFile:path);
            if(doc.AllNodes.Where(n=>scoped.IsSweepController(doc,n)).Any(n=>scoped.GeometryCreation(doc,n) is{} use&&use.Creation.SyntaxTree.FilePath.Equals(found.Creation.SyntaxTree.FilePath,StringComparison.OrdinalIgnoreCase)&&use.Creation.Span==found.Creation.Span))records[path]=new(path,doc.Source){Identities=doc.Identities};
        }
        var documents=new Dictionary<string,BehaviorDocument>(StringComparer.OrdinalIgnoreCase);var consumers=0;
        foreach(var record in records.Values)
        {
            var doc=new BehaviorDocument(record.Source,scoped,record.Identities,record.SourceFile);doc.LockedNodes.UnionWith(record.LockedNodes);documents[Path.GetFullPath(record.SourceFile)]=doc;
            foreach(var node in doc.AllNodes.Where(n=>scoped.IsSweepController(doc,n)))if(scoped.GeometryCreation(doc,node) is{} use&&use.Creation.SyntaxTree.FilePath.Equals(found.Creation.SyntaxTree.FilePath,StringComparison.OrdinalIgnoreCase)&&use.Creation.Span==found.Creation.Span)
            {consumers++;if(!doc.CanEditRegion(node.Id))throw new InvalidOperationException("A shared sweep geometry consumer is locked.");var owner=doc.AllRoots.Single(r=>r.Root.Descendants().Any(n=>n.Id==node.Id));if(owner.Definition is{} definition)foreach(var caller in doc.AllRoots.Where(r=>definition.Encounters.Contains(r.EntityId)))if(caller.Root.Descendants().Any(n=>doc.LockedNodes.Contains(n.Id)))throw new InvalidOperationException("A shared sweep caller is locked.");}
            if(Path.GetFullPath(record.SourceFile).Equals(declaringFile,StringComparison.OrdinalIgnoreCase)&&doc.LockedNodes.Select(doc.Find).Any(n=>n.Start<=found.Creation.SpanStart&&n.Start+n.Length>=found.Creation.Span.End))throw new InvalidOperationException("The declaring geometry region is locked.");
        }
        if(consumers==0)throw new ArgumentException("No accepted sweep geometry consumer was found.");
        string[] names=["projectileIndexes","armCount","startAngle","degreesPerSecond","innerRadius","bandSpacing","lifetimeMs","refreshMs","startupMs","bossLabOnly"];var arguments=found.Creation.ArgumentList!.Arguments;var rewritten=arguments;
        foreach(var pair in values){var index=Enumerable.Range(0,arguments.Count).FirstOrDefault(i=>(arguments[i].NameColon?.Name.Identifier.ValueText??names[i])==pair.Key,-1);var expression=SyntaxFactory.ParseExpression(pair.Value);if(expression.ContainsDiagnostics)throw new ArgumentException("Enter a valid native geometry value.");if(index>=0)rewritten=rewritten.Replace(rewritten[index],rewritten[index].WithExpression(expression.WithTriviaFrom(rewritten[index].Expression)));else rewritten=rewritten.Add(SyntaxFactory.Argument(expression).WithNameColon(SyntaxFactory.NameColon(pair.Key)));}
        var changed=found.Creation.WithArgumentList(found.Creation.ArgumentList.WithArguments(rewritten)).ToString();var afterSource=geometryRecord.Source.Remove(found.Creation.SpanStart,found.Creation.Span.Length).Insert(found.Creation.SpanStart,changed);var beforeDoc=documents[declaringFile];var candidate=new BehaviorDocument(afterSource,scoped,beforeDoc.Identities,declaringFile);if(beforeDoc.Identities.Any(i=>!candidate.Identities.Any(n=>n.Id==i.Id)))throw new ArgumentException("The geometry edit must preserve every declaring document identity.");records[declaringFile]=geometryRecord with{Source=afterSource,Identities=candidate.Identities};foreach(var pair in documents.Where(p=>!p.Key.Equals(declaringFile,StringComparison.OrdinalIgnoreCase)))records[pair.Key]=records[pair.Key] with{Identities=pair.Value.Identities};
        var primaryPath=Path.GetFullPath(project.SourceFile);var primary=records[primaryPath];var after=project with{Source=primary.Source,Identities=primary.Identities,AdditionalSources=records.Where(p=>!p.Key.Equals(primaryPath,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Value).ToList()};var final=adapter.ForProject(after);
        foreach(var record in records.Values){var doc=new BehaviorDocument(record.Source,final,record.Identities,record.SourceFile);var errors=final.Validate(doc,null).Where(d=>d.IsError).ToArray();if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));foreach(var node in doc.AllNodes.Where(n=>final.IsSweepController(doc,n)))if(final.GeometryCreation(doc,node) is{}use&&Path.GetFullPath(use.Creation.SyntaxTree.FilePath).Equals(declaringFile,StringComparison.OrdinalIgnoreCase)&&use.Creation.SpanStart==found.Creation.SpanStart&&final.ReadSweepGeometry(doc,node)==null)throw new ArgumentException("Shared native geometry values violate the original constructor contract.");}
        return new(project,after,[new(declaringFile,geometryRecord.Source,afterSource,beforeDoc.Identities,[]){AfterIdentities=candidate.Identities}]){Selection=[controller],SelectionSourceFile=sourceFile};
    }
}
