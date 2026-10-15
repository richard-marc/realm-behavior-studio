using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Core;

public record FragmentValue(int ExpressionIndex, int Start, int Length, string DefaultExpression);
public record FragmentParameter(string Name, string Label, FragmentValue Value)
{
    public string? DisplayExpression { get; init; }
}
public record FragmentReference(string Kind, string Target, FragmentValue Value);
public record FragmentBinding(Guid NodeId, string Parameter, string Name);
public record FragmentParameterCandidate(Guid NodeId, string Parameter, string Label, string Expression);
public record FragmentDependencyPart(string Imports, string Declaration, string SourceSha256);
public record FragmentDependency(string Token, string Name, string Namespace, string? Host, string Imports, string Declaration, string SourceSha256)
{
    public IReadOnlyList<FragmentDependencyPart> Parts { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<FragmentDependencyPart> Declarations => new[] { new FragmentDependencyPart(Imports, Declaration, SourceSha256) }.Concat(Parts ?? []);
}
public record FragmentDependencyBundle(IReadOnlyList<FragmentDependency> Members)
{
    public IReadOnlyList<string> ExternalPhases { get; init; } = [];
    public bool UnresolvedPhases { get; init; }
    public bool SelfOrders { get; init; }
    public IReadOnlyList<string> OrderOnlyExternalPhases { get; init; } = [];
}
public sealed record BehaviorFragment(int Version, Guid Id, string Name, string Description, string OriginEntity, string SourceSha256,
    BehaviorClipboard Contents, IReadOnlyList<FragmentParameter> Parameters, IReadOnlyList<FragmentReference> References, bool RequiresSourceContext)
{
    public FragmentDependencyBundle? Dependencies { get; init; }
    public bool IsSelfOrderReference(FragmentReference reference)
    {
        var syntax = SyntaxFactory.ParseExpression(Contents.Expressions[reference.Value.ExpressionIndex]);
        return References.Where(r => r.Kind == "self-entity" && r.Value.ExpressionIndex == reference.Value.ExpressionIndex)
            .Select(r => syntax.DescendantNodesAndSelf().OfType<BaseObjectCreationExpressionSyntax>().Where(n => n.SpanStart <= r.Value.Start && n.Span.End >= r.Value.Start + r.Value.Length).OrderBy(n => n.Span.Length).FirstOrDefault())
            .Any(n => n != null && reference.Value.Start >= n.SpanStart && reference.Value.Start + reference.Value.Length <= n.Span.End);
    }
    public bool KeepOriginalPhase(string phase, string targetEntity, bool retargetSelfOrders) => !retargetSelfOrders && targetEntity != OriginEntity &&
        (Dependencies?.OrderOnlyExternalPhases.Contains(phase) == true || Dependencies == null && References.Where(r => r.Kind == "phase-external" && r.Target == phase).Any() && References.Where(r => r.Kind == "phase-external" && r.Target == phase).All(IsSelfOrderReference));
    public void Validate()
    {
        if (Version is not (1 or 2 or 3) || Version == 1 && Dependencies != null || Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 256 || Description == null || Description.Length > 8192 || string.IsNullOrEmpty(OriginEntity) || OriginEntity.Length > 256 || SourceSha256 == null || SourceSha256.Length != 64 || Contents is not { Version: 1, Expressions: not null } || Contents.Expressions.Count is < 1 or > 256 || Contents.Expressions.Any(e => e == null) || Contents.Expressions.Sum(e => (long)e.Length) > 2_000_000 || Parameters == null || Parameters.Count > 1024 || References == null || References.Count > 4096)
            throw new ArgumentException("Unsupported or oversized fragment.");
        foreach (var expression in Contents.Expressions)
        {
            var syntax = SyntaxFactory.ParseExpression(expression);
            if (syntax.ContainsDiagnostics || syntax.FullSpan.Length != expression.Length) throw new ArgumentException("A fragment contains invalid C# syntax.");
        }
        if (Version >= 2 && Dependencies is not { Members: not null } || Dependencies?.Members.Count is > 256 ||
            Dependencies?.Members.Sum(m => m?.Declarations.Sum(p => (long)(p?.Declaration?.Length ?? 0) + (p?.Imports?.Length ?? 0)) ?? 0) > 2_000_000 ||
            Dependencies?.Members.Sum(m => 1L + (m?.Parts?.Count ?? 0)) > 256)
            throw new ArgumentException("Invalid or oversized fragment dependencies.");
        if (Dependencies is { } bundle)
        {
            if (Version < 3 && bundle.Members.Any(m => m?.Parts?.Count > 0)) throw new ArgumentException("Partial declarations require fragment version 3.");
            if (bundle.ExternalPhases == null || bundle.ExternalPhases.Count > 4096 || bundle.ExternalPhases.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 1024)) throw new ArgumentException("Invalid dependency phase references.");
            if (bundle.OrderOnlyExternalPhases == null || bundle.OrderOnlyExternalPhases.Any(p => !bundle.ExternalPhases.Contains(p))) throw new ArgumentException("Invalid self-order phase metadata.");
            if (bundle.Members.Any(m => m == null || string.IsNullOrWhiteSpace(m.Token) || m.Token.Length > 128 ||
                !SyntaxFacts.IsValidIdentifier(m.Token) || string.IsNullOrWhiteSpace(m.Name) || m.Name.Length > 512 || m.Namespace == null || m.Namespace.Length > 1024 ||
                m.Host is not (null or "BehaviorDb") || m.Parts == null || m.Imports == null || m.Declaration == null || m.SourceSha256?.Length != 64 ||
                SyntaxFactory.ParseMemberDeclaration(m.Declaration) is not { ContainsDiagnostics: false }) ||
                bundle.Members.Select(m => m.Token).Distinct(StringComparer.Ordinal).Count() != bundle.Members.Count)
                throw new ArgumentException("Invalid fragment dependency declarations.");
            foreach (var member in bundle.Members)
            {
                if (member.Namespace.Length > 0 && (SyntaxFactory.ParseName(member.Namespace) is not { ContainsDiagnostics: false } parsed || parsed.ToString() != member.Namespace)) throw new ArgumentException("Invalid dependency namespace.");
                foreach (var part in member.Declarations)
                {
                    if (part == null || part.Imports == null || part.Declaration == null || part.SourceSha256?.Length != 64 ||
                        SyntaxFactory.ParseMemberDeclaration(part.Declaration) is not { ContainsDiagnostics: false }) throw new ArgumentException("Invalid dependency declaration part.");
                    var imports = SyntaxFactory.ParseCompilationUnit(part.Imports);
                    if (imports.ContainsDiagnostics || imports.Members.Count != 0 || imports.Externs.Count != 0) throw new ArgumentException("Dependency imports must contain only using directives.");
                    if (!part.SourceSha256.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid dependency source fingerprint.");
                }
            }
        }
        if (Parameters.Any(p => p == null || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 128 || p.Label == null || p.Label.Length > 512) || Parameters.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != Parameters.Count)
            throw new ArgumentException("Template inputs need unique, non-empty names.");
        if (Parameters.Any(p => p.DisplayExpression?.Length > 2_000_000)) throw new ArgumentException("Template display values exceed supported bounds.");
        if (References.Any(r => r == null || r.Kind is not ("phase-internal" or "phase-external" or "self-entity" or "unresolved-phase") || r.Target == null || r.Target.Length > 1024)) throw new ArgumentException("Invalid fragment reference metadata.");
        var values = Parameters.Select(p => p.Value).Concat(References.Select(r => r.Value)).ToArray();
        foreach (var value in values)
        {
            if (value == null || value.ExpressionIndex < 0 || value.ExpressionIndex >= Contents.Expressions.Count || value.Start < 0 || value.Length < 1 || value.Start > Contents.Expressions[value.ExpressionIndex].Length - value.Length || value.DefaultExpression == null || Contents.Expressions[value.ExpressionIndex].Substring(value.Start, value.Length) != value.DefaultExpression)
                throw new ArgumentException("A fragment value no longer matches its source expression.");
        }
        foreach (var group in values.GroupBy(v => v.ExpressionIndex))
        {
            var ordered = group.OrderBy(v => v.Start).ToArray();
            for (var i = 1; i < ordered.Length; i++) if (ordered[i - 1].Start + ordered[i - 1].Length > ordered[i].Start) throw new ArgumentException("Fragment values overlap.");
        }
    }
    public static BehaviorFragment Load(string path)
    {
        if (new FileInfo(path).Length > 4_000_000) throw new ArgumentException("Fragment files are limited to four megabytes.");
        var fragment = JsonSerializer.Deserialize<BehaviorFragment>(File.ReadAllText(path), BehaviorDocument.JsonOptions) ?? throw new ArgumentException("The fragment file is empty.");
        fragment.Validate(); return fragment;
    }
    public void Save(string path)
    {
        Validate(); var json = JsonSerializer.Serialize(this, BehaviorDocument.JsonOptions);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 4_000_000) throw new ArgumentException("Fragment files are limited to four megabytes.");
        var target = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
