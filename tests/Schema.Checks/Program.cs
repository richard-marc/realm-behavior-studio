using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Studio.Core;
using Studio.Raple;
using TKR.WorldServer.logic;
using TKR.WorldServer.core.objects;
using TKR.Shared.resources;

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
Console.WriteLine($"{count} native schema checks passed.");
