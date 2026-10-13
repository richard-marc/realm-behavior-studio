using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;
public static class SourceDefinitionRemoval
{
    public static SourcePhaseEdit Remove(RapleAdapter adapter,StudioProject project,string sourceFile,Guid selected)
    {
        SourceVerification.SourceSet(adapter.ServerRoot,project);sourceFile=Path.GetFullPath(sourceFile);
        var records=project.AdditionalSources.Prepend(new(project.SourceFile,project.Source){Identities=project.Identities,LockedNodes=project.LockedNodes,CollapsedNodes=project.CollapsedNodes}).ToDictionary(s=>Path.GetFullPath(s.SourceFile),StringComparer.OrdinalIgnoreCase);
        if(!records.TryGetValue(sourceFile,out var record))throw new ArgumentException("The definition source must be accepted in this project.");
        var scoped=adapter.ForProject(project);var doc=new BehaviorDocument(record.Source,scoped,record.Identities,sourceFile);doc.LockedNodes.UnionWith(record.LockedNodes);
        var definition=doc.Definitions.SingleOrDefault(d=>d.Root.Id==selected)??throw new ArgumentException("Select a declaring definition body.");
        if(!definition.Definition!.StructurallyComplete||definition.Root.Descendants().Any(n=>n.Kind=="Opaque"))throw new ArgumentException("Resolve generated definition ownership in Source before removing its declaration.");
        if(!doc.CanEditRegion(selected))throw new InvalidOperationException("The definition body is locked.");
        var compiler=scoped.SourceCompilation(project);var tree=compiler.SyntaxTrees.Single(t=>Path.GetFullPath(t.FilePath).Equals(sourceFile,StringComparison.OrdinalIgnoreCase));var model=compiler.GetSemanticModel(tree);
        var expression=tree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().Single(e=>e.SpanStart==definition.Root.Start&&e.Span.Length==definition.Root.Length);
        var declaration=expression.AncestorsAndSelf().FirstOrDefault(n=>n is MethodDeclarationSyntax or PropertyDeclarationSyntax or VariableDeclaratorSyntax);
        var symbol=declaration==null?null:model.GetDeclaredSymbol(declaration);
        if(symbol is not (IMethodSymbol or IPropertySymbol or IFieldSymbol)||symbol.DeclaredAccessibility!=Accessibility.Private||symbol.GetAttributes().Length>0||symbol.DeclaringSyntaxReferences.Length!=1)throw new ArgumentException("Remove a private, singly declared source method, field or getter. Other declaration ownership remains in Source.");
        SyntaxNode removable=declaration!;
        if(declaration is VariableDeclaratorSyntax variable)
        {
            if(variable.Parent?.Parent is not FieldDeclarationSyntax field)throw new ArgumentException("Local definition removal remains in Source.");
            if(field.Declaration.Variables.Count==1)removable=field;
        }
        foreach(var candidateTree in compiler.SyntaxTrees)
        {
            var candidateModel=compiler.GetSemanticModel(candidateTree);
            foreach(var name in candidateTree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>().Where(n=>n.Identifier.ValueText==symbol.Name))
            {
                if(candidateTree==tree&&removable.Span.Contains(name.Span))continue;
                var info=candidateModel.GetSymbolInfo(name);IEnumerable<ISymbol> references=info.Symbol==null?info.CandidateSymbols:new[]{info.Symbol};
                if(references.Any(s=>SymbolEqualityComparer.Default.Equals(s.OriginalDefinition,symbol.OriginalDefinition)))throw new ArgumentException("The definition still has a compiler-bound reference in "+Path.GetFileName(candidateTree.FilePath)+". Remove or replace its callers first.");
                if(name.Parent is MemberAccessExpressionSyntax access&&candidateModel.GetTypeInfo(access.Expression).Type?.TypeKind==TypeKind.Dynamic)throw new ArgumentException("A dynamic member with this name needs source ownership before declaration removal.");
                if(name.Parent is MemberBindingExpressionSyntax binding&&binding.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault() is {} conditional&&candidateModel.GetTypeInfo(conditional.Expression).Type?.TypeKind==TypeKind.Dynamic)throw new ArgumentException("A conditional dynamic member needs source ownership before declaration removal.");
            }
            foreach(var call in candidateTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Where(c=>c.Expression switch{MemberAccessExpressionSyntax member=>member.Name.Identifier.ValueText is "GetMethod" or "GetField" or "GetProperty" or "GetMember",MemberBindingExpressionSyntax member=>member.Name.Identifier.ValueText is "GetMethod" or "GetField" or "GetProperty" or "GetMember",IdentifierNameSyntax name=>name.Identifier.ValueText is "GetMethod" or "GetField" or "GetProperty" or "GetMember",_=>false}))
            {
                if(candidateTree==tree&&removable.Span.Contains(call.Span))continue;
                if(candidateModel.GetSymbolInfo(call).Symbol is IMethodSymbol lookup&&lookup.ContainingType.ToDisplayString()=="System.Type"&&lookup.Name is "GetMethod" or "GetField" or "GetProperty" or "GetMember"&&call.ArgumentList.Arguments.FirstOrDefault() is {} argument&&candidateModel.GetConstantValue(argument.Expression) is {HasValue:true,Value:string name}&&string.Equals(name,symbol.Name,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("A named reflection lookup needs source ownership before declaration removal.");
            }
        }
        var rewritten=tree.GetRoot().RemoveNode(removable,SyntaxRemoveOptions.KeepExteriorTrivia)??throw new ArgumentException("The declaring source cannot be removed.");var afterSource=rewritten.ToFullString();
        var primaryPath=Path.GetFullPath(project.SourceFile);var primaryRecord=records[primaryPath];var primaryBefore=new BehaviorDocument(primaryRecord.Source,scoped,primaryRecord.Identities,primaryPath);
        var candidate=new BehaviorDocument(afterSource,scoped,doc.Identities,sourceFile);var removed=definition.Root.Descendants().Select(n=>n.Id).ToHashSet();
        if(doc.Identities.Where(i=>!removed.Contains(i.Id)).Any(i=>!candidate.Identities.Any(n=>n.Id==i.Id)))throw new ArgumentException("Removing this declaration would lose another source body's identity.");
        records[primaryPath]=primaryRecord with{Identities=primaryBefore.Identities};records[sourceFile]=record with{Source=afterSource,Identities=candidate.Identities,CollapsedNodes=record.CollapsedNodes.Where(id=>!removed.Contains(id)).ToList()};
        var primary=records[primaryPath];var after=project with{Source=primary.Source,Identities=primary.Identities,AdditionalSources=records.Where(p=>!p.Key.Equals(primaryPath,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Value).ToList()};var final=adapter.ForProject(after);
        foreach(var source in records.Values)
        {
            var checkedDoc=new BehaviorDocument(source.Source,final,source.Identities,source.SourceFile);var errors=final.Validate(checkedDoc,null).Where(d=>d.IsError).ToArray();if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
        }
        var survivingPrimary=new BehaviorDocument(after.Source,final,after.Identities,primaryPath);var selection=survivingPrimary.Entities.Single(e=>e.EntityId==project.EntityId).Root.Id;
        return new(project,after,[new(sourceFile,doc.Source,afterSource,doc.Identities,[]){AfterIdentities=candidate.Identities}]){Selection=[selection],SelectionSourceFile=primaryPath,OriginSourceFile=sourceFile,OriginSelection=[selected]};
    }
}
