using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Core;

public sealed partial class BehaviorDocument
{
    public BehaviorNode? Parent(Guid id) => Entities.SelectMany(e => e.Root.Descendants()).FirstOrDefault(n => n.Children.Any(c => c.Id == id));
    public static string? LiteralString(string expression) => SyntaxFactory.ParseExpression(expression) is LiteralExpressionSyntax { Token.Value: string value } ? value : null;
    public string? StateName(Guid id) => Find(id).Kind == "State" ? Find(id).Parameters.Select(p => LiteralString(p.Expression)).FirstOrDefault(v => v != null) : null;
    EntityBehavior OwningEntity(Guid id) => Entities.Single(e => e.Root.Descendants().Any(n => n.Id == id));
    void Editable(BehaviorNode node)
    {
        if (LockedNodes.Select(Find).Any(locked => node.Path == locked.Path || node.Path.StartsWith(locked.Path + "/")) || node.Descendants().Any(n => LockedNodes.Contains(n.Id)))
            throw new InvalidOperationException("This region, a descendant or an ancestor is locked.");
    }
    BehaviorNode StructuralParent(BehaviorNode node)
    {
        var parent = Parent(node.Id) ?? throw new ArgumentException("The encounter root cannot be duplicated, moved or deleted.");
        if (parent.Kind != "State") throw new ArgumentException("Organize nodes directly inside a phase. Grouped constructor arguments require source editing.");
        return parent;
    }
    void LiteralReferences(EntityBehavior entity, IEnumerable<string> affectedNames)
    {
        var affected = affectedNames.ToHashSet(StringComparer.Ordinal);
        foreach (var node in entity.Root.Descendants())
        {
            if (node.Parameters.Any(p => p.Name.StartsWith("targetState", StringComparison.Ordinal) && (p.Name != "targetState" || LiteralString(p.Expression) == null)) ||
                node.Kind == "Opaque" && SyntaxFactory.ParseExpression(Expression(node)).DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>().Any(l => l.Token.Value is string value && affected.Contains(value)))
                throw new ArgumentException("This encounter contains a phase reference that cannot be safely rewritten. Update it in source first.");
        }
    }
    public void RenameState(Guid id, string name, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        var node = Find(id); var oldName = StateName(id) ?? throw new ArgumentException("Select a named phase to rename.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a non-empty phase name.");
        if (name == oldName) return;
        var entity = OwningEntity(id);
        if (entity.Root.Descendants().Any(n => n.Id != id && n.Kind == "State" && StateName(n.Id) == name)) throw new ArgumentException("A phase already uses that name.");
        LiteralReferences(entity, [oldName]); Editable(node);
        var edits = new List<(int Start, int Length, string Text)>();
        var nameParameter = node.Parameters.First(p => LiteralString(p.Expression) == oldName);
        var literal = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(name)).ToFullString();
        edits.Add((nameParameter.Start, nameParameter.Length, literal));
        foreach (var reference in entity.Root.Descendants())
            foreach (var parameter in reference.Parameters.Where(p => p.Name == "targetState" && LiteralString(p.Expression) == oldName))
            {
                Editable(reference); edits.Add((parameter.Start, parameter.Length, literal));
            }
        var changed = Source;
        foreach (var edit in edits.OrderByDescending(e => e.Start)) changed = changed.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        CommitStructure(changed, [], validate);
    }
    public Guid DuplicateNode(Guid id, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        var node = Find(id); var parent = StructuralParent(node); Editable(node);
        var entity = OwningEntity(id); var stateNodes = node.Descendants().Where(n => n.Kind == "State").ToArray();
        var taken = entity.Root.Descendants().Where(n => n.Kind == "State").Select(n => StateName(n.Id)).Where(n => n != null).ToHashSet(StringComparer.Ordinal);
        var rename = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var state in stateNodes)
        {
            var name = StateName(state.Id) ?? throw new ArgumentException("Name every phase in this branch before duplicating it.");
            if (rename.ContainsKey(name)) throw new ArgumentException("Resolve duplicate phase names before duplicating this branch.");
            var candidate = name + "-copy"; var suffix = 2;
            while (taken.Contains(candidate)) candidate = name + "-copy-" + suffix++;
            taken.Add(candidate); rename.Add(name, candidate);
        }
        if (rename.Count > 0) LiteralReferences(entity, rename.Keys);
        var expression = Expression(node);
        var edits = new List<(int Start, int Length, string Text)>();
        foreach (var descendant in node.Descendants())
            foreach (var parameter in descendant.Parameters)
            {
                var value = LiteralString(parameter.Expression);
                if (value == null || !rename.TryGetValue(value, out var newName) || !(descendant.Kind == "State" && parameter == descendant.Parameters.First() || parameter.Name == "targetState")) continue;
                edits.Add((parameter.Start - node.Start, parameter.Length, SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(newName)).ToFullString()));
            }
        foreach (var edit in edits.OrderByDescending(e => e.Start)) expression = expression.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        BehaviorNode Clone(BehaviorNode original) => original with { Id = Guid.NewGuid(), Children = original.Children.Select(Clone).ToArray() };
        var clone = Clone(node); var siblings = parent.Children.ToList(); var index = siblings.FindIndex(n => n.Id == id); siblings.Insert(index + 1, clone);
        var tree = CSharpSyntaxTree.ParseText(Source).GetRoot(); var creation = Creation(tree, parent);
        var argument = creation.ArgumentList!.Arguments.Single(a => a.Expression.SpanStart == node.Start);
        var inserted = SyntaxFactory.Argument(SyntaxFactory.ParseExpression(expression)).WithTriviaFrom(argument);
        var updated = creation.WithArgumentList(creation.ArgumentList.WithArguments(creation.ArgumentList.Arguments.Insert(creation.ArgumentList.Arguments.IndexOf(argument) + 1, inserted)));
        CommitStructure(tree.ReplaceNode(creation, updated).ToFullString(), new() { [parent.Id] = siblings }, validate);
        return clone.Id;
    }
    public void MoveNode(Guid id, int direction, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var node = Find(id); var parent = StructuralParent(node); Editable(node);
        var siblings = parent.Children.ToList(); var index = siblings.FindIndex(n => n.Id == id); var next = index + direction;
        if (next < 0 || next >= siblings.Count) return;
        Editable(siblings[next]);
        var tree = CSharpSyntaxTree.ParseText(Source).GetRoot(); var creation = Creation(tree, parent); var arguments = creation.ArgumentList!.Arguments;
        var sourceIndex = arguments.IndexOf(arguments.Single(a => a.Expression.SpanStart == node.Start));
        var targetIndex = arguments.IndexOf(arguments.Single(a => a.Expression.SpanStart == siblings[next].Start));
        // Replace by index: identical expressions are still separate, stable editor nodes.
        var reordered = arguments.ToArray(); (reordered[sourceIndex], reordered[targetIndex]) = (reordered[targetIndex], reordered[sourceIndex]);
        var updated = creation.WithArgumentList(creation.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(reordered, arguments.GetSeparators())));
        (siblings[index], siblings[next]) = (siblings[next], siblings[index]);
        CommitStructure(tree.ReplaceNode(creation, updated).ToFullString(), new() { [parent.Id] = siblings }, validate);
    }
    public Guid DeleteNode(Guid id, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        var node = Find(id); var parent = StructuralParent(node); Editable(node);
        var entity = OwningEntity(id); var removed = node.Descendants().Select(n => n.Id).ToHashSet();
        var phaseNames = node.Descendants().Where(n => n.Kind == "State").Select(n => StateName(n.Id)).Where(n => n != null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        if (phaseNames.Count > 0) LiteralReferences(entity, phaseNames);
        var nodes = new List<BehaviorNode> { node };
        foreach (var reference in entity.Root.Descendants().Where(n => !removed.Contains(n.Id) && n.Parameters.Any(p => p.Name == "targetState" && phaseNames.Contains(LiteralString(p.Expression) ?? ""))))
        {
            if (!reference.Kind.EndsWith("Transition", StringComparison.Ordinal)) throw new ArgumentException("Update the action referencing this phase before deleting it.");
            Editable(reference); StructuralParent(reference); nodes.Add(reference);
        }
        var orders = nodes.GroupBy(n => Parent(n.Id)!).ToDictionary(g => g.Key.Id, g => g.Key.Children.Where(n => !g.Any(deleted => deleted.Id == n.Id)).ToList());
        var tree = CSharpSyntaxTree.ParseText(Source).GetRoot(); var parents = nodes.GroupBy(n => Parent(n.Id)!).ToDictionary(g => Creation(tree, g.Key), g => g.Select(n => n.Start).ToHashSet());
        var changed = tree.ReplaceNodes(parents.Keys, (original, current) =>
        {
            var arguments = current.ArgumentList!.Arguments;
            var indices = original.ArgumentList!.Arguments.Select((a, i) => (a, i)).Where(pair => parents[original].Contains(pair.a.Expression.SpanStart)).Select(pair => pair.i);
            foreach (var index in indices.OrderDescending()) arguments = arguments.RemoveAt(index);
            return current.WithArgumentList(current.ArgumentList.WithArguments(arguments));
        }).ToFullString();
        CommitStructure(changed, orders, validate); return parent.Id;
    }
    static ObjectCreationExpressionSyntax Creation(SyntaxNode tree, BehaviorNode node) => tree.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single(c => c.SpanStart == node.Start && c.Span.Length == node.Length);
    void CommitStructure(string changed, Dictionary<Guid, List<BehaviorNode>> orders, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate)
    {
        var layout = new Dictionary<string, Guid>(StringComparer.Ordinal);
        void Walk(BehaviorNode node, string path)
        {
            layout.Add(path, node.Id);
            var children = orders.GetValueOrDefault(node.Id) ?? node.Children.ToList();
            for (var i = 0; i < children.Count; i++) Walk(children[i], path + "/" + i);
        }
        foreach (var entity in Entities) Walk(entity.Root, entity.EntityId + "/root");
        var probe = new BehaviorDocument(changed, names);
        var ids = probe.Identities.Select(identity => identity with { Id = layout[identity.Path] }).ToList();
        Commit(changed, ids, validate);
    }
}
