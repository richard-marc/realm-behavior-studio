using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;

namespace Studio.Core;

public record FragmentValue(int ExpressionIndex, int Start, int Length, string DefaultExpression);
public record FragmentParameter(string Name, string Label, FragmentValue Value);
public record FragmentReference(string Kind, string Target, FragmentValue Value);
public record FragmentBinding(Guid NodeId, string Parameter, string Name);
public record FragmentParameterCandidate(Guid NodeId, string Parameter, string Label, string Expression);
public sealed record BehaviorFragment(int Version, Guid Id, string Name, string Description, string OriginEntity, string SourceSha256,
    BehaviorClipboard Contents, IReadOnlyList<FragmentParameter> Parameters, IReadOnlyList<FragmentReference> References, bool RequiresSourceContext)
{
    public void Validate()
    {
        if (Version != 1 || Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 256 || Description == null || Description.Length > 8192 || string.IsNullOrEmpty(OriginEntity) || OriginEntity.Length > 256 || SourceSha256 == null || SourceSha256.Length != 64 || Contents is not { Version: 1, Expressions: not null } || Contents.Expressions.Count is < 1 or > 256 || Contents.Expressions.Any(e => e == null) || Contents.Expressions.Sum(e => (long)e.Length) > 2_000_000 || Parameters == null || Parameters.Count > 1024 || References == null || References.Count > 4096)
            throw new ArgumentException("Unsupported or oversized fragment.");
        foreach (var expression in Contents.Expressions)
        {
            var syntax = SyntaxFactory.ParseExpression(expression);
            if (syntax.ContainsDiagnostics || syntax.FullSpan.Length != expression.Length) throw new ArgumentException("A fragment contains invalid C# syntax.");
        }
        if (Parameters.Any(p => p == null || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 128 || p.Label == null || p.Label.Length > 512) || Parameters.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != Parameters.Count)
            throw new ArgumentException("Template inputs need unique, non-empty names.");
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
