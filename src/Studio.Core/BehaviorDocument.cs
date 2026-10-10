using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;

namespace Studio.Core;

public record Parameter(string Name, string Expression, int Start, int Length);
public sealed record BehaviorNode(Guid Id, string Path, string Kind, string Label, int Start, int Length,
    IReadOnlyList<Parameter> Parameters, IReadOnlyList<BehaviorNode> Children)
{
    public ChildArgument? Argument { get; init; }
    public IReadOnlyList<BehaviorNode> StateChildren => Kind == "State" ? Children.SelectMany(n => n.Kind == "ArgumentGroup" ? n.Children : [n]).ToArray() : Children;
    public IEnumerable<BehaviorNode> Descendants() => new[] { this }.Concat(Children.SelectMany(n => n.Descendants()));
    public override string ToString() => Label;
}
public record EntityBehavior(string EntityId, int Start, BehaviorNode Root);
public record EditOperation(Guid NodeId, string Parameter, string Expression);
public record DocumentPatch(long ExpectedRevision, string Description, IReadOnlyList<EditOperation> Operations);
public record DocumentDiagnostic(string Message, Guid? NodeId = null, bool IsError = true);
public record NodeIdentity(Guid Id, string Path, string Kind, string? Signature = null);
public record PlayerPreset(int Hp = 1000, int Speed = 50, int Defense = 25, bool Invulnerable = false, bool Spectator = false,
    string WeaponId = "Staff of Diabolic Secrets", int Attack = 50, int Dexterity = 50);
public sealed record StudioProject
{
    public int Version { get; init; } = 1;
    public string SourceFile { get; init; } = "";
    public string Source { get; init; } = "";
    public string EntityId { get; init; } = "Cave Pirate Brawler";
    public string Brief { get; init; } = "";
    public int Seed { get; init; } = 1;
    public int ArenaSize { get; init; } = 48;
    public PlayerPreset Player { get; init; } = new();
    public List<NodeIdentity> Identities { get; init; } = new();
    public Dictionary<string, double[]> Layout { get; init; } = new();
    public List<Guid> LockedNodes { get; init; } = new();
    public List<Guid> CollapsedNodes { get; init; } = new();
}

