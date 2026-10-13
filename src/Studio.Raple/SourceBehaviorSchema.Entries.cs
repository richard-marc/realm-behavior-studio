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
        return model.GetCollectionInitializerSymbolInfo(actual).Symbol is IMethodSymbol add&&add.Parameters.Count(p=>Child(p.Type)&&!Collection(p.Type))==1&&!add.Parameters.Any(p=>p.IsParams||Child(p.Type)&&p.IsOptional||Collection(p.Type)&&Child(Element(p.Type)))&&!model.GetDiagnostics(actual.Span).Any(d=>d.Severity==DiagnosticSeverity.Error);
    }
    SourceExpression? DescribeEntry(InitializerExpressionSyntax entry)
    {
        if(Model.GetCollectionInitializerSymbolInfo(entry).Symbol is not IMethodSymbol method)return null;
        var children=method.Parameters.Where(p=>Child(p.Type)&&!Collection(p.Type)).ToArray();
        if(children.Length!=1||children[0].IsOptional||method.Parameters.Any(p=>p.IsParams||Collection(p.Type)&&Child(Element(p.Type)))||entry.Expressions.Count>method.Parameters.Length)return null;
        var child=children.Single();
        return new("InitializerEntry","Add · "+method.ContainingType.Name,false,method.Parameters.Take(entry.Expressions.Count).Select(p=>new SourceParameter(p.Name,TypeName(p.Type),Default(p))).ToArray(),method.Parameters.Take(entry.Expressions.Count).Select(p=>p.Name).ToArray(),[new(child.Name,TypeName(child.Type),false,false,1)]){RequiresCompiled=true};
    }
}
