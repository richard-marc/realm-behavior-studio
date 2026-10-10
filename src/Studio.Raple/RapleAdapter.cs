using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;
using TKR.WorldServer.logic;
using BossPatternSim;

namespace Studio.Raple;

public record PrimitiveParameter(string Name, string Type, string Default, string Unit, string Explanation);
public record Primitive(string Name, string Category, IReadOnlyList<PrimitiveParameter> Parameters, bool Simulatable);
public record CatalogEntry(string Id, ushort ObjectType, string SourceFile)
{
    public override string ToString() => Id;
}
public sealed class RapleAdapter
{
    public string ServerRoot { get; }
    static readonly Assembly EngineAssembly = typeof(State).Assembly;
    static Type RuntimeType(string name) => EngineAssembly.GetTypes().Single(t => t.Name == name && (typeof(IStateChildren).IsAssignableFrom(t) || t.FullName == "TKR.WorldServer.logic.Cooldown"));
    // Explicit scope, independent of the reflection catalog. Constructors outside this list remain opaque.
    static readonly HashSet<string> Supported = ["State", "Shoot", "Wander", "Follow", "StayCloseToSpawn", "Orbit",
        "Prioritize", "HpLessTransition", "TimedTransition", "PlayerWithinTransition", "EntityNotExistsTransition",
        "EntityExistsTransition", "Spawn", "ConditionalEffect", "ConditionalEffect2", "Taunt", "Cooldown"];
    public IReadOnlyList<Primitive> Primitives { get; }
    public RapleAdapter(string root)
    {
        ServerRoot = Path.GetFullPath(root);
        if (!Directory.Exists(Path.Combine(ServerRoot, "TKR.Shared/resources/xml"))) throw new DirectoryNotFoundException("Choose the Raple Realms server source folder, containing TKR.Shared/resources/xml.");
        SimEngine.Root = ServerRoot;
        Primitives = EngineAssembly.GetTypes().Where(t => typeof(IStateChildren).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructors().Length > 0)
            .OrderBy(t => t.Name).Select(t => new Primitive(t.Name,
                typeof(Transition).IsAssignableFrom(t) ? "Transitions" : t.Name == "State" ? "Phases" :
                t.Name.Contains("Shoot") || t.Name.Contains("Ring") || t.Name.Contains("Attack") ? "Attacks" :
                t.Name.Contains("Spawn") ? "Spawning" : "Actions",
                Constructor(t).GetParameters().Select(p => new PrimitiveParameter(p.Name!, p.ParameterType.Name,
                    p.HasDefaultValue ? FormatDefault(p) : "required", Unit(p.Name!), Explain(p.Name!))).ToArray(), Supported.Contains(t.Name))).ToArray();
    }
    public static ConstructorInfo Constructor(Type type) => type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
    static string FormatDefault(ParameterInfo p) => p.DefaultValue == null ? (p.ParameterType.IsValueType ? "default" : "null") : Convert.ToString(p.DefaultValue, CultureInfo.InvariantCulture) ?? "default";
    public string[] ParameterNames(string kind) => kind == "State" ? ["name"] : Primitives.FirstOrDefault(p => p.Name == kind)?.Parameters.Select(p => p.Name).ToArray() ?? [];
    static string Unit(string name) => name.Contains("Angle", StringComparison.OrdinalIgnoreCase) ? "degrees" : name.Contains("coolDown", StringComparison.OrdinalIgnoreCase) || name is "time" or "duration" ? "ms" : name.Contains("Range", StringComparison.OrdinalIgnoreCase) || name is "radius" or "dist" or "range" ? "tiles" : name == "threshold" ? "fraction of max HP" : name == "speed" ? "runtime speed coefficient" : "";
    static string Explain(string name) => name switch
    {
        "coolDown" => "Runtime countdown, quantized to 100 ms ticks. Shoot generally adds a tick between volleys; 0 normalizes to its runtime default.",
        "threshold" => "HpLessTransition uses strict less-than, not less-than-or-equal.",
        "shootAngle" => "Spacing between adjacent shots in degrees; omitted spacing defaults to 360 / count.",
        "rotateAngle" => "Degrees added per volley. Shoot stores its rotation count on the behavior instance.",
        "projectileIndex" => "Index in this entity's resource XML; it is not a projectile object type.",
        "speed" => "Passed unchanged to the runtime; this is not tiles per second.",
        "targetState" => "Exact state name; duplicate state names are ambiguous in the runtime's global name map.",
        _ => "Constructor parameter from the supplied runtime. Expressions beyond literal values may require advanced source editing."
    };
    public IReadOnlyList<CatalogEntry> Catalog()
    {
        var result = new List<CatalogEntry>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(ServerRoot, "TKR.WorldServer/logic/db"), "*.cs"))
        {
            var doc = new BehaviorDocument(File.ReadAllText(file), ParameterNames);
            foreach (var e in doc.Entities)
                if (SimEngine.Data.IdToObjectType.TryGetValue(e.EntityId, out var type) && SimEngine.Data.ObjectDescs[type].Enemy)
                    result.Add(new(e.EntityId, type, file));
        }
        return result.OrderBy(e => e.Id).ToArray();
    }
    public IEnumerable<DocumentDiagnostic> Validate(BehaviorDocument document, string? entityId = null)
    {
        foreach (var entity in document.Entities.Where(e => entityId == null || e.EntityId == entityId))
        {
            var nodes = entity.Root.Descendants().ToArray();
            var states = nodes.Where(n => n.Kind == "State").ToArray();
            foreach (var group in states.Where(s => s.Label != "Root").GroupBy(s => s.Label).Where(g => g.Count() > 1))
                yield return new("Duplicate state name: " + group.Key, group.First().Id);
            foreach (var n in nodes)
            {
                if (!Supported.Contains(n.Kind)) yield return new($"{n.Kind} is preserved in export; Studio cannot simulate it yet.", n.Id, false);
                foreach (var p in n.Parameters)
                {
                    var parameterType = n.Kind == "State" ? typeof(string) : Primitives.FirstOrDefault(item => item.Name == n.Kind)?.Parameters.Any(item => item.Name == p.Name) == true
                        ? Constructor(RuntimeType(n.Kind)).GetParameters().First(item => item.Name == p.Name).ParameterType : null;
                    string? typeError = null;
                    if (parameterType != null)
                    {
                        var parsed = SyntaxFactory.ParseExpression(p.Expression);
                        if (parsed is LiteralExpressionSyntax or PrefixUnaryExpressionSyntax or MemberAccessExpressionSyntax || parsed is ObjectCreationExpressionSyntax { Type: IdentifierNameSyntax { Identifier.ValueText: "Cooldown" } })
                            try { Evaluate(parsed, parameterType); } catch (Exception e) { typeError = e.GetBaseException().Message; }
                    }
                    if (typeError != null) yield return new($"{n.Kind}.{p.Name}: {typeError}", n.Id);
                    if (p.Name == "targetState" && BehaviorDocument.LiteralString(p.Expression) is { } stateTarget && !states.Any(s => s.Label == stateTarget))
                        yield return new("Missing transition target: " + p.Expression, n.Id);
                    if (p.Name is "children" or "target" && BehaviorDocument.LiteralString(p.Expression) is { } resourceTarget && !SimEngine.Data.IdToObjectType.ContainsKey(resourceTarget))
                        yield return new("Missing entity resource: " + p.Expression, n.Id);
                    if (double.TryParse(p.Expression, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        if (!double.IsFinite(value) || p.Name == "count" && (value < 1 || value > 255 || value != Math.Truncate(value)) ||
                            p.Name == "threshold" && (value < 0 || value > 1) || p.Name is "radius" or "speed" && value < 0 ||
                            p.Name == "maxChildren" && (value < 0 || value > 128))
                            yield return new("Invalid value for " + p.Name + ": " + p.Expression, n.Id);
                        if (p.Name == "projectileIndex" && SimEngine.Data.IdToObjectType.TryGetValue(entity.EntityId, out var type) && !SimEngine.Data.ObjectDescs[type].Projectiles.ContainsKey((int)value))
                            yield return new("This entity has no projectile index " + value, n.Id);
                    }
                }
            }
        }
    }
    public State BuildState(BehaviorDocument document, string entityId)
    {
        var entity = document.Entities.Single(e => e.EntityId == entityId);
        var errors = Validate(document, entityId).Where(d => d.IsError).ToArray();
        if (errors.Length > 0) throw new ArgumentException(string.Join("\n", errors.Select(d => d.Message)));
        var expression = SyntaxFactory.ParseExpression(document.Expression(entity.Root));
        var root = (State)Evaluate(expression, typeof(State))!;
        var states = new Dictionary<string, State>();
        typeof(State).GetMethod("Resolve", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(root, [states]);
        typeof(State).GetMethod("ResolveChildren", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(root, [states]);
        return root;
    }
    public static object? Evaluate(ExpressionSyntax expr, Type expected)
    {
        var nullable = Nullable.GetUnderlyingType(expected); var type = nullable ?? expected;
        if (expr is LiteralExpressionSyntax literal)
        {
            if (literal.IsKind(SyntaxKind.NullLiteralExpression))
            {
                if (expected.IsValueType && nullable == null) throw new ArgumentException("This parameter does not accept null.");
                return null;
            }
            if (type == typeof(string) && literal.Token.Value is not string || type == typeof(bool) && literal.Token.Value is not bool || type != typeof(bool) && literal.Token.Value is bool || type != typeof(string) && literal.Token.Value is string)
                throw new ArgumentException("Expected a " + type.Name + " value.");
            if ((type == typeof(int) || type.Name == "Cooldown") && literal.Token.Value is not int) throw new ArgumentException("Expected a 32-bit integer value.");
            if (type.Name == "Cooldown") return Activator.CreateInstance(type, Convert.ToInt32(literal.Token.Value, CultureInfo.InvariantCulture), 0);
            return Convert.ChangeType(literal.Token.Value, type, CultureInfo.InvariantCulture);
        }
        if (expr is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.UnaryMinusExpression))
        {
            if (type == typeof(string) || type == typeof(bool)) throw new ArgumentException("This parameter does not accept a negative number.");
            if ((type == typeof(int) || type.Name == "Cooldown") && unary.Operand is not LiteralExpressionSyntax { Token.Value: int }) throw new ArgumentException("Expected a 32-bit integer value.");
            var value = -(double)Evaluate(unary.Operand, typeof(double))!;
            if (type.Name == "Cooldown") return Activator.CreateInstance(type, (int)value, 0);
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }
        if (expr is MemberAccessExpressionSyntax member && type.IsEnum && member.Expression.ToString().Split('.').Last() == type.Name)
            return Enum.Parse(type, member.Name.Identifier.ValueText);
        if (expr is not ObjectCreationExpressionSyntax creation) throw new NotSupportedException("Opaque expression cannot be simulated: " + expr);
        var name = creation.Type.ToString().Split('.').Last();
        if (!Supported.Contains(name)) throw new NotSupportedException(name + " is preserved, but not yet supported in the Studio runtime adapter.");
        var runtimeType = RuntimeType(name);
        if (name == "State")
        {
            var args = creation.ArgumentList!.Arguments;
            var hasName = args.Count > 0 && args[0].Expression is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.StringLiteralExpression);
            var stateName = hasName ? (string)Evaluate(args[0].Expression, typeof(string))! : "";
            return new State(stateName, args.Skip(hasName ? 1 : 0).Select(a => (IStateChildren)Evaluate(a.Expression, typeof(IStateChildren))!).ToArray());
        }
        var ctor = Constructor(runtimeType); var parameters = ctor.GetParameters();
        var arguments = creation.ArgumentList?.Arguments.ToArray() ?? [];
        var values = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var named = arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == p.Name);
            var positional = i < arguments.Length && arguments[i].NameColon == null ? arguments[i] : null;
            if (p.GetCustomAttribute<ParamArrayAttribute>() != null)
            {
                var tail = arguments.Skip(i).ToArray(); var array = Array.CreateInstance(p.ParameterType.GetElementType()!, tail.Length);
                for (var j = 0; j < tail.Length; j++) array.SetValue(Evaluate(tail[j].Expression, p.ParameterType.GetElementType()!), j);
                values[i] = array;
            }
            else if ((named ?? positional) is { } arg) values[i] = Evaluate(arg.Expression, p.ParameterType);
            else if (p.HasDefaultValue) values[i] = p.DefaultValue ?? (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null);
            else throw new ArgumentException($"{name} requires parameter {p.Name}.");
        }
        if (arguments.Any(a => a.NameColon != null && parameters.All(p => p.Name != a.NameColon.Name.Identifier.ValueText))) throw new ArgumentException("Unknown parameter in " + name);
        return ctor.Invoke(values);
    }
    public string ProjectileSummary(string entityId) => SimEngine.Data.IdToObjectType.TryGetValue(entityId, out var type)
        ? string.Join("\n", SimEngine.Data.ObjectDescs[type].Projectiles.Select(p => $"#{p.Key}: {p.Value.ObjectId} · speed {p.Value.Speed} · life {p.Value.LifetimeMS} ms · damage {p.Value.MinDamage}–{p.Value.MaxDamage}")) : "Entity resource is missing.";
}
