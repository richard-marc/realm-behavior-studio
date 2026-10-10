using Studio.Core;
using Studio.Raple;
using System.Text.Json;

var root = Path.GetFullPath(args.FirstOrDefault() ?? "../raple-realms-7-server");
var adapter = new RapleAdapter(root);
var checks = new List<string>();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks.Add(message); Console.WriteLine("PASS " + message); }
void Reject(Action action, string message) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { Check(true, message); return; } throw new Exception("Expected rejection: " + message); }
var file = Path.Combine(root, "TKR.WorldServer/logic/db/BehaviorDb.PirateCave.cs");
var original = File.ReadAllText(file);
var doc = new BehaviorDocument(original, adapter);
Check(doc.Entities.Any(e => e.EntityId == "Cave Pirate Brawler"), "Import real C# behavior file");
Check(doc.Source == original, "Lossless full-source import/export");
var shoot = doc.Entities.Single(e => e.EntityId == "Cave Pirate Brawler").Root.Descendants().Single(n => n.Kind == "Shoot");
var patch = new DocumentPatch(doc.Revision, "Wider fan", [new(shoot.Id, "count", "5")]);
doc.Apply(patch, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(doc.Find(shoot.Id).Parameters.Single(p => p.Name == "count").Expression == "5", "Typed visual edit keeps node identity");
Reject(() => doc.Apply(patch), "Stale proposal rejected");
Check(doc.Undo() && doc.Source == original && doc.Redo(), "Atomic undo/redo");
var lastValid = doc.Source;
Reject(() => doc.ReplaceSource("new State("), "Invalid source rejected");
Check(doc.Source == lastValid, "Invalid source keeps last valid document");
Reject(() => doc.Apply(new(doc.Revision, "Invalid count", [new(shoot.Id, "count", "-1")]), d => adapter.Validate(d, "Cave Pirate Brawler")), "Invalid parameter rejected");
doc.LockedNodes.Add(shoot.Id);
Reject(() => doc.Apply(new(doc.Revision, "Locked", [new(shoot.Id, "count", "6")])), "Locked region rejected");
doc.LockedNodes.Clear();
var outDir = Path.GetFullPath(".artifacts/verification"); Directory.CreateDirectory(outDir);
var projectFile = Path.Combine(outDir, "roundtrip.rbs");
BehaviorDocument.Save(projectFile, new StudioProject { Source = doc.Source, SourceFile = file, Identities = doc.Identities });
var p = BehaviorDocument.Load(projectFile); var reopened = new BehaviorDocument(p.Source, adapter, p.Identities);
Check(reopened.Source == doc.Source && reopened.Find(shoot.Id).Id == shoot.Id, "Save/reopen retains edits and identities");
var watch = System.Diagnostics.Stopwatch.StartNew();
var session = new EncounterSession(adapter, doc, "Cave Pirate Brawler", 7, new(1000, 50, 0));
for (var i = 0; i < 100; i++) session.Step(new());
Check(session.Events.Any(e => e.Kind == "attack"), "Real executor emits edited attack packets");
Check(session.Events.Any(e => e.Message.Contains("5 ×")), "Simulation uses the shared edited model");
session.ExportTrace(Path.Combine(outDir, "edited-trace.json"));
Check(session.PlayerHp < 1000 && session.Events.Any(e => e.Kind == "hit"), "Damageable test player uses original defense helper");
var repeat = new EncounterSession(adapter, doc, "Cave Pirate Brawler", 7, new(1000, 50, 0));
for (var i = 0; i < 100; i++) repeat.Step(new());
Check(JsonSerializer.Serialize(session.Events) == JsonSerializer.Serialize(repeat.Events), "Seeded stationary replay repeats event trace");
var warmMs = repeat.StartMs;
Check(new EncounterSession(adapter, doc, "Cave Pirate Brawler", 7, new(1000, 50, 0), phase: "that").Events.Any(e => e.Kind == "forced-phase"), "Forced-phase mode explicitly labeled");
var unsupported = new BehaviorDocument(original, adapter);
Reject(() => new EncounterSession(adapter, unsupported, "Dreadstump the Pirate King", 1, new()), "Unsupported opaque behavior blocks simulation");
Check(unsupported.Source == original, "Unsupported content preserved");
var originalDoc = new BehaviorDocument(original, adapter);
var interpreted = new EncounterSession(adapter, originalDoc, "Cave Pirate Brawler", 7, new(1000, 50, 0));
for (var i = 0; i < 100; i++) interpreted.Step(new());
var compiled = new EncounterSession(adapter, originalDoc, "Cave Pirate Brawler", 7, new(1000, 50, 0), originalRuntimeReference: true);
for (var i = 0; i < 100; i++) compiled.Step(new());
Check(JsonSerializer.Serialize(interpreted.Shots) == JsonSerializer.Serialize(compiled.Shots), "Differential: imported tree matches independent compiled production tree emissions exactly");
compiled.ExportTrace(Path.Combine(outDir, "compiled-original-trace.json")); interpreted.ExportTrace(Path.Combine(outDir, "imported-original-trace.json"));
var referencePath = ".artifacts/reference-brawler/result.json";
if (File.Exists(referencePath))
{
    using var reference = JsonDocument.Parse(File.ReadAllText(referencePath)); var shots = reference.RootElement.GetProperty("shots").EnumerateArray().ToArray();
    Check(shots.Length == interpreted.Shots.Count && shots.Zip(interpreted.Shots).All(pair =>
        pair.First.GetProperty("t").GetInt64() == pair.Second.Ms && pair.First.GetProperty("n").GetInt32() == pair.Second.Count &&
        pair.First.GetProperty("damage").GetInt32() == pair.Second.Damage && Math.Abs(pair.First.GetProperty("x").GetDouble() - pair.Second.X) <= .00051 &&
        Math.Abs(pair.First.GetProperty("y").GetDouble() - pair.Second.Y) <= .00051 && Math.Abs(pair.First.GetProperty("angleDeg").GetDouble() - pair.Second.Angle * 180 / Math.PI) <= .00051),
        "Differential: captured original .NET 8 harness matches .NET 10 Studio (rounded report tolerance 0.00051 tiles/degrees)");
}
var acceptance = new BehaviorDocument(original, adapter);
var acceptanceRoot = acceptance.Entities.Single(e => e.EntityId == "Cave Pirate Brawler").Root;
var fragment = File.ReadAllText("examples/three-phase.fragment.cs.txt");
acceptance.ReplaceSource(acceptance.Source.Remove(acceptanceRoot.Start, acceptanceRoot.Length).Insert(acceptanceRoot.Start, fragment));
var scenario = new EncounterSession(adapter, acceptance, "Cave Pirate Brawler", 11, new(1000, 50, 0));
for (var i = 0; i < 5; i++) scenario.Step(new());
scenario.SetBossHpFraction(.7); scenario.Step(new());
Check(!scenario.Events.Any(e => e.Kind == "transition" && e.Message.EndsWith("→ helpers")), "HP threshold preserves strict less-than at 70%");
scenario.SetBossHpFraction(.6); for (var i = 0; i < 5; i++) scenario.Step(new());
Check(scenario.Events.Any(e => e.Kind == "transition" && e.Message.EndsWith("→ helpers")) && scenario.Entities.Count == 3, "Three-phase acceptance: health transition and source-based minion spawning");
scenario.SetBossHpFraction(.3); for (var i = 0; i < 5; i++) scenario.Step(new());
Check(scenario.Events.Any(e => e.Kind == "transition" && e.Message.EndsWith("→ waiting-minions")), "Nested finale phase inherits parent attack");
scenario.RemoveMinionsForTest(); for (var i = 0; i < 5; i++) scenario.Step(new());
Check(scenario.Events.Any(e => e.Kind == "transition" && e.Message.EndsWith("→ last-stand")), "Minion-related transition uses original runtime condition");
scenario.ExportTrace(Path.Combine(outDir, "acceptance-trace.json"));
BehaviorDocument.Save(Path.Combine(outDir, "three-phase.rbs"), new StudioProject { SourceFile = file, Source = acceptance.Source, Identities = acceptance.Identities, EntityId = "Cave Pirate Brawler", Seed = 11,
    Player = new(1000, 50, 0), Brief = "Three HP phases: approach with aimed fans; helpers below 70%; rotating finale below 35%; extra attacks after nearby helpers are removed. Health forced for tests; complete player mechanics remain pending." });
var movement = new List<object>();
foreach (var speed in new[] { 20, 75 }) foreach (var defense in new[] { 0, 40 })
{
    var run = new EncounterSession(adapter, acceptance, "Cave Pirate Brawler", 11, new(1000, speed, defense));
    for (var i = 0; i < 100; i++) run.Step(new(i < 50 ? 0 : -1, i < 50 ? 1 : 0));
    var expected = run.Inputs.ToArray(); var rerun = new EncounterSession(adapter, acceptance, "Cave Pirate Brawler", 11, new(1000, speed, defense));
    foreach (var input in expected) rerun.Step(input.Input);
    Check(JsonSerializer.Serialize(run.Shots) == JsonSerializer.Serialize(rerun.Shots) && run.PlayerHp == rerun.PlayerHp, $"Recorded path repeats at speed {speed}, defense {defense}");
    movement.Add(new { speed, defense, hp = run.PlayerHp, shots = run.Shots.Count });
}
File.WriteAllText(Path.Combine(outDir, "player-scenarios.json"), JsonSerializer.Serialize(movement, BehaviorDocument.JsonOptions));
var phaseNode = doc.Entities.Single(e => e.EntityId == "Cave Pirate Brawler").Root.Children.Single();
doc.LockedNodes.Add(phaseNode.Id);
Reject(() => doc.Apply(new(doc.Revision, "Locked ancestor", [new(shoot.Id, "count", "6")])), "Lock on ancestor protects descendant parameter");
doc.LockedNodes.Clear();
Reject(() => doc.Apply(new(doc.Revision, "Wrong type", [new(shoot.Id, "count", "1.5")]), d => adapter.Validate(d, "Cave Pirate Brawler")), "Fractional integer parameter rejected");
Reject(() => doc.Apply(new(doc.Revision, "Opaque identifier", [new(shoot.Id, "count", "missingSymbol")])), "Visual edit cannot invent an identifier");
var optionalDoc = new BehaviorDocument(original, adapter, originalDoc.Identities);
var optionalShot = optionalDoc.Entities.Single(e => e.EntityId == "Cave Pirate Brawler").Root.Descendants().Single(n => n.Kind == "Shoot");
optionalDoc.Apply(new(0, "Optional pattern parameters", [new(optionalShot.Id, "shootAngle", "12"), new(optionalShot.Id, "rotateAngle", "15")]), d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(optionalDoc.Find(optionalShot.Id).Parameters.Any(p => p.Name == "rotateAngle" && p.Expression == "15"), "Optional constructor parameters can be added in one atomic edit");
var verification = SourceVerification.Check(root, file, doc.Source);
File.WriteAllText(Path.Combine(outDir, "export-compile.json"), JsonSerializer.Serialize(verification, BehaviorDocument.JsonOptions));
Check(verification.Accepted, "Edited export accepted by original server C# compiler: " + string.Join("\n", verification.Errors.Take(5)));
Check(!SourceVerification.Check(root, file, doc.Source + "\ninternal sealed class OriginalTargetGuard { private System.Threading.Lock? gate; }").Accepted, "Export gate rejects APIs unavailable on the original .NET 8 target");
File.WriteAllText(Path.Combine(outDir, "results.json"), JsonSerializer.Serialize(new { checks, warmStartMs = warmMs, coldStartMs = session.StartMs,
    resourceSha256 = SourceVerification.Fingerprint(Path.Combine(root, "TKR.Shared/resources/xml")), fidelity = "Enemy executor reused; player/collision proxy is not full-client verified." }, BehaviorDocument.JsonOptions));
Console.WriteLine($"{checks.Count} checks passed. Warm start: {warmMs:F1} ms. Results: {outDir}");
