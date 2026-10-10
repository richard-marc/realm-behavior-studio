using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Core;

public record BehaviorClipboard(int Version, IReadOnlyList<string> Expressions);

public sealed partial class BehaviorDocument
{
    public IReadOnlyList<BehaviorNode> NormalizeSelection(IEnumerable<Guid> ids)
    {
        var selected = ids.Distinct().Select(Find).OrderBy(n => n.Start).ToArray();
        if (selected.Length == 0) throw new ArgumentException("Select at least one phase or action.");
        return selected.Where(n => !selected.Any(ancestor => ancestor.Id != n.Id && n.Path.StartsWith(ancestor.Path + "/", StringComparison.Ordinal))).ToArray();
    }
    void EditableContainer(BehaviorNode parent)
    {
        if (parent.Kind != "State") throw new ArgumentException("Choose a phase as the destination.");
        if (LockedNodes.Select(Find).Any(locked => parent.Path == locked.Path || parent.Path.StartsWith(locked.Path + "/", StringComparison.Ordinal)))
            throw new InvalidOperationException("The destination phase or an ancestor is locked.");
    }
    public BehaviorClipboard CopyNodes(IEnumerable<Guid> ids)
    {
        var nodes = NormalizeSelection(ids);
        foreach (var node in nodes) { StructuralParent(node); Editable(node); }
        return new(1, nodes.Select(Expression).ToArray());
    }
    public IReadOnlyList<Guid> PasteNodes(Guid parentId, int index, BehaviorClipboard clipboard, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        var parent = Find(parentId); EditableContainer(parent);
        if (index < 0 || index > parent.Children.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (clipboard.Version != 1 || clipboard.Expressions == null || clipboard.Expressions.Count is < 1 or > 256 || clipboard.Expressions.Sum(e => e?.Length ?? 0) > 2_000_000)
            throw new ArgumentException("Unsupported or oversized behavior clipboard.");
        foreach (var expression in clipboard.Expressions)
            if (expression == null || SyntaxFactory.ParseExpression(expression) is not ObjectCreationExpressionSyntax syntax || syntax.ContainsDiagnostics)
                throw new ArgumentException("Clipboard entries must be valid constructor expressions.");
        var temporary = new BehaviorDocument("class Fragment { void Build() { db.Init(\"__fragment__\", new State(" + string.Join(",\n", clipboard.Expressions) + ")); } }", names, schema: schema);
        var fragmentRoot = temporary.Entities.Single().Root;
        var phases = fragmentRoot.Children.SelectMany(n => n.Descendants()).Where(n => n.Kind == "State").ToArray();
        var originals = phases.Select(n => temporary.StateName(n.Id) ?? throw new ArgumentException("Name phases before copying them.")).ToArray();
        if (originals.Distinct(StringComparer.Ordinal).Count() != originals.Length) throw new ArgumentException("Copied phases must have unique names.");
        var taken = OwningEntity(parentId).Root.Descendants().Where(n => n.Kind == "State").Select(n => StateName(n.Id)).Concat(originals).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < phases.Length; i++)
        {
            var candidate = originals[i] + "-copy"; var suffix = 2;
            while (taken.Contains(candidate)) candidate = originals[i] + "-copy-" + suffix++;
            taken.Add(candidate); temporary.RenameState(phases[i].Id, candidate);
        }
        var additions = temporary.Entities.Single().Root.Children.ToArray();
        var arguments = additions.ToDictionary(n => n.Id, n => SyntaxFactory.Argument(SyntaxFactory.ParseExpression(temporary.Expression(n))));
        var children = parent.Children.ToList(); children.InsertRange(index, additions);
        RewriteChildren(new() { [parent.Id] = children }, arguments, validate);
        return additions.Select(n => n.Id).ToArray();
    }
    public void MoveNodes(IEnumerable<Guid> ids, Guid parentId, int index, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        var nodes = NormalizeSelection(ids); var target = Find(parentId); EditableContainer(target);
        if (index < 0 || index > target.Children.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var entity = OwningEntity(parentId);
        foreach (var node in nodes)
        {
            StructuralParent(node); Editable(node);
            if (OwningEntity(node.Id).EntityId != entity.EntityId) throw new ArgumentException("Move within one encounter; use copy/paste between encounters.");
            if (target.Id == node.Id || target.Path.StartsWith(node.Path + "/", StringComparison.Ordinal)) throw new ArgumentException("A branch cannot be moved into itself or its descendants.");
        }
        var selected = nodes.Select(n => n.Id).ToHashSet();
        var originalTarget = target.Children.ToList();
        var adjusted = index - originalTarget.Take(index).Count(n => selected.Contains(n.Id));
        var orders = nodes.Select(n => Parent(n.Id)!).Append(target).DistinctBy(n => n.Id)
            .ToDictionary(n => n.Id, n => n.Children.Where(c => !selected.Contains(c.Id)).ToList());
        orders[parentId].InsertRange(adjusted, nodes);
        // A locked sibling may keep its relative source position during a reorder.
        foreach (var node in nodes.Where(n => Parent(n.Id)!.Id == parentId))
        {
            var from = originalTarget.FindIndex(n => n.Id == node.Id); var to = orders[parentId].FindIndex(n => n.Id == node.Id);
            foreach (var sibling in originalTarget.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1)) Editable(sibling);
        }
        if (orders.All(pair => Find(pair.Key).Children.Select(n => n.Id).SequenceEqual(pair.Value.Select(n => n.Id)))) return;
        RewriteChildren(orders, [], validate);
    }
    public Guid GroupNodes(IEnumerable<Guid> ids, string name, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        var nodes = NormalizeSelection(ids); var parent = StructuralParent(nodes[0]); EditableContainer(parent);
        foreach (var node in nodes) { Editable(node); if (StructuralParent(node).Id != parent.Id) throw new ArgumentException("Group nodes that share one phase."); }
        if (string.IsNullOrWhiteSpace(name) || OwningEntity(parent.Id).Root.Descendants().Any(n => StateName(n.Id) == name)) throw new ArgumentException("Choose a unique, non-empty phase name.");
        var siblings = parent.Children.ToList(); var positions = nodes.Select(n => siblings.FindIndex(s => s.Id == n.Id)).ToArray();
        if (positions[^1] - positions[0] + 1 != positions.Length) throw new ArgumentException("Select adjacent nodes to preserve their source order when grouping.");
        var tree = CSharpSyntaxTree.ParseText(Source).GetRoot(); var parentArguments = Creation(tree, parent).ArgumentList!.Arguments;
        var groupCreation = (ObjectCreationExpressionSyntax)SyntaxFactory.ParseExpression("new State(" + SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(name)).ToFullString() + ")");
        var selectedArguments = nodes.Select(node =>
        {
            var argumentIndex = parentArguments.IndexOf(parentArguments.Single(a => a.Expression.SpanStart == node.Start));
            return ArgumentWithSeparatorTrivia(parentArguments[argumentIndex], parentArguments, argumentIndex);
        });
        groupCreation = groupCreation.WithArgumentList(groupCreation.ArgumentList!.WithArguments(SyntaxFactory.SeparatedList(groupCreation.ArgumentList.Arguments.Concat(selectedArguments)))).NormalizeWhitespace();
        var group = new BehaviorNode(Guid.NewGuid(), "", "State", name, 0, 0, [], nodes);
        siblings.RemoveRange(positions[0], nodes.Count); siblings.Insert(positions[0], group);
        RewriteChildren(new() { [parent.Id] = siblings }, new() { [group.Id] = SyntaxFactory.Argument(groupCreation) }, validate);
        return group.Id;
    }
    public Guid UngroupState(Guid id, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        var group = Find(id); if (group.Kind != "State") throw new ArgumentException("Select a grouped phase.");
        var parent = StructuralParent(group); Editable(group); EditableContainer(parent);
        var entity = OwningEntity(id); var name = StateName(id) ?? ""; LiteralReferences(entity, [name]);
        if (entity.Root.Descendants().Any(n => n.Parameters.Any(p => p.Name == "targetState" && LiteralString(p.Expression) == name)))
            throw new ArgumentException("Reroute transitions targeting this phase before ungrouping it.");
        var siblings = parent.Children.ToList(); var index = siblings.FindIndex(n => n.Id == id); siblings.RemoveAt(index); siblings.InsertRange(index, group.Children);
        RewriteChildren(new() { [parent.Id] = siblings }, [], validate); return parent.Id;
    }
    public Guid DeleteNodes(IEnumerable<Guid> ids, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        var nodes = NormalizeSelection(ids); var fallback = StructuralParent(nodes[0]);
        foreach (var node in nodes) { StructuralParent(node); Editable(node); }
        var removed = nodes.SelectMany(n => n.Descendants()).Select(n => n.Id).ToHashSet();
        var deletions = nodes.ToList();
        foreach (var entityGroup in nodes.GroupBy(n => OwningEntity(n.Id)))
        {
            var phaseNames = entityGroup.SelectMany(n => n.Descendants()).Where(n => n.Kind == "State").Select(n => StateName(n.Id)).OfType<string>().ToHashSet(StringComparer.Ordinal);
            if (phaseNames.Count > 0) LiteralReferences(entityGroup.Key, phaseNames);
            foreach (var reference in entityGroup.Key.Root.Descendants().Where(n => !removed.Contains(n.Id) && n.Parameters.Any(p => p.Name == "targetState" && phaseNames.Contains(LiteralString(p.Expression) ?? ""))))
            {
                if (!reference.Kind.EndsWith("Transition", StringComparison.Ordinal)) throw new ArgumentException("Update actions referencing deleted phases first.");
                StructuralParent(reference); Editable(reference); deletions.Add(reference);
            }
        }
        var allDeleted = deletions.Select(n => n.Id).ToHashSet();
        var orders = deletions.Select(n => Parent(n.Id)!).DistinctBy(n => n.Id).ToDictionary(n => n.Id, n => n.Children.Where(c => !allDeleted.Contains(c.Id)).ToList());
        RewriteChildren(orders, [], validate); return fallback.Id;
    }
    void RewriteChildren(Dictionary<Guid, List<BehaviorNode>> orders, Dictionary<Guid, ArgumentSyntax> additions, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate)
    {
        var tree = CSharpSyntaxTree.ParseText(Source).GetRoot();
        var creations = orders.Keys.ToDictionary(id => Creation(tree, Find(id)), id => id);
        var arguments = new Dictionary<Guid, ArgumentSyntax>(additions);
        foreach (var parent in Entities.SelectMany(e => e.Root.Descendants()).Where(n => n.Kind == "State"))
        {
            var list = Creation(tree, parent).ArgumentList!.Arguments;
            foreach (var child in parent.Children)
            {
                var index = list.IndexOf(list.Single(a => a.Expression.SpanStart == child.Start));
                arguments[child.Id] = ArgumentWithSeparatorTrivia(list[index], list, index);
            }
        }
        var changed = tree.ReplaceNodes(creations.Keys, (original, current) =>
        {
            var parent = Find(creations[original]); var originalList = original.ArgumentList!.Arguments; var currentList = current.ArgumentList!.Arguments;
            foreach (var child in parent.Children)
            {
                var index = originalList.IndexOf(originalList.Single(a => a.Expression.SpanStart == child.Start));
                arguments[child.Id] = ArgumentWithSeparatorTrivia(currentList[index], originalList, index);
            }
            var retained = originalList.Select((a, i) => (a, i)).Where(pair => !parent.Children.Any(c => c.Start == pair.a.Expression.SpanStart)).Select(pair => ArgumentWithSeparatorTrivia(currentList[pair.i], originalList, pair.i));
            var reordered = retained.Concat(orders[parent.Id].Select(n => arguments[n.Id]));
            return current.WithArgumentList(current.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(reordered)));
        }).ToFullString();
        CommitStructure(changed, orders, validate);
    }
    static ArgumentSyntax ArgumentWithSeparatorTrivia(ArgumentSyntax argument, SeparatedSyntaxList<ArgumentSyntax> list, int index)
    {
        if (index >= list.SeparatorCount) return argument;
        var separator = list.GetSeparator(index);
        return argument.WithTrailingTrivia(argument.GetTrailingTrivia().Concat(separator.LeadingTrivia).Concat(separator.TrailingTrivia));
    }
}
