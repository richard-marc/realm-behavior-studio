using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;
internal sealed partial class SourceBehaviorSchema
{
    bool AcceptEntry(ExpressionSyntax owner,string? collectionText,string text)
    {
        var parsed=SyntaxFactory.ParseExpression("new EntryProbe(){"+text+"}") as ObjectCreationExpressionSyntax;
        if(parsed?.Initializer?.Expressions is not {Count:1} expressions||parsed.ContainsDiagnostics||expressions[0] is not InitializerExpressionSyntax entry||!entry.IsKind(SyntaxKind.ComplexElementInitializerExpression))return false;
        var collection=owner.DescendantNodesAndSelf().OfType<BaseObjectCreationExpressionSyntax>().SingleOrDefault(e=>e.ToString()==(collectionText??owner.ToString()));if(collection?.Initializer==null)return false;
        var annotation=new SyntaxAnnotation();var replacement=collection.WithInitializer(collection.Initializer.WithExpressions(SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(entry.WithAdditionalAnnotations(annotation))));
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
