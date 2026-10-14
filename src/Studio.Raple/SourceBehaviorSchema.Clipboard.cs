using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;
internal sealed partial class SourceBehaviorSchema
{
    static string ClipboardScope(ISymbol symbol)
    {
        if(symbol.OriginalDefinition.GetDocumentationCommentId() is {} documented)return documented;
        var parent=symbol.ContainingSymbol==null?"":ClipboardScope(symbol.ContainingSymbol);
        var identity=parent+"/"+symbol.Kind+"/"+symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if(symbol is IMethodSymbol {MethodKind:MethodKind.AnonymousFunction or MethodKind.LocalFunction}&&symbol.DeclaringSyntaxReferences.FirstOrDefault() is {} declaration)
            identity+="/"+Path.GetFullPath(declaration.SyntaxTree.FilePath).ToUpperInvariant()+"/"+declaration.Span.Start;
        return identity;
    }
    public IReadOnlyList<ClipboardSymbol> ClipboardSymbols(string text)
    {
        var expression=(SyntaxNode?)Expression(text);
        if(expression==null&&position>=0)
        {
            var candidate=context.Binding.Value.Tree.GetRoot().FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(position,text.Length),getInnermostNodeForTie:true);
            if(candidate.SpanStart==position&&candidate.Span.Length==text.Length&&candidate.ToString()==text)expression=candidate;
        }
        if(expression==null)throw new ArgumentException("Clipboard binding requires its actual source expression.");
        var result=new List<ClipboardSymbol>();
        foreach(var node in expression.DescendantNodesAndSelf())
        {
            if(node is not (SimpleNameSyntax or BaseObjectCreationExpressionSyntax or InvocationExpressionSyntax)&&node is not InitializerExpressionSyntax {RawKind:(int)SyntaxKind.ComplexElementInitializerExpression}||node.Parent is NameColonSyntax)continue;
            var symbol=node is InitializerExpressionSyntax initializer?EntryMethod(Model,initializer):Model.GetSymbolInfo(node).Symbol;
            if(symbol==null)continue;
            var key=symbol.OriginalDefinition.GetDocumentationCommentId();
            if(key==null&&symbol is IParameterSymbol parameter)key=ClipboardScope(parameter.ContainingSymbol)+"/parameter/"+parameter.Ordinal;
            if(key==null&&symbol is ILocalSymbol local)key=ClipboardScope(local.ContainingSymbol)+"/local/"+local.Name;
            if(symbol is ILocalSymbol or IParameterSymbol&&symbol.DeclaringSyntaxReferences.FirstOrDefault() is {} declaration)
            {
                key+="/"+Path.GetFullPath(declaration.SyntaxTree.FilePath).ToUpperInvariant()+"/"+StudioSourceDraft.Hash(declaration.GetSyntax().ToString());
                if(symbol.ContainingSymbol is IMethodSymbol {MethodKind:MethodKind.AnonymousFunction})key+="/lambda/"+declaration.GetSyntax().Ancestors().OfType<AnonymousFunctionExpressionSyntax>().First().SpanStart;
            }
            key??=ClipboardScope(symbol);
            result.Add(new(0,node.SpanStart-expression.SpanStart,node.Span.Length,key));
        }
        return result;
    }
}
