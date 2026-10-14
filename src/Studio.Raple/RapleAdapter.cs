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
    static Type RuntimeType(string name) => EngineAssembly.GetTypes().Single(t => t.Name == name && (typeof(IStateChildren).IsAssignableFrom(t) || t.FullName is "TKR.WorldServer.logic.Cooldown" or "TKR.WorldServer.logic.behaviors.BossSweepGeometry"));
    internal static string? NativeTypeName(string name) => EngineAssembly.GetTypes().Where(t => t.Name == name && typeof(IStateChildren).IsAssignableFrom(t)).Select(t => t.FullName?.Replace('+', '.')).SingleOrDefault();
    public string CreationTypeName(string name) => "global::" + string.Join(".", RuntimeType(name).FullName!.Replace('+', '.').Split('.').Select(part => SyntaxFacts.GetKeywordKind(part) == SyntaxKind.None ? part : "@" + part));
    // Explicit scope, independent of the reflection catalog. Constructors outside this list remain opaque.
    static readonly HashSet<string> Supported = ["State", "Shoot", "Wander", "Follow", "StayCloseToSpawn", "Orbit",
        "Prioritize", "HpLessTransition", "TimedTransition", "PlayerWithinTransition", "NoPlayerWithinTransition", "AnyEntityWithinTransition", "NoEntityWithinTransition", "EntityWithinTransition", "EntitiesNotExistsTransition", "OnGroundTransition", "NotMovingTransition", "EntityNotExistsTransition",
        "EntityExistsTransition", "Spawn", "ConditionEffectBehavior", "Taunt", "Cooldown", "Timed", "OnDeathBehavior",
        "MoveTo", "MoveTo2", "MoveTo3", "SpawnRelativeMove", "MoveLine", "NewMove", "OrderedBehavior",
        "InstantSpawnRing", "Order", "OrderOnce", "OrderOnDeath", "GroundTransform", "TerrainWave", "OwnedLaneEmitters", "TimedRandomTransition",
        "Protect", "BackAndForth", "PatrolCircle", "RemoveConditionalEffect", "ScaleHP", "ScaleHP2", "ScaleDefense", "TransferDamageOnDeath", "DamageTakenTransition", "EntityHpLessTransition", "HealSelf", "GiveTemporaryDefense", "ChangeSize", "SizeTransform", "SizeTransformComplete", "SetAltTexture", "Transform", "TransformOnDeath", "SequentialAimedSweep", "GordoSpiral", "GapRing", "GordoRotation", "ReturnToSpawn", "StayBack", "Charge", "FollowDistance", "LockedFollowDistance", "DelayedOriginHazards", "FollowDistanceCompleteTransition", "LockedFollowDistanceCompleteTransition", "SpawnGroup", "Reproduce", "ReproduceGroup", "GroupNotExistTransition", "EdgeCrossCut", "ClockSweep", "CorridorPulse", "OrientationEcho", "ConvergingSnare", "EchoSnare"];
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
                typeof(Transition).IsAssignableFrom(t) ? "Transitions" : t.Name is "ScaleHP" or "ScaleHP2" or "ScaleDefense" or "ConditionEffectBehavior" or "RemoveConditionalEffect" or "GiveTemporaryDefense" or "HealSelf" ? "Defense" : t.Name == "State" ? "Phases" :
                t.Name is "Protect" or "BackAndForth" or "PatrolCircle" or "FollowDistance" or "LockedFollowDistance" or "ReturnToSpawn" or "StayBack" or "Charge" or "Wander" or "Follow" or "Orbit" or "StayCloseToSpawn" ? "Movement" : t.Name.Contains("Spawn") || t.Name is "Reproduce" or "ReproduceGroup" ? "Spawning" :
                t.Name.Contains("Shoot") || t.Name.Contains("Ring") || t.Name.Contains("Attack") || t.Name is "ConvergingSnare" or "EchoSnare" or "ClockSweep" or "CorridorPulse" or "OrientationEcho" or "EdgeCrossCut" or "DelayedOriginHazards" or "OwnedLaneEmitters" or "SequentialAimedSweep" or "GordoSpiral" or "GordoRotation" ? "Attacks" : "Actions",
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
        "startRadius" when kind=="TerrainWave"=>"Initial tile-center radius around original Enemy.SpawnPoint. The ordering of start/end radii determines direction; moving the host does not move the anchor.",
        "endRadius" when kind=="TerrainWave"=>"Final radius of the moving front. Crossed tile centers with selected source grounds change; other grounds remain intact.",
        "durationMs" when kind=="TerrainWave"=>"At least 100 ms. Total elapsed age interpolates linearly after delay; coarse ticks convert the complete crossed band.",
        "delayMs" when kind=="TerrainWave"=>"Nonnegative integer delay before mutation. Entry resolves every ground XML ID even while delayed or excluded from Boss Lab.",
        "restoreOnExit" when kind=="TerrainWave"=>"Native exit/death restores recorded original grounds in reverse order, overwriting later edits and incrementing tile updates. Wrapper callbacks determine dispatch.",
        "allowBossLab" when kind=="TerrainWave"=>"Native default false excludes Boss Lab. Enable explicitly for this prototype; full ground-hazard player mechanics retain their existing prerequisites.",
        "protectee" when kind=="Protect"=>"Exact XML type looked up anew each tick in the enemy/static collision index. A matching host can select itself. This movement does not add defense or assign ownership.",
        "reprotectRange" when kind=="Protect"=>"Arrival distance while approaching: movement continues when distance is greater than this value. The native fixed step can overshoot and is not clamped to the remaining distance.",
        "protectionRange" when kind=="Protect"=>"Once arrived, movement restarts only when the nearest matching entity is farther than this value. The native constructor does not require it to be greater than reprotectRange.",
        "acquireRange" when kind=="Protect"=>"Strict nearest-type query. Losing or changing the nearest target changes movement immediately; it does not retain one protectee ID. Constructor values narrow to float.",
        "distance" when kind=="BackAndForth"=>"Integer signed segment budget. Positive values move right then left, consuming attempted travel even when blocked. Negative values restart negative segments and can continue left rather than alternate. Zero still attempts a movement step.",
        "radius" when kind=="PatrolCircle"=>"Positive circle radius around original Enemy.SpawnPoint. Off-circle positions steer toward the next point; this is not a teleport onto an exact circular path.",
        "clockwise" when kind=="PatrolCircle"=>"Positive angle is clockwise on the screen because Y increases downward. Native step length uses GetSpeed and tick DeltaTime; paralysis stops movement.",
        "effect" when kind=="RemoveConditionalEffect"=>"Removes the selected native host condition on state entry. It has no ongoing refresh and no exit restoration. OnDeathBehavior invokes this entry action at death.",
        "amountPerPlayer" when kind=="ScaleHP"=>"HP per newly counted player name beyond scaleAfter. Names are retained for the entry; leaving range does not remove them. Current HP fraction is converted to an integer before the native maximum cap.",
        "maxAdditional" when kind=="ScaleHP"=>"Additional maximum HP above the entry baseline, with per-batch increment clamping and a final total maximum clamp. Zero means no cap. The original order can change current HP fraction when a batch reaches the cap.",
        "healAfterMax" when kind=="ScaleHP"=>"After reaching the cap, new names can still update current HP when true. False consumes those names without updating HP/max. Entry resets counting state; it does not undo an earlier maximum-HP change.",
        "scaleAfter" when kind=="ScaleHP"=>"Number of counted names before scaling begins; native default is one. Counting is by Name, not player ID, and does not filter Hidden, Invisible or dead player entries.",
        "dist" when kind=="ScaleHP"=>"Strict distance when positive. Zero or negative values count every player entry in the world. Names remain counted after leaving range.",
        "amount" when kind=="ScaleHP2"=>"Integer percent of original XML maximum HP per newly counted name beyond scaleStart. Integer multiplication/division and current-plus-maximum increments use the original source order; this does not preserve the HP fraction.",
        "scaleStart" when kind=="ScaleHP2"=>"Initial player-name threshold. Native default is zero. Entry resets counters without restoring earlier HP/max changes.",
        "range" when kind is "ScaleHP2" or "ScaleDefense"=>"Strict native player query with seeInvis true, so Hidden and Invisible players count. Names persist after leaving range. Zero/negative range still runs the nearest-player query and does not mean the whole world.",
        "perPerson" when kind=="ScaleDefense"=>"Defense becomes original XML defense plus ceiling(counted names * perPerson). The native behavior periodically writes an absolute defense value; it does not add to temporary defense or restore on exit.",
        "target" when kind=="TransferDamageOnDeath"=>"Exact XML object type of the nearest Enemy recipient. Native typed queries can select a matching dying host itself. Unknown IDs use the original Pirate fallback. No parent/owned filter is applied.",
        "radius" when kind=="TransferDamageOnDeath"=>"Float tiles with strict native nearest-entity distance. Death copies LastHitter and TotalDamage, then adds contributor records to the recipient; recorded total is replaced rather than summed. It does not transfer health damage or create a parent link.",
        "dist" when kind is "AnyEntityWithinTransition" or "NoEntityWithinTransition"=>"Integer tiles with strict distance < dist. Queries other enemies and players that are not Hidden; Invisible players still count. This host and static objects are excluded. This is proximity, not minion ownership.",
        "dist" when kind=="NoPlayerWithinTransition"=>"Strict native nearest-player distance. Hidden and Invisible players are excluded by IsVisibleToEnemy. This uses a different visibility rule from AnyEntityWithinTransition.",
        "entity" when kind=="EntityWithinTransition"=>"Case-insensitive XML IdName substring in the enemy/static collision index. A matching host counts. Display-name overrides are ignored; null matches any descriptor name.",
        "dist" when kind is "EntityWithinTransition" or "EntitiesNotExistsTransition"=>"Strict native distance < dist in the enemy/static collision index. Matching hosts and queryable static objects count. There is no parent/owned-minion filter.",
        "targets" when kind=="EntitiesNotExistsTransition"=>"Exact XML IDs resolved to object types. Every selected type must be absent in range. Order and duplicates remain in source; an empty list always returns false. Unknown IDs use the native Pirate fallback; null IDs fail construction.",
        "ground" when kind=="OnGroundTransition"=>"Exact ground XML ID, resolved lazily on the first tick. Reads the host's current map cell using integer X/Y coordinates. Missing IDs fail native lookup.",
        "delay" when kind=="NotMovingTransition"=>"Milliseconds between sampled position comparisons. First tick captures position and returns false; countdown ticks subtract elapsed time, with comparison on a later tick. Exact Vector2 equality triggers. Movement between checks can return to the same position and still match; this is not continuous motion tracking.",
        "projectileIndex" when kind is "ConvergingSnare" or "EchoSnare"=>"Owned straight constant-speed XML projectile. Its lifetime must reach the ring anchor; radius / (XML speed / 10000) gives travel time in milliseconds.",
        "radius" when kind is "ConvergingSnare" or "EchoSnare"=>"Ring radius in tiles. Each visible, passable origin fires inward. Target/clamped anchor distance plus radius must fit the native 14-tile envelope.",
        "count" when kind is "ConvergingSnare" or "EchoSnare"=>"Three to thirty-two evenly spaced slots. Each surviving origin emits one independent shot; gapSlots removes a contiguous run.",
        "gapSlots" when kind is "ConvergingSnare" or "EchoSnare"=>"Zero to count minus two omitted slots centered on the gap. Even gap counts offset the ring by half a slot. This is an opening in emitted rays, not a guaranteed safe area.",
        "anchor" when kind=="ConvergingSnare"=>"Target clamps the current target to targetRange. Host follows the actor's live position; Spawn uses its original spawn position.",
        "gap" when kind=="ConvergingSnare"=>"AwayFromHost or TowardHost use the anchor bearing when at least 0.5 tiles apart. Fixed uses gapAngle. Rotating adds gapStep per unstunned attempt, including empty attempts.",
        "gapAngle" when kind is "ConvergingSnare" or "EchoSnare"=>"Degrees. Converging uses this for Fixed, the first Rotating opening or an undefined host bearing. Echo uses it only when both travel and host bearing are too small.",
        "gapStep" when kind=="ConvergingSnare"=>"Degrees added per unstunned Rotating attempt, including attempts without a target or emitted ray. Other opening modes ignore this value.",
        "markerRadius" when kind=="ConvergingSnare"=>"Nonnegative radius of the native SanctuaryWarning visual cue at the meeting point. It does not change projectile collision or the inward ring radius.",
        "targetRange" or "clampRange" when kind is "ConvergingSnare" or "EchoSnare"=>"Maximum anchor distance toward the target or its recorded past position. Combined with ring radius it must fit within 14 tiles.",
        "acquireRange" when kind is "ConvergingSnare" or "EchoSnare"=>"At least the anchor clamp range. Native target selection and world presence determine emission. Echo requires observed history rather than inventing a past position.",
        "lagMs" when kind=="EchoSnare"=>"At least 100 ms of player history. Native per-actor tracks interpolate recorded samples. Exit/death/removal releases history; projectile clears clear marks but retain tracks.",
        "telegraph" when kind=="ConvergingSnare"=>"Native SanctuaryWarning at the meeting point for the XML travel time. The marker radius is a visual cue rather than a hitbox.",
        "markRadius" or "markColor" when kind=="EchoSnare"=>"Native clearable LedgerMark ring at the history anchor. Radius is positive; color is a C# uint ARGB value. Native cleanup clears owned marks.",
        "intervalMs" when kind is "ConvergingSnare" or "EchoSnare"=>"At least 100 ms between attempts. One attempt per native tick; stunned attempts consume the interval without advancing the volley sequence. Echo still observes history while stunned.",
        "startupMs" when kind is "ConvergingSnare" or "EchoSnare"=>"Nonnegative age from state entry before the first attempt, rounded up by 100 ms native ticks. Echo also needs enough recorded history to satisfy lagMs.",
        "sweep" when kind is "ClockSweep" or "CorridorPulse" or "OrientationEcho"=>"Shared native BossSweepGeometry constructor. Compatible XML bands supply the radius, angular rate and lifetime. Edit arms, opening angle and timing in Shared sweep geometry.",
        "delayMs" when kind=="OrientationEcho"=>"Positive history delay in milliseconds. Echoes use the sweep heading at age minus delay, with stationary XML bands. Projectile clears suppress history from before the clear.",
        "projectileIndexes" when kind=="OrientationEcho"=>"Distinct stationary radial XML indexes in band order, with the same count, radii and lifetime as the shared sweep. Their angular rate must be zero.",
        "intervalMs" when kind is "CorridorPulse" or "OrientationEcho"=>"At least 100 ms between native attempts. One attempt per tick; stun consumes the interval. Sweep rotation continues to follow elapsed state age.",
        "startupMs" when kind=="CorridorPulse"=>"Nonnegative age from state entry before the first pulse attempt. Pulses use the position captured on entry and a half-arm angular offset.",
        "radius" when kind=="EdgeCrossCut"=>"Positive distance from the position captured at state entry to each edge center. Requested origins must fit within 14 tiles of that anchor; live host distance and map passability can suppress packets.",
        "spacing" when kind=="EdgeCrossCut"=>"Positive tiles between centered parallel origins. sqrt(radius² + ((count - 1) * spacing / 2)²) must not exceed 14.",
        "count" when kind=="EdgeCrossCut"=>"One to eight single-projectile origins. The native sequence alternates opposing horizontal edges, opposing vertical edges, then four diagonals. Suppressed origins still advance the attempt's edge step.",
        "intervalMs" when kind=="EdgeCrossCut"=>"At least 100 ms between native attempts. One attempt per tick, no backlog. Stun consumes the interval without advancing the edge sequence.",
        "startupMs" when kind=="EdgeCrossCut"=>"Nonnegative age from state entry before the first attempt, quantized by native 100 ms ticks. Exit, death and encounter removal release the per-actor state.",
        "group" when kind is "SpawnGroup" or "ReproduceGroup"=>"Exact case-sensitive XML Group. Native selection uses every matching ObjectDesc, not XML aliases or an enemy-only filter. Empty selection cannot emit; members needing extra world context retain that prerequisite.",
        "group" when kind=="GroupNotExistTransition"=>"Native group lookup uses invariant case-insensitive matching and strict distance in the enemy/static collision index. A blank group always returns false. This host can match its own group; no parent/ownership filter is applied.",
        "maxChildren" when kind=="SpawnGroup"=>"Ongoing cap on the per-entry attempt counter, not live minion count. Initial attempts are trunc(maxChildren * initialSpawn) and can exceed this cap. Blocked attempts count; death/removal never replenishes it.",
        "initialSpawn" when kind=="SpawnGroup"=>"Fraction multiplied by maxChildren and truncated to an integer on construction. Entry makes that many attempts before ongoing ticks. Native source permits values beyond the usual 0–1 fraction.",
        "radius" when kind=="SpawnGroup"=>"Native spawn offsets are independently Random.NextDouble * radius in X and Y. Positive radius samples a positive-quadrant square, not a centered circle. Passability can suppress each attempt.",
        "coolDown" when kind=="SpawnGroup"=>"Zero default remains zero; after a failed/completed attempt the cooldown resets. Ordinary countdown ticks subtract 100 ms, with a later attempt tick. Children are not assigned ParentEntity ownership by this controller.",
        "children" when kind=="Reproduce"=>"Null reproduces this host's object type. An ID resolves that exact native object type. Reproduction is suppressed by a player strictly inside 15 tiles; native Hidden is skipped, while Invisible is not.",
        "densityRadius" when kind is "Reproduce" or "ReproduceGroup"=>"Strict native CountEntity distance, including this host when its type/group matches. This is local population density, not the number of owned children.",
        "densityMax" when kind is "Reproduce" or "ReproduceGroup"=>"Emit one child when the native nearby count is below this value. Counts use current world/collision state; children give no XP and have no ParentEntity assigned here.",
        "coolDown" when kind is "Reproduce" or "ReproduceGroup"=>"Default normalizes to 60000 ms. First tick initializes then decrements the countdown; a later tick attempts reproduction. ReproduceGroup has no Reproduce-style nearby-player gate.",
        "regionRange" when kind=="ReproduceGroup"=>"The supplied group controller compares integer/fractional host coordinates, not candidate tile coordinates. Keep this source behavior; matching region tiles can be far away.",
        "regionRange" when kind=="Reproduce"=>"Inclusive X/Y tile-coordinate bounds around the integer host position. Native source can throw when matching region tiles exist but none fall inside this range.",
        "region" when kind is "Reproduce" or "ReproduceGroup"=>"None emits at the host. Other values cache matching native map regions at entry on the behavior object; region/world/cache ownership must be tested in its actual context.",
        "targetState" when kind is "FollowDistanceCompleteTransition" or "LockedFollowDistanceCompleteTransition"=>"Destination when the exact referenced movement instance reports completion for this actor. A separately constructed movement never observes the phase action's state. Use Movement completion to select a stable shared reference.",
        "projectileIndex" when kind=="DelayedOriginHazards"=>"Owner XML projectile with zero base speed, positive acceleration and speed cap, acceleration delay at least 500 ms, and arming between acceleration and expiry. Native entry validates the complete motion contract.",
        "layout" when kind=="DelayedOriginHazards"=>"Trail emits three backward rays at the host only after movement of at least 0.1 tile since its last unstunned attempt. Lattice emits two rays at each of four fixed offsets (±5, ±5) from the original spawn point.",
        "intervalMs" when kind=="DelayedOriginHazards"=>"Native attempt interval, at least 100 ms. One attempt per enemy tick; stun consumes the interval without advancing the volley. Suppressed trail or out-of-bounds lattice emissions still consume an unstunned volley.",
        "startupMs" when kind=="DelayedOriginHazards"=>"Nonnegative age from state entry before the first attempt, rounded up by native 100 ms ticks. Projectile dormancy and arming come from XML rather than a delayed server callback.",
        "volleyLimit" when kind=="DelayedOriginHazards"=>"Maximum unstunned emission attempts per entry, including attempts producing no packets. Default int.MaxValue. Exit, death and encounter removal release the per-actor state.",
        "outwardFirst" when kind=="DelayedOriginHazards"=>"Lattice alternates inward and outward volleys. False begins inward; true begins outward. Trail ignores this input. Origin fits and the native 14-tile host distance gate apply to each site.",
        "distance" when kind is "FollowDistance" or "LockedFollowDistance"=>"Maximum cumulative distance actually moved during this state entry. Native steps clamp to the remaining budget and stop boundary; 500 ms of blocked movement completes the segment early.",
        "stopRange" when kind is "FollowDistance" or "LockedFollowDistance"=>"Stop within this many tiles of the target. LockedFollowDistance requires this to be strictly smaller than acquireRange. There is no Follow-style range + 1 hysteresis.",
        "acquireRange" when kind=="FollowDistance"=>"Nearest visible player search radius, evaluated again each tick. No target pauses the segment without consuming its travel budget.",
        "acquireRange" when kind=="LockedFollowDistance"=>"Inclusive player acquisition radius at state entry. The nearest living visible player is selected with ID as the tie-breaker, then retained even outside this radius. No target completes the segment.",
        "timeoutMs" when kind=="LockedFollowDistance"=>"Positive segment lifetime in milliseconds, default 3000. Age advances even while paralyzed; target invalidation or timeout completes without reacquiring. Exit, death and encounter removal release native target state.",
        "returnWithinRadius" when kind=="ReturnToSpawn"=>"Completion radius around the enemy's original SpawnPoint. The native comparison is distance greater than this radius; it does not replace the anchor on state entry.",
        "speed" when kind=="ReturnToSpawn"=>"Converted to native float and passed to GetSpeed with BehaviourTickTime. Native Slowed and the game's movement scaling apply; steps are not clamped to the remaining distance.",
        "distance" when kind=="StayBack"=>"Strict nearby-target acquisition distance in tiles. The enemy retreats while a target is within this range; the native one-second cycle timer advances only during active retreat.",
        "entity" when kind=="StayBack"=>"Null selects a visible player. A string uses the game's case-insensitive XML-name substring query and can match the host itself; source ownership determines the actual target.",
        "speed" when kind=="StayBack"=>"Native GetSpeed and BehaviourTickTime control retreat. This controller uses MoveWithinBossLab rather than the ordinary ValidateAndMove collision path.",
        "range" when kind=="Charge"=>"Native float player acquisition radius. A charge starts only when both target X and Y differ from the host; axis-aligned targets are intentionally rejected by this source.",
        "speed" when kind=="Charge"=>"Unscaled native movement coefficient. Slowed applies, while the game's ordinary movement multiplier does not. The acquired direction stays fixed through the charge.",
        "coolDown" when kind=="Charge"=>"Native cooldown normalizes zero to 2000 ms. Charge travel duration uses distance / unscaled speed, integer milliseconds, then a separate cooldown completion tick.",
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
        "TerrainWave" when name is "startRadius" or "endRadius"=>"tiles",
        "TerrainWave" when name is "durationMs" or "delayMs"=>"ms",
        "TerrainWave" when name is "from" or "to"=>"ground XML ID",
        "Protect" when name is "acquireRange" or "protectionRange" or "reprotectRange"=>"tiles",
        "BackAndForth" when name=="distance"=>"signed tiles",
        "ScaleHP" when name is "amountPerPlayer" or "maxAdditional"=>"HP",
        "ScaleHP2" when name=="amount"=>"percent of XML max HP",
        "ScaleDefense" when name=="perPerson"=>"defense per name",
        "NotMovingTransition" when name=="delay"=>"ms",
        "EntitiesNotExistsTransition" when name=="targets"=>"XML IDs",
        "OnGroundTransition" when name=="ground"=>"ground XML ID",
        "ConvergingSnare" or "EchoSnare" when name is "intervalMs" or "startupMs" or "lagMs"=>"ms",
        "ConvergingSnare" or "EchoSnare" when name=="projectileIndex"=>"XML index",
        "ConvergingSnare" or "EchoSnare" when name is "radius" or "targetRange" or "clampRange" or "acquireRange" or "markerRadius" or "markRadius"=>"tiles",
        "ConvergingSnare" or "EchoSnare" when name is "gapAngle" or "gapStep"=>"degrees",
        "CorridorPulse" or "OrientationEcho" when name is "intervalMs" or "startupMs" or "delayMs"=>"ms",
        "CorridorPulse" when name=="projectileIndex"=>"XML index",
        "OrientationEcho" when name=="projectileIndexes"=>"XML indexes",
        "Reproduce" or "ReproduceGroup" when name is "densityRadius" or "regionRange"=>"tiles",
        "EdgeCrossCut" when name=="spacing"=>"tiles",
        "EdgeCrossCut" when name is "intervalMs" or "startupMs"=>"ms",
        "DelayedOriginHazards" when name is "intervalMs" or "startupMs"=>"ms",
        "DelayedOriginHazards" when name=="projectileIndex"=>"XML index",
        "FollowDistance" or "LockedFollowDistance" when name is "distance" or "stopRange" or "acquireRange"=>"tiles",
        "LockedFollowDistance" when name=="timeoutMs"=>"ms",
        "ReturnToSpawn" when name=="returnWithinRadius"=>"tiles",
        "StayBack" when name=="distance"=>"tiles",
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
        foreach (var diagnostic in ValidateDelayedHazards(document,entityId)) yield return diagnostic;
        foreach (var diagnostic in ValidateNativeGroups(document,entityId)) yield return diagnostic;
        foreach (var diagnostic in ValidateCrossCuts(document,entityId)) yield return diagnostic;
        foreach (var diagnostic in ValidateSweepControllers(document,entityId)) yield return diagnostic;
        foreach (var diagnostic in ValidateSnares(document,entityId)) yield return diagnostic;
        foreach (var diagnostic in ValidatePresenceConditions(document,entityId)) yield return diagnostic;
        foreach (var diagnostic in ValidateCustomMovement(document,entityId)) yield return diagnostic;
        foreach (var diagnostic in ValidateTerrainWaves(document,entityId)) yield return diagnostic;
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
                            p.Name == "threshold" && (value < 0 || value > 1) || p.Name == "speed" && value < 0 || p.Name == "radius" && n.Kind!="SpawnGroup" && value < 0 ||
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
            if (!Supported.Contains(name)&&name!="BossSweepGeometry") throw new NotSupportedException(name + " is preserved, but not yet supported in the Studio runtime adapter.");
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
