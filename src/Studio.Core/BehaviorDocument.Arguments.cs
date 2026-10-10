using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Core;
public sealed partial class BehaviorDocument
{
    public bool CanEditChildren(Guid id)
    {
        try { ChildContainer(id); return true; } catch (ArgumentException) { return false; }
    }
    public bool CanOrganize(Guid id) => Find(id).Kind is not ("ArgumentGroup" or "Opaque") && Parent(id) is { } parent && CanEditChildren(parent.Id) && ChildContainer(parent.Id).Id == parent.Id;
    public bool AcceptsChild(Guid id, string expression)
    {
        try
        {
            var container = ChildContainer(id); var slot = Slot(container);
            var owner = container.Kind == "ArgumentGroup" ? Parent(container.Id)! : container;
            return (slot.IsArray || container.Children.Count == 0) && (schema?.AcceptsChild(Expression(owner), slot.Name, expression, container.Kind == "ArgumentGroup" ? Expression(container) : null) ?? true);
        }
        catch (ArgumentException) { return false; }
    }
    public BehaviorNode ChildContainer(Guid id)
    {
        var node = Find(id);
        if (node.Kind == "ArgumentGroup") return node;
        var slots = schema?.ChildArguments(Expression(node)) ?? (node.Kind == "State" ? [new ChildArgument("children", "IStateChildren", true, true)] : []);
        if (slots.Count != 1) throw new ArgumentException("Select a typed argument list or phase with one known child slot.");
        var group = node.Children.SingleOrDefault(n => n.Kind == "ArgumentGroup");
        if (group != null) return group;
        if (!slots[0].IsParams && slots[0].IsArray) throw new ArgumentException("Create an explicit array for this child parameter first.");
        return node;
    }
    ChildArgument Slot(BehaviorNode container) => container.Argument ?? schema?.ChildArguments(Expression(container)).Single() ?? new("children", "IStateChildren", true, true);
    bool PhaseContainer(BehaviorNode node) => node.Kind == "State" || node.Kind == "ArgumentGroup" && Parent(node.Id)?.Kind == "State";
    InitializerExpressionSyntax Initializer(ExpressionSyntax expression) => expression switch
    {
        ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer,
        ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer,
        _ => throw new ArgumentException("This array requires source editing.")
    };
    ExpressionSyntax NodeExpression(SyntaxNode tree, BehaviorNode node) => tree.DescendantNodes().OfType<ExpressionSyntax>().Single(n => n.SpanStart == node.Start && n.Span.Length == node.Length);
    IEnumerable<ExpressionSyntax> ContainerExpressions(ExpressionSyntax expression, BehaviorNode node) => node.Kind == "ArgumentGroup" ? Initializer(expression).Expressions : ((ObjectCreationExpressionSyntax)expression).ArgumentList!.Arguments.Select(a => a.Expression);
    ExpressionSyntax WithSeparator(ExpressionSyntax expression, SeparatedSyntaxList<ExpressionSyntax> list, int index)
    {
        if (index >= list.SeparatorCount) return expression;
        var separator = list.GetSeparator(index);
        return expression.WithTrailingTrivia(expression.GetTrailingTrivia().Concat(separator.LeadingTrivia).Concat(separator.TrailingTrivia));
    }
    Dictionary<Guid, ExpressionSyntax> ChildExpressions(SyntaxNode tree)
    {
        var result = new Dictionary<Guid, ExpressionSyntax>();
        foreach (var parent in Entities.SelectMany(e => e.Root.Descendants()).Where(n => CanEditChildren(n.Id) && ChildContainer(n.Id).Id == n.Id))
        {
            var expression = NodeExpression(tree, parent);
            if (parent.Kind == "ArgumentGroup")
            {
                var list = Initializer(expression).Expressions;
                foreach (var child in parent.Children) { var index = list.IndexOf(list.Single(e => e.SpanStart == child.Start)); result[child.Id] = WithSeparator(list[index], list, index); }
            }
            else
            {
                var list = ((ObjectCreationExpressionSyntax)expression).ArgumentList!.Arguments;
                foreach (var child in parent.Children) { var index = list.IndexOf(list.Single(a => a.Expression.SpanStart == child.Start)); result[child.Id] = ArgumentWithSeparatorTrivia(list[index], list, index).Expression.WithTriviaFrom(ArgumentWithSeparatorTrivia(list[index], list, index)); }
            }
        }
        return result;
    }
    ExpressionSyntax RewriteContainer(ExpressionSyntax current, ExpressionSyntax original, BehaviorNode parent, IReadOnlyList<ExpressionSyntax> children)
    {
        if (parent.Kind == "ArgumentGroup")
        {
            var initializer = Initializer(current).WithExpressions(SyntaxFactory.SeparatedList(children));
            if (current is ArrayCreationExpressionSyntax array) return array.WithInitializer(initializer);
            var implicitArray = (ImplicitArrayCreationExpressionSyntax)current;
            if (children.Count > 0) return implicitArray.WithInitializer(initializer);
            var element = schema?.ArrayElementType(Expression(parent)) ?? throw new ArgumentException("Cannot infer the empty array's native element type.");
            return SyntaxFactory.ArrayCreationExpression(SyntaxFactory.ArrayType(SyntaxFactory.ParseTypeName("global::" + element), SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier(SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(SyntaxFactory.OmittedArraySizeExpression())))), initializer).WithNewKeyword(SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space)).WithTriviaFrom(current);
        }
        var creation = (ObjectCreationExpressionSyntax)current; var source = (ObjectCreationExpressionSyntax)original;
        var slot = Slot(parent);
        if (!slot.IsArray)
        {
            var index = source.ArgumentList!.Arguments.IndexOf(source.ArgumentList.Arguments.Single(a => parent.Children.Any(c => c.Start == a.Expression.SpanStart)));
            return creation.WithArgumentList(creation.ArgumentList!.WithArguments(creation.ArgumentList.Arguments.Replace(creation.ArgumentList.Arguments[index], creation.ArgumentList.Arguments[index].WithExpression(children.Single()))));
        }
        var retained = source.ArgumentList!.Arguments.Select((a, i) => (a, i)).Where(pair => !parent.Children.Any(c => c.Start == pair.a.Expression.SpanStart)).Select(pair => ArgumentWithSeparatorTrivia(creation.ArgumentList!.Arguments[pair.i], source.ArgumentList.Arguments, pair.i));
        return creation.WithArgumentList(creation.ArgumentList!.WithArguments(SyntaxFactory.SeparatedList(retained.Concat(children.Select(SyntaxFactory.Argument)))));
    }
}
