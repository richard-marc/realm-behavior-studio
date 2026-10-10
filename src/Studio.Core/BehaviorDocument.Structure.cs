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
        if (!CanOrganize(node.Id)) throw new ArgumentException("Select a behavior in a known native child slot. Argument lists and opaque expressions are edited through their contents or source.");
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
        var parent = StructuralParent(Find(id)); var index = parent.Children.ToList().FindIndex(n => n.Id == id);
        return PasteNodes(parent.Id, index + 1, CopyNodes([id]), validate).Single();
    }
    public void MoveNode(Guid id, int direction, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var parent = StructuralParent(Find(id)); var index = parent.Children.ToList().FindIndex(n => n.Id == id);
        if (index + direction < 0 || index + direction >= parent.Children.Count) return;
        MoveNodes([id], parent.Id, index + (direction == 1 ? 2 : -1), validate);
    }
    public Guid DeleteNode(Guid id, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null) => DeleteNodes([id], validate);
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
        var probe = new BehaviorDocument(changed, names, schema: schema);
        var ids = probe.Identities.Select(identity => identity with { Id = layout[identity.Path] }).ToList();
        Commit(changed, ids, validate);
    }
}
