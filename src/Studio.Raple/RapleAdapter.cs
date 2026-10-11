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
public sealed partial class RapleAdapter : IBehaviorSchema
{
    static readonly object resourceGate = new();
    static (string Root, string Hash)? loadedResources;
    public string LoadedResourceHash { get; }
    public string ServerRoot { get; }
    public string FragmentRoot(IReadOnlyList<string> expressions) => "new global::TKR.WorldServer.logic.State(" + string.Join(",\n", expressions) + ")";
    public string CollectionFragmentRoot(IReadOnlyList<string> expressions) => "new global::TKR.WorldServer.logic.State(children: [" + string.Join(",\n", expressions) + "])";
    readonly SourceSchemaCache sourceSchemas;
    public IBehaviorSchema ForSource(string source, string? sourceFile)
    {
        if (string.IsNullOrEmpty(sourceFile)) return this;
        var path = Path.GetFullPath(sourceFile);
        var directory = Path.GetFullPath(Path.Combine(ServerRoot, "TKR.WorldServer")) + Path.DirectorySeparatorChar;
        return File.Exists(path) && path.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
            ? new SourceBehaviorSchema(this, sourceSchemas, source, path) : this;
    }
    static readonly Assembly EngineAssembly = typeof(State).Assembly;
    static readonly Lazy<NativeConstructorSchema> ConstructorSchema = new(() => new(EngineAssembly));
    static Type RuntimeType(string name) => EngineAssembly.GetTypes().Single(t => t.Name == name && (typeof(IStateChildren).IsAssignableFrom(t) || t.FullName == "TKR.WorldServer.logic.Cooldown"));
    internal static string? NativeTypeName(string name) => EngineAssembly.GetTypes().Where(t => t.Name == name && typeof(IStateChildren).IsAssignableFrom(t)).Select(t => t.FullName?.Replace('+', '.')).SingleOrDefault();
    public string CreationTypeName(string name) => "global::" + string.Join(".", RuntimeType(name).FullName!.Replace('+', '.').Split('.').Select(part => SyntaxFacts.GetKeywordKind(part) == SyntaxKind.None ? part : "@" + part));
    // Explicit scope, independent of the reflection catalog. Constructors outside this list remain opaque.
    static readonly HashSet<string> Supported = ["State", "Shoot", "Wander", "Follow", "StayCloseToSpawn", "Orbit",
        "Prioritize", "HpLessTransition", "TimedTransition", "PlayerWithinTransition", "EntityNotExistsTransition",
        "EntityExistsTransition", "Spawn", "ConditionEffectBehavior", "Taunt", "Cooldown", "Timed", "OnDeathBehavior",
        "MoveTo", "MoveTo2", "MoveTo3", "SpawnRelativeMove", "MoveLine", "NewMove", "OrderedBehavior",
        "InstantSpawnRing", "Order", "OrderOnce", "OrderOnDeath"];
    public IReadOnlyList<Primitive> Primitives { get; }
    public RapleAdapter(string root)
    {
        ServerRoot = Path.GetFullPath(root);
        sourceSchemas = new(ServerRoot);
        if (!Directory.Exists(Path.Combine(ServerRoot, "TKR.Shared/resources/xml"))) throw new DirectoryNotFoundException("Choose the Raple Realms server source folder, containing TKR.Shared/resources/xml.");
        SimEngine.Root = ServerRoot;
        lock (resourceGate)
        {
            if (loadedResources == null)
            {
                var resources = Path.Combine(ServerRoot, "TKR.Shared/resources");
                var before = SourceVerification.Fingerprint(resources, "*");
                _ = SimEngine.Data;
                if (before != SourceVerification.Fingerprint(resources, "*")) throw new InvalidOperationException("Game resources changed while loading. Restart Studio.");
                loadedResources = (ServerRoot, before);
            }
            LoadedResourceHash = string.Equals(loadedResources.Value.Root, ServerRoot, StringComparison.OrdinalIgnoreCase) ? loadedResources.Value.Hash : "";
        }
        Primitives = EngineAssembly.GetTypes().Where(t => typeof(IStateChildren).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructors().Length > 0)
            .OrderBy(t => t.Name).Select(t => new Primitive(t.Name,
                typeof(Transition).IsAssignableFrom(t) ? "Transitions" : t.Name == "State" ? "Phases" :
                t.Name.Contains("Spawn") ? "Spawning" :
                t.Name.Contains("Shoot") || t.Name.Contains("Ring") || t.Name.Contains("Attack") ? "Attacks" : "Actions",
                Constructor(t).GetParameters().Select(p => new PrimitiveParameter(p.Name!, p.ParameterType.Name,
                    p.HasDefaultValue ? FormatDefault(p) : "required", Unit(p.Name!, t.Name), Explain(p.Name!))).ToArray(), Supported.Contains(t.Name))).ToArray();
    }
    public static ConstructorInfo Constructor(Type type) => type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
    static string FormatDefault(ParameterInfo p) => p.DefaultValue == null ? (p.ParameterType.IsValueType ? "default" : "null") : Convert.ToString(p.DefaultValue, CultureInfo.InvariantCulture) ?? "default";
    public string[] ParameterNames(string kind) => kind == "State" ? ["name"] : EngineAssembly.GetTypes().Where(t => t.Name == kind && typeof(IStateChildren).IsAssignableFrom(t))
        .SelectMany(t => t.GetConstructors()).SelectMany(c => c.GetParameters()).Select(p => p.Name!).Distinct().ToArray();
    static string Unit(string name, string? kind = null) => kind == "NewMove" && name == "cooldown" ? "seconds" : IsPathKind(kind) && name is "x" or "y" or "X" or "Y" or "offsetX" or "offsetY" or "distance" ? "tiles" : IsPathKind(kind) && name == "direction" ? "degrees" : name.Contains("Angle", StringComparison.OrdinalIgnoreCase) ? "degrees" : name.Contains("coolDown", StringComparison.OrdinalIgnoreCase) || name is "time" or "duration" ? "ms" : name.Contains("Range", StringComparison.OrdinalIgnoreCase) || name is "radius" or "dist" or "range" ? "tiles" : name == "threshold" ? "fraction of max HP" : name == "speed" ? "runtime speed coefficient" : "";
    static string Explain(string name) => name switch
    {
        "coolDown" => "Runtime countdown, quantized to 100 ms ticks. Shoot generally adds a tick between volleys; 0 normalizes to its runtime default.",
        "threshold" => "HpLessTransition uses strict less-than, not less-than-or-equal.",
        "shootAngle" => "Spacing between adjacent shots in degrees; omitted spacing defaults to 360 / count.",
        "rotateAngle" => "Degrees added per volley. Shoot stores its rotation count on the behavior instance.",
        "projectileIndex" => "Index in this entity's resource XML; it is not a projectile object type.",
        "speed" => "Passed unchanged to the runtime; this is not tiles per second.",
        "period" => "Timed subtracts the native tick duration once for EACH child, in source order. Completion/reset can happen within that loop; this is not a single wall-clock duration shared by all children.",
        "targetState" => "Exact state name; duplicate state names are ambiguous in the runtime's global name map.",
        _ => "Constructor parameter from the supplied runtime. Expressions beyond literal values may require advanced source editing."
    };
    public IReadOnlyList<CatalogEntry> Catalog()
    {
        var result = new List<CatalogEntry>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(ServerRoot, "TKR.WorldServer/logic/db"), "*.cs"))
        {
            foreach (var id in EntityIds(File.ReadAllText(file), file))
                if (SimEngine.Data.IdToObjectType.TryGetValue(id, out var type) && SimEngine.Data.ObjectDescs[type].Enemy)
                    result.Add(new(id, type, file));
        }
        return result.OrderBy(e => e.Id).ToArray();
    }
    public IReadOnlyList<string> EntityIds(string source, string sourceFile)
    {
        // Library discovery needs roots, not semantic binding of every nested action
        // in every file. Resolve the selected document fully when it is opened.
        var ids = new BehaviorDocument(source, this).Entities.Select(e => e.EntityId).ToList();
        var scoped = ForSource(source, sourceFile);
        foreach (var call in CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Init" } || call.ArgumentList.Arguments.Count < 2 ||
                call.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax { Token.Value: string id } || ids.Contains(id)) continue;
            var root = call.ArgumentList.Arguments[1].Expression;
            if (scoped.At(root.SpanStart).DescribeExpression(root.ToString())?.IsState == true) ids.Add(id);
        }
        return ids;
    }
    public IEnumerable<DocumentDiagnostic> Validate(BehaviorDocument document, string? entityId = null)
    {
        foreach (var diagnostic in document.SourceDiagnostics()) yield return diagnostic;
        foreach (var entity in document.Entities.Where(e => entityId == null || e.EntityId == entityId))
        {
            var nodes = entity.Root.Descendants().ToArray();
            var states = nodes.Where(n => n.Kind == "State").ToArray();
            foreach (var group in states.Where(s => document.StateName(s.Id) != null).GroupBy(s => document.StateName(s.Id)).Where(g => g.Count() > 1))
                yield return new("Duplicate state name: " + group.Key, group.First().Id);
            foreach (var n in nodes)
            {
                if (n.Kind == "ArgumentGroup") continue;
                if (n.Kind == "Spread") { yield return new("Spread contents are evaluated by compiled source preview; their contents are retained in source.", n.Id, false); continue; }
                var sourceBinding = document.SchemaAt(n)?.DescribeExpression(document.Expression(n));
                if (n.Kind == "Opaque") yield return new("Visual structure is incomplete; the original expression is retained.", n.Id, false);
                else if (!Supported.Contains(n.Kind)) yield return new($"{n.Kind} requires compiled source preview. Its game-context fidelity is not yet certified.", n.Id, false);
                var binding = Binding(document.Expression(n));
                if (n.Kind == "Prioritize" && n.Children.Sum(c => c.Kind == "ArgumentGroup" ? c.Children.Count : 1) == 0 && n.Parameters.All(p => p.Name != "children"))
                    yield return new("Prioritize requires at least one cycle behavior for native execution.", n.Id);
                if (sourceBinding == null && binding.RequiresContext && n.Kind != "Opaque") yield return new($"{n.Kind} requires source context for complete constructor binding.", n.Id, false);
                else if (sourceBinding == null && binding.Errors.Length > 0 && n.Kind != "Opaque") foreach (var error in binding.Errors.Distinct()) yield return new(n.Kind + ": " + error, n.Id);
                foreach (var p in n.Parameters)
                {
                    // Factory parameter names do not establish native units, resource
                    // IDs or numeric limits. Its own source defines that contract.
                    if (!Supported.Contains(n.Kind)) continue;
                    var parameterType = ParameterType(binding, p.Name);
                    string? typeError = null;
                    if (parameterType != null && !binding.RequiresContext)
                    {
                        var parsed = SyntaxFactory.ParseExpression(p.Expression);
                        if (parsed is LiteralExpressionSyntax or PrefixUnaryExpressionSyntax or MemberAccessExpressionSyntax || parsed is ObjectCreationExpressionSyntax { Type: IdentifierNameSyntax { Identifier.ValueText: "Cooldown" } })
                            try { Evaluate(parsed, parameterType); } catch (Exception e) { typeError = e.GetBaseException().Message; }
                    }
                    if (typeError != null) yield return new($"{n.Kind}.{p.Name}: {typeError}", n.Id);
                    if (p.Name is "targetState" or "state" && (document.SchemaAt(n)?.TargetsPhaseOf(document.Expression(n), p.Name, entity.EntityId, entity.EntityId) ?? TargetsPhaseOf(document.Expression(n), p.Name, entity.EntityId, entity.EntityId)) == true && !nodes.Any(n => n.Kind is "Opaque" or "Factory" or "Reference" or "Spread") && !states.Any(s => s.Parameters.Any(p => p.Name is "name" or "$0" && BehaviorDocument.LiteralString(p.Expression) == null)) && BehaviorDocument.LiteralString(p.Expression) is { } stateTarget && !states.Any(s => (document.StateName(s.Id) ?? "") == stateTarget))
                        yield return new("Missing transition target: " + p.Expression, n.Id);
                    if (p.Name is "children" or "target" or "childName" && BehaviorDocument.LiteralString(p.Expression) is { } resourceTarget && !SimEngine.Data.IdToObjectType.ContainsKey(resourceTarget))
                        yield return new("Missing entity resource: " + p.Expression, n.Id);
                    if (double.TryParse(p.Expression, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        if (!double.IsFinite(value) || p.Name == "count" && (value < 1 || value > 255 || value != Math.Truncate(value)) ||
                            p.Name == "threshold" && (value < 0 || value > 1) || p.Name is "radius" or "speed" && value < 0 ||
                            p.Name == "maxChildren" && (value < 0 || value > 128) || n.Kind == "InstantSpawnRing" && p.Name == "count" && value > 12)
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
    public bool NeedsCompiledRuntime(BehaviorDocument document, string entityId) => document.Entities.Single(e => e.EntityId == entityId)
        .Root.Descendants().Any(n => n.Kind != "ArgumentGroup" && (document.SchemaAt(n)?.DescribeExpression(document.Expression(n))?.RequiresCompiled == true || !Supported.Contains(n.Kind) || Binding(document.Expression(n)).RequiresContext));
    public static object? Evaluate(ExpressionSyntax expr, Type expected)
    {
        var nullable = Nullable.GetUnderlyingType(expected); var type = nullable ?? expected;
        if (!ConstructorSchema.Value.CanConvert(expr.ToString(), expected))
            throw new ArgumentException("Value does not have a valid C# conversion to " + expected.Name + ".");
        if (expr is ParenthesizedExpressionSyntax parenthesized) return Evaluate(parenthesized.Expression, expected);
        if (expr is DefaultExpressionSyntax || expr.IsKind(SyntaxKind.DefaultLiteralExpression))
            return expected.IsValueType ? Activator.CreateInstance(expected) : null;
        if (expr is CastExpressionSyntax cast && type.Name == "Cooldown")
        {
            var value = Evaluate(cast.Expression, typeof(int));
            return type.GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [value]);
        }
        if (expr is CollectionExpressionSyntax collection)
        {
            if (!expected.IsArray) throw new NotSupportedException("This collection target requires compiled source preview.");
            if (collection.Elements.Any(e => e is SpreadElementSyntax)) throw new NotSupportedException("Spread inputs require compiled source preview.");
            var element = expected.GetElementType()!; var array = Array.CreateInstance(element, collection.Elements.Count);
            for (var i = 0; i < collection.Elements.Count; i++) array.SetValue(Evaluate(((ExpressionElementSyntax)collection.Elements[i]).Expression, element), i);
            return array;
        }
        InitializerExpressionSyntax? initializer = expr switch { ArrayCreationExpressionSyntax array => array.Initializer, ImplicitArrayCreationExpressionSyntax array => array.Initializer, _ => null };
        if (initializer != null)
        {
            var sourceType = ConstructorSchema.Value.ValueInfo(expr.ToString()).Type;
            var arrayType = sourceType?.IsArray == true ? sourceType : expected;
            if (!arrayType.IsArray) throw new ArgumentException("Expected an array value.");
            var element = arrayType.GetElementType()!; var array = Array.CreateInstance(element, initializer.Expressions.Count);
            for (var i = 0; i < initializer.Expressions.Count; i++) array.SetValue(Evaluate(initializer.Expressions[i], element), i);
            return array;
        }
        if (expr is ObjectCreationExpressionSyntax creation)
        {
            var name = creation.Type.GetLastToken().ValueText;
            if (!Supported.Contains(name)) throw new NotSupportedException(name + " is preserved, but not yet supported in the Studio runtime adapter.");
            var result = EvaluateConstructor(creation, RuntimeType(name));
            if (!expected.IsInstanceOfType(result) && !type.IsInstanceOfType(result)) throw new ArgumentException("Constructor result does not match " + expected.Name);
            return result;
        }
        var constant = ConstructorSchema.Value.ValueInfo(expr.ToString());
        if (!constant.Constant) throw new NotSupportedException("Opaque expression requires source context: " + expr);
        if (constant.Value == null) return null;
        if (type.Name == "Cooldown") return type.GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [Convert.ToInt32(constant.Value, CultureInfo.InvariantCulture)]);
        if (type.IsEnum) return Enum.ToObject(type, constant.Value);
        return type.IsInstanceOfType(constant.Value) ? constant.Value : Convert.ChangeType(constant.Value, type, CultureInfo.InvariantCulture);
    }
    public string ProjectileSummary(string entityId) => SimEngine.Data.IdToObjectType.TryGetValue(entityId, out var type)
        ? string.Join("\n", SimEngine.Data.ObjectDescs[type].Projectiles.Select(p => $"#{p.Key}: {p.Value.ObjectId} · speed {p.Value.Speed} · life {p.Value.LifetimeMS} ms · damage {p.Value.MinDamage}–{p.Value.MaxDamage}")) : "Entity resource is missing.";
}
