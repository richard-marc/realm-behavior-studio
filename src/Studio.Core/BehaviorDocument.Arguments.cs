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
    public bool IsScalarChildSlot(Guid id) => CanEditChildren(id) && !Slot(ChildContainer(id)).IsCollection;
    public bool AcceptsChild(Guid id, string expression, bool replace = false)
    {
        try
        {
            var container = ChildContainer(id); var slot = Slot(container);
            var owner = ChildOwner(container);
            return (!replace || !slot.IsCollection) && (replace || slot.IsCollection || container.Children.Count == 0) && (OwningEntity(container.Id).Definition?.Collection!=null&&Parent(container.Id)==null?SchemaAt(container)?.AcceptsDefinitionElement(Expression(container),expression)==true:SchemaAt(owner)?.AcceptsChild(Expression(owner), slot.Name, expression, container.Kind == "ArgumentGroup" && !container.ArgumentFromCall ? Expression(container) : null) ?? true);
        }
        catch (ArgumentException) { return false; }
    }
    public BehaviorNode ChildContainer(Guid id)
    {
        var node = Find(id);
        if (node.Kind == "ArgumentGroup") return !node.ArgumentRequiresSource ? node : throw new ArgumentException("This generated/shared child collection requires source editing.");
        var slots = SchemaAt(node)?.ChildArguments(Expression(node)) ?? (node.Kind == "State" ? [new ChildArgument("children", "IStateChildren", true, true)] : []);
        if (slots.Count != 1) throw new ArgumentException("Select a typed argument list or phase with one known child slot.");
        var groups=node.Children.Where(n=>n.Kind=="ArgumentGroup").ToArray();if(groups.Length>1)throw new ArgumentException("Select the constructor or member child list to edit.");
        var group = groups.SingleOrDefault();
        if (group != null) return group;
        if (SyntaxFactory.ParseExpression(Expression(node)) is BaseObjectCreationExpressionSyntax { Initializer.Expressions.Count: > 0 }) throw new ArgumentException("This source initializer may add hidden children or references. Open Source to edit this region.");
        if (!slots[0].IsParams && slots[0].IsCollection) throw new ArgumentException("Create an explicit collection for this child parameter first.");
        return node;
    }
    ChildArgument Slot(BehaviorNode container) => container.Argument ?? SchemaAt(container)?.ChildArguments(Expression(container)).Single() ?? new("children", "IStateChildren", true, true);
    BehaviorNode ChildOwner(BehaviorNode container)
    {
        var owner=container.Kind=="ArgumentGroup"?Parent(container.Id)??container:container;
        while(owner.IsMemberInitializer)owner=Parent(owner.Id)??throw new ArgumentException("The member initializer has no native owner.");return owner;
    }
    bool PhaseContainer(BehaviorNode node) => node.Kind == "State" || node.Kind == "ArgumentGroup"&&node.Argument?.IsMember!=true && Parent(node.Id)?.Kind == "State";
    static IReadOnlyList<SyntaxNode>? ListItems(ExpressionSyntax expression) => expression switch
    {
        CollectionExpressionSyntax collection => collection.Elements.Select(e => e is ExpressionElementSyntax value ? (SyntaxNode)value.Expression : e).ToArray(),
        ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions.Cast<SyntaxNode>().ToArray(),
        ImplicitArrayCreationExpressionSyntax array => array.Initializer.Expressions.Cast<SyntaxNode>().ToArray(),
        ObjectCreationExpressionSyntax { Initializer: { } initializer } when SimpleCollectionInitializer(initializer) => initializer.Expressions.Cast<SyntaxNode>().ToArray(),
        ImplicitObjectCreationExpressionSyntax { Initializer: { } initializer } when SimpleCollectionInitializer(initializer) => initializer.Expressions.Cast<SyntaxNode>().ToArray(),
        InitializerExpressionSyntax initializer when SimpleCollectionInitializer(initializer)=>initializer.Expressions.Cast<SyntaxNode>().ToArray(),
        _ => null
    };
    static bool SimpleCollectionInitializer(InitializerExpressionSyntax initializer) => (initializer.IsKind(SyntaxKind.CollectionInitializerExpression) || initializer.IsKind(SyntaxKind.ObjectInitializerExpression) && initializer.Expressions.Count == 0) && !initializer.Expressions.Any(e=>e is AssignmentExpressionSyntax);
    bool CollectionContainer(BehaviorNode node) => node.Kind == "ArgumentGroup" && !node.ArgumentFromCall && SyntaxFactory.ParseExpression(Expression(node)) is CollectionExpressionSyntax;
    static string ChildListCount(IReadOnlyList<BehaviorNode> children)
    {
        var spreads = children.Count(n => n.Kind == "Spread");
        return spreads == 0 ? children.Count.ToString() : (children.Count - spreads) + " + " + (spreads == 1 ? "spread" : spreads + " spreads");
    }
    static ExpressionSyntax ChildValue(SyntaxNode item) => item is SpreadElementSyntax spread ? SyntaxFactory.ParseExpression(spread.ToFullString()) : (ExpressionSyntax)item;
    InitializerExpressionSyntax Initializer(ExpressionSyntax expression) => expression switch
    {
        ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer,
        ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer,
        ObjectCreationExpressionSyntax { Initializer: { } initializer } when SimpleCollectionInitializer(initializer) => initializer,
        ImplicitObjectCreationExpressionSyntax { Initializer: { } initializer } when SimpleCollectionInitializer(initializer) => initializer,
        InitializerExpressionSyntax initializer when SimpleCollectionInitializer(initializer)=>initializer,
        _ => throw new ArgumentException("This collection requires source editing.")
    };
    ExpressionSyntax NodeExpression(SyntaxNode tree, BehaviorNode node) => tree.DescendantNodes().OfType<ExpressionSyntax>().Single(n => n.SpanStart == node.Start && n.Span.Length == node.Length);
    static ArgumentListSyntax CallArguments(ExpressionSyntax expression) => expression switch
    {
        BaseObjectCreationExpressionSyntax { ArgumentList: { } args } => args,
        InvocationExpressionSyntax call => call.ArgumentList,
        _ => throw new ArgumentException("This expression has no editable argument list.")
    };
    static ExpressionSyntax WithCallArguments(ExpressionSyntax expression, ArgumentListSyntax arguments) => expression switch
    {
        ObjectCreationExpressionSyntax constructor => constructor.WithArgumentList(arguments),
        ImplicitObjectCreationExpressionSyntax constructor => constructor.WithArgumentList(arguments),
        InvocationExpressionSyntax call => call.WithArgumentList(arguments),
        _ => throw new ArgumentException("This expression has no editable argument list.")
    };
    int[] CallSlotIndices(BehaviorNode node)
    {
        var owner = node.ArgumentFromCall ? Parent(node.Id)! : node;
        if(owner.Kind=="InitializerEntry")return EntryIndices(node);
        var names = SchemaAt(owner)!.ConstructorParameterNames(Expression(owner));
        return names.Select((name, index) => (name, index)).Where(p => p.name.Split('[')[0] == Slot(node).Name).Select(p => p.index).ToArray();
    }
    IEnumerable<SyntaxNode> ContainerExpressions(ExpressionSyntax expression, BehaviorNode node) => expression is InitializerExpressionSyntax entry&&(node.Kind=="InitializerEntry"||node.ArgumentFromCall&&Parent(node.Id)?.Kind=="InitializerEntry")?EntryIndices(node).Select(i=>entry.Expressions[i]):node.Kind != "ArgumentGroup" ? CallArguments(expression).Arguments.Select(a => a.Expression) : node.ArgumentFromCall ? CallSlotIndices(node).Select(i => CallArguments(expression).Arguments[i].Expression) : ListItems(expression) ?? throw new ArgumentException("Select an editable native child list.");
    int[] EntryIndices(BehaviorNode node)=>SchemaAt(ChildOwner(node))!.ConstructorParameterNames(Expression(ChildOwner(node))).Select((name,i)=>(name,i)).Where(p=>p.name==Slot(node).Name).Select(p=>p.i).ToArray();
    ExpressionSyntax WithSeparator(ExpressionSyntax expression, SeparatedSyntaxList<ExpressionSyntax> list, int index)
    {
        if (index >= list.SeparatorCount) return expression;
        var separator = list.GetSeparator(index);
        return expression.WithTrailingTrivia(expression.GetTrailingTrivia().Concat(separator.LeadingTrivia).Concat(separator.TrailingTrivia));
    }
    Dictionary<Guid, ExpressionSyntax> ChildExpressions(SyntaxNode tree)
    {
        var result = new Dictionary<Guid, ExpressionSyntax>();
        foreach (var parent in AllNodes.Where(n => CanEditChildren(n.Id) && ChildContainer(n.Id).Id == n.Id))
        {
            var expression = NodeExpression(tree, parent);
            if(expression is InitializerExpressionSyntax entry&&(parent.Kind=="InitializerEntry"||parent.ArgumentFromCall&&Parent(parent.Id)?.Kind=="InitializerEntry"))
            {
                foreach(var child in parent.Children){var index=entry.Expressions.IndexOf(entry.Expressions.Single(e=>e.SpanStart==child.Start));result[child.Id]=WithSeparator(entry.Expressions[index],entry.Expressions,index);}continue;
            }
            if (parent.Kind == "ArgumentGroup" && !parent.ArgumentFromCall)
            {
                if (expression is CollectionExpressionSyntax collection)
                {
                    foreach (var child in parent.Children)
                    {
                        var element = collection.Elements.Single(e => e.SpanStart == child.Start); var index = collection.Elements.IndexOf(element);
                        var value = ChildValue(element is ExpressionElementSyntax item ? item.Expression : element);
                        if (index < collection.Elements.SeparatorCount) { var separator = collection.Elements.GetSeparator(index); value = value.WithTrailingTrivia(value.GetTrailingTrivia().Concat(separator.LeadingTrivia).Concat(separator.TrailingTrivia)); }
                        result[child.Id] = value;
                    }
                }
                else
                {
                    var list = Initializer(expression).Expressions;
                    foreach (var child in parent.Children) { var index = list.IndexOf(list.Single(e => e.SpanStart == child.Start)); result[child.Id] = WithSeparator(list[index], list, index); }
                }
            }
            else
            {
                var list = CallArguments(expression).Arguments;
                foreach (var child in parent.Children) { var index = list.IndexOf(list.Single(a => a.Expression.SpanStart == child.Start)); result[child.Id] = ArgumentWithSeparatorTrivia(list[index], list, index).Expression.WithTriviaFrom(ArgumentWithSeparatorTrivia(list[index], list, index)); }
            }
        }
        return result;
    }
    ExpressionSyntax RewriteContainer(ExpressionSyntax current, ExpressionSyntax original, BehaviorNode parent, IReadOnlyList<ExpressionSyntax> children)
    {
        if (parent.Kind == "ArgumentGroup")
        {
            if (parent.ArgumentFromCall) return RewriteCallSlots(current, original, [(parent, children)]);
            if (current is CollectionExpressionSyntax collection)
                return collection.WithElements(SyntaxFactory.SeparatedList(children.Select(child => child is RangeExpressionSyntax { LeftOperand: null, RightOperand: { } operand } range ? (CollectionElementSyntax)SyntaxFactory.SpreadElement(range.OperatorToken, operand).WithTriviaFrom(child) : SyntaxFactory.ExpressionElement(child))));
            var initializer = Initializer(current).WithExpressions(SyntaxFactory.SeparatedList(children));
            if(current is InitializerExpressionSyntax)return initializer;
            if (current is ArrayCreationExpressionSyntax array) return array.WithInitializer(initializer);
            if (current is ObjectCreationExpressionSyntax constructor) return constructor.WithInitializer(initializer);
            if (current is ImplicitObjectCreationExpressionSyntax implicitConstructor) return implicitConstructor.WithInitializer(initializer);
            var implicitArray = (ImplicitArrayCreationExpressionSyntax)current;
            if (children.Count > 0) return implicitArray.WithInitializer(initializer);
            var element = SchemaAt(parent)?.ArrayElementType(Expression(parent)) ?? throw new ArgumentException("Cannot infer the empty array's native element type.");
            return SyntaxFactory.ArrayCreationExpression(SyntaxFactory.ArrayType(SyntaxFactory.ParseTypeName("global::" + element), SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier(SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(SyntaxFactory.OmittedArraySizeExpression())))), initializer).WithNewKeyword(SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space)).WithTriviaFrom(current);
        }
        if(parent.Kind=="InitializerEntry")return RewriteCallSlots(current,original,[(parent,children)]);
        var arguments = CallArguments(current); var source = CallArguments(original);
        var slot = Slot(parent);
        if (!slot.IsCollection)
            return RewriteCallSlots(current, original, [(parent, children)]);
        var retained = source.Arguments.Select((a, i) => (a, i)).Where(pair => !parent.Children.Any(c => c.Start == pair.a.Expression.SpanStart)).Select(pair => ArgumentWithSeparatorTrivia(arguments.Arguments[pair.i], source.Arguments, pair.i));
        return WithCallArguments(current, arguments.WithArguments(SyntaxFactory.SeparatedList(retained.Concat(children.Select(SyntaxFactory.Argument)))));
    }
    ExpressionSyntax RewriteCallSlots(ExpressionSyntax current, ExpressionSyntax original, IReadOnlyList<(BehaviorNode Node, IReadOnlyList<ExpressionSyntax> Children)> edits)
    {
        if(original is InitializerExpressionSyntax entry&&current is InitializerExpressionSyntax updated)
        {
            var expressions=updated.Expressions.ToArray();foreach(var edit in edits)
            {
                var indices=EntryIndices(edit.Node);if(indices.Length!=1||edit.Children.Count!=1)throw new ArgumentException("A native Add entry requires its one typed child input.");expressions[indices[0]]=edit.Children[0];
            }
            return updated.WithExpressions(SyntaxFactory.SeparatedList(expressions));
        }
        var source = CallArguments(original); var arguments = CallArguments(current).Arguments;
        var replacements = edits.Select(e => (e.Node, e.Children, Indices: CallSlotIndices(e.Node))).ToArray();
        var owner = edits[0].Node.ArgumentFromCall ? Parent(edits[0].Node.Id)! : edits[0].Node;
        var ownerSchema = SchemaAt(owner)!; var names = ownerSchema.ConstructorParameterNames(Expression(owner));
        var slots = ownerSchema.ChildArguments(Expression(owner));
        var normalize = replacements.Any(e => e.Indices.Length == 0 || !Slot(e.Node).IsCollection && e.Children.Count == 0);
        var result = new List<ArgumentSyntax>();
        for (var i = 0; i < arguments.Count; i++)
        {
            var replacement = replacements.FirstOrDefault(e => e.Indices.Contains(i));
            if (replacement.Node == null)
            {
                var name = names[i].Split('[')[0]; var slot = slots.FirstOrDefault(s => s.Name == name);
                if (normalize && slot?.IsParams == true && names[i].Contains('['))
                {
                    var indices = names.Select((n, j) => (n, j)).Where(p => p.n.Split('[')[0] == name).Select(p => p.j).ToArray();
                    if (indices[0] == i) result.Add(NamedArray(name, indices.Select(j => ArgumentWithSeparatorTrivia(arguments[j], source.Arguments, j).Expression.WithTriviaFrom(ArgumentWithSeparatorTrivia(arguments[j], source.Arguments, j))).ToArray()));
                }
                else result.Add(RetainedArgument(i, name));
                continue;
            }
            if (i != replacement.Indices[0]) continue;
            var replacementSlot = Slot(replacement.Node);
            if (replacementSlot.IsCollection)
            {
                if (normalize || !replacementSlot.IsParams || !names[i].Contains('['))
                {
                    var retained = RetainedArgument(i, replacementSlot.Name);
                    result.Add(retained.WithExpression(ArrayExpression(replacementSlot.Name, replacement.Children)).WithTriviaFrom(retained));
                }
                else result.AddRange(replacement.Children.Select(SyntaxFactory.Argument));
            }
            else if (replacement.Children.Count > 0)
            {
                var retained = RetainedArgument(i, replacementSlot.Name);
                result.Add(retained.WithExpression(replacement.Children.Single()).WithTriviaFrom(retained));
            }
        }
        foreach (var replacement in replacements.Where(e => e.Indices.Length == 0))
        {
            var slot = Slot(replacement.Node);
            if (slot.IsCollection) result.Add(NamedArray(slot.Name, replacement.Children));
            else foreach (var child in replacement.Children) result.Add(NamedArgument(slot.Name, child));
        }
        return WithCallArguments(current, CallArguments(current).WithArguments(SyntaxFactory.SeparatedList(result)));
        ExpressionSyntax ArrayExpression(string name, IReadOnlyList<ExpressionSyntax> children) => SyntaxFactory.ParseExpression(ownerSchema.ChildList(Expression(owner), name, children.Select(c => c.ToFullString()).ToArray()));
        ArgumentSyntax NamedArray(string name, IReadOnlyList<ExpressionSyntax> children) => NamedArgument(name, ArrayExpression(name, children));
        ArgumentSyntax RetainedArgument(int index, string name)
        {
            var retained = ArgumentWithSeparatorTrivia(arguments[index], source.Arguments, index);
            return normalize && retained.NameColon == null ? retained.WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(EscapeIdentifier(name)))) : retained;
        }
        static ArgumentSyntax NamedArgument(string name, ExpressionSyntax expression) => SyntaxFactory.Argument(expression).WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(EscapeIdentifier(name))));
    }
    static string EscapeIdentifier(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
    public Guid ReplaceChild(Guid parentId, string expression, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        var parent = ChildContainer(parentId); EditableContainer(parent);
        if (Slot(parent).IsCollection) throw new ArgumentException("Replace a scalar child slot; use list operations for a collection.");
        foreach (var child in parent.Children) Editable(child);
        var phases = parent.Children.SelectMany(n => n.Descendants()).Where(n => n.Kind == "State").Select(n => StateName(n.Id)).OfType<string>().ToArray();
        if (phases.Length > 0) LiteralReferences(OwningEntity(parent.Id), phases);
        ValidateChildExpression(expression); var (_, fragment) = ChildFragment(parent, [expression]); var replacement = fragment.Children.Single();
        RewriteChildren(new() { [parent.Id] = [replacement] }, new() { [replacement.Id] = SyntaxFactory.Argument(SyntaxFactory.ParseExpression(expression)) }, validate);
        return replacement.Id;
    }
}
