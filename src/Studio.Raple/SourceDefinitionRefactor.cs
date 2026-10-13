using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;
public static partial class SourceDefinitionRefactor
{
    public static SourcePhaseEdit Extract(RapleAdapter adapter,StudioProject project,string sourceFile,Guid selected,string destinationFile,string name)
        =>Extract(adapter,project,sourceFile,[selected],destinationFile,name);
    public static SourcePhaseEdit Extract(RapleAdapter adapter,StudioProject project,string sourceFile,IEnumerable<Guid> selection,string destinationFile,string name)
    {
        var token=SyntaxFactory.ParseToken(name);
        if(!token.IsKind(SyntaxKind.IdentifierToken)||token.ContainsDiagnostics||token.ToFullString()!=name)throw new ArgumentException("Enter a valid C# helper name.");
        sourceFile=Path.GetFullPath(sourceFile);destinationFile=Path.GetFullPath(destinationFile);
        SourceVerification.SourceSet(adapter.ServerRoot,project);
        var records=project.AdditionalSources.Prepend(new(project.SourceFile,project.Source){Identities=project.Identities,LockedNodes=project.LockedNodes,CollapsedNodes=project.CollapsedNodes})
            .ToDictionary(s=>Path.GetFullPath(s.SourceFile),StringComparer.OrdinalIgnoreCase);
        if(!records.ContainsKey(sourceFile)||!records.ContainsKey(destinationFile))throw new ArgumentException("Choose source files already accepted in this project.");
        var scoped=adapter.ForProject(project);var original=records[sourceFile];var doc=new BehaviorDocument(original.Source,scoped,original.Identities,sourceFile);doc.LockedNodes.UnionWith(original.LockedNodes);
        var nodes=doc.NormalizeSelection(selection);var node=nodes[0];var selected=node.Id;var multiple=nodes.Count>1;
        if(nodes.Any(n=>!doc.CanEditRegion(n.Id)))throw new InvalidOperationException("The extracted region or one of its source uses is locked.");
        if(nodes.Any(n=>n.Kind is "Spread" or "Opaque"||n.Kind=="ArgumentGroup"&&(multiple||n.ArgumentFromCall||n.ArgumentRequiresSource)))throw new ArgumentException("Extract native expressions or an explicit child collection; generated/expanded call slots need source ownership.");
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
        var regions=nodes.Select(n=>tree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().Single(e=>e.SpanStart==n.Start&&e.Span.Length==n.Length)).ToArray();
        var slice=multiple?PrepareSlice(doc,nodes,model):null;
        var typeInfo=model.GetTypeInfo(expression);var type=slice?.Type??typeInfo.Type??typeInfo.ConvertedType??throw new ArgumentException("The selected expression has no resolved native type.");
        if(type.TypeKind==TypeKind.Error||type is INamedTypeSymbol {IsAnonymousType:true}||ContainsTypeParameter(type))throw new ArgumentException("This expression needs generic/anonymous source ownership before extraction.");
        var hostSyntax=expression.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()??throw new ArgumentException("A helper needs its containing source type.");
        var host=model.GetDeclaredSymbol(hostSyntax)!;
        if(host.ContainingType!=null||host.IsGenericType||!hostSyntax.Modifiers.Any(SyntaxKind.PartialKeyword))throw new ArgumentException("Extract into a top-level, nongeneric partial source type.");
        if(host.GetMembers(token.ValueText).Length>0)throw new ArgumentException("This source type already has that member name.");
        var captured=regions.SelectMany(e=>e.DescendantNodesAndSelf()).OfType<IdentifierNameSyntax>().Where(n=>n.Parent is not NameColonSyntax).Select(n=>model.GetSymbolInfo(n).Symbol)
            .Where(s=>s is ILocalSymbol or IParameterSymbol).OfType<ISymbol>()
            .Where(s=>s.DeclaringSyntaxReferences.All(r=>r.SyntaxTree!=tree||!regions.Any(e=>e.Span.Contains(r.Span)))).Distinct(SymbolEqualityComparer.Default).ToArray();
        if(captured.Select(s=>s.Name).Distinct(StringComparer.Ordinal).Count()!=captured.Length)throw new ArgumentException("Captured values have ambiguous lexical names.");
        if(regions.SelectMany(e=>e.DescendantNodes()).OfType<AnonymousFunctionExpressionSyntax>().Any(lambda=>lambda.DescendantNodes().OfType<IdentifierNameSyntax>().Any(n=>captured.Any(s=>SymbolEqualityComparer.Default.Equals(s,model.GetSymbolInfo(n).Symbol)))))
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
            var storageFlow=multiple?model.AnalyzeDataFlow(regions.First(e=>e.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(n=>SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(n).Symbol,value)))):flow;
            var output=storageFlow?.Succeeded==true&&!storageFlow.DataFlowsIn.Contains(value,SymbolEqualityComparer.Default)&&(storageFlow.AlwaysAssigned.Contains(value,SymbolEqualityComparer.Default)||multiple&&!storageFlow.DefinitelyAssignedOnEntry.Contains(value,SymbolEqualityComparer.Default));
            var mode=readonlyValue?"in":output?"out":"ref";
            parameters.Add(mode+" "+valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+" "+identifier);arguments.Add(mode+" "+identifier);
        }
        var instance=regions.SelectMany(e=>e.DescendantNodesAndSelf()).Any(n=>n is ThisExpressionSyntax or BaseExpressionSyntax||n is IdentifierNameSyntax id&&!(id.Parent is MemberAccessExpressionSyntax member&&member.Name==id)&&id.Parent is not MemberBindingExpressionSyntax&&model.GetSymbolInfo(id).Symbol is {IsStatic:false,Kind:SymbolKind.Field or SymbolKind.Property or SymbolKind.Method});
        var expressionText=slice?.Expression??expression.ToFullString();
        var body=constants.Count==0?" => "+expressionText+";":" { "+string.Join(" ",constants)+" return "+expressionText+"; }";
        var helper="private "+(instance?"":"static ")+type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+" "+name+"("+string.Join(", ",parameters)+")"+body;
        var imports=((CompilationUnitSyntax)tree.GetRoot()).Usings.Concat(expression.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().SelectMany(n=>n.Usings));
        var usingText=string.Join("\n",imports.GroupBy(u=>u.Alias?.Name.Identifier.ValueText??u.ToString()).Select(g=>g.Last().WithoutTrivia().ToFullString()));
        var classText=(host.IsStatic?"static ":"")+"partial "+hostSyntax.Keyword.Text+" "+hostSyntax.Identifier.Text+" { "+helper+" }";
        var ns=host.ContainingNamespace.IsGlobalNamespace?null:host.ContainingNamespace.ToDisplayString();
        var destinationTree=compiler.SyntaxTrees.Single(t=>Path.GetFullPath(t.FilePath).Equals(destinationFile,StringComparison.OrdinalIgnoreCase));
        var scopedNamespace=destinationTree.GetRoot().DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().SingleOrDefault();
        if(scopedNamespace!=null&&compiler.GetSemanticModel(destinationTree).GetDeclaredSymbol(scopedNamespace)?.ToDisplayString()!=ns)throw new ArgumentException("The destination's file-scoped namespace must match the original source type.");
        // Existing file-scoped imports apply to the appended partial type. Do not add imports
        // that could silently rebind the destination's earlier declarations.
        var declarationText="\n"+(scopedNamespace!=null?classText:ns==null?usingText+"\n"+classText:"namespace "+ns+" {\n"+usingText+"\n"+classText+"\n}")+"\n";
        var call=name+"("+string.Join(", ",arguments)+")";
        records[sourceFile]=original with{Source=slice?.Replace(call)??original.Source.Remove(node.Start,node.Length).Insert(node.Start,call)};
        records[destinationFile]=records[destinationFile] with{Source=records[destinationFile].Source+declarationText};
        var after=Project(records,project);var final=adapter.ForProject(after);var documents=records.ToDictionary(p=>p.Key,p=>new BehaviorDocument(p.Value.Source,final,sourceFile:p.Key),StringComparer.OrdinalIgnoreCase);
        foreach(var candidate in documents.Values)
        {
            var errors=final.Validate(candidate,project.EntityId).Where(d=>d.IsError).ToArray();if(errors.Length>0)throw new ArgumentException(string.Join("\n",errors.Select(d=>d.Message)));
        }
        var destination=documents[destinationFile];var definition=destination.Definitions.Single(d=>d.Definition!.Label==token.ValueText);
        var moved=nodes.SelectMany(n=>n.Descendants()).ToArray();var copied=(multiple?definition.Root.Children.SelectMany(n=>n.Descendants()):definition.Root.Descendants()).ToArray();
        if(moved.Length!=copied.Length||!moved.Select(n=>n.Kind).SequenceEqual(copied.Select(n=>n.Kind)))throw new ArgumentException("Extraction changed the native expression's projected structure.");
        // Compare all noncaptured semantic references in their actual new source location.
        var finalCompiler=final.SourceCompilation(after);var finalTree=finalCompiler.SyntaxTrees.Single(t=>Path.GetFullPath(t.FilePath).Equals(destinationFile,StringComparison.OrdinalIgnoreCase));var finalModel=finalCompiler.GetSemanticModel(finalTree);
        var extracted=finalTree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().Single(e=>e.SpanStart==definition.Root.Start&&e.Span.Length==definition.Root.Length);
        var excluded=finalModel.GetDeclaredSymbol(extracted.Ancestors().OfType<MethodDeclarationSyntax>().First())!.Parameters.Cast<ISymbol>().Concat(constants.Count==0?[]:extracted.Ancestors().OfType<MethodDeclarationSyntax>().First().DescendantNodes().OfType<VariableDeclaratorSyntax>().Select(v=>finalModel.GetDeclaredSymbol(v)!)).ToArray();
        var afterRegions=multiple?definition.Root.Children.Select(n=>finalTree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().Single(e=>e.SpanStart==n.Start&&e.Span.Length==n.Length)).ToArray():[extracted];
        var beforeSymbols=regions.SelectMany(e=>Symbols(model,e,captured));var afterSymbols=afterRegions.SelectMany(e=>Symbols(finalModel,e,excluded));
        if(!beforeSymbols.SequenceEqual(afterSymbols))throw new ArgumentException("The helper's noncaptured source references changed binding at the destination.");
        var movedIds=moved.Select(n=>n.Id).ToHashSet();var changes=new List<SourcePhaseChange>();
        foreach(var path in new[]{sourceFile,destinationFile}.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var oldRecord=path.Equals(sourceFile,StringComparison.OrdinalIgnoreCase)?original:project.AdditionalSources.Prepend(new(project.SourceFile,project.Source){Identities=project.Identities,LockedNodes=project.LockedNodes,CollapsedNodes=project.CollapsedNodes}).Single(r=>Path.GetFullPath(r.SourceFile).Equals(path,StringComparison.OrdinalIgnoreCase));
            var oldDoc=new BehaviorDocument(oldRecord.Source,scoped,oldRecord.Identities,path);var candidate=documents[path];var ids=candidate.Identities;
            var claimed=new HashSet<Guid>();
            foreach(var old in oldDoc.AllNodes.Where(n=>!movedIds.Contains(n.Id)).OrderBy(n=>n.Start))
            {
                BehaviorNode? matching;
                if(multiple)
                {
                    var oldOwner=oldDoc.AllRoots.Single(r=>r.Root.Descendants().Any(n=>n.Id==old.Id));var newOwner=candidate.AllRoots.FirstOrDefault(r=>r.EntityId==oldOwner.EntityId);
                    var pool=newOwner?.Root.Descendants().Where(n=>n.Kind==old.Kind&&!claimed.Contains(n.Id)).ToArray()??[];
                    matching=old.Id==oldOwner.Root.Id?newOwner?.Root:pool.FirstOrDefault(n=>candidate.Expression(n)==oldDoc.Expression(old))??pool.FirstOrDefault(n=>n.Path==old.Path);
                }
                else matching=candidate.AllNodes.FirstOrDefault(n=>n.Path==old.Path&&n.Kind==old.Kind);
                if(matching!=null){claimed.Add(matching.Id);var index=ids.FindIndex(i=>i.Id==matching.Id);ids[index]=ids[index] with{Id=old.Id};}
            }
            if(path.Equals(destinationFile,StringComparison.OrdinalIgnoreCase))for(var index=0;index<moved.Length;index++)
            {var slot=ids.FindIndex(i=>i.Id==copied[index].Id);ids[slot]=ids[slot] with{Id=moved[index].Id};}
            records[path]=records[path] with{Identities=ids,CollapsedNodes=oldRecord.CollapsedNodes.Where(id=>!movedIds.Contains(id)||path.Equals(destinationFile,StringComparison.OrdinalIgnoreCase)).Concat(path.Equals(destinationFile,StringComparison.OrdinalIgnoreCase)?original.CollapsedNodes.Where(movedIds.Contains):[]).Distinct().ToList()};
            changes.Add(new(path,oldRecord.Source,records[path].Source,oldDoc.Identities,[]){AfterIdentities=ids});
        }
        if(slice is {Owner:{} ownerId,Binding:{} expectedBinding})
        {
            var record=records[sourceFile];var accepted=new BehaviorDocument(record.Source,final,record.Identities,sourceFile);var retained=accepted.Find(ownerId);
            var callerTree=finalCompiler.SyntaxTrees.Single(t=>Path.GetFullPath(t.FilePath).Equals(sourceFile,StringComparison.OrdinalIgnoreCase));
            var callExpression=callerTree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>().Single(e=>e.SpanStart==retained.Start&&e.Span.Length==retained.Length);
            if(finalCompiler.GetSemanticModel(callerTree).GetSymbolInfo(callExpression).Symbol is not IMethodSymbol method||MethodBinding(method)!=expectedBinding)throw new ArgumentException("Selection extraction changed the native constructor/factory overload or type arguments.");
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
