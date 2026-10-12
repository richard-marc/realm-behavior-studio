using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;
public static class SourceDefinitionRefactor
{
    public static SourcePhaseEdit Extract(RapleAdapter adapter,StudioProject project,string sourceFile,Guid selected,string destinationFile,string name)
    {
        var token=SyntaxFactory.ParseToken(name);
        if(!token.IsKind(SyntaxKind.IdentifierToken)||token.ContainsDiagnostics||token.ToFullString()!=name)throw new ArgumentException("Enter a valid C# helper name.");
        sourceFile=Path.GetFullPath(sourceFile);destinationFile=Path.GetFullPath(destinationFile);
        SourceVerification.SourceSet(adapter.ServerRoot,project);
        var records=project.AdditionalSources.Prepend(new(project.SourceFile,project.Source){Identities=project.Identities,LockedNodes=project.LockedNodes,CollapsedNodes=project.CollapsedNodes})
            .ToDictionary(s=>Path.GetFullPath(s.SourceFile),StringComparer.OrdinalIgnoreCase);
        if(!records.ContainsKey(sourceFile)||!records.ContainsKey(destinationFile))throw new ArgumentException("Choose source files already accepted in this project.");
        var scoped=adapter.ForProject(project);var original=records[sourceFile];var doc=new BehaviorDocument(original.Source,scoped,original.Identities,sourceFile);doc.LockedNodes.UnionWith(original.LockedNodes);
        var node=doc.Find(selected);if(!doc.CanEditRegion(selected))throw new InvalidOperationException("The extracted region or one of its source uses is locked.");
        if(node.Kind is "ArgumentGroup" or "Spread" or "Opaque")throw new ArgumentException("Extract a native phase/action expression; child-list ownership needs collection extraction.");
        var owner=doc.AllRoots.Single(r=>r.Root.Descendants().Any(n=>n.Id==selected));
        var owners=owner.Definition?.Encounters??new[]{owner.EntityId};
        // A shared edit cannot bypass locks in any known source caller.
        foreach(var path in owner.Definition?.CallerFiles??new[]{sourceFile})
        {
            if(!records.TryGetValue(Path.GetFullPath(path),out var caller))continue;
            var callerDoc=new BehaviorDocument(caller.Source,scoped,caller.Identities,caller.SourceFile);
            if(caller.LockedNodes.Any(id=>callerDoc.AllRoots.Where(r=>r.Definition?.Encounters.Any(e=>owners.Any(o=>RapleAdapter.SameEntity(e,o)))==true||owners.Any(o=>RapleAdapter.SameEntity(r.EntityId,o))).SelectMany(r=>r.Root.Descendants()).Any(n=>n.Id==id)))
                throw new InvalidOperationException("An extracted definition's caller is locked.");
        }
        if(records[destinationFile].LockedNodes.Count>0)throw new InvalidOperationException("Unlock the destination source before introducing a helper declaration.");
        var compiler=scoped.SourceCompilation(project);var tree=compiler.SyntaxTrees.Single(t=>Path.GetFullPath(t.FilePath).Equals(sourceFile,StringComparison.OrdinalIgnoreCase));var model=compiler.GetSemanticModel(tree);
        var expression=tree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().Single(e=>e.SpanStart==node.Start&&e.Span.Length==node.Length);
        var type=model.GetTypeInfo(expression).Type??throw new ArgumentException("The selected expression has no resolved native type.");
        if(type.TypeKind==TypeKind.Error||type is INamedTypeSymbol {IsAnonymousType:true}||ContainsTypeParameter(type))throw new ArgumentException("This expression needs generic/anonymous source ownership before extraction.");
        var hostSyntax=expression.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()??throw new ArgumentException("A helper needs its containing source type.");
        var host=model.GetDeclaredSymbol(hostSyntax)!;
        if(host.ContainingType!=null||host.IsGenericType||!hostSyntax.Modifiers.Any(SyntaxKind.PartialKeyword))throw new ArgumentException("Extract into a top-level, nongeneric partial source type.");
        if(host.GetMembers(token.ValueText).Length>0)throw new ArgumentException("This source type already has that member name.");
        var captured=expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Where(n=>n.Parent is not NameColonSyntax).Select(n=>model.GetSymbolInfo(n).Symbol)
            .Where(s=>s is ILocalSymbol or IParameterSymbol).OfType<ISymbol>()
            .Where(s=>s.DeclaringSyntaxReferences.All(r=>r.SyntaxTree!=tree||!expression.Span.Contains(r.Span))).Distinct(SymbolEqualityComparer.Default).ToArray();
        if(captured.Select(s=>s.Name).Distinct(StringComparer.Ordinal).Count()!=captured.Length)throw new ArgumentException("Captured values have ambiguous lexical names.");
        if(expression.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>().Any(lambda=>lambda.DescendantNodes().OfType<IdentifierNameSyntax>().Any(n=>captured.Any(s=>SymbolEqualityComparer.Default.Equals(s,model.GetSymbolInfo(n).Symbol)))))
            throw new ArgumentException("A deferred closure needs shared capture ownership before extraction; keep this source expression intact.");
        var flow=model.AnalyzeDataFlow(expression);var parameters=new List<string>();var arguments=new List<string>();var constants=new List<string>();
        foreach(var value in captured)
        {
            var valueType=value is ILocalSymbol local?local.Type:((IParameterSymbol)value).Type;
            if(ContainsTypeParameter(valueType)||valueType is INamedTypeSymbol {IsAnonymousType:true})throw new ArgumentException("A captured value needs its generic/anonymous source scope.");
            var identifier=SyntaxFacts.GetKeywordKind(value.Name)!=SyntaxKind.None?"@"+value.Name:value.Name;
            if(value is ILocalSymbol {IsConst:true})
            {
                var declaration=(VariableDeclaratorSyntax)value.DeclaringSyntaxReferences.Single().GetSyntax();
                var constant=model.GetConstantValue(declaration.Initializer!.Value);
                if(!constant.HasValue)throw new ArgumentException("A captured constant lacks a resolved value.");
                var literal=ConstantText(constant.Value);if(valueType.TypeKind==TypeKind.Enum)literal="("+valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+")"+literal;
                constants.Add("const "+valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+" "+identifier+" = "+literal+";");continue;
            }
            var readonlyValue=value is IParameterSymbol {RefKind:RefKind.In or RefKind.RefReadOnlyParameter}||value is ILocalSymbol {RefKind:RefKind.RefReadOnly};
            var mode=readonlyValue?"in":flow?.Succeeded==true&&flow.AlwaysAssigned.Contains(value,SymbolEqualityComparer.Default)&&!flow.DataFlowsIn.Contains(value,SymbolEqualityComparer.Default)?"out":"ref";
            parameters.Add(mode+" "+valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+" "+identifier);arguments.Add(mode+" "+identifier);
        }
        var instance=expression.DescendantNodesAndSelf().Any(n=>n is ThisExpressionSyntax or BaseExpressionSyntax||n is IdentifierNameSyntax id&&!(id.Parent is MemberAccessExpressionSyntax member&&member.Name==id)&&id.Parent is not MemberBindingExpressionSyntax&&model.GetSymbolInfo(id).Symbol is {IsStatic:false,Kind:SymbolKind.Field or SymbolKind.Property or SymbolKind.Method});
        var body=constants.Count==0?" => "+expression.ToFullString()+";":" { "+string.Join(" ",constants)+" return "+expression.ToFullString()+"; }";
        var helper="private "+(instance?"":"static ")+type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+" "+name+"("+string.Join(", ",parameters)+")"+body;
        var imports=((CompilationUnitSyntax)tree.GetRoot()).Usings.Concat(expression.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().SelectMany(n=>n.Usings));
        var usingText=string.Join("\n",imports.GroupBy(u=>u.Alias?.Name.Identifier.ValueText??u.ToString()).Select(g=>g.Last().WithoutTrivia().ToFullString()));
        var classText=(host.IsStatic?"static ":"")+"partial "+hostSyntax.Keyword.Text+" "+hostSyntax.Identifier.Text+" { "+helper+" }";
        var ns=host.ContainingNamespace.IsGlobalNamespace?null:host.ContainingNamespace.ToDisplayString();
        if(CSharpSyntaxTree.ParseText(records[destinationFile].Source).GetRoot().DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().Any())throw new ArgumentException("This destination uses a file-scoped namespace; choose a block-namespace source until declaration migration is available.");
        var declarationText="\n"+(ns==null?usingText+"\n"+classText:"namespace "+ns+" {\n"+usingText+"\n"+classText+"\n}")+"\n";
        var call=name+"("+string.Join(", ",arguments)+")";
        records[sourceFile]=original with{Source=original.Source.Remove(node.Start,node.Length).Insert(node.Start,call)};
        records[destinationFile]=records[destinationFile] with{Source=records[destinationFile].Source+declarationText};
        var after=Project(records,project);var final=adapter.ForProject(after);var documents=records.ToDictionary(p=>p.Key,p=>new BehaviorDocument(p.Value.Source,final,sourceFile:p.Key),StringComparer.OrdinalIgnoreCase);
        foreach(var candidate in documents.Values)
        {
            var errors=final.Validate(candidate,project.EntityId).Where(d=>d.IsError).ToArray();if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
        }
        var destination=documents[destinationFile];var definition=destination.Definitions.Single(d=>d.Definition!.Label==token.ValueText);
        var moved=node.Descendants().ToArray();var copied=definition.Root.Descendants().ToArray();
        if(moved.Length!=copied.Length||!moved.Select(n=>n.Kind).SequenceEqual(copied.Select(n=>n.Kind)))throw new ArgumentException("Extraction changed the native expression's projected structure.");
        // Compare all noncaptured semantic references in their actual new source location.
        var finalCompiler=final.SourceCompilation(after);var finalTree=finalCompiler.SyntaxTrees.Single(t=>Path.GetFullPath(t.FilePath).Equals(destinationFile,StringComparison.OrdinalIgnoreCase));var finalModel=finalCompiler.GetSemanticModel(finalTree);
        var extracted=finalTree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().Single(e=>e.SpanStart==definition.Root.Start&&e.Span.Length==definition.Root.Length);
        var beforeSymbols=Symbols(model,expression,captured);var afterSymbols=Symbols(finalModel,extracted,finalModel.GetDeclaredSymbol(extracted.Ancestors().OfType<MethodDeclarationSyntax>().First())!.Parameters.Cast<ISymbol>().Concat(constants.Count==0?[]:extracted.Ancestors().OfType<MethodDeclarationSyntax>().First().DescendantNodes().OfType<VariableDeclaratorSyntax>().Select(v=>finalModel.GetDeclaredSymbol(v)!)).ToArray());
        if(!beforeSymbols.SequenceEqual(afterSymbols))throw new ArgumentException("The helper's noncaptured source references changed binding at the destination.");
        var movedIds=moved.Select(n=>n.Id).ToHashSet();var changes=new List<SourcePhaseChange>();
        foreach(var path in new[]{sourceFile,destinationFile}.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var oldRecord=path.Equals(sourceFile,StringComparison.OrdinalIgnoreCase)?original:project.AdditionalSources.Prepend(new(project.SourceFile,project.Source){Identities=project.Identities,LockedNodes=project.LockedNodes,CollapsedNodes=project.CollapsedNodes}).Single(r=>Path.GetFullPath(r.SourceFile).Equals(path,StringComparison.OrdinalIgnoreCase));
            var oldDoc=new BehaviorDocument(oldRecord.Source,scoped,oldRecord.Identities,path);var candidate=documents[path];var ids=candidate.Identities;
            foreach(var old in oldDoc.AllNodes.Where(n=>!movedIds.Contains(n.Id)))
            {
                var matching=candidate.AllNodes.FirstOrDefault(n=>n.Path==old.Path&&n.Kind==old.Kind);
                if(matching!=null){var index=ids.FindIndex(i=>i.Id==matching.Id);ids[index]=ids[index] with{Id=old.Id};}
            }
            if(path.Equals(destinationFile,StringComparison.OrdinalIgnoreCase))for(var index=0;index<moved.Length;index++)
            {var slot=ids.FindIndex(i=>i.Id==copied[index].Id);ids[slot]=ids[slot] with{Id=moved[index].Id};}
            records[path]=records[path] with{Identities=ids,CollapsedNodes=oldRecord.CollapsedNodes.Where(id=>!movedIds.Contains(id)||path.Equals(destinationFile,StringComparison.OrdinalIgnoreCase)).Concat(path.Equals(destinationFile,StringComparison.OrdinalIgnoreCase)?original.CollapsedNodes.Where(movedIds.Contains):[]).Distinct().ToList()};
            changes.Add(new(path,oldRecord.Source,records[path].Source,oldDoc.Identities,[]){AfterIdentities=ids});
        }
        return new(project,Project(records,project),changes){Selection=[selected],OriginSourceFile=sourceFile,SelectionSourceFile=destinationFile};
    }
    static bool ContainsTypeParameter(ITypeSymbol type)=>type.TypeKind==TypeKind.TypeParameter||type is IArrayTypeSymbol a&&ContainsTypeParameter(a.ElementType)||type is INamedTypeSymbol n&&n.TypeArguments.Any(ContainsTypeParameter);
    static string ConstantText(object? value)=>value switch{null=>"null",string s=>SyntaxFactory.Literal(s).Text,char c=>SyntaxFactory.Literal(c).Text,bool b=>b?"true":"false",float f=>SyntaxFactory.Literal(f).Text,double d=>SyntaxFactory.Literal(d).Text,decimal m=>SyntaxFactory.Literal(m).Text,long l=>SyntaxFactory.Literal(l).Text,uint u=>SyntaxFactory.Literal(u).Text,ulong u=>SyntaxFactory.Literal(u).Text,_=>Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)!};
    static string[] Symbols(SemanticModel model,ExpressionSyntax expression,IEnumerable<ISymbol> excluded)
        =>expression.DescendantNodesAndSelf().Where(n=>n is SimpleNameSyntax or BaseObjectCreationExpressionSyntax or InvocationExpressionSyntax&&n.Parent is not NameColonSyntax)
            .Select(n=>model.GetSymbolInfo(n).Symbol).Where(s=>s!=null&&!excluded.Contains(s,SymbolEqualityComparer.Default)).Select(s=>s!.OriginalDefinition.GetDocumentationCommentId()??s.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToArray();
    static StudioProject Project(Dictionary<string,StudioSourceDocument> records,StudioProject template)
    {var primary=records[Path.GetFullPath(template.SourceFile)];return template with{Source=primary.Source,Identities=primary.Identities,CollapsedNodes=primary.CollapsedNodes,AdditionalSources=records.Where(p=>!p.Key.Equals(Path.GetFullPath(template.SourceFile),StringComparison.OrdinalIgnoreCase)).Select(p=>p.Value).ToList()};}
}
