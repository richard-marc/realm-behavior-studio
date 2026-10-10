using System.Text.Json;
using Studio.Core;
using Studio.Raple;

var server = Path.GetFullPath(args.FirstOrDefault() ?? "../raple-realms-7-server");
var adapter = new RapleAdapter(server);
var file = Path.Combine(server, "TKR.WorldServer/logic/db/BehaviorDb.PirateCave.cs");
var original = File.ReadAllText(file);
var sourceRoot = new BehaviorDocument(original, adapter).Entities.Single(e => e.EntityId == "Cave Pirate Brawler").Root;
var checks = new List<string>();
void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); Console.WriteLine("PASS " + label); }
void Reject(Action action, string label) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
const string helpers = """

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    const int StudioContextCount = 4;
    const double StudioContextSpeed = .3;
    const string StudioContextPhaseName = "source name";
    static State StudioContextRoot(int count = 4, double speed = .3) => new State(new State("source factory", new TKR.WorldServer.logic.behaviors.Follow(speed, range: 1), StudioContextFan(count)));
    static TKR.WorldServer.logic.behaviors.Shoot StudioContextFan(int count = 4, double range = 8) => new TKR.WorldServer.logic.behaviors.Shoot(range, count: count, shootAngle: 12, fixedAngle: 90, coolDown: 200);
    static TKR.WorldServer.logic.behaviors.Shoot StudioContextFan(string text) => new TKR.WorldServer.logic.behaviors.Shoot(8, count: text.Length);
    static State StudioContextShared { get { throw new System.InvalidOperationException("authoring must not execute this getter"); } }
    static TKR.WorldServer.logic.behaviors.Timed StudioContextWrap(params TKR.WorldServer.logic.Behavior[] children) => new TKR.WorldServer.logic.behaviors.Timed(1000, children);
    static State StudioContextMessages(params string[] text) => new State(string.Join("-", text));
    static State StudioContextKeyword(int @event = 1) => new State(@event.ToString());
    static State StudioContextAmbiguous(int count, double speed = 1) => new State(count.ToString());
    static State StudioContextAmbiguous(int count, float speed = 1) => new State(count.ToString());
    sealed class StudioContextDerivedState : State { public StudioContextDerivedState() : base("derived source") { } }
    static StudioContextDerivedState StudioContextDerived() => new StudioContextDerivedState();
    static State StudioContextCounter(int count = -1, string target = "label") => new State(target + count);
} }
""";
BehaviorDocument Doc(string expression) => new(original.Remove(sourceRoot.Start, sourceRoot.Length).Insert(sourceRoot.Start, expression) + helpers, adapter, sourceFile: file);
BehaviorNode Root(BehaviorDocument doc) => doc.Entities.Single(e => e.EntityId == "Cave Pirate Brawler").Root;
void Edit(BehaviorDocument doc, BehaviorNode node, string name, string value) => doc.Apply(new(doc.Revision, "Edit source argument", [new(node.Id, name, value)]), d => adapter.Validate(d, "Cave Pirate Brawler"));
var nativeLiteral = new BehaviorDocument(original, adapter, sourceFile: file);
Check(!adapter.NeedsCompiledRuntime(nativeLiteral, "Cave Pirate Brawler"), "Actual native literal constructors retain quick playback including nullable parameter types");
var factory = Doc("StudioContextRoot(count: StudioContextCount)"); var root = Root(factory); var id = root.Id;
Check(root.Kind == "Factory" && root.Label.Contains("StudioContextRoot") && root.Children.Count == 0, "Factory State root is discovered without inventing its generated phases");
Check(adapter.EntityIds(factory.Source, file).Contains("Cave Pirate Brawler"), "Library root discovery includes source factories without binding every nested catalog action");
Check(root.Parameters.Single().Name == "count" && root.Parameters.Single().Expression == "StudioContextCount", "Source constant retains its original argument expression and formal name");
Check(adapter.ParametersFor(factory, root).Any(p => p.Name == "speed" && p.Default == "0.3") && adapter.ParameterFor(factory, root, root.Parameters.Single())?.Type == "Int32", "Inspector uses real helper parameter types and optional defaults");
var diagnostics = adapter.Validate(factory, "Cave Pirate Brawler").ToArray();
foreach (var diagnostic in diagnostics.Where(d => d.IsError)) Console.WriteLine(diagnostic.Message);
Check(!diagnostics.Any(d => d.IsError) && adapter.NeedsCompiledRuntime(factory, "Cave Pirate Brawler"), "Factory root validates in the original compiler context and selects compiled execution");
Edit(factory, root, "count", "3"); Edit(factory, factory.Find(id), "speed", ".4");
Check(factory.Find(id).Parameters.Any(p => p.Name == "speed" && p.Expression == ".4") && factory.Source.Contains("count: 3, speed: .4"), "Helper arguments support exact scalar edits and omitted named optional values");
Check(adapter.ParametersFor(factory, factory.Find(id)).Single(p => p.Name == "speed").Unit == "", "Helper parameter names do not invent native movement units");
var valid = factory.Source; var revision = factory.Revision;
Reject(() => Edit(factory, factory.Find(id), "speed", "\"wrong type\""), "Actual compiler rejects a helper argument of the wrong type");
Check(factory.Source == valid && factory.Revision == revision, "Rejected source-context edit preserves last valid source and revision");
factory.LockedNodes.Add(id); Reject(() => Edit(factory, factory.Find(id), "count", "2"), "Factory argument editing respects region locks"); factory.LockedNodes.Clear();
Check(factory.Undo() && factory.Find(id).Parameters.All(p => p.Name != "speed") && factory.Redo() && factory.Find(id).Parameters.Any(p => p.Name == "speed"), "Source binding and factory identity survive undo and redo");
Edit(factory, factory.Find(id), "count", "StudioContextCount");
Check(factory.Find(id).Parameters.Single(p => p.Name == "count").Expression == "StudioContextCount", "Visual fields can retain a compiler-resolved source constant");
Reject(() => Edit(factory, factory.Find(id), "count", "StudioMissingValue"), "Unresolved identifiers never fall back to a guessed literal value");
var nested = Doc("new State(StudioContextFan(range: 7, count: 2), StudioContextFan(\"abc\"), StudioContextShared, new Wander(StudioContextSpeed))");
var nodes = Root(nested).Children;
var legacy = new BehaviorDocument(nested.Source, adapter);
var upgraded = new BehaviorDocument(legacy.Source, adapter, legacy.Identities, file);
Check(Root(upgraded).Children.Take(3).Select(n => n.Id).SequenceEqual(Root(legacy).Children.Take(3).Select(n => n.Id)), "Existing opaque helper and reference identities upgrade without losing their saved regions");
Check(nodes[0].Kind == "Factory" && nodes[0].Parameters.Select(p => p.Name).SequenceEqual(["range", "count"]) && nodes[1].Parameters.Single().Name == "text", "Overloaded helpers and out-of-order named arguments bind to actual source methods");
Check(adapter.ParameterFor(nested, nodes[0], nodes[0].Parameters[0])?.Type == "Double" && adapter.ParameterFor(nested, nodes[1], nodes[1].Parameters[0])?.Type == "String", "Identical helper names keep distinct overload metadata at each source position");
Check(nodes[2].Kind == "Reference" && nodes[2].Parameters.Count == 0 && nodes[2].Children.Count == 0, "Shared State property is retained as a reference without executing its getter");
Check(nodes[3].Kind == "Wander" && nodes[3].Parameters.Single().Name == "speed" && !adapter.Validate(nested, "Cave Pirate Brawler").Any(d => d.IsError), "Native constructors bind source variables using the edited original file");
var sharedRoot = Doc("StudioContextShared");
Check(Root(sharedRoot).Kind == "Reference" && adapter.NeedsCompiledRuntime(sharedRoot, "Cave Pirate Brawler"), "Shared native State root is discovered without evaluating its getter");
var derived = Doc("StudioContextDerived()");
Check(Root(derived).Kind == "Factory" && !adapter.Validate(derived, "Cave Pirate Brawler").Any(d => d.IsError), "Factory root discovery follows actual source inheritance into native State");
var aliasedSource = "using StudioContextStateAlias = TKR.WorldServer.logic.State;\n" + Doc("new StudioContextStateAlias(\"aliased source\", new Wander(StudioContextSpeed))").Source;
var aliased = new BehaviorDocument(aliasedSource, adapter, sourceFile: file);
Check(Root(aliased).Kind == "State" && Root(aliased).Label == "aliased source" && !adapter.Validate(aliased, "Cave Pirate Brawler").Any(d => d.IsError), "Source aliases keep the actual native State kind and phase parameters");
var shadowedSource = "using Shoot = TKR.WorldServer.logic.behaviors.Follow;\n" + Doc("new State(new StudioContextAliasPlaceholder(.3, range: 1))").Source.Replace("new Shoot(", "new TKR.WorldServer.logic.behaviors.Shoot(").Replace("new StudioContextAliasPlaceholder(", "new Shoot(");
var shadowed = new BehaviorDocument(shadowedSource, adapter, sourceFile: file);
Check(Root(shadowed).Children.Single().Kind == "Follow" && adapter.NeedsCompiledRuntime(shadowed, "Cave Pirate Brawler") && !adapter.Validate(shadowed, "Cave Pirate Brawler").Any(d => d.IsError), "A source alias of a familiar primitive resolves its actual constructor and requires compiled execution");
var dynamicName = Doc("new State(StudioContextPhaseName, new Wander(StudioContextSpeed), new TimedTransition(100, \"source name\"))");
Check(Root(dynamicName).Parameters.Single().Name == "name" && Root(dynamicName).Children.Count == 2 && Root(dynamicName).Label.Contains("StudioContextPhaseName"), "Source-defined State names retain scalar parameters instead of becoming fake child behaviors");
Check(!adapter.Validate(dynamicName, "Cave Pirate Brawler").Any(d => d.IsError) && BehaviorAnalysis.Analyze(Root(dynamicName)).Diagnostics.Any(d => d.Message.StartsWith("Reachability analysis is incomplete")), "Dynamic phase names suppress guessed targets and definitive reachability claims");
var counter = Doc("StudioContextCounter()"); Edit(counter, Root(counter), "count", "-2"); Edit(counter, Root(counter), "target", "\"arbitrary label\"");
Check(!adapter.Validate(counter, "Cave Pirate Brawler").Any(d => d.IsError), "Helper names do not impose guessed Shoot count limits or entity resource semantics");
var messages = Doc("StudioContextMessages(\"one\", \"two\")"); var messageRoot = Root(messages);
Check(messageRoot.Parameters.Select(p => p.Name).SequenceEqual(["text[0]", "text[1]"]) && adapter.ParameterFor(messages, messageRoot, messageRoot.Parameters[1])?.Type == "String", "Expanded helper params expose separate fields with actual element types");
Edit(messages, messageRoot, "text[1]", "\"changed\"");
Check(Root(messages).Parameters[0].Expression == "\"one\"" && Root(messages).Parameters[1].Expression == "\"changed\"", "Editing one repeated helper argument leaves its neighbor unchanged");
var keyword = Doc("StudioContextKeyword()"); Edit(keyword, Root(keyword), "event", "2");
Check(keyword.Source.Contains("StudioContextKeyword(@event: 2)"), "Optional helper parameters escape actual C# keyword names");
Reject(() => factory.ReplaceSource(factory.Source.Replace("StudioContextRoot(count: StudioContextCount, speed: .4)", "StudioContextAmbiguous(1)")), "Ambiguous helper overloads are rejected rather than guessed");
var inferred = Doc("new State(StudioContextWrap(new[] { new Shoot(8, count: 2) }))"); var inferredGroup = Root(inferred).Children.Single().Children.Single();
inferred.DeleteNodes(inferredGroup.Children.Select(n => n.Id), d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(inferred.Find(inferredGroup.Id).Children.Count == 0 && inferred.Source.Contains("Shoot[]"), "Empty inferred factory array preserves the narrower source element type");
var grouped = Doc("new State(StudioContextWrap(children: new TKR.WorldServer.logic.Behavior[] { new Wander(StudioContextSpeed), new Shoot(8, count: 2) }))");
var wrap = Root(grouped).Children.Single(); var group = wrap.Children.Single(); var wander = group.Children[0]; var shoot = group.Children[1];
Check(wrap.Kind == "Factory" && group.Kind == "ArgumentGroup" && group.Argument?.ElementType == "Behavior" && grouped.CanEditChildren(group.Id), "Factory child arrays expose their real typed argument list");
grouped.MoveNodes([shoot.Id], group.Id, 0, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(grouped.Find(group.Id).Children.Select(n => n.Id).SequenceEqual([shoot.Id, wander.Id]) && grouped.Source.Contains("children:"), "Factory child reorder preserves named array syntax and stable identities");
Reject(() => grouped.AddChild(group.Id, "new TimedTransition(100, \"hidden\")", d => adapter.Validate(d, "Cave Pirate Brawler")), "Factory Behavior array rejects a native Transition subtype");
grouped.DeleteNodes([shoot.Id, wander.Id], d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(grouped.Find(group.Id).Children.Count == 0 && grouped.Source.Contains("Behavior[]"), "Empty factory array preserves its explicit native element type");
Check(grouped.Undo() && grouped.Find(group.Id).Children.Count == 2, "Factory child structure restores atomically through undo");
var phase = Doc("new State(new State(\"visible\", new Shoot(8)), StudioContextFan())");
Reject(() => phase.RenameState(Root(phase).Children[0].Id, "renamed"), "Visible phase rename refuses to guess hidden helper references");
Reject(() => factory.ReplaceSource(factory.Source.Replace("count: count", "count: StudioNoSuchCount")), "Compiler errors in a helper body reject complete source replacement");
var output = Path.GetFullPath(".artifacts/source-context"); Directory.CreateDirectory(output);
var project = new StudioProject { SourceFile = file, Source = factory.Source, EntityId = "Cave Pirate Brawler", Identities = factory.Identities, Seed = 7 };
var saved = Path.Combine(output, "factory.rbs"); BehaviorDocument.Save(saved, project); var loaded = BehaviorDocument.Load(saved);
var reopened = new BehaviorDocument(loaded.Source, adapter, loaded.Identities, loaded.SourceFile);
Check(Root(reopened).Id == id && Root(reopened).Kind == "Factory" && reopened.Source == factory.Source, "Factory document persists its source context and root identity through save and reopen");
await using (var native = await CompiledRuntime.StartAsync(server, loaded, artifactRoot: output))
{
    for (var i = 0; i < 10; i++) await native.AdvanceAsync(new(Y: 1));
    Check(native.Shots.Count > 0 && native.Shots.All(s => s.Count == 4), "Reopened factory root loads and executes the original helper-generated native attacks");
    Check(native.Entities.Single().Phase == "source factory" && native.Entities.Single().X != 64.5, "Generated phase and source-bound Follow execute in the actual native encounter");
    native.ExportTrace(Path.Combine(output, "factory-trace.json"));
}
await using (var native = await CompiledRuntime.StartAsync(server, project with { Source = shadowed.Source, Identities = shadowed.Identities }, artifactRoot: output))
{
    var startX = native.Entities.Single().X;
    for (var i = 0; i < 10; i++) await native.AdvanceAsync(new(Y: 1));
    Check(native.Shots.Count == 0 && native.Entities.Single().X != startX, "Aliased Shoot source executes native Follow movement without substituting a Shoot attack");
}
Check(File.ReadAllText(file) == original, "Source-context editing and compiled execution leave the original game file unchanged");
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { checks }, BehaviorDocument.JsonOptions));
Console.WriteLine($"{checks.Count} source-context checks passed.");
