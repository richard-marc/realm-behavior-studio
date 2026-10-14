using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Raple;
internal sealed partial class SourceBehaviorSchema
{
    internal bool StableDashReference(string text)
    {
        var expression = Expression(text); if (expression == null) return false;
        var symbol = Model.GetSymbolInfo(expression).Symbol;
        if (symbol is IFieldSymbol field) return field.IsReadOnly && field.Type.ToDisplayString() == "TKR.WorldServer.logic.behaviors.Dash";
        if (symbol is not ILocalSymbol { RefKind: RefKind.None } local || local.Type.ToDisplayString() != "TKR.WorldServer.logic.behaviors.Dash") return false;
        var declaration = local.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax();
        var scope = declaration?.Ancestors().OfType<BlockSyntax>().FirstOrDefault(); if (scope == null) return false;
        foreach (var use in scope.DescendantNodes().OfType<IdentifierNameSyntax>().Where(n => SymbolEqualityComparer.Default.Equals(Model.GetSymbolInfo(n).Symbol, local)))
        {
            var ancestors = use.Ancestors().TakeWhile(n => n != scope).ToArray();
            if (ancestors.OfType<AssignmentExpressionSyntax>().Any(a => a.Left.Span.Contains(use.Span)) ||
                ancestors.Any(n => n is RefExpressionSyntax) ||
                ancestors.OfType<ArgumentSyntax>().Any(a => a.RefKindKeyword.RawKind != 0)) return false;
        }
        return true;
    }
}
