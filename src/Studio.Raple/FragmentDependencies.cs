using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;

public static partial class FragmentDependencies
{
    sealed record Declaration(ISymbol Symbol, SyntaxNode Syntax, MemberDeclarationSyntax Member, string? Host);
    static string SymbolName(ISymbol symbol) => symbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    static string Namespace(ISymbol symbol) => symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString();
    static string Imports(SyntaxNode node) => string.Join("\n", ((CompilationUnitSyntax)node.SyntaxTree.GetRoot()).Usings
        .Concat(node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().SelectMany(n => n.Usings))
        .GroupBy(u => u.Alias?.Name.ToString() ?? u.ToString()).Select(g => g.Last().WithGlobalKeyword(default).WithoutTrivia().ToString()));

    public static BehaviorFragment Capture(RapleAdapter adapter, StudioProject project, BehaviorDocument document, IEnumerable<Guid> selection, BehaviorFragment fragment)
    {
        fragment.Validate();
        if (fragment.SourceSha256 != StudioSourceDraft.Hash(document.Source)) throw new InvalidOperationException("The selected fragment source changed before dependency capture.");
        var nodes = document.NormalizeSelection(selection);
        if (nodes.Count != fragment.Contents.Expressions.Count || !nodes.Select(document.Expression).SequenceEqual(fragment.Contents.Expressions)) throw new ArgumentException("Capture dependencies from the original selected expressions.");
        if (document.SourceFile == null || !document.SchemaAt(nodes[0])!.HasSourceContext) return fragment;
        var scoped = adapter.ForProject(project); var compiler = scoped.SourceCompilation(project);
        var sourceTree = compiler.SyntaxTrees.Single(t => Path.GetFullPath(t.FilePath).Equals(Path.GetFullPath(document.SourceFile), StringComparison.OrdinalIgnoreCase));
        if (sourceTree.GetText().ToString() != document.Source) throw new InvalidOperationException("Dependency capture needs the accepted project source.");
        var expressions = nodes.Select(n => sourceTree.GetRoot().DescendantNodes().First(e => e is ExpressionSyntax or SpreadElementSyntax && e.SpanStart == n.Start && e.Span.Length == n.Length)).ToArray();
        var accepted = project.AdditionalSources.Select(s => Path.GetFullPath(s.SourceFile)).Append(Path.GetFullPath(project.SourceFile)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var declarations = new Dictionary<ISymbol, Declaration>(SymbolEqualityComparer.Default);
        var nativeMembers = typeof(TKR.WorldServer.logic.State).Assembly.GetType("TKR.WorldServer.logic.BehaviorDb")!.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var queue = new Queue<SyntaxNode>(expressions);
        bool Covered(ISymbol symbol) => declarations.Keys.Any(s => s is INamedTypeSymbol && Within(symbol, s));
        void Add(ISymbol symbol)
        {
            symbol = symbol.OriginalDefinition;
            if (declarations.ContainsKey(symbol) || Covered(symbol)) return;
            var valueType = symbol switch { IFieldSymbol f => f.Type, IPropertySymbol p => p.Type, IMethodSymbol m => m.ReturnType, _ => null };
            if (valueType?.ToDisplayString() == "TKR.WorldServer.logic.BehaviorDb._") throw new ArgumentException("Encounter registrations need whole-source ownership before bundling: " + symbol.Name);
            if (symbol.DeclaringSyntaxReferences.Length != 1) throw new ArgumentException("A dependency needs one complete declaration before it can be bundled: " + symbol.Name);
            var syntax = symbol.DeclaringSyntaxReferences[0].GetSyntax();
            var host = symbol.ContainingType?.Name == "BehaviorDb" && symbol.ContainingType.ContainingType == null ? "BehaviorDb" : null;
            MemberDeclarationSyntax member = syntax switch
            {
                MethodDeclarationSyntax m => m,
                PropertyDeclarationSyntax p => p,
                VariableDeclaratorSyntax v when v.Parent?.Parent is FieldDeclarationSyntax f => f,
                BaseTypeDeclarationSyntax t => t,
                DelegateDeclarationSyntax d => d,
                _ => throw new ArgumentException("Extract local or generated dependencies into declared helpers before bundling: " + symbol.Name)
            };
            if (symbol is not INamedTypeSymbol && host == null) throw new ArgumentException("Bundle the complete declaring type for " + symbol.Name + ".");
            if (symbol is INamedTypeSymbol type)
            {
                foreach (var old in declarations.Keys.Where(s => Within(s, type)).ToArray()) declarations.Remove(old);
            }
            declarations.Add(symbol, new(symbol, syntax, member, host)); queue.Enqueue(syntax);
            if (symbol is IFieldSymbol && member is FieldDeclarationSyntax field) queue.Enqueue(field.Declaration.Type);
            if (declarations.Count > 256) throw new ArgumentException("Fragment dependencies exceed 256 declarations.");
        }
        INamedTypeSymbol? BundleType(INamedTypeSymbol? type)
        {
            if (type == null || type.Name == "BehaviorDb" || type.DeclaringSyntaxReferences.Length == 0) return null;
            var top = type;
            while (top.ContainingType != null && top.ContainingType.Name != "BehaviorDb") top = top.ContainingType;
            return top.ContainingType?.Name == "BehaviorDb" || top.DeclaringSyntaxReferences.Any(r => accepted.Contains(Path.GetFullPath(r.SyntaxTree.FilePath))) ? top.OriginalDefinition : null;
        }
        while (queue.TryDequeue(out var region))
        {
            var model = compiler.GetSemanticModel(region.SyntaxTree);
            foreach (var name in region.DescendantNodesAndSelf().OfType<SimpleNameSyntax>().Where(n => n.Parent is not NameColonSyntax))
            {
                var symbol = model.GetSymbolInfo(name).Symbol;
                if (symbol == null) continue;
                if (symbol is ILocalSymbol or IParameterSymbol)
                {
                    if (symbol.DeclaringSyntaxReferences.All(r => r.SyntaxTree != region.SyntaxTree || !region.Span.Contains(r.Span)) &&
                        !expressions.Any(e => e.SyntaxTree == region.SyntaxTree && e.Span.Contains(name.Span)) && !Covered(symbol)) continue;
                    if (expressions.Any(e => e.SyntaxTree == name.SyntaxTree && e.Span.Contains(name.Span)) && symbol.DeclaringSyntaxReferences.All(r => !expressions.Any(e => e.SyntaxTree == r.SyntaxTree && e.Span.Contains(r.Span))))
                        throw new ArgumentException("A fragment captures a local value. Extract its shared source ownership before bundling: " + symbol.Name);
                    continue;
                }
                if (symbol is IMethodSymbol reduced && reduced.ReducedFrom != null) symbol = reduced.ReducedFrom;
                if (BundleType(symbol is INamedTypeSymbol named ? named : symbol.ContainingType) is { } dependencyType) { Add(dependencyType); continue; }
                if (symbol is IMethodSymbol or IFieldSymbol or IPropertySymbol && symbol.ContainingType?.Name == "BehaviorDb" &&
                    symbol.ContainingType.ContainingType == null && symbol.DeclaringSyntaxReferences.Length > 0 && !Covered(symbol))
                {
                    if (symbol is IMethodSymbol method && method.MethodKind != MethodKind.Ordinary) continue;
                    if (nativeMembers.Contains(symbol.Name) && symbol.DeclaringSyntaxReferences.All(r => !accepted.Contains(Path.GetFullPath(r.SyntaxTree.FilePath)))) continue;
                    Add(symbol);
                }
            }
        }
        var ordered = declarations.Values.OrderBy(d => d.Syntax.SyntaxTree.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Syntax.SpanStart).ToArray();
        var names = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var reserved = ordered.Select(d => d.Syntax.SyntaxTree).Append(sourceTree).Distinct().SelectMany(t => t.GetRoot().DescendantTokens()).Where(t => t.IsKind(SyntaxKind.IdentifierToken)).Select(t => t.ValueText).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < ordered.Length; i++)
        {
            var token = "__RbsDependency_" + fragment.Id.ToString("N") + "_" + i;
            while (!reserved.Add(token)) token += "_";
            names[ordered[i].Symbol] = token;
        }
        var annotations = new Dictionary<SyntaxTree, Dictionary<SyntaxNode, List<SyntaxAnnotation>>>();
        void Annotate(SyntaxNode node, string key)
        {
            if (!annotations.TryGetValue(node.SyntaxTree, out var nodesForTree)) annotations[node.SyntaxTree] = nodesForTree = [];
            if (!nodesForTree.TryGetValue(node, out var list)) nodesForTree[node] = list = [];
            list.Add(new("rbs-fragment", key));
        }
        for (var i = 0; i < expressions.Length; i++) Annotate(expressions[i], "expression-" + i);
        var values = fragment.Parameters.Select(p => p.Value).Concat(fragment.References.Select(r => r.Value)).Distinct().ToArray();
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i]; var expression = expressions[value.ExpressionIndex];
            var selected = expression.DescendantNodesAndSelf().OfType<ExpressionSyntax>().FirstOrDefault(n => n.SpanStart == expression.SpanStart + value.Start && n.Span.Length == value.Length)
                ?? throw new ArgumentException("A template value needs its complete source expression.");
            Annotate(selected, "value-" + i);
        }
        for (var i = 0; i < ordered.Length; i++) Annotate(ordered[i].Member, "member-" + i);
        var rewritten = new Dictionary<SyntaxTree, SyntaxNode>();
        foreach (var tree in annotations.Keys)
        {
            var root = tree.GetRoot(); var marked = root.ReplaceNodes(annotations[tree].Keys, (old, updated) => updated.WithAdditionalAnnotations(annotations[tree][old]));
            rewritten[tree] = new BundleRewriter(compiler.GetSemanticModel(tree), names).Visit(marked)!;
        }
        SyntaxNode Marked(SyntaxTree tree, string key) => rewritten[tree].GetAnnotatedNodes("rbs-fragment").Single(n => n.GetAnnotations("rbs-fragment").Any(a => a.Data == key));
        var output = expressions.Select((e, i) => Marked(e.SyntaxTree, "expression-" + i)).ToArray();
        var mappedValues = values.Select((v, i) =>
        {
            var node = Marked(sourceTree, "value-" + i); var root = output[v.ExpressionIndex];
            return new FragmentValue(v.ExpressionIndex, node.SpanStart - root.SpanStart, node.Span.Length, node.ToString());
        }).ToArray();
        FragmentValue Mapped(FragmentValue value) => mappedValues[Array.IndexOf(values, value)];
        var members = ordered.Select((d, i) =>
        {
            var member = (MemberDeclarationSyntax)Marked(d.Member.SyntaxTree, "member-" + i);
            if (d.Symbol is IFieldSymbol) member = ((FieldDeclarationSyntax)member).WithDeclaration(((FieldDeclarationSyntax)member).Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(((FieldDeclarationSyntax)member).Declaration.Variables.Single(v => v.Identifier.ValueText == names[d.Symbol]))));
            return new FragmentDependency(names[d.Symbol], d.Symbol.Name, Namespace(d.Symbol), d.Host, Imports(d.Member), member.ToString(), StudioSourceDraft.Hash(d.Syntax.SyntaxTree.GetText().ToString()));
        }).ToArray();
        var native = NativeReferences(scoped, compiler, expressions.Concat(ordered.Select(d => d.Syntax)), fragment.OriginEntity);
        var result = fragment with { Version = 2, Contents = new(1, output.Select(n => n.ToString()).ToArray()), Parameters = fragment.Parameters.Select(p => p with { Value = Mapped(p.Value), DisplayExpression = p.Value.DefaultExpression }).ToArray(), References = fragment.References.Select(r => r with { Value = Mapped(r.Value), Kind = r.Kind == "phase-external" && native.Phases.Contains(r.Target) ? "phase-internal" : r.Kind }).ToArray(), Dependencies = new(members) { ExternalPhases = native.Targets.Except(native.Phases).Distinct().ToArray(), UnresolvedPhases = native.Unresolved, SelfOrders = native.SelfOrders, OrderOnlyExternalPhases = native.Targets.Except(native.Phases).Except(native.OrdinaryTargets).ToArray() } };
        result.Validate(); return result;
    }
    static bool Within(ISymbol symbol, ISymbol owner)
    {
        for (var current = symbol; current != null; current = current.ContainingSymbol) if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, owner.OriginalDefinition)) return true;
        return false;
    }

    sealed class BundleRewriter : CSharpSyntaxRewriter
    {
        readonly SemanticModel model; readonly IReadOnlyDictionary<ISymbol, string> names;
        readonly Dictionary<(int Start, int Length, int Kind), SyntaxNode> originals;
        public BundleRewriter(SemanticModel model, IReadOnlyDictionary<ISymbol, string> names)
        { this.model = model; this.names = names; originals = model.SyntaxTree.GetRoot().DescendantNodesAndSelf().GroupBy(n => (n.SpanStart, n.Span.Length, n.RawKind)).ToDictionary(g => g.Key, g => g.First()); }
        SyntaxNode Original(SyntaxNode node) => originals[(node.SpanStart, node.Span.Length, node.RawKind)];
        ISymbol? Symbol(SyntaxNode node) => model.GetSymbolInfo(Original(node)).Symbol;
        string TypeName(ITypeSymbol type) => string.Concat(type.ToDisplayParts(SymbolDisplayFormat.FullyQualifiedFormat).Select(p => p.Symbol != null && names.TryGetValue(p.Symbol.OriginalDefinition, out var renamed) ? renamed : p.ToString()));
        string? Renamed(ISymbol? symbol) => symbol != null && names.TryGetValue(symbol.OriginalDefinition, out var name) ? name : null;
        SyntaxNode Name(SimpleNameSyntax node)
        {
            SyntaxNode Unchanged() => node is GenericNameSyntax generic ? base.VisitGenericName(generic)! : base.VisitIdentifierName((IdentifierNameSyntax)node)!;
            if (node.Parent is NameColonSyntax || node.Parent is MemberAccessExpressionSyntax member && member.Name == node || node.Parent is QualifiedNameSyntax || node.Parent is AliasQualifiedNameSyntax) return Unchanged();
            var symbol = Symbol(node); string? replacement = null;
            if (symbol is INamedTypeSymbol type) replacement = TypeName(type);
            else if (Renamed(symbol) is { } renamed) replacement = node is GenericNameSyntax generic ? renamed + ((GenericNameSyntax)base.VisitGenericName(generic)!).TypeArgumentList : renamed;
            else if (symbol is { IsStatic: true, ContainingType: not null } && symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol) replacement = TypeName(symbol.ContainingType) + "." + node;
            return replacement == null ? Unchanged() : node.CopyAnnotationsTo(SyntaxFactory.ParseName(replacement).WithTriviaFrom(node));
        }
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) => Name(node);
        public override SyntaxNode? VisitGenericName(GenericNameSyntax node) => Name(node);
        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node) => Symbol(node) is INamedTypeSymbol type ? node.CopyAnnotationsTo(SyntaxFactory.ParseName(TypeName(type)).WithTriviaFrom(node)) : base.VisitQualifiedName(node);
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (Symbol(node) is INamedTypeSymbol type) return node.CopyAnnotationsTo(SyntaxFactory.ParseExpression(TypeName(type)).WithTriviaFrom(node));
            var updated = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
            return Renamed(Symbol(node)) is { } name ? updated.WithName(updated.Name is GenericNameSyntax generic ? generic.WithIdentifier(SyntaxFactory.Identifier(name)) : SyntaxFactory.IdentifierName(name).WithTriviaFrom(updated.Name)) : updated;
        }
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var method = Symbol(node) as IMethodSymbol; var updated = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            if (method?.ReducedFrom is not { } original || updated.Expression is not MemberAccessExpressionSyntax member) return updated;
            var name = Renamed(original) ?? original.Name;
            SimpleNameSyntax called = member.Name is GenericNameSyntax generic ? generic.WithIdentifier(SyntaxFactory.Identifier(name)) : SyntaxFactory.IdentifierName(name);
            var argument = SyntaxFactory.Argument(member.Expression);
            if (original.Parameters[0].RefKind == RefKind.Ref) argument = argument.WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword));
            return node.CopyAnnotationsTo(updated.WithExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.ParseExpression(TypeName(original.ContainingType)), called))
                .WithArgumentList(updated.ArgumentList.WithArguments(updated.ArgumentList.Arguments.Insert(0, argument))));
        }
        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) { var symbol = model.GetDeclaredSymbol(Original(node)); var result = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!; return Renamed(symbol) is { } name ? result.WithIdentifier(SyntaxFactory.Identifier(name).WithTriviaFrom(result.Identifier)) : result; }
        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node) { var symbol = model.GetDeclaredSymbol(Original(node)); var result = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node)!; return Renamed(symbol) is { } name ? result.WithIdentifier(SyntaxFactory.Identifier(name).WithTriviaFrom(result.Identifier)) : result; }
        public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node) { var symbol = model.GetDeclaredSymbol(Original(node)); var result = (VariableDeclaratorSyntax)base.VisitVariableDeclarator(node)!; return Renamed(symbol) is { } name ? result.WithIdentifier(SyntaxFactory.Identifier(name).WithTriviaFrom(result.Identifier)) : result; }
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node) { var symbol = model.GetDeclaredSymbol(Original(node)); var result = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!; return Renamed(symbol) is { } name ? result.WithIdentifier(SyntaxFactory.Identifier(name).WithTriviaFrom(result.Identifier)) : result; }
        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node) { var symbol = model.GetDeclaredSymbol(Original(node)); var result = (StructDeclarationSyntax)base.VisitStructDeclaration(node)!; return Renamed(symbol) is { } name ? result.WithIdentifier(SyntaxFactory.Identifier(name).WithTriviaFrom(result.Identifier)) : result; }
        public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node) { var symbol = model.GetDeclaredSymbol(Original(node)); var result = (RecordDeclarationSyntax)base.VisitRecordDeclaration(node)!; return Renamed(symbol) is { } name ? result.WithIdentifier(SyntaxFactory.Identifier(name).WithTriviaFrom(result.Identifier)) : result; }
        public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) { var symbol = model.GetDeclaredSymbol(Original(node)); var result = (InterfaceDeclarationSyntax)base.VisitInterfaceDeclaration(node)!; return Renamed(symbol) is { } name ? result.WithIdentifier(SyntaxFactory.Identifier(name).WithTriviaFrom(result.Identifier)) : result; }
        public override SyntaxNode? VisitEnumDeclaration(EnumDeclarationSyntax node) { var symbol = model.GetDeclaredSymbol(Original(node)); var result = (EnumDeclarationSyntax)base.VisitEnumDeclaration(node)!; return Renamed(symbol) is { } name ? result.WithIdentifier(SyntaxFactory.Identifier(name).WithTriviaFrom(result.Identifier)) : result; }
        public override SyntaxNode? VisitDelegateDeclaration(DelegateDeclarationSyntax node) { var symbol = model.GetDeclaredSymbol(Original(node)); var result = (DelegateDeclarationSyntax)base.VisitDelegateDeclaration(node)!; return Renamed(symbol) is { } name ? result.WithIdentifier(SyntaxFactory.Identifier(name).WithTriviaFrom(result.Identifier)) : result; }
        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node) { var symbol = model.GetDeclaredSymbol(Original(node)); var result = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node)!; return Renamed(symbol?.ContainingType) is { } name ? result.WithIdentifier(SyntaxFactory.Identifier(name).WithTriviaFrom(result.Identifier)) : result; }
    }
}
