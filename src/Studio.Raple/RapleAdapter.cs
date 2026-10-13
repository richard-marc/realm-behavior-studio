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
public record CatalogEntry(string Id, ushort ObjectType, string SourceFile,string RuntimeKind="Enemy")
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
    internal CSharpCompilation SourceCompilation(StudioProject project)=>sourceSchemas.Bind(project.Source,project.SourceFile).Compilation;
    internal bool PhasePrimitive(BehaviorDocument document,BehaviorNode node)=>Supported.Contains(node.Kind)&&NativeKind(document,node)==node.Kind;
    public bool HasAdditionalSources {get;}
    public RapleAdapter ForProject(StudioProject project)
    {
        project.ValidateSourceSet();
        return project.AdditionalSources.Count==0 ? HasAdditionalSources ? new(ServerRoot,null,false,sourceSchemas) : this : new(ServerRoot,SourceVerification.SourceSet(ServerRoot,project),true,sourceSchemas);
    }
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
        "InstantSpawnRing", "Order", "OrderOnce", "OrderOnDeath", "GroundTransform", "OwnedLaneEmitters", "TimedRandomTransition",
        "DamageTakenTransition", "EntityHpLessTransition", "HealSelf", "GiveTemporaryDefense", "ChangeSize", "SizeTransform", "SizeTransformComplete", "SetAltTexture", "Transform", "TransformOnDeath", "SequentialAimedSweep", "GordoSpiral", "GapRing", "GordoRotation"];
    public IReadOnlyList<Primitive> Primitives { get; }
    public RapleAdapter(string root):this(root,null,false){}
    RapleAdapter(string root,IReadOnlyDictionary<string,string>? sources,bool additional,SourceSchemaCache? shared=null)
    {
        ServerRoot = Path.GetFullPath(root);
        sourceSchemas = new(ServerRoot,sources,shared);HasAdditionalSources=additional;
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
                t.Name.Contains("Shoot") || t.Name.Contains("Ring") || t.Name.Contains("Attack") || t.Name is "OwnedLaneEmitters" or "SequentialAimedSweep" or "GordoSpiral" or "GordoRotation" ? "Attacks" : "Actions",
                Constructor(t).GetParameters().Select(p => new PrimitiveParameter(p.Name!, p.ParameterType.Name,
                    p.HasDefaultValue ? FormatDefault(p) : "required", Unit(p.Name!, t.Name), Explain(p.Name!,t.Name))).ToArray(), Supported.Contains(t.Name))).ToArray();
    }
    public static ConstructorInfo Constructor(Type type) => type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
    static string FormatDefault(ParameterInfo p) => p.DefaultValue == null ? (p.ParameterType.IsValueType ? "default" : "null") : Convert.ToString(p.DefaultValue, CultureInfo.InvariantCulture) ?? "default";
    public string[] ParameterNames(string kind) => kind == "State" ? ["name"] : EngineAssembly.GetTypes().Where(t => t.Name == kind && typeof(IStateChildren).IsAssignableFrom(t))
        .SelectMany(t => t.GetConstructors()).SelectMany(c => c.GetParameters()).Select(p => p.Name!).Distinct().ToArray();
    static string DefaultUnit(string name, string? kind = null) => kind=="GiveTemporaryDefense"&&name=="amount"?"defense points":kind=="HealSelf"&&name=="amount"?"HP / percent of current HP":kind=="DamageTakenTransition"&&name=="damage"?"credited damage":kind=="OwnedLaneEmitters"&&name is "intervalMs" or "startupMs" ? "ms" : kind == "NewMove" && name == "cooldown" ? "seconds" : IsPathKind(kind) && name is "x" or "y" or "X" or "Y" or "offsetX" or "offsetY" or "distance" ? "tiles" : IsPathKind(kind) && name == "direction" ? "degrees" : name.Contains("Angle", StringComparison.OrdinalIgnoreCase) ? "degrees" : name.Contains("coolDown", StringComparison.OrdinalIgnoreCase) || name is "time" or "duration" ? "ms" : name.Contains("Range", StringComparison.OrdinalIgnoreCase) || name is "radius" or "dist" or "range" ? "tiles" : name == "threshold" ? "fraction of max HP" : name == "speed" ? "runtime speed coefficient" : "";
    static string Explain(string name,string? kind=null) => name switch
    {
        "projectile" when kind is "GordoSpiral" or "GapRing"=>"Index in this enemy's XML. A missing native projectile lookup suppresses the volley; choose an owned definition.",
        "count" when kind=="GordoSpiral"=>"Projectiles in each volley, from 1 to 64. Native Dazed rounds half upward; the first warned volley never exceeds its marked count.",
        "slots" when kind=="GapRing"=>"Full ring divisions, from 4 to 64. Native spacing is 360 / slots degrees.",
        "missing" when kind=="GapRing"=>"Contiguous omitted ring divisions. Must be at least 1 and less than slots - 1. The native volley emits slots - missing projectiles.",
        "gapAngle" when kind=="GapRing"=>"Center of the opening in degrees before native seeded offset, rotation and alternating sides. The ring's centered heading is gapAngle + 180.",
        "spacing" when kind=="GordoSpiral"=>"Signed degrees between adjacent rays. The native packet centers the volley around its heading; spacing may span multiple turns.",
        "rotation" when kind is "GordoSpiral" or "GapRing"=>"Degrees added per successful volley, signed by GordoRotation in the current leaf when present. Suppressed volleys do not advance the step.",
        "angle" when kind=="GordoSpiral"=>"Base centered heading in degrees, before native random offset, alternating sides, step rotation and age-based drift.",
        "interval" when kind is "GordoSpiral" or "GapRing"=>"At least 100 ms between native attempts. The countdown subtracts a tick before checking, emits at most one packet per tick and never catches up a backlog.",
        "delay" when kind is "GordoSpiral" or "GapRing"=>"Initial countdown in milliseconds. With warnFirstVolley, the full countdown starts when the original cue is sent. A warned volley requires positive delay.",
        "reverseEvery" when kind=="GordoSpiral"=>"Successful-volley steps before the triangular rotation sequence reverses. Zero keeps the step increasing.",
        "randomStartStep" when kind is "GordoSpiral" or "GapRing"=>"Seeded starting offset in degree multiples. Zero disables it; positive values must divide 360 exactly.",
        "warnFirstVolley" when kind is "GordoSpiral" or "GapRing"=>"Sends the game's RingWarning packet before the first volley. Its marked count captures Dazed at warning time; later Dazed can reduce the first shot count. Continuous age-based drift is not included in that original cue.",
        "alternateSides" when kind is "GordoSpiral" or "GapRing"=>"Adds 180 degrees on odd successful volleys before the native step rotation is applied.",
        "degreesPerSecond" when kind=="GordoSpiral"=>"Age-based angular drift, signed by current-leaf GordoRotation. Age advances on every tick, including warning delay and suppressed attempts.",
        "projectile" when kind=="SequentialAimedSweep"=>"Projectile index in this enemy's XML. The sweep emits from the native host position.",
        "count" when kind=="SequentialAimedSweep"=>"Total projectiles in one activation, from 2 to 255. The final volley uses only the remaining projectiles.",
        "shotsPerVolley" when kind=="SequentialAimedSweep"=>"Projectiles per native tick, from 1 to 12 and no more than count. One traces a sweep; larger volleys alternate full-width and inset fans.",
        "intervalMs" when kind=="SequentialAimedSweep"=>"At least 100 ms between volleys. Native 100 ms ticks quantize the interval upward; at most one packet fires per tick, with no backlog burst.",
        "recoveryMs" when kind=="SequentialAimedSweep"=>"Additional pause after the final firing interval. The next activation reverses sweep direction and reacquires a nearby player.",
        "triggerRange" when kind=="SequentialAimedSweep"=>"Player acquisition distance at activation. Must be positive. An acquired living player remains tracked even after leaving this range.",
        "aimRange" when kind=="SequentialAimedSweep"=>"Reacquisition distance if the original target leaves its world or dies, when lockTarget is false. Must be at least triggerRange.",
        "sweepDegrees" when kind=="SequentialAimedSweep"=>"Sweep width from 0 to 360 degrees around the tracked player's current direction. Multi-shot volleys alternate full and inset widths.",
        "lockTarget" when kind=="SequentialAimedSweep"=>"True abandons an activation when its player becomes invalid or invisible; false may reacquire another player. This constructor does not change existing single-shot semantics.",
        "multiplier" when kind=="SizeTransform"=>"Multiplier of this actor's size on entry. The original animation grows smoothly, then shrinks back to that size.",
        "growMs" when kind=="SizeTransform"=>"Native smooth-step growth duration in milliseconds. Zero grows immediately on the first native tick.",
        "shrinkMs" when kind=="SizeTransform"=>"Native smooth-step return duration. An HP interruption shrinks from the visible intermediate size.",
        "maxDurationMs" when kind=="SizeTransform"=>"Time from entry to the start of shrinking, including growth. Null uses only the HP threshold; at least one stopping condition is required.",
        "hpThreshold" when kind=="SizeTransform"=>"Shrink when current HP / maximum HP is less than or equal to this fraction. Null uses only the timeout.",
        "glowColor" when kind=="SizeTransform"=>"Native RGB glow value. The animation restores the actor's previous glow on completion or exit.",
        "scaleProjectiles" when kind=="SizeTransform"=>"Changes the native ProjectileSizeBase setting during this transform and restores it on completion/exit. Full client projectile scaling is not yet rendered by this preview.",
        "transform" when kind=="SizeTransformComplete"=>"Reference to the SAME SizeTransform instance running on this actor. A separately constructed transform never completes this transition.",
        "rate" when kind=="ChangeSize"=>"Size points added by the native callback, then a 150 ms countdown. With 100 ms enemy ticks, changes normally occur every third tick. A wrong-signed rate can clamp straight to the target. Size persists after exit.",
        "target" when kind=="ChangeSize"=>"Native size points, with 100 as ordinary scale. The rate's direction is retained; the source does not correct a rate pointing away from the target.",
        "minValue" when kind=="SetAltTexture"=>"First XML AltTexture ID, applied on entry. Zero restores the base texture; undefined IDs leave the client's previous texture intact.",
        "maxValue" when kind=="SetAltTexture"=>"Last ID in the native integer sequence. -1 holds the first texture. Undefined IDs still consume their native timer slots.",
        "loop" when kind=="SetAltTexture"=>"Wraps from maxValue to minValue. This does not skip undefined XML IDs; leaving the phase does not restore the original texture.",
        "target" when kind is "Transform" or "TransformOnDeath"=>"Actual XML entity ID resolved by the game. The replacement runs its own native behavior. Spawned enemies preserve the native Spawned flag and receive permanent Invisible; parent ownership is not assigned here.",
        "min" when kind=="TransformOnDeath"=>"Inclusive minimum replacement count. The native object mutates max to min if min exceeds max.",
        "max" when kind=="TransformOnDeath"=>"Inclusive maximum replacement count: the game uses Random.Next(min, max + 1).",
        "probability" when kind=="TransformOnDeath"=>"Converted to native float, then compared strictly with Random.NextDouble(). Values outside 0–1 retain their native behavior rather than being clamped.",
        "damage" when kind=="DamageTakenTransition"=>"Inclusive threshold for the original counter's damage credited to players currently present in their world. This is not HP lost since phase entry.",
        "wipeProgress" when kind=="DamageTakenTransition"=>"In this game source, this flag only resets a local accumulator that already starts at zero. It does not clear stored damage; credit persists across phases.",
        "entity" when kind=="EntityHpLessTransition"=>"Case-insensitive substring of the native XML ID name. The nearest match can be this host; null matches any name. The runtime expects that match to be an Enemy.",
        "amount" when kind=="HealSelf"=>"Added by the original HealSelf. Percentage uses current HP, not maximum HP. Null adds zero; an increment equal to current HP is skipped by this source.",
        "percentage" when kind=="HealSelf"=>"Interprets amount as a percent of current HP; the original code truncates to an integer and clamps the resulting HP to maximum.",
        "amount" when kind=="GiveTemporaryDefense"=>"Added on native phase entry and subtracted on phase exit. Negative values reduce defense. It is not a replacement XML defense value.",
        "coolDown" => "Runtime countdown, quantized to 100 ms ticks. Shoot generally adds a tick between volleys; 0 normalizes to its runtime default.",
        "threshold" => "HpLessTransition uses strict less-than, not less-than-or-equal.",
        "shootAngle" => "Spacing between adjacent shots in degrees; omitted spacing defaults to 360 / count.",
        "rotateAngle" => "Degrees added per volley. Shoot stores its rotation count on the behavior instance.",
        "projectileIndex" => "Index in this entity's resource XML; it is not a projectile object type.",
        "speed" => "Passed unchanged to the runtime; this is not tiles per second.",
        "period" => "Timed subtracts the native tick duration once for EACH child, in source order. Completion/reset can happen within that loop; this is not a single wall-clock duration shared by all children.",
        "targetState" => "Exact state name; duplicate state names are ambiguous in the runtime's global name map.",
        "states" when kind=="TimedRandomTransition"=>"The native random transition chooses uniformly among these list entries. Repeating a phase name increases its chance; order and duplicates are retained.",
        "randomizedTime" when kind=="TimedRandomTransition"=>"The initial native countdown is Random.Next(time), then runs on 100 ms ticks. A zero time chooses immediately.",
        _ => "Constructor parameter from the supplied runtime. Expressions beyond literal values may require advanced source editing."
    };
    static string Unit(string name,string? kind=null)=>kind switch
    {
        "GordoSpiral" or "GapRing" when name is "interval" or "delay"=>"ms",
        "GordoSpiral" or "GapRing" when name is "spacing" or "rotation" or "angle" or "gapAngle" or "randomStartStep"=>"degrees",
        "GordoSpiral" when name=="degreesPerSecond"=>"degrees/s",
        "GordoSpiral" or "GapRing" when name=="projectile"=>"XML index",
        "SequentialAimedSweep" when name is "intervalMs" or "recoveryMs"=>"ms",
        "SequentialAimedSweep" when name is "triggerRange" or "aimRange"=>"tiles",
        "SequentialAimedSweep" when name=="sweepDegrees"=>"degrees",
        "SequentialAimedSweep" when name=="projectile"=>"XML index",
        "SizeTransform" when name is "growMs" or "shrinkMs" or "maxDurationMs"=>"ms",
        "SizeTransform" when name=="multiplier"=>"× entry size",
        "SizeTransform" when name=="hpThreshold"=>"fraction of max HP",
        "SizeTransform" when name=="glowColor"=>"RGB",
        "ChangeSize" when name is "rate" or "target"=>"size points",
        "SetAltTexture" when name is "minValue" or "maxValue"=>"texture ID",
        _=>DefaultUnit(name,kind)
    };
    public IReadOnlyList<CatalogEntry> Catalog()
    {
        var result = new List<CatalogEntry>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(ServerRoot, "TKR.WorldServer/logic/db"), "*.cs"))
        {
            var source=File.ReadAllText(file);var ranges=source.Contains("InitMany",StringComparison.Ordinal)?ForSource(source,file).AdditionalEncounters().Select(r=>r.EntityId).ToHashSet(StringComparer.Ordinal):[];
            foreach (var id in EntityIds(source, file))
                if (SimEngine.Data.IdToObjectType.TryGetValue(id, out var type) && SimEngine.Data.ObjectDescs.TryGetValue(type,out var desc) && (desc.Enemy||ranges.Contains(id)))
                    result.Add(new(id, type, file,desc.Enemy||desc.Class=="Character"?"Enemy":desc.Class));
        }
        return result.OrderBy(e => e.Id).ToArray();
    }
    public string? PreviewContextRequirement(BehaviorDocument document,string id)
    {
        if(document.Entities.Where(e=>e.EntityId==id).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Contains(id))).Any(e=>e.Root.Descendants().Any(n=>NativeKind(document,n)=="PetFollow")))
            return "PetFollow authoring is available; playback needs an actual Pet and its player owner context, beyond the current enemy prototype.";
        return SimEngine.Data.IdToObjectType.TryGetValue(id,out var type)&&SimEngine.Data.ObjectDescs.TryGetValue(type,out var desc)&&!desc.Enemy&&desc.Class!="Character"
            ?desc.Class+" authoring is available; playback needs its actual world/player owner context, beyond the current enemy prototype.":null;
    }
    public IReadOnlyList<string> EntityIds(string source, string sourceFile)
    {
        // Library discovery needs roots, not semantic binding of every nested action
        // in every file. Resolve the selected document fully when it is opened.
        var ids = new BehaviorDocument(source, this).Entities.Select(e => e.EntityId).ToList();
        var scoped = ForSource(source, sourceFile);
        if(source.Contains("InitMany",StringComparison.Ordinal))ids.AddRange(scoped.AdditionalEncounters().Select(r=>r.EntityId).Where(id=>!ids.Contains(id)));
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
        foreach (var diagnostic in ValidateOwnedEmitters(document,entityId)) yield return diagnostic;
        foreach (var diagnostic in ValidateSequentialSweeps(document,entityId)) yield return diagnostic;
        foreach (var diagnostic in ValidateRingPatterns(document,entityId)) yield return diagnostic;
        foreach (var entity in document.Entities.Where(e => entityId == null || e.EntityId == entityId).Concat(document.Definitions.Where(d=>entityId==null||d.Definition!.Encounters.Contains(entityId))))
        {
            var nodes = entity.Root.Descendants().ToArray();
            var states = nodes.Where(n => n.Kind == "State").ToArray();
            var callerPhaseScopes=KnownLocalPhaseScopes(document,entity);
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
                if(n.Kind=="SizeTransform")
                {
                    var sizeExpression=SyntaxFactory.ParseExpression(document.Expression(n));
                    if(sizeExpression is ImplicitObjectCreationExpressionSyntax implicitSize)sizeExpression=SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(CreationTypeName("SizeTransform"))).WithArgumentList(implicitSize.ArgumentList).NormalizeWhitespace();
                    string? sizeError=null;
                    if(!Binding(sizeExpression.ToString()).RequiresContext)try{Evaluate(sizeExpression,RuntimeType("SizeTransform"));}catch(Exception error){sizeError=error.GetBaseException().Message;}
                    if(sizeError!=null)yield return new("SizeTransform: "+sizeError,n.Id);
                }
                if(n.Kind=="SizeTransformComplete"&&(n.Parameters.Any(p=>p.Name=="transform"&&SyntaxFactory.ParseExpression(p.Expression) is BaseObjectCreationExpressionSyntax)||n.Children.SelectMany(c=>c.Descendants()).Any(c=>c.Kind=="SizeTransform")))yield return new("Reference the same SizeTransform instance running in the phase; a new transform inside this transition is never ticked.",n.Id);
                if(n.Kind=="TimedRandomTransition")
                {
                    if(document.PhaseChoices(n.Id) is {Count:0})yield return new("A native random transition needs at least one phase choice.",n.Id);
                    if(n.Parameters.Any(p=>p.Name.Split('[')[0]=="states"&&SyntaxFactory.ParseExpression(p.Expression).IsKind(SyntaxKind.NullLiteralExpression)))yield return new("Native phase choices cannot be null.",n.Id);
                    if(ReadNumericParameter(document,n,"time",null,out var randomTime)&&randomTime<0&&n.Parameters.Any(p=>p.Name=="randomizedTime"&&p.Expression=="true"))yield return new("Randomized transition time cannot be negative.",n.Id);
                }
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
                    if (entity.Definition?.CompletePhaseContents!=false && (document.SchemaAt(n)?.TargetsPhaseOf(document.Expression(n), p.Name, entity.EntityId, entity.EntityId) ?? TargetsPhaseOf(document.Expression(n), p.Name, entity.EntityId, entity.EntityId)) == true && !nodes.Any(n => n.Kind is "Opaque" or "Factory" or "Reference" or "Spread") && !states.Any(s => s.Parameters.Any(p => p.Name is "name" or "$0" && BehaviorDocument.LiteralString(p.Expression) == null)) && BehaviorDocument.PhaseLiterals(p.Expression) is {} phaseTargets)
                        foreach(var stateTarget in phaseTargets.Where(t=>callerPhaseScopes==null?!states.Any(s=>(document.StateName(s.Id)??"")==t.Name):callerPhaseScopes.Any(scope=>!scope.Contains(t.Name))).DistinctBy(t=>t.Name))yield return new("Missing transition target: " + SyntaxFactory.Literal(stateTarget.Name).Text, n.Id);
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
        if(entity.Registration?.Shared==true)throw new InvalidOperationException("Shared native range templates use compiled preview so the original factory and registration order execute.");
        var errors = Validate(document, entityId).Where(d => d.IsError).ToArray();
        if (errors.Length > 0) throw new ArgumentException(string.Join("\n", errors.Select(d => d.Message)));
        var expression = SyntaxFactory.ParseExpression(document.Expression(entity.Root));
        var root = (State)Evaluate(expression, typeof(State))!;
        var states = new Dictionary<string, State>();
        typeof(State).GetMethod("Resolve", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(root, [states]);
        typeof(State).GetMethod("ResolveChildren", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(root, [states]);
        return root;
    }
    public bool NeedsCompiledRuntime(BehaviorDocument document, string entityId) => HasAdditionalSources || document.Entities.Single(e => e.EntityId == entityId).Registration?.Shared==true || document.Entities.Single(e => e.EntityId == entityId)
        .Root.Descendants().Any(n => n.Kind != "ArgumentGroup" && (n.Kind is "Transform" or "TransformOnDeath" || document.SchemaAt(n)?.DescribeExpression(document.Expression(n))?.RequiresCompiled == true || !Supported.Contains(n.Kind) || Binding(document.Expression(n)).RequiresContext));
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
