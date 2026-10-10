using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Core;

public record PhaseTransition(BehaviorNode Node, Guid OwnerId, int Priority, string Condition, string? TargetName, Guid? TargetId);
public record PhaseAnalysis(IReadOnlyList<PhaseTransition> Transitions, IReadOnlyList<DocumentDiagnostic> Diagnostics);

// Conservative authoring analysis, not a prediction of HP, timers or world conditions.
public static class BehaviorAnalysis
{
    static readonly HashSet<string> Known = ["State", "Shoot", "Wander", "Follow", "StayCloseToSpawn", "Orbit", "Prioritize",
        "HpLessTransition", "TimedTransition", "PlayerWithinTransition", "EntityExistsTransition", "EntityNotExistsTransition",
        "Spawn", "ConditionEffectBehavior", "Taunt", "Cooldown"];
    public static string? Expression(BehaviorNode node, string parameter) => node.Parameters.FirstOrDefault(p => p.Name == parameter)?.Expression;
    static double? Number(BehaviorNode node, string parameter)
    {
        var expression = Expression(node, parameter);
        if (expression == null) return null;
        var syntax = SyntaxFactory.ParseExpression(expression);
        var sign = 1;
        if (syntax is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.UnaryMinusExpression)) { sign = -1; syntax = unary.Operand; }
        return syntax is LiteralExpressionSyntax { Token.Value: var value } && value is int or long or float or double or decimal
            ? sign * Convert.ToDouble(value, CultureInfo.InvariantCulture) : null;
    }
    static string Value(BehaviorNode node, string parameter, string fallback = "?") => Expression(node, parameter) ?? fallback;
    static string Resource(BehaviorNode node) => BehaviorDocument.LiteralString(Value(node, "target")) ?? Value(node, "target");
    public static string Condition(BehaviorNode node) => node.Kind switch
    {
        "TimedTransition" => $"After {Value(node, "time")} ms" + (Value(node, "randomized", "false") == "true" ? " · random start" : ""),
        "HpLessTransition" => Number(node, "threshold") is { } hp ? $"HP < {hp * 100:0.##}%" : $"HP < {Value(node, "threshold")}",
        "PlayerWithinTransition" => $"Player within {Value(node, "dist")} tiles" + (Value(node, "seeInvis", "false") == "true" ? " · sees invisible" : ""),
        "EntityExistsTransition" => $"{Resource(node)} within {Value(node, "dist")} tiles",
        "EntityNotExistsTransition" => Value(node, "checkAttackTarget", "false") == "true" ? "Attack target absent" : $"No {(Value(node, "target") == "null" ? "player" : Resource(node))} within {Value(node, "dist")} tiles",
        _ => node.Kind
    };
    static bool Covers(BehaviorNode earlier, BehaviorNode later)
    {
        // Timers are stateful and later timers are not ticked after an earlier match. Do not compare their durations.
        if (earlier.Kind == "TimedTransition" && Number(earlier, "time") == 0 && Value(earlier, "randomized", "false") == "false") return true;
        if (earlier.Kind != later.Kind) return false;
        if (earlier.Kind == "HpLessTransition") return Number(earlier, "threshold") is { } a && Number(later, "threshold") is { } b && a >= b;
        if (earlier.Kind == "PlayerWithinTransition") return Number(earlier, "dist") is { } a && Number(later, "dist") is { } b && a >= b &&
            Value(earlier, "seeInvis", "false") == Value(later, "seeInvis", "false");
        return false;
    }
    public static PhaseAnalysis Analyze(BehaviorNode root)
    {
        var nodes = root.Descendants().ToArray();
        var states = nodes.Where(n => n.Kind == "State").ToArray();
        string Name(BehaviorNode state) => state.Parameters.Select(p => BehaviorDocument.LiteralString(p.Expression)).FirstOrDefault(n => n != null) ?? "";
        var names = states.GroupBy(Name).ToDictionary(g => g.Key, g => g.ToArray());
        var edges = new List<PhaseTransition>(); var diagnostics = new List<DocumentDiagnostic>();
        foreach (var state in states)
        {
            var transitions = state.Children.Where(n => n.Kind.EndsWith("Transition", StringComparison.Ordinal)).ToArray();
            for (var i = 0; i < transitions.Length; i++)
            {
                var node = transitions[i]; var expression = Expression(node, "targetState");
                var targetName = expression == null ? null : BehaviorDocument.LiteralString(expression);
                var target = targetName != null && names.TryGetValue(targetName, out var targets) && targets.Length == 1 ? targets[0] : null;
                edges.Add(new(node, state.Id, i + 1, Condition(node), targetName, target?.Id));
                var shadow = transitions.Take(i).FirstOrDefault(earlier => Covers(earlier, node));
                if (shadow != null) diagnostics.Add(new($"Priority conflict: transition {i + 1} in {state.Label} is shadowed by transition {Array.IndexOf(transitions, shadow) + 1}. The first matching transition wins.", node.Id, false));
                if (target?.Id == state.Id) diagnostics.Add(new($"Self transition in {state.Label}: stays in this branch or enters its first nested phase. Common-ancestor entry callbacks are not rerun.", node.Id, false));
            }
        }
        var incomplete = nodes.Any(n => !Known.Contains(n.Kind)) || names.Any(g => g.Value.Length != 1) || edges.Any(e => e.TargetName == null);
        if (incomplete) diagnostics.Add(new("Reachability analysis is incomplete: opaque/custom nodes, dynamic targets or duplicate names may change phase entry. Unreachable-phase notices are suppressed.", root.Id, false));
        else
        {
            var parent = new Dictionary<Guid, BehaviorNode>();
            foreach (var state in states) foreach (var child in state.Children.Where(n => n.Kind == "State")) parent[child.Id] = state;
            var reachable = new HashSet<Guid>(); var pending = new Queue<BehaviorNode>();
            void Enter(BehaviorNode state)
            {
                while (state.Children.FirstOrDefault(n => n.Kind == "State") is { } child) state = child;
                pending.Enqueue(state);
            }
            Enter(root);
            var visitedLeaves = new HashSet<Guid>();
            while (pending.TryDequeue(out var leaf))
            {
                if (!visitedLeaves.Add(leaf.Id)) continue;
                for (BehaviorNode? state = leaf; state != null; state = parent.GetValueOrDefault(state.Id))
                {
                    reachable.Add(state.Id);
                    foreach (var edge in edges.Where(e => e.OwnerId == state.Id && e.TargetId != null)) Enter(states.Single(s => s.Id == edge.TargetId));
                }
            }
            foreach (var state in states.Where(s => !reachable.Contains(s.Id))) diagnostics.Add(new($"Unreachable phase: {state.Label}. No literal transition or first-child entry reaches this phase.", state.Id, false));
        }
        return new(edges, diagnostics);
    }
}
