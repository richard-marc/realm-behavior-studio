using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Studio.Core;

public sealed partial class BehaviorDocument
{
    public IReadOnlyList<FragmentParameterCandidate> FragmentParameterCandidates(IEnumerable<Guid> ids)
    {
        var nodes = NormalizeSelection(ids); var owner = OwningEntity(nodes[0].Id);
        var result = new List<FragmentParameterCandidate>();
        foreach (var node in nodes.SelectMany(n => n.Descendants()))
        {
            var protectedFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in node.Parameters)
                if (ReferencesEntityPhase(node, parameter, owner) is null or true)
                { protectedFields.Add(parameter.Name); if (SchemaAt(node)?.PhaseEntityParameter(Expression(node), parameter.Name) is { } entity) protectedFields.Add(entity); }
            foreach (var parameter in node.Parameters.Where(p => !protectedFields.Contains(p.Name) && !(node.Kind == "State" && p.Name is "name" or "$0")))
            {
                var phase = Parent(node.Id); while (phase != null && phase.Kind != "State") phase = Parent(phase.Id);
                var context = phase?.Label ?? owner.EntityId;
                result.Add(new(node.Id, parameter.Name, context + " / " + node.Label + " · line " + (Source.AsSpan(0, node.Start).Count('\n') + 1) + " · " + parameter.Name, parameter.Expression));
            }
        }
        return result;
    }
    public BehaviorFragment CaptureFragment(IEnumerable<Guid> ids, string name, string description = "", IEnumerable<FragmentBinding>? bindings = null, string? ownerEntity = null)
    {
        var nodes = NormalizeSelection(ids); var contents = CopyNodes(nodes.Select(n => n.Id)); var owner = OwningEntity(nodes[0].Id);
        if (nodes.Any(n => OwningEntity(n.Id).EntityId != owner.EntityId)) throw new ArgumentException("Save a fragment from one encounter at a time.");
        var callers = PhaseOwners(owner).Distinct(StringComparer.Ordinal).ToArray();
        ownerEntity ??= callers.Length == 1 ? callers[0] : throw new ArgumentException("Choose one caller when saving a shared source fragment.");
        if (callers.Length > 0 && !callers.Contains(ownerEntity, StringComparer.Ordinal)) throw new ArgumentException("The selected entity does not use this source fragment.");
        var phases = nodes.SelectMany(n => n.Descendants()).Where(n => n.Kind == "State").Select(n => StateName(n.Id)).Where(n => n != null).ToHashSet(StringComparer.Ordinal);
        FragmentValue Value(BehaviorNode node, Parameter parameter)
        {
            var index = nodes.ToList().FindIndex(n => n.Id == node.Id || node.Path.StartsWith(n.Path + "/", StringComparison.Ordinal));
            return new(index, parameter.Start - nodes[index].Start, parameter.Length, parameter.Expression);
        }
        var references = new List<FragmentReference>();
        foreach (var node in nodes.SelectMany(n => n.Descendants())) foreach (var parameter in node.Parameters)
        {
            var scope = SchemaAt(node) is { } binding ? binding.TargetsPhaseOf(Expression(node), parameter.Name, ownerEntity, ownerEntity) : ReferencesEntityPhase(node, parameter, owner);
            if (scope == false) continue;
            var targets = PhaseLiterals(parameter.Expression);
            if(scope==null||targets==null)references.Add(new("unresolved-phase",parameter.Expression,Value(node,parameter)));
            else foreach(var target in targets)references.Add(new(phases.Contains(target.Name)?"phase-internal":"phase-external",target.Name,Value(node,parameter) with {Start=Value(node,parameter).Start+target.Start,Length=target.Length,DefaultExpression=parameter.Expression.Substring(target.Start,target.Length)}));
            if (scope == true && SchemaAt(node)?.PhaseEntityParameter(Expression(node), parameter.Name) is { } field && node.Parameters.FirstOrDefault(p => p.Name == field) is { } entityParameter)
            {
                var value = Value(node, entityParameter);
                if (!references.Any(r => r.Kind == "self-entity" && r.Value == value)) references.Add(new("self-entity", ownerEntity, value));
            }
        }
        var available = FragmentParameterCandidates(nodes.Select(n => n.Id)); var parameters = new List<FragmentParameter>();
        foreach (var binding in bindings ?? [])
        {
            var field = available.SingleOrDefault(p => p.NodeId == binding.NodeId && p.Parameter == binding.Parameter) ?? throw new ArgumentException("Expose a selected value; phase names and reconciled references cannot be template inputs.");
            var node = Find(binding.NodeId); var parameter = node.Parameters.Single(p => p.Name == binding.Parameter);
            parameters.Add(new(binding.Name, field.Label, Value(node, parameter)));
        }
        var fragment = new BehaviorFragment(1, Guid.NewGuid(), name, description, ownerEntity, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Source))), contents, parameters, references,
            nodes.SelectMany(n => n.Descendants()).Any(n => n.Kind is "Factory" or "Reference" or "Spread" || SchemaAt(n)?.DescribeExpression(Expression(n))?.RequiresCompiled == true));
        fragment.Validate(); return fragment;
    }
    public IReadOnlyList<Guid> ImportFragment(long expectedRevision, Guid parentId, int index, BehaviorFragment fragment,
        IReadOnlyDictionary<string, string>? values = null, IReadOnlyDictionary<string, string>? phaseTargets = null, bool retargetSelfOrders = true,
        Func<BehaviorDocument, IEnumerable<DocumentDiagnostic>>? validate = null, string? targetEntity = null)
    {
        if (expectedRevision != Revision) throw new InvalidOperationException("This fragment preview is stale. Preview it against the current revision.");
        if (fragment.Dependencies != null) throw new ArgumentException("Import this fragment with its source dependency bundle.");
        return PasteNodes(parentId, index, new(1, ResolveFragmentExpressions(parentId, fragment, values, phaseTargets, retargetSelfOrders, targetEntity)), validate);
    }
    public IReadOnlyList<string> ResolveFragmentExpressions(Guid parentId, BehaviorFragment fragment,
        IReadOnlyDictionary<string, string>? values = null, IReadOnlyDictionary<string, string>? phaseTargets = null, bool retargetSelfOrders = true, string? targetEntity = null)
    {
        fragment.Validate(); var owner = OwningEntity(parentId); var expressions = fragment.Contents.Expressions.ToArray();
        var callers = PhaseOwners(owner).Distinct(StringComparer.Ordinal).ToArray();
        targetEntity ??= callers.Length == 1 ? callers[0] : throw new ArgumentException("Choose one caller for a shared source destination.");
        if (callers.Length > 0 && !callers.Contains(targetEntity, StringComparer.Ordinal)) throw new ArgumentException("The selected entity does not use this destination.");
        values ??= new Dictionary<string, string>(); phaseTargets ??= new Dictionary<string, string>();
        if (values.Keys.Any(k => fragment.Parameters.All(p => p.Name != k)) || phaseTargets.Keys.Any(k => fragment.References.All(r => r.Kind != "phase-external" || r.Target != k))) throw new ArgumentException("Unknown fragment input or phase mapping.");
        var edits = new List<(FragmentValue Value, string Expression)>();
        foreach (var parameter in fragment.Parameters)
        {
            var expression = values.GetValueOrDefault(parameter.Name, parameter.Value.DefaultExpression);
            if (expression == parameter.DisplayExpression) expression = parameter.Value.DefaultExpression;
            var syntax = SyntaxFactory.ParseExpression(expression);
            if (syntax.ContainsDiagnostics || syntax.FullSpan.Length != expression.Length || expression != parameter.Value.DefaultExpression && !VisualValue(syntax) && !(schema?.HasSourceContext == true && syntax is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax)) throw new ArgumentException("Changed template inputs need literal, enum, Cooldown or bound source values.");
            edits.Add((parameter.Value, expression));
        }
        foreach (var reference in fragment.References)
        {
            if (!retargetSelfOrders && targetEntity != fragment.OriginEntity && reference.Kind is "phase-external" or "phase-internal" && fragment.IsSelfOrderReference(reference)) continue;
            if (reference.Kind == "phase-external")
            {
                var target = phaseTargets.GetValueOrDefault(reference.Target, reference.Target);
                if (!FragmentDestinationPhases(parentId,targetEntity).Contains(target)) throw new ArgumentException("Map external phase '" + reference.Target + "' to one named destination phase.");
                edits.Add((reference.Value, SyntaxFactory.Literal(target).Text));
            }
            else if (reference.Kind == "self-entity" && retargetSelfOrders) edits.Add((reference.Value, SyntaxFactory.Literal(targetEntity).Text));
            else if (reference.Kind == "unresolved-phase" && targetEntity != fragment.OriginEntity) throw new ArgumentException("A fragment phase owner or target is unresolved. Reconcile it in source before moving it to another entity.");
        }
        foreach (var edit in edits.OrderByDescending(e => e.Value.Start))
        { var v = edit.Value; expressions[v.ExpressionIndex] = expressions[v.ExpressionIndex].Remove(v.Start, v.Length).Insert(v.Start, edit.Expression); }
        return expressions;
    }
    public IReadOnlyList<string> FragmentDestinationPhases(Guid parentId,string? ownerEntity=null)
    {
        var owner = OwningEntity(parentId); var callers = PhaseOwners(owner).ToHashSet(StringComparer.Ordinal);
        if(callers.Count==0&&ownerEntity!=null)callers.Add(ownerEntity);
        return Definitions.Where(d => d.Definition!.Encounters.Any(callers.Contains)).Select(d => d.Root).Concat(Entities.Where(e => callers.Contains(e.EntityId)).Select(e => e.Root)).Append(owner.Root)
            .SelectMany(r => r.Descendants()).Where(n => n.Kind == "State").DistinctBy(n => (n.Start,n.Length))
            .Select(n => StateName(n.Id)).OfType<string>().GroupBy(n => n,StringComparer.Ordinal).Where(g => g.Count() == 1).Select(g => g.Key).Order(StringComparer.Ordinal).ToArray();
    }
}
