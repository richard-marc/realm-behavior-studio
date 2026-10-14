using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Studio.Core;

public record PhaseDestination(string Name,Guid? Id,int Slots);
public record PhaseTransition(BehaviorNode Node, Guid OwnerId, int Priority, string Condition, string? TargetName, Guid? TargetId)
{
    public IReadOnlyList<PhaseDestination> Destinations {get;init;}=[];
    public bool TargetsKnown {get;init;}
    public bool CanMatch {get;init;}=true;
}
public record PhaseCycle(IReadOnlyList<Guid> PhaseIds,IReadOnlyList<Guid> TransitionIds,IReadOnlyList<Guid> ExitTransitionIds,bool Reachable);
public record PhaseAnalysis(IReadOnlyList<PhaseTransition> Transitions, IReadOnlyList<DocumentDiagnostic> Diagnostics)
{
    public IReadOnlyList<PhaseCycle> Cycles {get;init;}=[];
}

// Conservative authoring analysis, not a prediction of HP, timers or world conditions.
public static partial class BehaviorAnalysis
{
    static readonly HashSet<string> Known = ["State", "ArgumentGroup", "Shoot", "Wander", "Follow", "StayCloseToSpawn", "Orbit", "Prioritize",
        "HpLessTransition", "TimedTransition", "PlayerWithinTransition", "NoPlayerWithinTransition", "AnyEntityWithinTransition", "NoEntityWithinTransition", "EntityWithinTransition", "EntitiesNotExistsTransition", "OnGroundTransition", "NotMovingTransition", "EntityExistsTransition", "EntityNotExistsTransition",
        "Spawn", "ConditionEffectBehavior", "Taunt", "Cooldown", "Timed", "OnDeathBehavior", "TimedRandomTransition",
        "DamageTakenTransition", "EntityHpLessTransition", "HealSelf", "GiveTemporaryDefense", "SizeTransform", "ChangeSize", "SetAltTexture", "SizeTransformComplete", "SequentialAimedSweep", "GordoSpiral", "GapRing", "GordoRotation", "ReturnToSpawn", "StayBack", "Charge", "FollowDistance", "LockedFollowDistance", "DelayedOriginHazards", "FollowDistanceCompleteTransition", "LockedFollowDistanceCompleteTransition", "SpawnGroup", "Reproduce", "ReproduceGroup", "GroupNotExistTransition", "EdgeCrossCut", "ClockSweep", "CorridorPulse", "OrientationEcho", "ConvergingSnare", "EchoSnare"];
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
        "TimedRandomTransition"=>$"After {Value(node,"time")} ms · choose"+(Value(node,"randomizedTime","false")=="true"?" · random start":""),
        "HpLessTransition" => Number(node, "threshold") is { } hp ? $"HP < {hp * 100:0.##}%" : $"HP < {Value(node, "threshold")}",
        "DamageTakenTransition"=>$"Credited damage ≥ {Value(node,"damage")}",
        "SizeTransformComplete"=>"Size transform complete",
        "FollowDistanceCompleteTransition"=>"Bounded follow complete",
        "LockedFollowDistanceCompleteTransition"=>"Locked follow complete",
        "GroupNotExistTransition"=>$"No group {Value(node,"group")} within {Value(node,"dist")} tiles",
        "EntityHpLessTransition"=>$"Nearest name {Value(node,"entity")} · HP < "+(Number(node,"threshold") is {} entityHp?$"{entityHp*100:0.##}%":Value(node,"threshold")),
        "PlayerWithinTransition" => $"Player within {Value(node, "dist")} tiles" + (Value(node, "seeInvis", "false") == "true" ? " · sees invisible" : ""),
        "NoPlayerWithinTransition"=>$"No visible player within {Value(node,"dist")} tiles",
        "AnyEntityWithinTransition"=>$"Another enemy or non-hidden player within {Value(node,"dist")} tiles",
        "NoEntityWithinTransition"=>$"No other enemy or non-hidden player within {Value(node,"dist")} tiles",
        "EntityWithinTransition"=>$"XML name {Value(node,"entity")} within {Value(node,"dist")} tiles",
        "EntitiesNotExistsTransition"=>CanMatch(node)?$"All selected types absent within {Value(node,"dist")} tiles":"Empty target list · never matches",
        "OnGroundTransition"=>$"On ground {Value(node,"ground")}",
        "NotMovingTransition"=>$"Position unchanged at {Value(node,"delay","250")} ms checks",
        "EntityExistsTransition" => $"{Resource(node)} within {Value(node, "dist")} tiles",
        "EntityNotExistsTransition" => Value(node, "checkAttackTarget", "false") == "true" ? "Attack target absent" : $"No {(Value(node, "target") == "null" ? "player" : Resource(node))} within {Value(node, "dist")} tiles",
        _ => node.Kind
    };
    static bool Covers(BehaviorNode earlier, BehaviorNode later)
    {
        if(!CanMatch(earlier)||!CanMatch(later))return false;
        // Timers are stateful and later timers are not ticked after an earlier match. Do not compare their durations.
        if (earlier.Kind == "TimedTransition" && Number(earlier, "time") == 0 && Value(earlier, "randomized", "false") == "false") return true;
        if(earlier.Kind=="TimedRandomTransition"&&Number(earlier,"time")==0&&ChoiceTargets(earlier) is {Count:>0})return true;
        if (earlier.Kind != later.Kind) return false;
        if(earlier.Kind=="NoPlayerWithinTransition")return Number(earlier,"dist") is{} a&&Number(later,"dist") is{} b&&a<=b;
        // Native Any*Nearby squares an int radius. Do not infer monotonicity
        // across negative radii or overflowing integer squares.
        if(earlier.Kind is "AnyEntityWithinTransition" or "NoEntityWithinTransition")return Number(earlier,"dist") is >=0 and <=46340 and var a&&Number(later,"dist") is >=0 and <=46340 and var b&&(earlier.Kind=="AnyEntityWithinTransition"?a>=b:a<=b);
        if(earlier.Kind=="OnGroundTransition")return BehaviorDocument.LiteralString(Value(earlier,"ground")) is{} ground&&ground==BehaviorDocument.LiteralString(Value(later,"ground"));
        if(earlier.Kind=="EntityWithinTransition")return BehaviorDocument.LiteralString(Value(earlier,"entity")) is{} name&&name==BehaviorDocument.LiteralString(Value(later,"entity"))&&Number(earlier,"dist") is{} a&&Number(later,"dist") is{} b&&a>=b;
        if (earlier.Kind == "HpLessTransition") return Number(earlier, "threshold") is { } a && Number(later, "threshold") is { } b && a >= b;
        if(earlier.Kind=="DamageTakenTransition")return Number(earlier,"damage") is {} a&&Number(later,"damage") is {} b&&a<=b;
        if(earlier.Kind=="EntityHpLessTransition")return BehaviorDocument.LiteralString(Value(earlier,"entity")) is {} aName&&BehaviorDocument.LiteralString(Value(later,"entity")) is {} bName&&string.Equals(aName,bName,StringComparison.OrdinalIgnoreCase)&&Number(earlier,"dist") is {} distance&&distance==Number(later,"dist")&&Number(earlier,"threshold") is {} a&&Number(later,"threshold") is {} b&&a>=b;
        if (earlier.Kind == "PlayerWithinTransition") return Number(earlier, "dist") is { } a && Number(later, "dist") is { } b && a >= b &&
            Value(earlier, "seeInvis", "false") == Value(later, "seeInvis", "false");
        return false;
    }
    static bool CanMatch(BehaviorNode node)
    {
        if(node.Kind!="EntitiesNotExistsTransition")return true;
        var lists=node.Parameters.Where(p=>p.Name.Split('[')[0]=="targets").Select(p=>BehaviorDocument.PhaseLiterals(p.Expression)).ToArray();
        return lists.Any(l=>l==null)||lists.Sum(l=>l!.Count)>0;
    }
    static IReadOnlyList<string>? ChoiceTargets(BehaviorNode node)
    {
        var values=node.Parameters.Where(p=>p.Name.Split('[')[0]=="states").Select(p=>BehaviorDocument.PhaseLiterals(p.Expression)).ToArray();
        return values.Any(v=>v==null)?null:values.SelectMany(v=>v!).Select(v=>v.Name).ToArray();
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
            var transitions = state.StateChildren.Where(n => n.IsTransition).ToArray();
            for (var i = 0; i < transitions.Length; i++)
            {
                var node = transitions[i]; var expression = Expression(node, "targetState");
                var values=node.Kind=="TimedRandomTransition"?ChoiceTargets(node):expression==null?null:BehaviorDocument.PhaseLiterals(expression)?.Select(v=>v.Name).ToArray();
                var destinations=values?.GroupBy(v=>v).Select(g=>new PhaseDestination(g.Key,names.TryGetValue(g.Key,out var matches)&&matches.Length==1?matches[0].Id:null,g.Count())).ToArray()??[];
                var targetName=destinations.Length==0?null:string.Join(" · ",destinations.Select(d=>d.Name+(d.Slots>1?" ×"+d.Slots:"")));
                var targetId=destinations.Length==1?destinations[0].Id:null;
                edges.Add(new(node, state.Id, i + 1, Condition(node), targetName, targetId){Destinations=destinations,TargetsKnown=values!=null,CanMatch=CanMatch(node)});
                var shadow = transitions.Take(i).FirstOrDefault(earlier => Covers(earlier, node));
                if (shadow != null) diagnostics.Add(new($"Priority conflict: transition {i + 1} in {state.Label} is shadowed by transition {Array.IndexOf(transitions, shadow) + 1}. The first matching transition wins.", node.Id, false));
                if (destinations.Any(d=>d.Id == state.Id)) diagnostics.Add(new($"Self transition in {state.Label}: stays in this branch or enters its first nested phase. Common-ancestor entry callbacks are not rerun.", node.Id, false));
            }
        }
        var incomplete = root.Kind!="State" || nodes.Any(n => !Known.Contains(n.Kind)) || states.Any(s => s.Parameters.Any(p => p.Name is "name" or "$0" && BehaviorDocument.LiteralString(p.Expression) == null)) || names.Any(g => g.Value.Length != 1) || edges.Any(e => !e.TargetsKnown||e.Destinations.Count==0);
        if (incomplete) diagnostics.Add(new("Reachability analysis is incomplete: detached source bodies, opaque/custom nodes, dynamic targets or duplicate names may change phase entry. Unreachable-phase notices are suppressed.", root.Id, false));
        else
        {
            var parent = new Dictionary<Guid, BehaviorNode>();
            foreach (var state in states) foreach (var child in state.StateChildren.Where(n => n.Kind == "State")) parent[child.Id] = state;
            var reachable = new HashSet<Guid>(); var pending = new Queue<BehaviorNode>();
            void Enter(BehaviorNode state)
            {
                while (state.StateChildren.FirstOrDefault(n => n.Kind == "State") is { } child) state = child;
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
                    foreach (var target in edges.Where(e => e.OwnerId == state.Id&&e.CanMatch).SelectMany(e=>e.Destinations).Where(d=>d.Id!=null)) Enter(states.Single(s => s.Id == target.Id));
                }
            }
            foreach (var state in states.Where(s => !reachable.Contains(s.Id))) diagnostics.Add(new($"Unreachable phase: {state.Label}. No literal transition or first-child entry reaches this phase.", state.Id, false));
        }
        var cycles=incomplete||edges.Any(e=>e.Destinations.Any(d=>d.Id==null))?[]:FindCycles(root,states,edges.Where(e=>e.CanMatch).ToArray());
        foreach(var cycle in cycles.Where(c=>c.PhaseIds.Count>1))
        {
            var labels=cycle.PhaseIds.Select(id=>states.Single(s=>s.Id==id).Label).ToArray();var title=string.Join(" · ",labels.Take(4))+(labels.Length>4?$" · {labels.Length} phases":"");
            var exits=cycle.ExitTransitionIds.Count==0?"No transition leaves this group in the analyzed graph.":$"{cycle.ExitTransitionIds.Count} transition{(cycle.ExitTransitionIds.Count==1?" can":"s can")} leave this group when their conditions match.";
            diagnostics.Add(new($"Phase cycle: {title}. {exits} "+(cycle.Reachable?"Repetition depends on conditions and runtime state.":"This cycle is outside known entry routes."),cycle.TransitionIds[0],false));
        }
        return new(edges, diagnostics){Cycles=cycles};
    }
}
