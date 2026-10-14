using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;
internal sealed partial class SourceBehaviorSchema
{
    static bool IsIndexedEntry(ExpressionSyntax? expression)=>expression is AssignmentExpressionSyntax {Left:ImplicitElementAccessSyntax};
    static IMethodSymbol? EntryMethod(SemanticModel model,ExpressionSyntax expression)=>expression is InitializerExpressionSyntax initializer?EntryMethod(model,initializer):expression is AssignmentExpressionSyntax assignment&&model.GetSymbolInfo(assignment.Left).Symbol is IPropertySymbol {IsIndexer:true} property?property.SetMethod:null;
    static IReadOnlyList<ExpressionSyntax> NativeEntryInputs(ExpressionSyntax expression)=>expression is InitializerExpressionSyntax initializer?initializer.Expressions.ToArray():expression is AssignmentExpressionSyntax {Left:ImplicitElementAccessSyntax index} assignment?index.ArgumentList.Arguments.Select(a=>a.Expression).Append(assignment.Right).ToArray():[];
    static AssignmentExpressionSyntax? TryIndexedEntry(string text)
    {
        var parsed=SyntaxFactory.ParseExpression("new EntryProbe(){"+text+"}") as ObjectCreationExpressionSyntax;
        return parsed?.Initializer?.Expressions is {Count:1} values&&!parsed.ContainsDiagnostics&&values[0] is AssignmentExpressionSyntax entry&&IsIndexedEntry(entry)?entry:null;
    }
    bool AcceptIndexedEntry(ExpressionSyntax owner,string? collectionText,string text)
    {
        var entry=TryIndexedEntry(text);var collection=owner.DescendantNodesAndSelf().OfType<BaseObjectCreationExpressionSyntax>().SingleOrDefault(e=>e.ToString()==(collectionText??owner.ToString()));
        if(entry==null||collection==null)return false;
        var annotation=new SyntaxAnnotation();var initializer=collection.Initializer??SyntaxFactory.InitializerExpression(SyntaxKind.ObjectInitializerExpression);
        var replacement=collection.WithInitializer(SyntaxFactory.InitializerExpression(SyntaxKind.ObjectInitializerExpression,initializer.OpenBraceToken,SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(entry.WithAdditionalAnnotations(annotation)),initializer.CloseBraceToken));
        var tree=Model.SyntaxTree.WithRootAndOptions(Model.SyntaxTree.GetRoot().ReplaceNode(collection,replacement),Model.SyntaxTree.Options);var compiler=context.Binding.Value.Compilation.ReplaceSyntaxTree(Model.SyntaxTree,tree);var model=compiler.GetSemanticModel(tree);var actual=(AssignmentExpressionSyntax)tree.GetRoot().GetAnnotatedNodes(annotation).Single();
        return new SourceBehaviorSchema(adapter,compiler,tree.FilePath).DescribeIndexedEntry(actual)!=null&&!model.GetDiagnostics(actual.Span).Any(d=>d.Severity==DiagnosticSeverity.Error);
    }
    SourceExpression? DescribeIndexedEntry(AssignmentExpressionSyntax entry)
    {
        if(EntryMethod(Model,(ExpressionSyntax)entry) is not{} method||method.Parameters.Length<2||!Child(Element(method.Parameters.Last().Type))||method.Parameters.Take(method.Parameters.Length-1).Any(p=>Child(Element(p.Type))))return null;
        var value=method.Parameters.Last();var list=Collection(value.Type);var inputs=NativeEntryInputs(entry);
        if(inputs.Count!=method.Parameters.Length)return null;
        var indices=((ImplicitElementAccessSyntax)entry.Left).ArgumentList.Arguments.Select((arg,i)=>arg.NameColon?.Name.Identifier.ValueText??method.Parameters[i].Name).Append(value.Name).ToArray();
        return new("InitializerEntry","Item · "+method.ContainingType.Name,false,method.Parameters.Select(p=>new SourceParameter(p.Name,TypeName(p.Type),"required")).ToArray(),indices,[new(value.Name,TypeName(Element(value.Type)),value.Type is IArrayTypeSymbol,false,list?0:1){CollectionType=value.Type is not IArrayTypeSymbol&&list?value.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat):null}]){RequiresCompiled=true};
    }
    static IMethodSymbol? EntryMethod(SemanticModel model,InitializerExpressionSyntax entry)
    {
        if(model.GetCollectionInitializerSymbolInfo(entry).Symbol is not IMethodSymbol method)return null;
        if(!method.IsExtensionMethod||method.ReducedFrom!=null)return method;
        var collection=entry.Ancestors().OfType<BaseObjectCreationExpressionSyntax>().FirstOrDefault();
        return collection==null?null:ModelType(model,collection) is{} type?method.ReduceExtensionMethod(type):null;
    }
    static ITypeSymbol? ModelType(SemanticModel model,ExpressionSyntax expression)=>model.GetTypeInfo(expression).Type??model.GetTypeInfo(expression).ConvertedType;
    static string EntrySignature(IMethodSymbol method)=>ClipboardScope(method)+"|"+method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    (IMethodSymbol Method,SourceExpression Metadata)? ProbeEntry(BaseObjectCreationExpressionSyntax collection,string text)
    {
        var parsed=SyntaxFactory.ParseExpression("new EntryProbe(){"+text+"}") as ObjectCreationExpressionSyntax;
        if(parsed?.Initializer?.Expressions is not {Count:1} values||parsed.ContainsDiagnostics||!(values[0] is InitializerExpressionSyntax {RawKind:(int)SyntaxKind.ComplexElementInitializerExpression}||IsIndexedEntry(values[0])))return null;
        var entry=values[0];
        var initializer=collection.Initializer??SyntaxFactory.InitializerExpression(SyntaxKind.CollectionInitializerExpression);
        var annotation=new SyntaxAnnotation();var replacement=collection.WithInitializer(SyntaxFactory.InitializerExpression(IsIndexedEntry(entry)?SyntaxKind.ObjectInitializerExpression:SyntaxKind.CollectionInitializerExpression,initializer.OpenBraceToken,SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(entry.WithAdditionalAnnotations(annotation)),initializer.CloseBraceToken));
        var tree=Model.SyntaxTree.WithRootAndOptions(Model.SyntaxTree.GetRoot().ReplaceNode(collection,replacement),Model.SyntaxTree.Options);var compiler=context.Binding.Value.Compilation.ReplaceSyntaxTree(Model.SyntaxTree,tree);var model=compiler.GetSemanticModel(tree);var actual=(ExpressionSyntax)tree.GetRoot().GetAnnotatedNodes(annotation).Single();
        if(EntryMethod(model,actual) is not IMethodSymbol add||model.GetDiagnostics(actual.Span).Any(d=>d.Severity==DiagnosticSeverity.Error))return null;
        var bound=new SourceBehaviorSchema(adapter,compiler,tree.FilePath);var metadata=actual is AssignmentExpressionSyntax indexed?bound.DescribeIndexedEntry(indexed):bound.DescribeEntry((InitializerExpressionSyntax)actual);
        return metadata==null?null:(add,metadata);
    }
    public string? InitializerEntryBinding(string text,string entry)=>Expression(text) is BaseObjectCreationExpressionSyntax collection&&ProbeEntry(collection,entry) is{} probe?EntrySignature(probe.Method):null;
    public IReadOnlyList<InitializerEntryOverload> InitializerEntryOverloads(string text)
    {
        if(Expression(text) is not BaseObjectCreationExpressionSyntax collection||collection.Initializer?.Expressions.Any(e=>e is AssignmentExpressionSyntax&&!IsIndexedEntry(e))==true||Model.GetTypeInfo(collection).Type is not INamedTypeSymbol type||!Collection(type)||!Child(Element(type)))return [];
        var result=new List<InitializerEntryOverload>();
        var indexers=new List<IPropertySymbol>();for(var current=type;current!=null;current=current.BaseType)indexers.AddRange(current.GetMembers().OfType<IPropertySymbol>().Where(p=>p.IsIndexer&&p.SetMethod!=null&&Model.IsAccessible(collection.SpanStart,p.SetMethod)));
        var methods=Model.LookupSymbols(collection.SpanStart,type,"Add",includeReducedExtensionMethods:true).OfType<IMethodSymbol>().Where(m=>collection.Initializer?.Expressions.Any(IsIndexedEntry)!=true).Concat(indexers.Where(p=>collection.Initializer?.Expressions.Any(e=>e is not AssignmentExpressionSyntax)!=true).Select(p=>p.SetMethod!));
        foreach(var method in methods.Where(m=>!m.IsStatic&&(!m.IsGenericMethod||m.TypeArguments.All(t=>t.TypeKind!=TypeKind.TypeParameter))&&m.Parameters.Length>0&&!m.Parameters.Any(p=>p.RefKind!=RefKind.None)&&m.Parameters.Any(p=>Child(Element(p.Type)))))
        {
            var seeds=method.Parameters.ToDictionary(p=>p.Name,p=>p.IsParams&&p.Type is IArrayTypeSymbol array?"new "+array.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+"[] { }":p.HasExplicitDefaultValue?"("+p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+")("+(Default(p)=="default"?"default("+p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+")":Default(p))+")":"default("+p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)+")");
            var indexed=method.MethodKind==MethodKind.PropertySet;var arguments=method.Parameters.Select(p=>seeds[p.Name]).ToArray();var entry=indexed?"["+string.Join(", ",arguments.Take(arguments.Length-1))+"] = "+arguments[^1]:"{"+string.Join(", ",arguments)+"}";
            if(ProbeEntry(collection,entry) is not{} probe||EntrySignature(method)!=EntrySignature(probe.Method))continue;
            result.Add(new(EntrySignature(probe.Method),(indexed?"Item(":"Add(")+string.Join(", ",probe.Metadata.Parameters.Select(p=>p.Name+": "+p.Type))+")",probe.Metadata,seeds){IsIndexer=indexed});
        }
        return result.DistinctBy(o=>o.Signature).OrderBy(o=>o.Label,StringComparer.Ordinal).ToArray();
    }
    bool AcceptEntry(ExpressionSyntax owner,string? collectionText,string text)
    {
        var parsed=SyntaxFactory.ParseExpression("new EntryProbe(){"+text+"}") as ObjectCreationExpressionSyntax;
        if(parsed?.Initializer?.Expressions is not {Count:1} expressions||parsed.ContainsDiagnostics||expressions[0] is not InitializerExpressionSyntax entry||!entry.IsKind(SyntaxKind.ComplexElementInitializerExpression))return false;
        var collection=owner.DescendantNodesAndSelf().OfType<BaseObjectCreationExpressionSyntax>().SingleOrDefault(e=>e.ToString()==(collectionText??owner.ToString()));if(collection==null)return false;
        var initializer=collection.Initializer??SyntaxFactory.InitializerExpression(SyntaxKind.CollectionInitializerExpression);
        var annotation=new SyntaxAnnotation();var replacement=collection.WithInitializer(SyntaxFactory.InitializerExpression(SyntaxKind.CollectionInitializerExpression,initializer.OpenBraceToken,SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(entry.WithAdditionalAnnotations(annotation)),initializer.CloseBraceToken));
        var tree=Model.SyntaxTree.WithRootAndOptions(Model.SyntaxTree.GetRoot().ReplaceNode(collection,replacement),Model.SyntaxTree.Options);var compiler=context.Binding.Value.Compilation.ReplaceSyntaxTree(Model.SyntaxTree,tree);var model=compiler.GetSemanticModel(tree);var actual=(InitializerExpressionSyntax)tree.GetRoot().GetAnnotatedNodes(annotation).Single();
        return EntryMethod(model,actual) is IMethodSymbol add&&add.Parameters.Any(p=>Child(Element(p.Type)))&&!add.Parameters.Any(p=>p.IsParams&&!Child(Element(p.Type)))&&!model.GetDiagnostics(actual.Span).Any(d=>d.Severity==DiagnosticSeverity.Error);
    }
    SourceExpression? DescribeEntry(InitializerExpressionSyntax entry)
    {
        if(EntryMethod(Model,entry) is not IMethodSymbol method)return null;
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
