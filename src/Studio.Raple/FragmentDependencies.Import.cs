using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;

namespace Studio.Raple;
public static partial class FragmentDependencies
{
    public sealed record ImportResult(IReadOnlyList<Guid> Selection, string Declarations);
    sealed record NativeSummary(HashSet<string> Phases, HashSet<string> Targets, bool Unresolved, bool SelfOrders, HashSet<string> OrdinaryTargets);
    static IEnumerable<(ArgumentSyntax Argument, string Name)> Arguments(BaseObjectCreationExpressionSyntax expression, IMethodSymbol constructor)
    {
        var args = expression.ArgumentList?.Arguments ?? default;
        for (var i = 0; i < args.Count; i++) yield return (args[i], args[i].NameColon?.Name.Identifier.ValueText ?? constructor.Parameters[Math.Min(i, constructor.Parameters.Length - 1)].Name);
    }
    static string[]? StringValues(SemanticModel model, ExpressionSyntax expression)
    {
        if (model.GetConstantValue(expression) is { HasValue: true, Value: string value }) return [value];
        var values = expression switch { ArrayCreationExpressionSyntax a => a.Initializer?.Expressions.ToArray(), ImplicitArrayCreationExpressionSyntax a => a.Initializer.Expressions.ToArray(), CollectionExpressionSyntax a when a.Elements.All(e => e is ExpressionElementSyntax) => a.Elements.Cast<ExpressionElementSyntax>().Select(e => e.Expression).ToArray(), _ => null };
        if (values == null) return null;
        var output = values.Select(e => model.GetConstantValue(e)).ToArray();
        return output.All(v => v.HasValue && v.Value is string) ? output.Select(v => (string)v.Value!).ToArray() : null;
    }
    static NativeSummary NativeReferences(RapleAdapter adapter, CSharpCompilation compiler, IEnumerable<SyntaxNode> regions, string entity)
    {
        var phases = new HashSet<string>(StringComparer.Ordinal); var targets = new HashSet<string>(StringComparer.Ordinal); var ordinary = new HashSet<string>(StringComparer.Ordinal); var unresolved = false; var selfOrders = false;
        foreach (var region in regions) foreach (var expression in region.DescendantNodesAndSelf().OfType<BaseObjectCreationExpressionSyntax>())
        {
            var model = compiler.GetSemanticModel(expression.SyntaxTree);
            if (model.GetSymbolInfo(expression).Symbol is not IMethodSymbol constructor) continue;
            var type = constructor.ContainingType;
            var known = adapter.Primitives.Any(p => p.Name == type.Name && p.Simulatable) && RapleAdapter.NativeTypeName(type.Name) == type.ToDisplayString();
            if (!known) { if (type.AllInterfaces.Any(i => i.ToDisplayString() == "TKR.WorldServer.logic.IStateChildren")) unresolved = true; continue; }
            var binding = new SourceBehaviorSchema(adapter, compiler, expression.SyntaxTree.FilePath).At(expression.SpanStart);
            foreach (var (argument, name) in Arguments(expression, constructor))
            {
                var stateName = constructor.ContainingType.ToDisplayString() == "TKR.WorldServer.logic.State" && name == "name";
                var owned = stateName ? true : binding.TargetsPhaseOf(expression.ToString(), name, entity, entity);
                if (owned == false) continue;
                if (owned == true && binding.PhaseEntityParameter(expression.ToString(), name) != null) selfOrders = true;
                var values = StringValues(model, argument.Expression);
                if (owned == null || values == null) { unresolved = true; continue; }
                foreach (var value in values) (stateName ? phases : targets).Add(value);
                if (!stateName && binding.PhaseEntityParameter(expression.ToString(), name) == null) ordinary.UnionWith(values);
            }
        }
        foreach (var region in regions) foreach (var expression in region.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
        {
            var symbol = compiler.GetSemanticModel(region.SyntaxTree).GetSymbolInfo(expression).Symbol;
            if (symbol?.ContainingType?.ToDisplayString() is "TKR.WorldServer.logic.State" && symbol.Name is "Name" or "ToString" ||
                symbol?.ContainingType?.ToDisplayString() is "TKR.WorldServer.core.objects.Entity" && symbol.Name is "CurrentState" or "SwitchTo") unresolved = true;
        }
        return new(phases, targets, unresolved, selfOrders, ordinary);
    }
    static StudioProject WithSource(StudioProject project, string file, string source, List<NodeIdentity> identities)
    {
        if (Path.GetFullPath(project.SourceFile).Equals(file, StringComparison.OrdinalIgnoreCase)) return project with { Source = source, Identities = identities };
        if (!project.AdditionalSources.Any(s => Path.GetFullPath(s.SourceFile).Equals(file, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Import into an accepted project source file.");
        return project with { AdditionalSources = project.AdditionalSources.Select(s => Path.GetFullPath(s.SourceFile).Equals(file, StringComparison.OrdinalIgnoreCase) ? s with { Source = source, Identities = identities } : s).ToList() };
    }
    static BehaviorFragment RenameTokens(BehaviorFragment fragment, IReadOnlyDictionary<string, string> names)
    {
        string Rewrite(string text) => new TokenRenamer(names).Visit(CSharpSyntaxTree.ParseText("class Value { object M()=>" + text + "; }").GetRoot())!.DescendantNodes().OfType<ArrowExpressionClauseSyntax>().Single().Expression.ToString();
        var expressions = fragment.Contents.Expressions.Select(Rewrite).ToArray();
        FragmentValue Value(FragmentValue value)
        {
            var original = fragment.Contents.Expressions[value.ExpressionIndex]; var before = original[..value.Start]; var body = original.Substring(value.Start, value.Length);
            int Delta(string text) => SyntaxFactory.ParseTokens(text).Where(t => t.IsKind(SyntaxKind.IdentifierToken) && names.ContainsKey(t.ValueText)).Sum(t => names[t.ValueText].Length - t.Text.Length);
            var start = value.Start + Delta(before); var length = value.Length + Delta(body);
            return new(value.ExpressionIndex, start, length, expressions[value.ExpressionIndex].Substring(start, length));
        }
        return fragment with { Contents = new(1, expressions), Parameters = fragment.Parameters.Select(p => p with { Value = Value(p.Value) }).ToArray(), References = fragment.References.Select(r => r with { Value = Value(r.Value) }).ToArray() };
    }
    sealed class TokenRenamer(IReadOnlyDictionary<string, string> names) : CSharpSyntaxRewriter
    {
        public override SyntaxToken VisitToken(SyntaxToken token) => token.IsKind(SyntaxKind.IdentifierToken) && names.TryGetValue(token.ValueText, out var name) ? SyntaxFactory.Identifier(token.LeadingTrivia, name, token.TrailingTrivia) : base.VisitToken(token);
    }
    public static IReadOnlyList<Guid> Import(RapleAdapter adapter, StudioProject project, BehaviorDocument destination, long expectedRevision, Guid parentId, int index,
        BehaviorFragment fragment, IReadOnlyDictionary<string, string>? values = null, IReadOnlyDictionary<string, string>? phaseTargets = null, bool retargetSelfOrders = true, Func<BehaviorDocument,IEnumerable<DocumentDiagnostic>>? validate = null)
        => ImportWithDetails(adapter, project, destination, expectedRevision, parentId, index, fragment, values, phaseTargets, retargetSelfOrders, validate).Selection;
    public static ImportResult ImportWithDetails(RapleAdapter adapter, StudioProject project, BehaviorDocument destination, long expectedRevision, Guid parentId, int index,
        BehaviorFragment fragment, IReadOnlyDictionary<string, string>? values = null, IReadOnlyDictionary<string, string>? phaseTargets = null, bool retargetSelfOrders = true, Func<BehaviorDocument,IEnumerable<DocumentDiagnostic>>? validate = null)
    {
        if (fragment.Dependencies == null) return new(destination.ImportFragment(expectedRevision, parentId, index, fragment, values, phaseTargets, retargetSelfOrders, validate ?? (d => adapter.Validate(d, project.EntityId)), project.EntityId), "");
        fragment.Validate();
        if (destination.Revision != expectedRevision) throw new InvalidOperationException("This dependency preview is stale.");
        var file = Path.GetFullPath(destination.SourceFile ?? throw new ArgumentException("Dependency import needs an original source file."));
        if (phaseTargets?.Keys.Any(k => !fragment.References.Any(r => r.Kind == "phase-external" && r.Target == k) && !fragment.Dependencies.ExternalPhases.Contains(k)) == true) throw new ArgumentException("Unknown dependency phase mapping.");
        if (project.SourceDrafts.Any(d => Path.GetFullPath(d.SourceFile).Equals(file, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Apply or discard the destination source draft before importing dependencies.");
        var working = WithSource(project, file, destination.Source, destination.Identities); var scoped = adapter.ForProject(working); var compiler = scoped.SourceCompilation(working);
        var reserved = compiler.SyntaxTrees.SelectMany(t => t.GetRoot().DescendantTokens()).Where(t => t.IsKind(SyntaxKind.IdentifierToken)).Select(t => t.ValueText).ToHashSet(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in fragment.Dependencies.Members)
        {
            var prefix = "Fragment_" + new string(member.Name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray()); var name = prefix; var suffix = 2;
            while (!reserved.Add(name)) name = prefix + suffix++;
            names.Add(member.Token, name);
        }
        var renamed = RenameTokens(fragment, names); var additions = new System.Text.StringBuilder();
        foreach (var member in fragment.Dependencies.Members)
        foreach (var part in member.Declarations)
        {
            var declaration = new TokenRenamer(names).Visit(SyntaxFactory.ParseMemberDeclaration(part.Declaration)!)!.ToString();
            var imports = new TokenRenamer(names).Visit(SyntaxFactory.ParseCompilationUnit(part.Imports))!.ToFullString();
            var body = imports + "\n" + (member.Host == null ? declaration : "partial class BehaviorDb {\n" + declaration + "\n}");
            additions.AppendLine().AppendLine(member.Namespace.Length == 0 ? body : "namespace " + member.Namespace + " {\n" + body + "\n}");
        }
        var addedSource = additions.ToString(); var probe = new BehaviorDocument(destination.Source + addedSource, scoped, destination.Identities, file); probe.LockedNodes.UnionWith(destination.LockedNodes);
        var coreTargets = phaseTargets?.Where(p => fragment.References.Any(r => r.Kind == "phase-external" && r.Target == p.Key)).ToDictionary(p => p.Key, p => p.Value);
        var expressions = probe.ResolveFragmentExpressions(parentId, renamed, values, coreTargets, retargetSelfOrders, project.EntityId);
        var container = probe.ChildContainer(parentId); var originalCount = container.Children.Count; var inserted = new List<Guid>();
        if (index < 0 || index > originalCount) throw new ArgumentOutOfRangeException(nameof(index));
        foreach (var expression in expressions) { probe.AddChild(container.Id, expression); inserted.Add(probe.Find(container.Id).Children.Last().Id); }
        if (index != originalCount) probe.MoveNodes(inserted, container.Id, index);
        working = WithSource(working, file, probe.Source, probe.Identities); scoped = adapter.ForProject(working); compiler = scoped.SourceCompilation(working);
        var tree = compiler.SyntaxTrees.Single(t => Path.GetFullPath(t.FilePath).Equals(file, StringComparison.OrdinalIgnoreCase)); var model = compiler.GetSemanticModel(tree);
        var bundleStart = probe.Source.Length - addedSource.Length;
        var regions = inserted.Select(id => probe.Find(id)).Select(n => (n.Start, End: n.Start + n.Length)).ToArray();
        bool New(SyntaxNode node) => node.SpanStart >= bundleStart || regions.Any(r => node.SpanStart >= r.Start && node.Span.End <= r.End);
        var constructors = tree.GetRoot().DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().Where(New).ToArray();
        var phaseNames = new Dictionary<string, string>(StringComparer.Ordinal); var edits = new List<SourceLiteralEdit>();
        var taken = destination.FragmentDestinationPhases(parentId,project.EntityId).ToHashSet(StringComparer.Ordinal);
        foreach (var expression in constructors)
        {
            if (model.GetSymbolInfo(expression).Symbol is not IMethodSymbol constructor || constructor.ContainingType.ToDisplayString() != "TKR.WorldServer.logic.State") continue;
            foreach (var (argument, name) in Arguments(expression, constructor).Where(a => a.Name == "name"))
            {
                var old = StringValues(model, argument.Expression)?.SingleOrDefault() ?? throw new ArgumentException("Generated dependency phase names need source reconciliation before import.");
                if (!phaseNames.TryGetValue(old, out var replacement)) { var prefix = old + "-copy"; replacement = prefix; var suffix = 2; while (!taken.Add(replacement)) replacement = prefix + "-" + suffix++; phaseNames.Add(old, replacement); }
                edits.Add(new(argument.Expression.SpanStart, argument.Expression.Span.Length, SyntaxFactory.Literal(replacement).Text));
            }
        }
        var actualRegions = tree.GetRoot().DescendantNodes().Where(n => New(n) && !n.Ancestors().Any(New)).ToArray();
        var actualNative = NativeReferences(scoped, compiler, actualRegions, fragment.OriginEntity);
        if ((fragment.Dependencies.UnresolvedPhases || actualNative.Unresolved) && (phaseNames.Count > 0 || project.EntityId != fragment.OriginEntity)) throw new ArgumentException("Dependency phase ownership is unresolved. Reconcile generated or custom references in Source before importing this package.");
        foreach (var expression in constructors)
        {
            if (model.GetSymbolInfo(expression).Symbol is not IMethodSymbol constructor) continue;
            var binding = new SourceBehaviorSchema(scoped, compiler, file).At(expression.SpanStart); var owner = expression.SpanStart >= bundleStart ? fragment.OriginEntity : project.EntityId;
            foreach (var (argument, name) in Arguments(expression, constructor))
            {
                if (constructor.ContainingType.ToDisplayString() == "TKR.WorldServer.logic.State" && name == "name") continue;
                if (binding.TargetsPhaseOf(expression.ToString(), name, owner, owner) != true) continue;
                if (!retargetSelfOrders && project.EntityId != fragment.OriginEntity && expression.SpanStart >= bundleStart && binding.PhaseEntityParameter(expression.ToString(), name) != null) continue;
                var targets = StringValues(model, argument.Expression) ?? throw new ArgumentException("A dependency phase target needs source reconciliation.");
                var mapped = targets.Select(t => phaseNames.GetValueOrDefault(t) ?? phaseTargets?.GetValueOrDefault(t) ?? t).ToArray();
                if (mapped.Any(t => !taken.Contains(t))) throw new ArgumentException("Map dependency phase targets to named destination phases.");
                var replacement = mapped.Length == 1 && model.GetConstantValue(argument.Expression) is { HasValue: true, Value: string } ? SyntaxFactory.Literal(mapped[0]).Text : "new string[] {" + string.Join(", ", mapped.Select(t => SyntaxFactory.Literal(t).Text)) + "}";
                edits.Add(new(argument.Expression.SpanStart, argument.Expression.Span.Length, replacement));
                if (retargetSelfOrders && expression.SpanStart >= bundleStart && binding.PhaseEntityParameter(expression.ToString(), name) is { } entityParameter)
                {
                    var entity = Arguments(expression, constructor).Single(a => a.Name == entityParameter).Argument.Expression;
                    edits.Add(new(entity.SpanStart, entity.Span.Length, SyntaxFactory.Literal(project.EntityId).Text));
                }
            }
        }
        var finalSource = probe.Source; foreach (var edit in edits.Distinct().OrderByDescending(e => e.Start)) finalSource = finalSource.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Expression);
        working = WithSource(working, file, finalSource, probe.Identities); scoped = adapter.ForProject(working);
        var candidate = new BehaviorDocument(finalSource, scoped, probe.Identities, file); var errors = scoped.Validate(candidate, project.EntityId).Concat(validate?.Invoke(candidate) ?? []).Where(d => d.IsError).Distinct().ToArray();
        if (errors.Length > 0) throw new ArgumentException(string.Join("\n", errors.Select(e => e.Message)));
        var declarationLength = addedSource.Length + edits.Distinct().Where(e => e.Start >= bundleStart).Sum(e => e.Expression.Length - e.Length);
        destination.ApplySourceRevision(expectedRevision, destination.Source, candidate.Source, candidate.Identities, scoped); return new(inserted, candidate.Source[^declarationLength..]);
    }
}