// The lossless C# syntax tree is the shared model. Unknown expressions and trivia are never regenerated.
public sealed partial class BehaviorDocument
{
    public string Source { get; private set; }
    public long Revision { get; private set; }
    public IReadOnlyList<EntityBehavior> Entities { get; private set; } = [];
    public IReadOnlyList<DocumentDiagnostic> Diagnostics { get; private set; } = [];
    public HashSet<Guid> LockedNodes { get; } = [];
    readonly Stack<(string Source, List<NodeIdentity> Ids)> undo = new(), redo = new();
    readonly Func<string, string[]> names;
    readonly IBehaviorSchema? schema;
    public BehaviorDocument(string source, IBehaviorSchema schema, IEnumerable<NodeIdentity>? identities = null) : this(source, schema.ParameterNames, identities, schema) { }
    public BehaviorDocument(string source, Func<string, string[]>? parameterNames = null, IEnumerable<NodeIdentity>? identities = null, IBehaviorSchema? schema = null)
    {
        names = parameterNames ?? (_ => []);
        this.schema = schema;
        Source = source;
        Parse(identities?.ToList() ?? []);
        if (Diagnostics.Any(d => d.IsError)) throw new ArgumentException(string.Join("\n", Diagnostics.Select(d => d.Message)));
    }
    public List<NodeIdentity> Identities => Entities.SelectMany(e => e.Root.Descendants()).Select(n => new NodeIdentity(n.Id, n.Path, n.Kind, Expression(n))).ToList();
    public BehaviorNode Find(Guid id) => Entities.SelectMany(e => e.Root.Descendants()).FirstOrDefault(n => n.Id == id)
        ?? throw new ArgumentException("The selected element is no longer in this revision.");
    public string Expression(BehaviorNode n) => Source.Substring(n.Start, n.Length);
    void Parse(List<NodeIdentity> identities)
    {
        var tree = CSharpSyntaxTree.ParseText(Source);
        Diagnostics = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => new DocumentDiagnostic($"Line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}")).ToArray();
        if (Diagnostics.Count != 0) return;
        var result = new List<EntityBehavior>();
        var usedIdentities = new HashSet<Guid>();
        foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Init" } || call.ArgumentList.Arguments.Count < 2) continue;
            if (call.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax id || !id.IsKind(SyntaxKind.StringLiteralExpression)) continue;
            if (call.ArgumentList.Arguments[1].Expression is not ObjectCreationExpressionSyntax creation || creation.Type.GetLastToken().ValueText != "State") continue;
            var entityId = id.Token.ValueText;
            BehaviorNode Read(ObjectCreationExpressionSyntax c, string path)
            {
                var kind = c.Type.GetLastToken().ValueText;
                var pars = schema?.ConstructorParameterNames(c.ToString()) ?? names(kind);
                var args = c.ArgumentList?.Arguments ?? default;
                var parameters = new List<Parameter>();
                var children = new List<BehaviorNode>();
                var index = 0;
                foreach (var arg in args)
                {
                    var childArgument = schema?.IsChildArgument(c.ToString(), index) == true;
                    var initializer = arg.Expression switch { ArrayCreationExpressionSyntax a => a.Initializer, ImplicitArrayCreationExpressionSyntax a => a.Initializer, _ => null };
                    if (childArgument && initializer != null)
                    {
                        var groupPath = path + "/" + children.Count;
                        var argumentName = (index < pars.Length ? pars[index] : arg.NameColon?.Name.Identifier.ValueText)?.Split('[')[0];
                        var slot = schema!.ChildArguments(c.ToString()).Single(p => p.Name == argumentName);
                        var elements = initializer.Expressions.Select((element, i) => element is ObjectCreationExpressionSyntax creation ? Read(creation, groupPath + "/" + i) : Opaque(element, groupPath + "/" + i)).ToArray();
                        var oldGroup = identities.FirstOrDefault(n => n.Path == groupPath && !usedIdentities.Contains(n.Id) && (n.Kind == "ArgumentGroup" || n.Kind == "Opaque" && n.Signature == arg.Expression.ToString()));
                        if (oldGroup != null) usedIdentities.Add(oldGroup.Id);
                        children.Add(new BehaviorNode(oldGroup?.Id ?? Guid.NewGuid(), groupPath, "ArgumentGroup", slot.Name + " · " + elements.Length, arg.Expression.SpanStart, arg.Expression.Span.Length, [], elements) { Argument = slot });
                    }
                    else if (arg.Expression is ObjectCreationExpressionSyntax child && (childArgument || schema == null && child.Type.ToString().Split('.').Last() != "Cooldown"))
                        children.Add(Read(child, path + "/" + children.Count));
                    else if (kind == "State" && arg.Expression is not LiteralExpressionSyntax { Token.Value: string })
                    {
                        var opaquePath = path + "/" + children.Count;
                        children.Add(Opaque(arg.Expression, opaquePath));
                    }
                    else parameters.Add(new(arg.NameColon?.Name.Identifier.ValueText ?? (index < pars.Length ? pars[index] : "$" + index),
                        arg.Expression.ToString(), arg.Expression.SpanStart, arg.Expression.Span.Length));
                    index++;
                }
                var matches = identities.Where(n => n.Kind == kind && n.Signature == c.ToString() && n.Path.StartsWith(entityId + "/") && !usedIdentities.Contains(n.Id)).ToArray();
                var old = matches.Length == 1 ? matches[0] : identities.FirstOrDefault(n => n.Path == path && n.Kind == kind && !usedIdentities.Contains(n.Id));
                if (old != null) usedIdentities.Add(old.Id);
                var label = kind == "State" ? (parameters.Where(p => p.Name is "name" or "$0").Select(p => LiteralString(p.Expression)).FirstOrDefault(v => v != null) ?? "Root") : kind;
                return new(old?.Id ?? Guid.NewGuid(), path, kind, label, c.SpanStart, c.Span.Length, parameters, children);
            }
            BehaviorNode Opaque(ExpressionSyntax expression, string path)
            {
                var old = identities.FirstOrDefault(n => n.Path == path && n.Kind == "Opaque" && !usedIdentities.Contains(n.Id));
                if (old != null) usedIdentities.Add(old.Id);
                return new(old?.Id ?? Guid.NewGuid(), path, "Opaque", "Opaque: " + expression, expression.SpanStart, expression.Span.Length, [], []);
            }
            result.Add(new(entityId, call.SpanStart, Read(creation, entityId + "/root")));
        }
        Entities = result;
    }
    public void Apply(DocumentPatch patch, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        if (patch.ExpectedRevision != Revision) throw new InvalidOperationException("This proposal is stale. Inspect the current revision before applying changes.");
        var rename = patch.Operations.FirstOrDefault(o => Find(o.NodeId).Kind == "State" && o.Parameter is "name" or "$0");
        if (rename != null)
        {
            if (patch.Operations.Count != 1) throw new ArgumentException("Rename a phase separately from other parameter edits.");
            var name = LiteralString(rename.Expression) ?? throw new ArgumentException("Phase names must be literal strings.");
            RenameState(rename.NodeId, name, validate); return;
        }
        var edits = new List<(int Start, int Length, string Text)>();
        if (patch.Operations.GroupBy(o => (o.NodeId, o.Parameter)).Any(g => g.Count() > 1)) throw new ArgumentException("A proposal edits the same parameter twice.");
        foreach (var operation in patch.Operations)
        {
            var n = Find(operation.NodeId);
            if (LockedNodes.Select(Find).Any(locked => n.Path == locked.Path || n.Path.StartsWith(locked.Path + "/"))) throw new InvalidOperationException("This behavior region or an ancestor is locked.");
            if (n.Descendants().Any(child => LockedNodes.Contains(child.Id))) throw new InvalidOperationException("This behavior contains a locked region.");
            var p = n.Parameters.FirstOrDefault(p => p.Name == operation.Parameter);
            var expr = SyntaxFactory.ParseExpression(operation.Expression);
            if (expr.ContainsDiagnostics || expr.FullSpan.Length != operation.Expression.Length) throw new ArgumentException("Enter a valid C# value.");
            if (!VisualValue(expr))
                throw new ArgumentException("Visual parameter edits require literal values, enums or Cooldown values. Use source editing for other expressions.");
            if (p != null) edits.Add((p.Start, p.Length, operation.Expression));
            else
            {
                if (!names(n.Kind).Contains(operation.Parameter)) throw new ArgumentException("Unknown constructor parameter: " + operation.Parameter);
                var c = SyntaxFactory.ParseExpression(Expression(n)) as ObjectCreationExpressionSyntax ?? throw new ArgumentException("This opaque element cannot accept parameters.");
                var position = n.Start + c.ArgumentList!.CloseParenToken.SpanStart;
                edits.Add((position, 0, (c.ArgumentList.Arguments.Count == 0 ? "" : ", ") + operation.Parameter + ": " + operation.Expression));
            }
        }
        var changed = Source;
        foreach (var group in edits.GroupBy(e => e.Start).OrderByDescending(g => g.Key))
        {
            var edit = group.First();
            if (group.Count() > 1 && group.Any(e => e.Length != 0)) throw new ArgumentException("Overlapping parameter edits are not allowed.");
            changed = changed.Remove(edit.Start, edit.Length).Insert(edit.Start, string.Concat(group.Select(e => e.Text)));
        }
        Commit(changed, Identities, validate);
    }
    public void ReplaceSource(string source, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        if (LockedNodes.Count > 0) throw new InvalidOperationException("Unlock regions before replacing the complete source.");
        Commit(source, Identities, validate);
    }
    void Commit(string source, List<NodeIdentity> ids, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate)
    {
        var candidate = new BehaviorDocument(source, names, ids, schema);
        var errors = validate?.Invoke(candidate).Where(d => d.IsError).ToArray() ?? [];
        if (errors.Length > 0) throw new ArgumentException(string.Join("\n", errors.Select(d => d.Message)));
        undo.Push((Source, Identities)); redo.Clear(); Source = source; Parse(ids); Revision++;
    }
    public void AddChild(Guid parentId, string expression, Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        var parent = ChildContainer(parentId); EditableContainer(parent);
        var child = SyntaxFactory.ParseExpression(expression);
        if (child.ContainsDiagnostics || child is not ObjectCreationExpressionSyntax) throw new ArgumentException("Invalid behavior expression.");
        var fragment = new BehaviorDocument("class Fragment { void Build() { db.Init(\"__fragment__\", new State(" + expression + ")); } }", names, schema: schema);
        var node = fragment.Entities.Single().Root.Children.Single();
        var children = parent.Children.ToList(); children.Add(node);
        RewriteChildren(new() { [parent.Id] = children }, new() { [node.Id] = SyntaxFactory.Argument(child) }, validate);
    }
    public bool Undo() => Restore(undo, redo);
    static bool VisualValue(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax => true,
        PrefixUnaryExpressionSyntax unary => VisualValue(unary.Operand),
        ParenthesizedExpressionSyntax parenthesized => VisualValue(parenthesized.Expression),
        MemberAccessExpressionSyntax => true,
        BinaryExpressionSyntax binary => VisualValue(binary.Left) && VisualValue(binary.Right),
        CastExpressionSyntax cast => VisualValue(cast.Expression),
        DefaultExpressionSyntax => true,
        ArrayCreationExpressionSyntax array => array.Initializer != null && array.Initializer.Expressions.All(VisualValue),
        ImplicitArrayCreationExpressionSyntax array => array.Initializer.Expressions.All(VisualValue),
        ObjectCreationExpressionSyntax creation => creation.Type.ToString().Split('.').Last() == "Cooldown" && creation.ArgumentList?.Arguments.All(a => VisualValue(a.Expression)) == true,
        _ => false
    };
    public bool Redo() => Restore(redo, undo);
    bool Restore(Stack<(string Source, List<NodeIdentity> Ids)> from, Stack<(string Source, List<NodeIdentity> Ids)> to)
    {
        if (from.Count == 0) return false;
        to.Push((Source, Identities)); var previous = from.Pop(); Source = previous.Source; Parse(previous.Ids); LockedNodes.IntersectWith(Identities.Select(i => i.Id)); Revision++; return true;
    }
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static void Save(string path, StudioProject project)
    {
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, JsonSerializer.Serialize(project, JsonOptions)); File.Move(temporary, full, true);
    }
    public static StudioProject Load(string path)
    {
        var p = JsonSerializer.Deserialize<StudioProject>(File.ReadAllText(path), JsonOptions) ?? throw new ArgumentException("Empty project.");
        if (p.Version != 1) throw new ArgumentException($"Project version {p.Version} is not supported.");
        return p;
    }
}
