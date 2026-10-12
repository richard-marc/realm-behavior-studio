using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;
using TKR.WorldServer.logic;

namespace Studio.Raple;

public record NativeCallback(string Stage, string Method, string DeclaringType, string SourceFile, int Line);
public record LifecycleAction(BehaviorNode Node, BehaviorNode Phase, bool Inherited, IReadOnlyList<NativeCallback> Callbacks);
public record NativeOverload(string Name, string Signature, IReadOnlyList<PrimitiveParameter> Parameters)
{
    public override string ToString() => Signature;
}

public sealed partial class RapleAdapter
{
    Dictionary<(string Type, string Method), (string File, int Line, bool Empty)>? callbackSource;
    void ReadCallbackSource()
    {
        if (callbackSource != null) return;
        callbackSource = [];
        foreach (var file in Directory.EnumerateFiles(Path.Combine(ServerRoot, "TKR.WorldServer/logic"), "*.cs", SearchOption.AllDirectories))
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                foreach (var method in type.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Modifiers.Any(t => t.IsKind(SyntaxKind.OverrideKeyword)) && m.Identifier.ValueText is "OnStateEntry" or "TickCore" or "TickCoreOrdered" or "OnStateExit" or "OnDeath"))
                    callbackSource[(type.Identifier.ValueText, method.Identifier.ValueText)] = (file, method.GetLocation().GetLineSpan().StartLinePosition.Line + 1, method.Body?.Statements.Count == 0 && method.ExpressionBody == null);
        }
    }
    public IReadOnlyList<NativeCallback> Callbacks(string kind)
    {
        var type = EngineAssembly.GetTypes().FirstOrDefault(t => t.Name == kind && typeof(Behavior).IsAssignableFrom(t));
        if (type == null) return [];
        ReadCallbackSource(); var result = new List<NativeCallback>();
        foreach (var (stage, name, arity) in new[] { ("Entry", "OnStateEntry", 3), ("Ongoing", "TickCore", 3), ("Exit", "OnStateExit", 3), ("Death", "OnDeath", 2) })
        {
            var method = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).FirstOrDefault(m => m.Name == name && m.GetParameters().Length == arity);
            if (method?.DeclaringType == null || method.DeclaringType == typeof(Behavior)) continue;
            if (!callbackSource!.TryGetValue((method.DeclaringType.Name, name), out var source) || source.Empty) continue;
            result.Add(new(stage, name, method.DeclaringType.Name, source.File, source.Line));
        }
        return result;
    }
    public IReadOnlyList<LifecycleAction> PhaseActions(BehaviorDocument document, Guid phaseId)
    {
        var phase = document.Find(phaseId);
        if (phase.Kind != "State") throw new ArgumentException("Select a phase for lifecycle actions.");
        var phases = document.AllNodes.Where(n => n.Kind == "State" && (n.Id == phaseId || phase.Path.StartsWith(n.Path + "/", StringComparison.Ordinal))).OrderByDescending(n => n.Path.Length);
        return phases.SelectMany(owner => owner.StateChildren.Where(n => EngineAssembly.GetTypes().Any(t => t.Name == n.Kind && typeof(Behavior).IsAssignableFrom(t)))
            .Select(node => new LifecycleAction(node, owner, owner.Id != phaseId, Callbacks(node.Kind)))).ToArray();
    }
    public IReadOnlyList<NativeCallback> EffectiveCallbacks(BehaviorDocument document, BehaviorNode node)
    {
        var callbacks = Callbacks(node.Kind).ToArray();
        var direct = node;
        for (var parent = document.Parent(node.Id); parent != null && parent.Kind != "State"; parent = document.Parent(parent.Id))
        {
            if (parent.Kind == "ArgumentGroup") continue;
            callbacks = parent.Kind switch {
                "OnDeathBehavior" => callbacks.Where(c => c.Stage == "Entry").Select(c => c with { Stage = "Death" }).ToArray(),
                "Prioritize" or "Timed" => callbacks.Where(c => c.Stage is "Entry" or "Ongoing").ToArray(),
                "OrderedBehavior" => direct.Id == node.Id ? OrderedCallbacks(node.Kind).ToArray() : [],
                _ => [] };
            direct = parent;
        }
        return callbacks;
    }
    IReadOnlyList<NativeCallback> OrderedCallbacks(string kind)
    {
        var type = EngineAssembly.GetTypes().FirstOrDefault(t => t.Name == kind && typeof(Behavior).IsAssignableFrom(t)); if (type == null) return [];
        ReadCallbackSource(); var method = type.GetMethod("TickCoreOrdered", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method?.DeclaringType == null || method.DeclaringType == typeof(Behavior) || !callbackSource!.TryGetValue((method.DeclaringType.Name, "TickCoreOrdered"), out var source) || source.Empty) return [];
        return [new("Ongoing", "TickCoreOrdered", method.DeclaringType.Name, source.File, source.Line)];
    }
    public IReadOnlyList<NativeOverload> Overloads(string name) => EngineAssembly.GetTypes().Where(t => t.Name == name && typeof(IStateChildren).IsAssignableFrom(t))
        .SelectMany(t => t.GetConstructors()).OrderBy(c => c.GetParameters().Length).Select(c => new NativeOverload(name,
            name + "(" + string.Join(", ", c.GetParameters().Select(p => (p.GetCustomAttribute<ParamArrayAttribute>() != null ? "params " : "") + FriendlyType(p.ParameterType) + " " + p.Name + (p.HasDefaultValue ? " = " + FormatDefault(p) : ""))) + ")",
            c.GetParameters().Select(p => new PrimitiveParameter(p.Name!, FriendlyType(p.ParameterType), p.HasDefaultValue ? FormatDefault(p) : p.GetCustomAttribute<ParamArrayAttribute>() != null && name != "Prioritize" ? "empty array" : "required", Unit(p.Name!, name), Explain(p.Name!,name))).ToArray())).ToArray();
    static string FriendlyType(Type type) => type.IsArray ? FriendlyType(type.GetElementType()!) + "[]" : Nullable.GetUnderlyingType(type) is { } inner ? FriendlyType(inner) + "?" : type.Name switch { "Int32" => "int", "Double" => "double", "Single" => "float", "Boolean" => "bool", "String" => "string", _ => type.Name };
}
