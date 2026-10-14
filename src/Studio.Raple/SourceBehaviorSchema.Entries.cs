using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;
internal sealed partial class SourceBehaviorSchema
{
    static string EntrySignature(IMethodSymbol method)=>ClipboardScope(method)+"|"+method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    (IMethodSymbol Method,SourceExpression Metadata)? ProbeEntry(BaseObjectCreationExpressionSyntax collection,string text)
    {
        var parsed=SyntaxFactory.ParseExpression("new EntryProbe(){"+text+"}") as ObjectCreationExpressionSyntax;
        if(parsed?.Initializer?.Expressions is not {Count:1} values||parsed.ContainsDiagnostics||values[0] is not InitializerExpressionSyntax entry||!entry.IsKind(SyntaxKind.ComplexElementInitializerExpression)||collection.Initializer==null)return null;
        var annotation=new SyntaxAnnotation();var replacement=collection.WithInitializer(SyntaxFactory.InitializerExpression(SyntaxKind.CollectionInitializerExpression,collection.Initializer.OpenBraceToken,SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(entry.WithAdditionalAnnotations(annotation)),collection.Initializer.CloseBraceToken));
        var tree=Model.SyntaxTree.WithRootAndOptions(Model.SyntaxTree.GetRoot().ReplaceNode(collection,replacement),Model.SyntaxTree.Options);var compiler=context.Binding.Value.Compilation.ReplaceSyntaxTree(Model.SyntaxTree,tree);var model=compiler.GetSemanticModel(tree);var actual=(InitializerExpressionSyntax)tree.GetRoot().GetAnnotatedNodes(annotation).Single();
        if(model.GetCollectionInitializerSymbolInfo(actual).Symbol is not IMethodSymbol add||model.GetDiagnostics(actual.Span).Any(d=>d.Severity==DiagnosticSeverity.Error))return null;
        var metadata=new SourceBehaviorSchema(adapter,compiler,tree.FilePath).DescribeEntry(actual);
        return metadata==null?null:(add,metadata);
    }
    public string? InitializerEntryBinding(string text,string entry)=>Expression(text) is BaseObjectCreationExpressionSyntax collection&&ProbeEntry(collection,entry) is{} probe?EntrySignature(probe.Method):null;
    public IReadOnlyList<InitializerEntryOverload> InitializerEntryOverloads(string text)
    {
        if(Expression(text) is not BaseObjectCreationExpressionSyntax {Initializer:{} initializer} collection||initializer.Expressions.Any(e=>e is AssignmentExpressionSyntax)||Model.GetTypeInfo(collection).Type is not INamedTypeSymbol type)return [];
        var result=new List<InitializerEntryOverload>();
        foreach(var method in Model.LookupSymbols(collection.SpanStart,type,"Add").OfType<IMethodSymbol>().Where(m=>!m.IsStatic&&!m.IsGenericMethod&&m.Parameters.Length>0&&!m.Parameters.Any(p=>p.RefKind!=RefKind.None)&&m.Parameters.Any(p=>Child(Element(p.Type)))))
        {
            var seeds=method.Parameters.ToDictionary(p=>p.Name,p=>p.IsParams&&p.Type is IArrayTypeSymbol array?"new "+array.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+"[] { }":p.HasExplicitDefaultValue?"("+p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+")("+(Default(p)=="default"?"default("+p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+")":Default(p))+")":"default("+p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+")");
            var entry="{"+string.Join(", ",method.Parameters.Select(p=>seeds[p.Name]))+"}";
            if(ProbeEntry(collection,entry) is not{} probe||EntrySignature(method)!=EntrySignature(probe.Method))continue;
            result.Add(new(EntrySignature(probe.Method),"Add("+string.Join(", ",probe.Metadata.Parameters.Select(p=>p.Name+": "+p.Type))+")",probe.Metadata,seeds));
        }
        return result.DistinctBy(o=>o.Signature).OrderBy(o=>o.Label,StringComparer.Ordinal).ToArray();
    }
    bool AcceptEntry(ExpressionSyntax owner,string? collectionText,string text)
    {
        var parsed=SyntaxFactory.ParseExpression("new EntryProbe(){"+text+"}") as ObjectCreationExpressionSyntax;
        if(parsed?.Initializer?.Expressions is not {Count:1} expressions||parsed.ContainsDiagnostics||expressions[0] is not InitializerExpressionSyntax entry||!entry.IsKind(SyntaxKind.ComplexElementInitializerExpression))return false;
        var collection=owner.DescendantNodesAndSelf().OfType<BaseObjectCreationExpressionSyntax>().SingleOrDefault(e=>e.ToString()==(collectionText??owner.ToString()));if(collection?.Initializer==null)return false;
        var annotation=new SyntaxAnnotation();var replacement=collection.WithInitializer(SyntaxFactory.InitializerExpression(SyntaxKind.CollectionInitializerExpression,collection.Initializer.OpenBraceToken,SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(entry.WithAdditionalAnnotations(annotation)),collection.Initializer.CloseBraceToken));
        var tree=Model.SyntaxTree.WithRootAndOptions(Model.SyntaxTree.GetRoot().ReplaceNode(collection,replacement),Model.SyntaxTree.Options);var compiler=context.Binding.Value.Compilation.ReplaceSyntaxTree(Model.SyntaxTree,tree);var model=compiler.GetSemanticModel(tree);var actual=(InitializerExpressionSyntax)tree.GetRoot().GetAnnotatedNodes(annotation).Single();
        return model.GetCollectionInitializerSymbolInfo(actual).Symbol is IMethodSymbol add&&add.Parameters.Any(p=>Child(Element(p.Type)))&&!add.Parameters.Any(p=>p.IsParams&&!Child(Element(p.Type)))&&!model.GetDiagnostics(actual.Span).Any(d=>d.Severity==DiagnosticSeverity.Error);
    }
    SourceExpression? DescribeEntry(InitializerExpressionSyntax entry)
    {
        if(Model.GetCollectionInitializerSymbolInfo(entry).Symbol is not IMethodSymbol method)return null;
        var children=method.Parameters.Where(p=>Child(Element(p.Type))).ToArray();
        if(children.Length==0||method.Parameters.Any(p=>p.IsParams&&!Child(Element(p.Type)))||entry.Expressions.Count>method.Parameters.Length&&!method.Parameters.Last().IsParams)return null;
        if(children.Any(p=>!p.IsOptional&&!p.IsParams&&p.Ordinal>=entry.Expressions.Count))return null;
        var names=new List<string>();var expanded=0;
        for(var i=0;i<entry.Expressions.Count;i++)
        {
            var parameter=method.Parameters[Math.Min(i,method.Parameters.Length-1)];
            var isExpanded=parameter.IsParams&&Model.GetTypeInfo(entry.Expressions[i]).ConvertedType is not IArrayTypeSymbol;
            names.Add(parameter.Name+(isExpanded?"["+expanded+++"]":""));
        }
        return new("InitializerEntry","Add \u00b7 "+method.ContainingType.Name,false,method.Parameters.Select(p=>new SourceParameter(p.Name,TypeName(p.Type),p.Type.TypeKind==TypeKind.Enum&&p.HasExplicitDefaultValue&&p.ExplicitDefaultValue!=null?"("+p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+")"+Default(p):Default(p))).ToArray(),names.ToArray(),children.Select(child=>new ChildArgument(child.Name,TypeName(Element(child.Type)),child.Type is IArrayTypeSymbol,child.IsParams,Collection(child.Type)||child.IsOptional?0:1){CollectionType=child.Type is not IArrayTypeSymbol&&Collection(child.Type)?child.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat):null}).ToArray()){RequiresCompiled=true};
    }
}
