using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Studio.Core;
using Studio.Raple;
using TKR.WorldServer.logic;
using TKR.WorldServer.core.objects;
using TKR.Shared.resources;
using TKR.WorldServer.utils;

var adapter = new RapleAdapter(Path.GetFullPath(args.FirstOrDefault() ?? "../raple-realms-7-server"));
var count = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); count++; }
void Reject(Action action, string label) { try { action(); } catch (Exception e) when (e is ArgumentException or NotSupportedException or InvalidOperationException) { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
object Native(string expression) => RapleAdapter.Evaluate(SyntaxFactory.ParseExpression(expression), typeof(IStateChildren))!;
object? Field(object native, string name) => native.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(native);
BehaviorDocument Doc(string expression) => new("class Fixture { void Build() { db.Init(\"Cave Pirate Brawler\", " + expression + "); } }", adapter);
var overloads = new[] {
    ("new Taunt(\"hello\")", "String[]"),
    ("new Taunt(.5, \"hello\")", "Double,String[]"),
    ("new Taunt(true, \"hello\")", "Boolean,String[]"),
    ("new Taunt(new Cooldown(1000, 0), \"hello\")", "Cooldown,String[]"),
    ("new Taunt(.5, true, \"hello\")", "Double,Boolean,String[]"),
    ("new Taunt(.5, new Cooldown(1000, 0), \"hello\")", "Double,Cooldown,String[]"),
    ("new Taunt(true, new Cooldown(1000, 0), \"hello\")", "Boolean,Cooldown,String[]"),
    ("new Taunt(.5, true, new Cooldown(1000, 0), \"hello\")", "Double,Boolean,Cooldown,String[]") };
foreach (var (expression, signature) in overloads) {
    var binding = RapleAdapter.Binding(expression);
    Check(binding.Errors.Length == 0 && string.Join(",", binding.Constructor!.GetParameters().Select(p => p.ParameterType.Name)) == signature, "Native overload: " + signature);
    Check(((string[])Field(Native(expression), "text")!).SequenceEqual(["hello"]), "Selected native overload receives text: " + signature);
}
Check(RapleAdapter.Binding("new Taunt(1000, \"hello\")").Errors.Any(e => e.Contains("ambiguous")), "Ambiguous numeric versus Cooldown overload is reported by C#");
Reject(() => Native("new Taunt(1000, \"hello\")"), "Ambiguous native overload is never guessed");
Check(RapleAdapter.Binding("new Taunt()").SlotNames.Length == 0 && ((string[])Field(Native("new Taunt()"), "text")!).Length == 0, "Empty params binds to the native empty array");
var named = Native("new Taunt(text: new[] { \"one\", \"two\" }, broadcast: true, probability: .25)");
Check((bool)Field(named, "broadcast")! && (float)Field(named, "probability")! == .25f && ((string[])Field(named, "text")!).Length == 2, "Named arguments bind independently of source order");
Check(RapleAdapter.Binding("new Taunt(\"one\", \"two\")").SlotNames.SequenceEqual(["text[0]", "text[1]"]), "Expanded params have separate stable field keys");
var doc = Doc("new State(new Taunt(\"one\", \"two\"))");
var taunt = doc.Entities.Single().Root.Children.Single(); var id = taunt.Id;
doc.Apply(new(doc.Revision, "Edit second message", [new(id, "text[1]", "\"edited\"")]), d => adapter.Validate(d));
Check(((string[])Field(adapter.BuildState(doc, "Cave Pirate Brawler").Behaviors.Single(), "text")!).SequenceEqual(["one", "edited"]), "Visual edit changes only the selected repeated parameter");
Check(doc.Undo() && doc.Find(id).Parameters[1].Expression == "\"two\"" && doc.Redo(), "Repeated parameter identities survive undo and redo");
var reopened = new BehaviorDocument(doc.Source, adapter, doc.Identities);
Check(reopened.Find(id).Parameters.Select(p => p.Name).SequenceEqual(["text[0]", "text[1]"]), "Save model retains overload field keys and identities");
var clone = doc.PasteNodes(doc.Entities.Single().Root.Id, 1, doc.CopyNodes([id]), d => adapter.Validate(d));
Check(doc.Find(clone.Single()).Parameters[1].Name == "text[1]", "Clipboard constructors keep their native schema");
var shoot = Native("new Shoot(radius: 8)");
var shootDoc = Doc("new State(new Shoot(8))");
Check(adapter.ParametersFor(shootDoc, shootDoc.Entities.Single().Root.Children.Single()).Any(p => p.Name == "count" && p.Default == "1"), "Selected native constructor exposes its optional default");
Check(RapleAdapter.Binding("new Shoot(8)").Errors.Length == 0, "Omitted optional native arguments are valid");
var coolType = typeof(State).Assembly.GetType("TKR.WorldServer.logic.Cooldown")!;
var cool = RapleAdapter.Evaluate(SyntaxFactory.ParseExpression("1000"), coolType)!;
Check((int)coolType.GetField("CoolDown")!.GetValue(cool)! == 1000, "Native implicit Cooldown conversion is used");
Check((int)coolType.GetField("CoolDown")!.GetValue(RapleAdapter.Evaluate(SyntaxFactory.ParseExpression("(Cooldown)1000"), coolType))! == 1000, "Explicit Cooldown cast uses the same native conversion");
Check((int)coolType.GetField("CoolDown")!.GetValue(RapleAdapter.Evaluate(SyntaxFactory.ParseExpression("default(Cooldown)"), coolType))! == 0, "Typed default preserves native struct zero value");
Check((int)coolType.GetField("CoolDown")!.GetValue(RapleAdapter.Evaluate(SyntaxFactory.ParseExpression("new Cooldown()"), coolType))! == 0, "Implicit parameterless struct constructor is supported");
Check(RapleAdapter.Evaluate(SyntaxFactory.ParseExpression("(int)(2.9 + 1)"), typeof(int)) is 3, "C# constant casts and arithmetic retain compiler semantics");
Reject(() => RapleAdapter.Evaluate(SyntaxFactory.ParseExpression("2.9"), typeof(int)), "Floating point does not silently round into an integer argument");
Reject(() => Native("new Shoot(8, count: 2.5)"), "Invalid integer constructor argument is rejected");
Reject(() => Native("new Shoot(8, missing: 2)"), "Unknown named constructor argument is rejected");
Reject(() => Native("new Shoot(radius: 8, radius: 9)"), "Duplicate named constructor argument is rejected");
Reject(() => Native("new Follow()"), "Missing required constructor argument is rejected");
Check(Native("new Prioritize(new Follow(.3), new Wander(.1))").GetType().Name == "Prioritize", "Native CycleBehavior array accepts actual cycle children");
Reject(() => Native("new Prioritize(new Taunt(\"no\"))"), "Behavior is not silently widened into native CycleBehavior");
var array = (State)Native("new State(children: new IStateChildren[] { new State(\"a\", new TimedTransition(100, \"b\")), new State(\"b\", new Wander(.2)) })");
Check(array.States.Select(s => s.Name).SequenceEqual(["a", "b"]), "Normal params arrays preserve native child order and types");
var arrayDoc = Doc("new State(children: new IStateChildren[] { new State(\"a\", new TimedTransition(100, \"b\")), new State(\"b\") })");
Check(!adapter.Validate(arrayDoc).Any(d => d.IsError) && adapter.BuildState(arrayDoc, "Cave Pirate Brawler").States.Count == 2, "Incomplete array visuals do not falsely reject hidden transition targets");
Check(((State)Native("new State(new[] { new State(\"a\"), new State(\"b\") })")).States.Count == 2, "Covariant inferred arrays keep actual native element type");
var qualified = Doc("new global::TKR.WorldServer.logic.State(children: new IStateChildren[] {}, name: \"named\")");
Check(qualified.Entities.Single().Root.Label == "named", "Qualified root and reordered named phase name import correctly");
Check(adapter.Validate(Doc("new State(new State(\"Root\"), new State(\"Root\"))")).Any(d => d.IsError && d.Message.StartsWith("Duplicate state")), "Literal Root names are validated independently of the root display label");
Check(!adapter.Validate(Doc("new State(new TimedTransition(100, \"\"))")).Any(d => d.IsError), "Unnamed native root can be a literal empty-string transition target");
var helperDoc = Doc("new State(new ClockSweep(new BossSweepGeometry(new[] { 0 })))");
Check(helperDoc.Entities.Single().Root.Children.Single().Children.Count == 0, "Helper constructors are not misidentified as behavior children");
var contextDoc = Doc("new State(new Follow(speed: tuning.Speed))");
Check(!adapter.Validate(contextDoc).Any(d => d.IsError) && RapleAdapter.Binding("new Follow(speed: tuning.Speed)").RequiresContext, "Unknown source constants are retained with an explicit context notice");
Reject(() => adapter.BuildState(contextDoc, "Cave Pirate Brawler"), "Source context cannot be guessed during simulation");
var effectDoc = Doc("new State(new State(\"a\", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable), new TimedTransition(100, \"b\")), new State(\"b\"))");
var session = new EncounterSession(adapter, effectDoc, "Cave Pirate Brawler", 7, new());
var boss = (Enemy)typeof(EncounterSession).GetField("boss", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
session.Step(new()); Check(boss.HasConditionEffect(ConditionEffectIndex.Invulnerable), "Native effect entry applies its condition");
session.Step(new()); Check(!boss.HasConditionEffect(ConditionEffectIndex.Invulnerable), "Native effect exit removes a temporary condition");
var permanent = Doc("new State(new State(\"a\", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, perm: true), new TimedTransition(100, \"b\")), new State(\"b\"))");
session = new EncounterSession(adapter, permanent, "Cave Pirate Brawler", 7, new());
boss = (Enemy)typeof(EncounterSession).GetField("boss", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
session.Step(new()); session.Step(new()); Check(boss.HasConditionEffect(ConditionEffectIndex.Invulnerable), "Native permanent condition remains after phase exit");
Check(adapter.Callbacks("ConditionEffectBehavior").Select(c => c.Stage).SequenceEqual(["Entry", "Exit"]), "Lifecycle schema excludes empty ongoing callbacks");
Check(adapter.Callbacks("Shoot").Select(c => c.Stage).SequenceEqual(["Entry", "Ongoing"]), "Shoot initializes on entry and emits from its native ongoing callback");
Check(adapter.Callbacks("OnDeathBehavior").Select(c => c.Stage).SequenceEqual(["Death"]), "Death wrapper is shown in its actual native callback stage");
Check(adapter.Callbacks("Shoot").All(c => File.ReadLines(c.SourceFile).Skip(c.Line - 1).First().Contains(c.Method)), "Lifecycle callback provenance points to the native declaration");
var inheritedDoc = Doc("new State(new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable), new State(\"a\", new Wander(.1), new TimedTransition(100, \"b\")), new State(\"b\", new Shoot(8)))");
var aPhase = inheritedDoc.Entities.Single().Root.Children.Single(n => n.Label == "a");
var lifecycle = adapter.PhaseActions(inheritedDoc, aPhase.Id);
Check(lifecycle.Count == 2 && lifecycle.Single(a => a.Inherited).Node.Kind == "ConditionEffectBehavior" && lifecycle.Single(a => !a.Inherited).Node.Kind == "Wander", "Phase lifecycle includes direct and inherited parent actions, excluding siblings");
session = new EncounterSession(adapter, inheritedDoc, "Cave Pirate Brawler", 7, new());
boss = (Enemy)typeof(EncounterSession).GetField("boss", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
session.Step(new()); session.Step(new());
Check(boss.CurrentState.Name == "b" && boss.HasConditionEffect(ConditionEffectIndex.Invulnerable), "Native common-parent effect stays active across child transitions");
Check(adapter.Overloads("Taunt").Count == 8 && adapter.Overloads("ConditionEffectBehavior").Single().Parameters.Single(p => p.Name == "duration").Default == "-1", "Creation catalog exposes all real overloads and defaults");
Check(RapleAdapter.Binding("new OtherNamespace.Shoot(8)").RequiresContext, "Unrelated qualified types are not replaced with game primitives");
var originalFile = Path.Combine(adapter.ServerRoot, "TKR.WorldServer/logic/db/BehaviorDb.PirateCave.cs");
var fixtureSource = File.ReadAllText(originalFile) + "\nnamespace TKR.WorldServer.logic.db { using TKR.WorldServer.logic.behaviors; internal static class NativeSchemaFixture { static void Check() { " + string.Join(" ", overloads.Select(pair => "_ = " + pair.Item1 + ";")) + " } } }";
var compile = SourceVerification.Check(adapter.ServerRoot, originalFile, fixtureSource);
Check(compile.Accepted, "Independent original .NET 8 compilation accepts all eight constructor fixtures: " + string.Join(";", compile.Errors.Take(2)));
var invalidCompile = SourceVerification.Check(adapter.ServerRoot, originalFile, fixtureSource.Replace("_ = new Taunt(\"hello\");", "_ = new Taunt(1000, \"hello\");"));
Check(!invalidCompile.Accepted && invalidCompile.Errors.Any(e => e.Contains("CS0121")), "Independent original compiler agrees on ambiguous Cooldown versus numeric overload");
var attackDoc = Doc("new State(new Shoot(8, count: 5, shootAngle: 15, fixedAngle: 90, angleOffset: 10, rotateAngle: 30, coolDown: 100))"); var attackNode = attackDoc.Entities.Single().Root.Children.Single(); var pattern = adapter.ReadShootPattern(attackDoc, attackNode)!;
Check(pattern is { Count: 5, Spacing: 15, FixedAngle: 90, Offset: 10, Rotation: 30, Radius: 8 } && pattern.Angles(0).SequenceEqual([70d, 85, 100, 115, 130]), "Visual attack geometry reads genuine native degrees, centered fan spacing, offset and instance rotation");
var defaultFan = Doc("new State(new Shoot(8, count: 4))"); var defaultPattern = adapter.ReadShootPattern(defaultFan, defaultFan.Entities.Single().Root.Children.Single())!;
Check(defaultPattern.Spacing == 90 && defaultPattern.FixedAngle == null && defaultPattern.Angles(90).SequenceEqual([-45d, 45, 135, 225]), "Default source fans retain 360/count spacing and player-target aim rather than becoming fixed attacks");
var singleFan = Doc("new State(new Shoot(8, shootAngle: 45))");
Check(adapter.ReadShootPattern(singleFan, singleFan.Entities.Single().Root.Children.Single())!.Spacing == 0, "Single native shots ignore supplied spacing exactly as the runtime constructor does");
Check(adapter.ReadShootPattern(attackDoc, attackDoc.Entities.Single().Root) == null, "Attack geometry is exposed only for the actual native Shoot primitive");
using (var attackSession = new EncounterSession(adapter, attackDoc, "Cave Pirate Brawler", 7, new()))
{
    for (var i = 0; i < 10 && attackSession.Shots.Count < 2; i++) attackSession.Step(new());
    var shots = attackSession.Shots.Take(2).ToArray();
    Check(shots.Length == 2 && shots.Select((s, volley) => Math.Abs(s.Angle - pattern.Angles(0, volley)[0] * Math.PI / 180) < .000001 && Math.Abs(s.AngleIncrement - pattern.Spacing * Math.PI / 180) < .000001 && s.Count == pattern.Count).All(v => v), "Visual fan directions match independent native Shoot packets across successive rotation counts within float tolerance");
}
var editedAttack = attackDoc.Source;
var fallbackFan = Doc("new State(new Shoot(5.5, count: 3, shootAngle: 20, defaultAngle: 60, angleOffset: 10, coolDown: 100))"); var fallbackPattern = adapter.ReadShootPattern(fallbackFan, fallbackFan.Entities.Single().Root.Children.Single())!;
Check(fallbackPattern.CanFire(null) && fallbackPattern.Angles(null).SequenceEqual([50d, 70, 90]) && fallbackPattern.Center(0) == 10, "Native default angle supplies fallback aim only when target acquisition has no player");
using (var fallbackSession = new EncounterSession(adapter, fallbackFan, "Cave Pirate Brawler", 7, new()))
{
    fallbackSession.Step(new());
    Check(fallbackSession.Shots.Count == 1 && Math.Abs(fallbackSession.Shots[0].Angle - fallbackPattern.Angles(null)[0] * Math.PI / 180) < .000001, "Fallback fan geometry matches a native packet with the actual player outside search radius");
}
var noTargetFan = Doc("new State(new Shoot(6, count: 3, coolDown: 100))"); var noTargetPattern = adapter.ReadShootPattern(noTargetFan, noTargetFan.Entities.Single().Root.Children.Single())!;
using (var noTargetSession = new EncounterSession(adapter, noTargetFan, "Cave Pirate Brawler", 7, new()))
{
    noTargetSession.Step(new());
    Check(!noTargetPattern.CanFire(null) && noTargetSession.Shots.Count == 0, "Native acquisition excludes a player exactly at search radius and emits no shots without fixed or fallback aim");
}
attackDoc.Apply(new(attackDoc.Revision, "Visual aim", [new(attackNode.Id, "fixedAngle", "180")]), d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(attackDoc.Find(attackNode.Id).Parameters.Single(p => p.Name == "fixedAngle").Expression == "180" && attackDoc.Undo() && attackDoc.Source == editedAttack, "Visual attack parameter commits use the same typed document validation, identity and single undo as the inspector");
Check(adapter.HasKnownProjectileOwner(attackDoc, "Cave Pirate Brawler", attackNode) && adapter.ReadProjectileIndex(attackDoc, attackNode) == 0, "Native State actions retain their XML host and the real omitted projectile-index default");
var ownedWrapper = Doc("new State(new Timed(1000, new Shoot(8, projectileIndex: 2)))"); var ownedShot = ownedWrapper.Entities.Single().Root.Descendants().Single(n => n.Kind == "Shoot");
Check(adapter.HasKnownProjectileOwner(ownedWrapper, "Cave Pirate Brawler", ownedShot) && adapter.ReadProjectileIndex(ownedWrapper, ownedShot) == 2, "Known native wrapper dispatch retains host ownership and an explicit projectile index");
Check(!adapter.HasKnownProjectileOwner(ownedWrapper, "Ghost Ship", ownedShot) && !adapter.IsNativeShoot(ownedWrapper, ownedWrapper.Entities.Single().Root), "Projectile selection rejects a foreign encounter owner and a non-Shoot primitive");
var followDoc = Doc("new State(new Follow(.3))"); var followNode = followDoc.Entities.Single().Root.Children.Single();
Check(adapter.ReadMovementPattern(followDoc, followNode) is { Kind: "Follow", Range: 6, ResumeRange: 7, AcquireRange: 10, Speed: .3, Target: null }, "Movement geometry reads native Follow defaults and its one-tile resume hysteresis");
var orbitDoc = Doc("new State(new Orbit(.3, 3))"); var orbitNode = orbitDoc.Entities.Single().Root.Children.Single();
Check(adapter.ReadMovementPattern(orbitDoc, orbitNode) is { Kind: "Orbit", Range: 3, AcquireRange: 10, RadiusVariance: 1, SpeedVariance: 1, Clockwise: false }, "Orbit geometry retains native default variances and direction instead of inventing a fixed circular path");
var homeDoc = Doc("new State(new StayCloseToSpawn(.3))"); var homeNode = homeDoc.Entities.Single().Root.Children.Single();
Check(adapter.ReadMovementPattern(homeDoc, homeNode) is { Kind: "StayCloseToSpawn", Range: 5, IntegerRange: true, AcquireRange: null }, "Home-range geometry retains the actual integer native boundary");
var signedOrbit = Doc("new State(new Orbit(.3, 3, radiusVariance: -1, orbitClockwise: null, target: \"Cave Pirate Brawler\"))");
Check(adapter.ReadMovementPattern(signedOrbit, signedOrbit.Entities.Single().Root.Children.Single()) is { RadiusVariance: -1, Clockwise: null, Target: "Cave Pirate Brawler" }, "Native signed radius variance, seeded direction and entity target are retained without constructor execution");
Check(adapter.ReadMovementPattern(attackDoc, attackNode) == null, "Movement controls are unavailable for a genuine attack primitive");
using (var followSession = new EncounterSession(adapter, followDoc, "Cave Pirate Brawler", 7, new()))
{
    followSession.Step(new()); var start = followSession.Entities.Single();
    var nativeEngine = typeof(EncounterSession).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(followSession)!;
    var nativeActor = (Entity)nativeEngine.GetType().GetField("Actor")!.GetValue(nativeEngine)!;
    nativeActor.Move(71, 64.5f); followSession.Step(new()); var resting = followSession.Entities.Single();
    nativeActor.Move(71.6f, 64.5f); followSession.Step(new()); var moving = followSession.Entities.Single();
    Check(start.X == 64.5 && start.Y == 64.5 && resting.X == start.X && resting.Y == start.Y && Math.Abs(Math.Sqrt(Math.Pow(moving.X - start.X, 2) + Math.Pow(moving.Y - start.Y, 2)) - .15) < .00001, "Native Follow stops at range, rests inside range+1, then resumes with its real BehaviourTickTime displacement");
}
var acquireDoc = Doc("new State(new Follow(.3, acquireRange: 6, range: 1))");
using (var acquireSession = new EncounterSession(adapter, acquireDoc, "Cave Pirate Brawler", 7, new()))
{
    acquireSession.Step(new()); Check(acquireSession.Entities.Single() is { X: 64.5f, Y: 64.5f }, "Native Follow acquisition excludes a target exactly at the acquisition boundary");
}
var fixedOrbitDoc = Doc("new State(new Orbit(.3, 3, radiusVariance: 0, speedVariance: 0, orbitClockwise: true))");
using (var orbitSession = new EncounterSession(adapter, fixedOrbitDoc, "Cave Pirate Brawler", 7, new()))
{
    orbitSession.Step(new()); var moved = orbitSession.Entities.Single();
    Check(moved.Y < 64.5 && Math.Abs(Math.Sqrt(Math.Pow(moved.X - 64.5, 2) + Math.Pow(moved.Y - 64.5, 2)) - .03) < .00001, "Native Orbit direction and DeltaTime displacement differ correctly from Follow's five-times tick factor");
}
using (var homeSession = new EncounterSession(adapter, homeDoc, "Cave Pirate Brawler", 7, new()))
{
    var nativeBoss = (Enemy)typeof(EncounterSession).GetField("boss", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(homeSession)!;
    nativeBoss.Move(70, 64.5f); homeSession.Step(new()); var inside = homeSession.Entities.Single();
    nativeBoss.Move(76, 64.5f); homeSession.Step(new()); var returning = homeSession.Entities.Single();
    Check(inside.X == 70 && Math.Abs(returning.X - 75.85) < .00001, "StayCloseToSpawn captures its entry position and uses the actual range before returning toward that anchor");
}
var enemyResources = EntityResources.Catalog(EntityResourceScope.Enemy);
var queryResources = EntityResources.Catalog(EntityResourceScope.Queryable);
var allResources = EntityResources.Catalog(EntityResourceScope.Resolvable);
var ghost = enemyResources.Single(r => r.Id == "Ghost Ship");
var ghostXml = System.Xml.Linq.XElement.Load(Path.Combine(adapter.ServerRoot, "TKR.Shared/resources/xml/characters/TK_Characters_chars16x16rEncounters.xml")).Elements("Object").Single(e => (string?)e.Attribute("id") == "Ghost Ship");
Check(ghost.ObjectType == Convert.ToUInt16(((string)ghostXml.Attribute("type")!)[2..], 16) && ghost.MaxHp == (int)ghostXml.Element("MaxHitPoints")! && ghost.Defense == (int)ghostXml.Element("Defense")! && ghost.SpriteId == (string)ghostXml.Attribute("id")!, "Entity choices retain the real XML ID, type, sprite key, HP and defense");
Check(allResources.Count > queryResources.Count && queryResources.Count > enemyResources.Count && allResources.Select(r => r.Id).Distinct().Count() == allResources.Count, "Resource catalogs distinguish native Resolve, collision-index and enemy-only spawn choices with unique native IDs");
var portalResource = allResources.First(r => r.RuntimeKind == "Portal");
Check(!enemyResources.Any(r => r.Id == portalResource.Id) && !queryResources.Any(r => r.Id == portalResource.Id), "Portals are resolvable but absent from the native enemy/static collision index");
Check(allResources.Where(r => r.RuntimeKind is "Container" or "Vendor").All(r => !queryResources.Any(q => q.Id == r.Id)), "Containers and vendors retain their own world buckets instead of being advertised as movement targets");
var entityDoc = Doc("new State(new Spawn(\"Cave Pirate Brawler\", maxChildren: 1, initialSpawn: 1))");
var entityNode = entityDoc.Entities.Single().Root.Children.Single();
var entityBefore = entityDoc.Source; var entityRevision = entityDoc.Revision;
adapter.ApplyEntityReference(entityDoc, "Cave Pirate Brawler", entityNode.Id, entityRevision, "children", ghost.Id);
Check(entityDoc.Revision == entityRevision + 1 && adapter.ReadEntityReference(entityDoc, entityDoc.Find(entityNode.Id), adapter.EntityParameterFor(entityDoc, entityDoc.Find(entityNode.Id))!) is { Known: true, Id: "Ghost Ship" }, "Choosing a spawn resource commits its actual ID once through the typed native parameter");
Check(entityDoc.Find(entityNode.Id).Parameters.Single(p => p.Name == "maxChildren").Expression == "1", "Entity selection preserves unrelated constructor arguments and node identity");
using (var spawnSession = new EncounterSession(adapter, entityDoc, "Cave Pirate Brawler", 7, new()))
{
    spawnSession.Step(new()); spawnSession.Step(new());
    Check(spawnSession.Entities.Any(e => e.Name == "Ghost Ship"), "Native Spawn creates the chosen XML enemy rather than an editor placeholder");
    var engine = typeof(EncounterSession).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(spawnSession)!;
    var server = (TKR.WorldServer.core.GameServer)engine.GetType().GetField("Server")!.GetValue(engine)!;
    var nativeData = (XmlData)engine.GetType().GetProperty("Data", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
    Check(allResources.All(r => nativeData.IdToObjectType[r.Id] == r.ObjectType && nativeData.ObjectDescs[r.ObjectType].IdName == r.SpriteId), "Every XML choice follows the final native ID lookup and resolved sprite descriptor, including aliases and overwritten IDs");
    var portal = Entity.Resolve(server, portalResource.ObjectType);
    Check(portal is Portal, "A resolvable portal choice constructs the actual native Portal class");
    var nativeBoss = (Enemy)typeof(EncounterSession).GetField("boss", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(spawnSession)!;
    portal.Move(65, 64.5f); nativeBoss.World.EnterWorld(portal);
    var staticResource = allResources.First(r => r.RuntimeKind == "World object"); var staticObject = Entity.Resolve(server, staticResource.ObjectType);
    staticObject.Move(65, 64.5f); nativeBoss.World.EnterWorld(staticObject);
    engine.GetType().GetMethod("FlushAdds")!.Invoke(engine, null);
    Check(nativeBoss.GetNearestEntity(10, (ushort?)portal.ObjectType) == null && nativeBoss.GetNearestEntity(10, (ushort?)staticObject.ObjectType) == staticObject, "The real native world query excludes a nearby portal while including a resolved static object");
}
entityDoc.Undo(); Check(entityDoc.Source == entityBefore, "One Undo restores the entire original entity argument");
Reject(() => adapter.ApplyEntityReference(entityDoc, "Cave Pirate Brawler", entityNode.Id, entityDoc.Revision, "children", portalResource.Id), "Spawn rejects non-enemy XML resources atomically");
Reject(() => adapter.ApplyEntityReference(entityDoc, "Cave Pirate Brawler", entityNode.Id, entityDoc.Revision, "children", null), "Spawn rejects null player targeting");
Reject(() => adapter.ApplyEntityReference(entityDoc, "Cave Pirate Brawler", entityNode.Id, entityDoc.Revision - 1, "children", ghost.Id), "Entity choices reject stale source revisions");
entityDoc.LockedNodes.Add(entityNode.Id);
Reject(() => adapter.ApplyEntityReference(entityDoc, "Cave Pirate Brawler", entityNode.Id, entityDoc.Revision, "children", ghost.Id), "Entity choices honor the shared source lock");
var targetDoc = Doc("new State(new Follow(.3, target: \"Ghost Ship\"))"); var targetNode = targetDoc.Entities.Single().Root.Children.Single();
adapter.ApplyEntityReference(targetDoc, "Cave Pirate Brawler", targetNode.Id, targetDoc.Revision, "target", null);
Check(targetDoc.Find(targetNode.Id).Parameters.Single(p => p.Name == "target").Expression == "null" && adapter.ReadMovementPattern(targetDoc, targetDoc.Find(targetNode.Id))!.Target == null, "Nearest-player selection writes the actual native null target without a fake entity ID");
var transitionDoc = Doc("new State(new State(\"waiting\", new Spawn(\"Ghost Ship\", maxChildren: 1, initialSpawn: 1), new EntityExistsTransition(\"Ghost Ship\", 10, \"found\")), new State(\"found\"))");
using (var transitionSession = new EncounterSession(adapter, transitionDoc, "Cave Pirate Brawler", 7, new()))
{
    transitionSession.Step(new()); transitionSession.Step(new()); transitionSession.Step(new());
    Check(transitionSession.Entities.Single(e => e.Name == "Cave Pirate Brawler").Phase == "found", "Entity-exists transitions query the actual spawned XML target through the native collision index");
}
var ignoredDoc = Doc("new State(new State(\"waiting\", new EntityNotExistsTransition(\"Ghost Ship\", 10, \"done\", checkAttackTarget: true)), new State(\"done\"))");
var ignoredNode = ignoredDoc.Entities.Single().Root.Descendants().Single(n => n.Kind == "EntityNotExistsTransition");
Check(adapter.EntityParameterFor(ignoredDoc, ignoredNode)!.InactiveReason != null, "The picker explains when native checkAttackTarget makes the XML target unused");
Reject(() => adapter.ApplyEntityReference(ignoredDoc, "Cave Pirate Brawler", ignoredNode.Id, ignoredDoc.Revision, "target", "Cave Pirate Brawler"), "Inactive XML targets cannot be changed through the resource picker");
Check(RapleAdapter.EntityParameterFor("Transform")?.Scope == EntityResourceScope.Resolvable && RapleAdapter.EntityParameterFor("TransformOnDeath")?.Scope == EntityResourceScope.Resolvable && RapleAdapter.EntityParameterFor("EngineTransform") == null, "Transform mappings follow native Resolve while context-specific EngineTransform retains its source boundary");
foreach (var (expression, kind, anchor, first, second) in new[] {
    ("new MoveTo(.3f, 68.5f, 65.5f)", "MoveTo", NativePathAnchor.Map, 68.5, 65.5),
    ("new MoveTo2(2f, -1f, speed: .3)", "MoveTo2", NativePathAnchor.StateEntry, 2d, -1d),
    ("new MoveTo3(68.5f, 65.5f, isMapPosition: true, instant: true)", "MoveTo3", NativePathAnchor.Map, 68.5, 65.5),
    ("new SpawnRelativeMove(.3f, 2f, -1f)", "SpawnRelativeMove", NativePathAnchor.Spawn, 2d, -1d),
    ("new MoveLine(.3, direction: 90, distance: 2)", "MoveLine", NativePathAnchor.CurrentPosition, 90d, 2d),
    ("new NewMove(distance: -2f, angle: 90f)", "NewMove", NativePathAnchor.CurrentPosition, 90d, -2d) })
{
    var pathDoc = Doc("new State(" + expression + ")"); var pathNode = pathDoc.Entities.Single().Root.Children.Single();
    var parsedPath = adapter.ReadPathPattern(pathDoc, pathNode);
    Check(parsedPath != null && parsedPath.Kind == kind && parsedPath.Anchor == anchor && parsedPath.First == first && parsedPath.Second == second && adapter.Primitives.Single(p => p.Name == kind).Simulatable, "Native path binds its real constructor, units and anchor: " + kind);
}
var defaultNewMove = Doc("new State(new NewMove())"); var defaultNewMoveNode = defaultNewMove.Entities.Single().Root.Children.Single();
Check(adapter.ReadPathPattern(defaultNewMove, defaultNewMoveNode) is { First: 0, Second: 3, Speed: 1, Cooldown: 10 } && adapter.ParametersFor(defaultNewMove, defaultNewMoveNode).Single(p => p.Name == "cooldown").Unit == "seconds" && adapter.Overloads("NewMove").Single().Parameters.Single(p => p.Name == "cooldown").Unit == "seconds", "NewMove retains native defaults and reports its DeltaTime cooldown in seconds");
var continuous = Doc("new State(new MoveLine(.3))"); var continuousNode = continuous.Entities.Single().Root.Children.Single();
Check(adapter.ReadPathPattern(continuous, continuousNode) is { Continuous: true } continuousPattern && continuousPattern.Destination(64.5, 64.5) == null, "Native zero-distance MoveLine is continuous and has no invented finite endpoint");
using (var native = new EncounterSession(adapter, continuous, "Cave Pirate Brawler", 7, new()))
{
    native.Step(new()); native.Step(new()); Check(Math.Abs(native.Entities.Single().X - 64.8) < .00001, "Native continuous MoveLine advances on each real enemy tick");
}
var finiteLine = Doc("new State(new MoveLine(.3, distance: .1))");
using (var native = new EncounterSession(adapter, finiteLine, "Cave Pirate Brawler", 7, new()))
{
    native.Step(new()); var first = native.Entities.Single().X; native.Step(new());
    Check(Math.Abs(first - 64.65) < .00001 && native.Entities.Single().X == first, "Native finite MoveLine overshoots a shorter requested distance by one full step rather than a guessed arrival clamp");
}
using (var native = new EncounterSession(adapter, Doc("new State(new MoveLine(.3, distance: -1))"), "Cave Pirate Brawler", 7, new()))
{
    native.Step(new()); Check(native.Entities.Single().X == 64.5, "Native negative MoveLine distance remains inactive instead of being converted into a reverse path");
}
var destinationDoc = Doc("new State(new MoveTo(.3f, 64.6f, 64.5f))"); var destinationNode = destinationDoc.Entities.Single().Root.Children.Single();
using (var native = new EncounterSession(adapter, destinationDoc, "Cave Pirate Brawler", 7, new()))
{
    native.Step(new()); Check(native.Entities.Single().X == 64.6f, "Native MoveTo clamps its final step exactly to its reachable destination");
}
var destinationBefore = destinationDoc.Source; var destinationRevision = destinationDoc.Revision;
adapter.ApplyPathValues(destinationDoc, "Cave Pirate Brawler", destinationNode.Id, destinationRevision, new Dictionary<string, double> { ["x"] = 68.25, ["y"] = 65.75 });
Check(destinationDoc.Revision == destinationRevision + 1 && destinationDoc.Find(destinationNode.Id).Parameters.Single(p => p.Name == "x").Expression == "68.25f" && destinationDoc.Find(destinationNode.Id).Parameters.Single(p => p.Name == "y").Expression == "65.75f", "Destination coordinates use native float literals and one atomic paired source patch");
Check(destinationDoc.Find(destinationNode.Id).Parameters.Single(p => p.Name == "speed").Expression == ".3f", "Destination authoring retains native speed and action identity");
destinationDoc.Undo(); Check(destinationDoc.Source == destinationBefore, "One Undo restores both native destination coordinates");
Reject(() => adapter.ApplyPathValues(destinationDoc, "Cave Pirate Brawler", destinationNode.Id, destinationDoc.Revision - 1, new Dictionary<string, double> { ["x"] = 68 }), "Native destination patches reject stale revisions");
Reject(() => adapter.ApplyPathValues(destinationDoc, "Cave Pirate Brawler", destinationNode.Id, destinationDoc.Revision, new Dictionary<string, double> { ["speed"] = 1 }), "Path gestures cannot rewrite unrelated native parameters");
Reject(() => adapter.ApplyPathValues(destinationDoc, "Cave Pirate Brawler", destinationNode.Id, destinationDoc.Revision, new Dictionary<string, double> { ["x"] = double.MaxValue }), "Native float coordinate overflow rejects without source changes");
destinationDoc.LockedNodes.Add(destinationNode.Id);
Reject(() => adapter.ApplyPathValues(destinationDoc, "Cave Pirate Brawler", destinationNode.Id, destinationDoc.Revision, new Dictionary<string, double> { ["x"] = 68 }), "Native path patches honor shared source locks");
foreach (var (expression, expected) in new[] { ("new MoveTo2(2f, 0f, speed: .3)", 70.15), ("new SpawnRelativeMove(.3f, 2f, 0f)", 69.85) })
{
    using var native = new EncounterSession(adapter, Doc("new State(new State(\"idle\"), new State(\"moving\", " + expression + "))"), "Cave Pirate Brawler", 7, new());
    var relocatedBoss = (Enemy)typeof(EncounterSession).GetField("boss", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(native)!;
    native.Step(new()); relocatedBoss.Move(70, 64.5f); relocatedBoss.SwitchTo(relocatedBoss.CurrentState.Parent.States.Single(s => s.Name == "moving")); native.Step(new());
    Check(Math.Abs(native.Entities.Single().X - expected) < .00001, "Native state-entry and first-tick spawn anchors differ after relocation: " + expression);
}
using (var native = new EncounterSession(adapter, Doc("new State(new MoveTo2(68.5f, 64.5f, isMapPosition: true, instant: true))"), "Cave Pirate Brawler", 7, new()))
{
    native.Step(new()); Check(native.Entities.Single().X == 68.5f, "Native instant MoveTo2 moves on entry instead of simulating a smooth interpolation path");
}
var orderedPathDoc = Doc("new State(new OrderedBehavior(new NewMove(speed: 1f, distance: 1f, cooldown: 10f), new NewMove(speed: 1f, distance: 1f, angle: 90f, cooldown: 10f)), new Shoot(8, coolDown: 100))");
using (var native = new EncounterSession(adapter, orderedPathDoc, "Cave Pirate Brawler", 7, new()))
{
    native.Step(new()); var first = native.Entities.Single(); var independentShot = native.Shots.Count == 1; native.Step(new()); var arrival = native.Entities.Single(); native.Step(new()); var next = native.Entities.Single(); native.Step(new()); var resumed = native.Entities.Single();
    Check(first.X == 65 && first.Y == 64.5 && arrival.X == 65.5 && arrival.Y == 64.5 && next.X == 65.5 && next.Y == 65 && resumed.Y == 64.5 && independentShot, "Native OrderedBehavior suppresses later ordered children while moving, advances on an idle tick and reactivates an earlier destination after another child moves the host");
}
using (var native = new EncounterSession(adapter, defaultNewMove, "Cave Pirate Brawler", 7, new()))
{
    native.Step(new()); Check(native.Entities.Single().X == 64.5, "NewMove placed directly in a normal State remains inactive because only TickCoreOrdered implements movement");
}
var orderedChild = orderedPathDoc.Entities.Single().Root.Descendants().First(n => n.Kind == "NewMove");
Check(adapter.EffectiveCallbacks(orderedPathDoc, orderedChild) is [{ Stage: "Ongoing", Method: "TickCoreOrdered" }] && adapter.EffectiveCallbacks(defaultNewMove, defaultNewMoveNode).All(c => c.Stage != "Ongoing"), "Lifecycle provenance shows ordered-only movement without inventing entry/exit forwarding or normal ticks");
var pathProject = new StudioProject { Source = "class Fixture { void Build() { db.Init(\"Cave Pirate Brawler\", new State(new MoveTo(.3f, 68.5f, 64.5f), new MoveLine(.3, direction: 90))); } }", Seed = 7 };
var sampledPath = await NativeMovementPreview.SampleAsync(adapter, pathProject, durationMs: 200);
Check(sampledPath.Points.Length == 3 && sampledPath.Points.Select(p => p.Ms).SequenceEqual([0L, 100L, 200L]) && Math.Abs(sampledPath.Points[1].X - 64.65) < .00001 && Math.Abs(sampledPath.Points[1].Y - 64.65) < .00001, "Whole-phase path samples execute both native movement actions in source order with authoritative tick times");
var repeatedPath = await NativeMovementPreview.SampleAsync(adapter, pathProject, durationMs: 200);
Check(sampledPath.Points.SequenceEqual(repeatedPath.Points) && sampledPath.SourceSha256 == repeatedPath.SourceSha256 && sampledPath.Seed == 7, "Native movement sampling repeats its seed/input and retains source provenance");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel(); var rejected = false; try { await NativeMovementPreview.SampleAsync(adapter, pathProject, cancellation: cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
    Check(rejected, "Cancelled native movement requests do not construct a preview session");
}
var ringDoc = Doc("new State(new State(\"ring\", new InstantSpawnRing(\"Ghost Ship\", count: 2, radius: 2), new TimedTransition(300, \"finished\")), new State(\"finished\"))");
var ringNode = ringDoc.Entities.Single().Root.Descendants().Single(n => n.Kind == "InstantSpawnRing");
Check(!adapter.NeedsCompiledRuntime(ringDoc, "Cave Pirate Brawler") && adapter.EntityParameterFor(ringDoc, ringNode) is { Name: "childName", Scope: EntityResourceScope.Enemy }, "Native ring spawning supports real constructors and an enemy-only XML helper picker");
Check(adapter.Callbacks("InstantSpawnRing").Select(c => c.Stage).SequenceEqual(["Entry", "Exit", "Death"]) && adapter.Callbacks("GordoSupport").All(c => c.DeclaringType == "InstantSpawnRing"), "Native ring and derived support lifecycle provenance retains inherited entry, exit and death cleanup");
using (var native = new EncounterSession(adapter, ringDoc, "Cave Pirate Brawler", 7, new()))
{
    native.Step(new()); var host = native.Entities.Single(e => e.Name == "Cave Pirate Brawler"); var helpersInRing = native.Entities.Where(e => e.ParentId == host.Id).OrderBy(e => e.Id).ToArray();
    Check(helpersInRing.Length == 2 && helpersInRing.All(e => e.SpawnedByBehavior) && Math.Abs(helpersInRing[0].X - 66.5) < .00001 && Math.Abs(helpersInRing[1].X - 62.5) < .00001 && helpersInRing.All(e => e.Y == 64.5), "Actual ring entry places two helpers on the native circle and snapshots their genuine parent and behavior-spawn flag");
    Check(native.Events.Count(e => e.Kind == "spawn" && e.ParentId == host.Id && helpersInRing.Any(h => h.Id == e.ActorId)) == 2, "Native ring spawn events retain distinct actor IDs and actual parent provenance");
    native.Advance(new(), 400);
    Check(native.Entities.Count == 1 && native.Entities.Single().Phase == "finished", "Leaving the owning phase executes native ring exit cleanup without keeping its helpers alive");
    Check(helpersInRing.All(h => native.Events.Any(e => e.Kind == "despawn" && e.ActorId == h.Id && e.ParentId == host.Id)), "Native removal events preserve each helper's actual parent after phase cleanup");
}
using (var native = new EncounterSession(adapter, Doc("new State(new InstantSpawnRing(\"Ghost Ship\", radius: 2))"), "Cave Pirate Brawler", 7, new()))
{
    native.Step(new()); native.SetBossHpFraction(.001);
    for (var i = 0; i < 15 && native.Entities.Any(e => e.Name == "Cave Pirate Brawler"); i++) native.Step(new(Attack: true, AimX: 64.5, AimY: 64.5));
    Check(native.Entities.Count == 0 && native.Events.Any(e => e.Kind == "death") && native.Events.Count(e => e.Kind == "despawn" && e.ParentId != null) == 2, "Weapon-triggered host death executes actual ring death cleanup and records removed owned helpers");
}
using (var native = new EncounterSession(adapter, Doc("new State(new Spawn(\"Ghost Ship\", maxChildren: 2, initialSpawn: 0, coolDown: 0))"), "Cave Pirate Brawler", 7, new()))
{
    native.Step(new()); var laterSpawn = native.Entities.Single(e => e.Name == "Ghost Ship");
    Check(laterSpawn.SpawnedByBehavior && laterSpawn.ParentId == null, "Ordinary ongoing Spawn preserves the game's unassigned parent instead of inventing an ownership link");
}
foreach (var (kind, phaseParameter, entityParameterName, stage) in new[] { ("Order", "targetState", "children", "Ongoing"), ("OrderOnce", "targetState", "children", "Entry"), ("OrderOnDeath", "state", "target", "Death") })
{
    var orderDoc = Doc("new State(new " + kind + "(10, \"Ghost Ship\", \"pause\"))"); var orderNode = orderDoc.Entities.Single().Root.Children.Single();
    Check(adapter.Callbacks(kind).Select(c => c.Stage).SequenceEqual([stage]) && adapter.EntityParameterFor(orderDoc, orderNode)?.Name == entityParameterName && !adapter.NeedsCompiledRuntime(orderDoc, "Cave Pirate Brawler"), "Native " + kind + " retains its actual callback, entity field and original helper-order implementation");
    adapter.ApplyEntityReference(orderDoc, "Cave Pirate Brawler", orderNode.Id, orderDoc.Revision, entityParameterName, "Cave Pirate Veteran");
    Check(orderDoc.Find(orderNode.Id).Parameters.Single(p => p.Name == phaseParameter).Expression == "\"pause\"", "Native " + kind + " entity selection preserves the target entity's independent phase name");
}
using (var native = new EncounterSession(adapter, Doc("new State(new InstantSpawnRing(\"Ghost Ship\", radius: 2), new OrderOnce(10, \"Ghost Ship\", \"pause\"))"), "Cave Pirate Brawler", 7, new()))
{
    native.Step(new());
    Check(native.Entities.Count(e => e.Name == "Ghost Ship" && e.Phase == "pause") == 2, "Native OrderOnce addresses pending ring helpers in the same tick and switches their actual production phases");
}
var orderReferences = new BehaviorDocument("class Fixture { void Build() { db.Init(\"Cave Pirate Brawler\", new State(new State(\"pause\", new OrderOnce(10, \"Ghost Ship\", \"pause\"), new OrderOnce(10, \"Cave Pirate Brawler\", \"pause\")))); db.Init(\"Ghost Ship\", new State(new State(\"pause\", new OrderOnDeath(10, \"Cave Pirate Brawler\", \"pause\")))); } }", adapter);
var referencePhase = orderReferences.Entities.Single(e => e.EntityId == "Cave Pirate Brawler").Root.Children.Single();
var incomingOrder = orderReferences.Entities.Single(e => e.EntityId == "Ghost Ship").Root.Descendants().Single(n => n.Kind == "OrderOnDeath");
orderReferences.LockedNodes.Add(incomingOrder.Id); var referenceBefore = orderReferences.Source;
Reject(() => orderReferences.RenameState(referencePhase.Id, "opening"), "Locked incoming orders protect the target entity's phase rename across definitions");
Check(orderReferences.Source == referenceBefore, "A rejected cross-entity phase rename retains complete source atomically"); orderReferences.LockedNodes.Clear();
orderReferences.RenameState(referencePhase.Id, "opening");
Check(orderReferences.Find(referencePhase.Id).Children.Single(n => n.Parameters.Any(p => p.Name == "children" && p.Expression == "\"Ghost Ship\"")).Parameters.Single(p => p.Name == "targetState").Expression == "\"pause\"" && orderReferences.Find(incomingOrder.Id).Parameters.Single(p => p.Name == "state").Expression == "\"opening\"" && orderReferences.Find(referencePhase.Id).Children.Single(n => n.Parameters.Any(p => p.Name == "children" && p.Expression == "\"Cave Pirate Brawler\"")).Parameters.Single(p => p.Name == "targetState").Expression == "\"opening\"", "Phase rename preserves external orders and rewrites actual self/incoming order targets across entity definitions");
var copiedReferencePhase = orderReferences.Find(orderReferences.DuplicateNode(referencePhase.Id));
Check(copiedReferencePhase.Children.Single(n => n.Parameters.Any(p => p.Name == "children" && p.Expression == "\"Cave Pirate Brawler\"")).Parameters.Single(p => p.Name == "targetState").Expression == "\"opening-copy\"" && copiedReferencePhase.Children.Single(n => n.Parameters.Any(p => p.Name == "children" && p.Expression == "\"Ghost Ship\"")).Parameters.Single(p => p.Name == "targetState").Expression == "\"pause\"", "Copied phases remap self orders using their real entity scope while preserving foreign target phases");
Reject(() => orderReferences.DeleteNode(referencePhase.Id), "Deleting a phase referenced by a different entity's order requires explicit rerouting first");
orderReferences.Undo(); orderReferences.Undo(); Check(orderReferences.Source == referenceBefore, "Cross-entity order rename and copy each restore complete source through one Undo");
Check(adapter.Validate(Doc("new State(new OrderOnce(10, \"Ghost Ship\", \"pause\"))"), "Cave Pirate Brawler").All(d => !d.IsError) && adapter.Validate(Doc("new State(new OrderOnce(10, \"Cave Pirate Brawler\", \"missing\"))"), "Cave Pirate Brawler").Any(d => d.IsError && d.Message.StartsWith("Missing transition target")), "Native validation distinguishes external target phases from missing self-order destinations");
Check(adapter.Validate(Doc("new State(new InstantSpawnRing(\"Ghost Ship\", count: 13))"), "Cave Pirate Brawler").Any(d => d.IsError), "Ring authoring rejects counts beyond the native twelve-helper constructor limit");
foreach (var kind in new[] { "Order", "OrderOnce" })
{
    using var native = new EncounterSession(adapter, Doc("new State(new InstantSpawnRing(\"Ghost Ship\", radius: 2), new " + kind + "(10, \"Ghost Ship\", \"pause\"))"), "Cave Pirate Brawler", 7, new());
    var engine = typeof(EncounterSession).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(native)!;
    var world = (TKR.WorldServer.core.worlds.World)engine.GetType().GetField("World")!.GetValue(engine)!;
    var data = (XmlData)engine.GetType().GetProperty("Data", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
    var unowned = (Enemy)world.CreateNewEntity(data.IdToObjectType["Ghost Ship"], 64.5f, 66.5f); engine.GetType().GetMethod("FlushAdds")!.Invoke(engine, null); native.Step(new());
    var firstOrderReachedUnowned = native.Entities.Single(e => e.Id == unowned.Id) is { ParentId: null, Phase: "pause" };
    unowned.SwitchTo(unowned.CurrentState.Parent.States.Single(s => s.Name == "idle")); native.Step(new());
    Check(firstOrderReachedUnowned && native.Entities.Single(e => e.Id == unowned.Id).Phase == (kind == "Order" ? "pause" : "idle"), "Native " + kind + " addresses unowned nearby entities and preserves its repeated-tick versus entry-only dispatch");
}
using (var native = new EncounterSession(adapter, Doc("new State(new InstantSpawnRing(\"Ghost Ship\", radius: 2), new OrderOnDeath(10, \"Ghost Ship\", \"pause\"))"), "Cave Pirate Brawler", 7, new()))
{
    var engine = typeof(EncounterSession).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(native)!;
    var world = (TKR.WorldServer.core.worlds.World)engine.GetType().GetField("World")!.GetValue(engine)!;
    var data = (XmlData)engine.GetType().GetProperty("Data", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
    var unowned = (Enemy)world.CreateNewEntity(data.IdToObjectType["Ghost Ship"], 64.5f, 66.5f); engine.GetType().GetMethod("FlushAdds")!.Invoke(engine, null); native.Step(new()); native.SetBossHpFraction(.001);
    for (var i = 0; i < 15 && native.Entities.Any(e => e.Name == "Cave Pirate Brawler"); i++) native.Step(new(Attack: true, AimX: 64.5, AimY: 64.5));
    Check(native.Entities is [{ Id: var remaining, ParentId: null, Phase: "pause" }] && remaining == unowned.Id, "Weapon-triggered native death clears owned ring helpers and orders a surviving unowned target through OrderOnDeath");
}
Console.WriteLine($"{count} native schema checks passed.");
