using System.Text.Json;
using Studio.Core;
using Studio.Raple;

var adapter = new RapleAdapter(Path.GetFullPath(args.FirstOrDefault() ?? "../raple-realms-7-server"));
var output = Path.GetFullPath(".artifacts/fragments"); Directory.CreateDirectory(output); var checks = new List<string>();
void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); Console.WriteLine("PASS " + label); }
void Reject(Action action, string label) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
BehaviorDocument Fixture(string entity, string children) => new("class Fixture { void Build() { db.Init(" + JsonSerializer.Serialize(entity) + ", new State(" + children + ")); } }", adapter);
BehaviorNode Root(BehaviorDocument d) => d.Entities.Single().Root;
const string originEntity = "Cave Pirate Brawler";
var origin = Fixture(originEntity, "new State(\"opening\", new Shoot(10, count: 2, coolDown: 1000), new Order(8, \"Cave Pirate Brawler\", \"loop\"), new TimedTransition(500, \"finish\")), new State(\"loop\", new TimedTransition(500, \"opening\")), new State(\"finish\")");
var opening = Root(origin).StateChildren[0]; var loop = Root(origin).StateChildren[1]; var shoot = opening.Children.Single(n => n.Kind == "Shoot");
var template = origin.CaptureFragment([opening.Id, loop.Id, shoot.Id], "Two-phase fan", "Native self orders plus an external finish phase", [new(shoot.Id, "count", "shots")]);
Check(template.Contents.Expressions.Count == 2 && template.Parameters.Single().Value.DefaultExpression == "2", "Fragment capture normalizes nested selections and exposes an exact constructor argument");
Check(template.References.Any(r => r.Kind == "phase-internal" && r.Target == "opening") && template.References.Any(r => r.Kind == "phase-external" && r.Target == "finish") && template.References.Any(r => r.Kind == "self-entity"), "Fragment capture distinguishes internal phases, external phases and genuine self-directed native orders");
var path = Path.Combine(output, "two-phase-fan.rbsfrag"); template.Save(path); template = BehaviorFragment.Load(path);
Check(template.Name == "Two-phase fan" && template.SourceSha256.Length == 64 && !template.RequiresSourceContext && template.Parameters.Single().Name == "shots", "Readable local fragment files preserve metadata, provenance and template inputs");
var destination = Fixture("Ghost Ship", "new State(\"destination\"), new State(\"done\")"); var target = Root(destination).StateChildren[0]; var before = destination.Source; var originalIds = destination.Identities.Select(i => i.Id).ToArray();
var ids = destination.ImportFragment(destination.Revision, target.Id, 0, template, new Dictionary<string, string> { ["shots"] = "5" }, new Dictionary<string, string> { ["finish"] = "done" }, validate: d => adapter.Validate(d, "Ghost Ship"));
var copied = destination.Find(ids[0]);
Check(ids.Count == 2 && copied.Label == "opening-copy" && destination.Find(ids[1]).Label == "loop-copy" && copied.Children.Single(n => n.Kind == "Shoot").Parameters.Single(p => p.Name == "count").Expression == "5", "Template import binds a new value and inserts both phases with fresh names");
Check(copied.Children.Single(n => n.Kind == "Order").Parameters.Any(p => p.Expression == "\"Ghost Ship\"") && copied.Children.Single(n => n.Kind == "Order").Parameters.Any(p => p.Expression == "\"loop-copy\"") && copied.Children.Single(n => n.Kind == "TimedTransition").Parameters.Any(p => p.Expression == "\"done\""), "Cross-entity imports retarget self orders, reconcile internal copies and map external phases");
Check(destination.Identities.Where(i => originalIds.Contains(i.Id)).Count() == originalIds.Length && ids.All(id => !originalIds.Contains(id)) && destination.Revision == 1, "Fragment insertion preserves surviving identities and commits one document revision");
using (var native = new EncounterSession(adapter, destination, "Ghost Ship", 7, new(Defense: 0)))
{
    for (var i = 0; i < 15; i++) native.Step(new());
    Check(native.Shots.Any(s => s.Count == 5) && native.Events.Any(e => e.Kind == "transition" && e.Message.Contains("loop-copy")), "Imported template values and reconciled native self orders execute in the actual encounter");
}
var applied = destination.Source;
Check(destination.Undo() && destination.Source == before && destination.Redo() && destination.Source == applied, "Fragment insertion is one atomic undo/redo with preserved source");
var project = Path.Combine(output, "imported.rbs"); BehaviorDocument.Save(project, new() { EntityId = "Ghost Ship", Source = destination.Source, Identities = destination.Identities });
var saved = BehaviorDocument.Load(project); var reopened = new BehaviorDocument(saved.Source, adapter, saved.Identities);
Check(reopened.Find(ids[0]).Label == "opening-copy", "Imported fragments retain their new identities after project save/reopen");
var stable = destination.Source; var revision = destination.Revision;
Reject(() => destination.ImportFragment(revision - 1, target.Id, 0, template), "Stale fragment previews reject before any source change");
Reject(() => destination.ImportFragment(revision, target.Id, 0, template, new Dictionary<string, string> { ["shots"] = "\"bad\"" }, new Dictionary<string, string> { ["finish"] = "done" }, validate: d => adapter.Validate(d, "Ghost Ship")), "Template inputs use actual native constructor type validation");
Reject(() => destination.ImportFragment(revision, target.Id, 0, template), "Unmapped missing external phases reject before insertion");
Reject(() => destination.ImportFragment(revision, target.Id, 0, template, new Dictionary<string, string> { ["unknown"] = "1" }), "Unknown template inputs reject instead of being silently ignored");
destination.LockedNodes.Add(target.Id);
Reject(() => destination.ImportFragment(revision, target.Id, 0, template, phaseTargets: new Dictionary<string, string> { ["finish"] = "done" }), "Locked destination regions reject fragment imports"); destination.LockedNodes.Clear();
Check(destination.Source == stable && destination.Revision == revision, "Failed fragment imports leave source, revision and undo history unchanged");
Reject(() => (template with { Version = 99 }).Validate(), "Unsupported fragment versions reject");
Reject(() => (template with { Parameters = [template.Parameters.Single() with { Value = template.Parameters.Single().Value with { Start = -1 } }] }).Validate(), "Malformed template source spans reject");
Reject(() => origin.CaptureFragment([opening.Id], "Duplicate inputs", bindings: [new(shoot.Id, "count", "shots"), new(shoot.Id, "count", "shots")]), "Duplicate or overlapping template input bindings reject");
Reject(() => origin.CaptureFragment([opening.Id], "Phase input", bindings: [new(opening.Id, "name", "phase")]), "Phase names use reconciliation rather than untracked template substitutions");
var foreign = Fixture(originEntity, "new State(\"owned\", new Order(8, \"Ghost Ship\", \"foreign phase\"))");
var foreignFragment = foreign.CaptureFragment([Root(foreign).StateChildren.Single().Id], "Foreign order");
Check(foreignFragment.References.Count == 0, "Orders directed at another entity remain foreign dependencies and are not remapped as self orders");
var file = Path.Combine(adapter.ServerRoot, "TKR.WorldServer/logic/db/BehaviorDb.PirateCave.cs"); var original = File.ReadAllText(file); var sourceDoc = new BehaviorDocument(original, adapter); var sourceRoot = sourceDoc.Entities.Single(e => e.EntityId == originEntity).Root;
const string helpers = "\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static TKR.WorldServer.logic.behaviors.Shoot StudioFragmentFan(int count = 3) => new TKR.WorldServer.logic.behaviors.Shoot(10, count: count, coolDown: 1000); } }";
var helperSource = original.Remove(sourceRoot.Start, sourceRoot.Length).Insert(sourceRoot.Start, "new State(new State(\"helper phase\", StudioFragmentFan(3)), new State(\"destination\"))") + helpers;
var helperDoc = new BehaviorDocument(helperSource, adapter, sourceFile: file); var helperRoot = helperDoc.Entities.Single(e => e.EntityId == originEntity).Root; var factory = helperRoot.Descendants().Single(n => n.Kind == "Factory");
var helperFragment = helperDoc.CaptureFragment([factory.Id], "Bound helper", bindings: [new(factory.Id, "count", "shots")]);
Check(helperFragment.RequiresSourceContext && helperFragment.Contents.Expressions.Single() == "StudioFragmentFan(3)", "Source-helper fragments retain their exact dependency expression and visible context requirement");
var orderHelpers = "\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { const string StudioFragmentEntity = \"Cave Pirate Brawler\"; static string StudioFragmentUnknown { get { throw new System.InvalidOperationException(\"fragment inspection must not execute this getter\"); } } } }";
var boundOrders = new BehaviorDocument("using FragmentOrder = TKR.WorldServer.logic.behaviors.Order;\n" + original.Remove(sourceRoot.Start, sourceRoot.Length).Insert(sourceRoot.Start, "new State(new State(\"self\", new FragmentOrder(8, StudioFragmentEntity, \"self\"), new TKR.WorldServer.logic.behaviors.OrderOnDeath(8, StudioFragmentEntity, \"self\")))") + orderHelpers, adapter, sourceFile: file);
var orderPhase = boundOrders.Entities.Single(e => e.EntityId == originEntity).Root.StateChildren.Single();
var orderFragment = boundOrders.CaptureFragment([orderPhase.Id], "Compiler-bound orders");
Check(orderFragment.References.Count(r => r.Kind == "self-entity" && r.Value.DefaultExpression == "StudioFragmentEntity") == 2, "Compiler-bound aliases, constant entity IDs and death orders identify their genuine retargetable entity parameters");
var unresolvedOrders = new BehaviorDocument(original.Remove(sourceRoot.Start, sourceRoot.Length).Insert(sourceRoot.Start, "new State(new State(\"self\", new Order(8, StudioFragmentUnknown, \"self\")))") + orderHelpers, adapter, sourceFile: file);
var unresolvedFragment = unresolvedOrders.CaptureFragment([unresolvedOrders.Entities.Single(e => e.EntityId == originEntity).Root.StateChildren.Single().Id], "Unknown owner");
Check(unresolvedFragment.References.Any(r => r.Kind == "unresolved-phase") && !unresolvedFragment.References.Any(r => r.Kind == "self-entity"), "Fragment ownership inspection preserves an unknown getter boundary without executing the getter");
var helperDestination = helperRoot.StateChildren.Single(n => n.Label == "destination");
helperDoc.ImportFragment(helperDoc.Revision, helperDestination.Id, 0, helperFragment, new Dictionary<string, string> { ["shots"] = "6" }, validate: d => adapter.Validate(d, originEntity));
Check(helperDoc.Find(helperDestination.Id).Children.Single().Parameters.Single().Expression == "6" && SourceVerification.Check(adapter.ServerRoot, file, helperDoc.Source).Accepted, "Destination-scoped helper template arguments pass the complete original compiler");
var compiledProject = new StudioProject { EntityId = originEntity, SourceFile = file, Source = helperDoc.Source };
await using (var native = await CompiledRuntime.StartAsync(adapter.ServerRoot, compiledProject, "destination", artifactRoot: Path.Combine(output, "runtime")))
{ await native.AdvanceAsync(new(), 100); Check(native.Shots.Any(s => s.Count == 6), "Imported source-helper template inputs execute in the actual compiled original runtime"); }
var noHelper = new BehaviorDocument(original.Remove(sourceRoot.Start, sourceRoot.Length).Insert(sourceRoot.Start, "new State(new State(\"destination\"))"), adapter, sourceFile: file); var missingBefore = noHelper.Source;
Reject(() => noHelper.ImportFragment(noHelper.Revision, noHelper.Entities.Single(e => e.EntityId == originEntity).Root.StateChildren.Single().Id, 0, helperFragment), "Missing helper dependencies reject in the destination compiler without copying unrelated source");
Check(noHelper.Source == missingBefore && File.ReadAllText(file) == original, "Dependency failures remain atomic and fragment workflows leave the supplied game source untouched");
var defaultSource = original.Remove(sourceRoot.Start, sourceRoot.Length).Insert(sourceRoot.Start, "new State(new State(\"defaults\", StudioFragmentFan(StudioFragmentDefaultCount())), new State(\"destination\"))") + helpers + "\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static int StudioFragmentDefaultCount() { throw new System.InvalidOperationException(\"default source values must not run during authoring\"); } } }";
var defaultDoc = new BehaviorDocument(defaultSource, adapter, sourceFile: file); var defaultRoot = defaultDoc.Entities.Single(e => e.EntityId == originEntity).Root; var defaultFactory = defaultRoot.Descendants().Single(n => n.Kind == "Factory");
var defaultTemplate = defaultDoc.CaptureFragment([defaultFactory.Id], "Source default", bindings: [new(defaultFactory.Id, "count", "shots")]); var defaultTarget = defaultRoot.StateChildren.Single(n => n.Label == "destination");
defaultDoc.ImportFragment(defaultDoc.Revision, defaultTarget.Id, 0, defaultTemplate);
Check(defaultDoc.Find(defaultTarget.Id).Children.Single().Parameters.Single().Expression == "StudioFragmentDefaultCount()", "Unchanged template defaults retain original source expressions without executing their methods during authoring");
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { checks }, BehaviorDocument.JsonOptions));
Console.WriteLine($"{checks.Count} reusable fragment checks passed.");
