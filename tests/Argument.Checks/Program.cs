using System.Reflection;
using System.Text.Json;
using Studio.Core;
using Studio.Raple;
using TKR.Shared.resources;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.logic;

var adapter = new RapleAdapter(Path.GetFullPath(args.FirstOrDefault() ?? "../raple-realms-7-server"));
var file = Path.Combine(adapter.ServerRoot, "TKR.WorldServer/logic/db/BehaviorDb.PirateCave.cs"); var source = File.ReadAllText(file);
var checks = new List<string>();
void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); Console.WriteLine("PASS " + label); }
void Reject(Action action, string label) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
BehaviorNode Root(BehaviorDocument d) => d.Entities.Single(e => e.EntityId == "Cave Pirate Brawler").Root;
BehaviorDocument Doc(string expression) { var d = new BehaviorDocument(source, adapter); var root = Root(d); d.ReplaceSource(d.Source.Remove(root.Start, root.Length).Insert(root.Start, expression)); return d; }
IEnumerable<DocumentDiagnostic> Validate(BehaviorDocument d) => adapter.Validate(d, "Cave Pirate Brawler");
string Ids(BehaviorDocument d) => string.Join(",", d.Identities.Select(i => i.Id).Order());
const string fixture = """
new State(new State("opening",
    new Prioritize(children: new CycleBehavior[] {
        new Follow(speed: .3), // retained cycle comment
        new Wander(speed: .1) }),
    new Timed(period: 500, behaviors: new Behavior[] { new Shoot(8) })),
    new State("closing", new Wander(.2)))
""";
var doc = Doc(fixture); var imported = doc.Source; var groups = Root(doc).Descendants().Where(n => n.Kind == "ArgumentGroup").ToArray();
var cycle = groups.Single(g => g.Argument!.ElementType == "CycleBehavior"); var timedGroup = groups.Single(g => g.Argument!.ElementType == "Behavior");
Check(doc.Source == imported && cycle.Children.Select(n => n.Kind).SequenceEqual(["Follow", "Wander"]) && timedGroup.Children.Single().Kind == "Shoot", "Explicit native arrays import losslessly as typed child lists");
Check(!Validate(doc).Any(d => d.IsError || d.Message.Contains("ArgumentGroup")), "Argument-list view nodes do not become fake runtime primitives or errors");
Check(doc.CanEditChildren(cycle.Id) && doc.ChildContainer(doc.Parent(cycle.Id)!.Id).Id == cycle.Id, "Native constructor destination resolves to its existing normal-array slot");
var follow = cycle.Children[0]; var wander = cycle.Children[1]; var initialIds = Ids(doc);
doc.Apply(new(doc.Revision, "Edit grouped speed", [new(follow.Id, "speed", ".4")]), Validate);
Check(doc.Find(follow.Id).Parameters.Single(p => p.Name == "speed").Expression == ".4" && Ids(doc) == initialIds, "Grouped primitive parameters preserve every identity");
doc.MoveNode(wander.Id, -1, Validate);
Check(doc.Find(cycle.Id).Children.Select(n => n.Id).SequenceEqual([wander.Id, follow.Id]) && doc.Source.Contains("retained cycle comment"), "Array reorder preserves source order, IDs and separator comments");
var copied = doc.DuplicateNode(follow.Id, Validate);
Check(copied != follow.Id && doc.Find(cycle.Id).Children.Count == 3 && doc.Source.Contains("children: new CycleBehavior[]"), "Duplicate inserts into the existing named typed array");
var before = doc.Source; var beforeIds = Ids(doc); var revision = doc.Revision;
Reject(() => doc.AddChild(cycle.Id, "new Taunt(\"no\")", Validate), "CycleBehavior slots reject regular Behavior children");
Check(doc.Source == before && Ids(doc) == beforeIds && doc.Revision == revision, "Failed typed insertion leaves source, identity layout and revision unchanged");
Reject(() => doc.GroupNodes([follow.Id, copied], "invalid-group", Validate), "Native cycle arguments cannot be replaced by an incompatible phase group");
doc.MoveNodes([copied], timedGroup.Id, 1, Validate);
Check(doc.Find(timedGroup.Id).Children.Last().Id == copied && doc.Find(cycle.Id).Children.Count == 2, "Cross-wrapper move preserves identity and accepts a derived Behavior");
var afterMove = doc.Source;
Check(doc.Undo() && doc.Find(cycle.Id).Children.Count == 3 && doc.Redo() && doc.Source == afterMove, "Cross-wrapper edit has one atomic undo/redo");
var paste = doc.PasteNodes(cycle.Id, 0, doc.CopyNodes([wander.Id]), Validate).Single();
Check(paste != wander.Id && doc.Find(cycle.Id).Children.First().Id == paste, "Grouped clipboard paste creates an independent node in the typed destination");
doc.DeleteNode(paste, Validate);
Check(doc.Find(cycle.Id).Children.Count == 2 && !Root(doc).Descendants().Any(n => n.Id == paste), "Grouped deletion removes only the selected array element");
doc.MoveNodes([follow.Id], Root(doc).Id, 1, Validate);
Check(Root(doc).Children[1].Id == follow.Id && doc.Find(cycle.Id).Children.Single().Id == wander.Id, "Nested array-to-ancestor move does not reintroduce the removed child");
Reject(() => doc.DeleteNode(wander.Id, Validate), "Prioritize cannot lose its final required native cycle");
Reject(() => doc.CopyNodes([cycle.Id]), "Argument containers are edited through contents rather than copied as behaviors");
doc.LockedNodes.Add(cycle.Id);
Reject(() => doc.DeleteNode(wander.Id, Validate), "Argument-list locks protect descendants");
doc.LockedNodes.Clear();
var narrow = Doc("new State(new Prioritize(new[] { new Follow(.1), new Follow(.2) }))"); var narrowGroup = Root(narrow).Descendants().Single(n => n.Kind == "ArgumentGroup");
Reject(() => narrow.AddChild(narrowGroup.Id, "new Wander(.3)", Validate), "Inferred Follow arrays retain their narrower native element type");
var stateArray = Doc("new State(children: new IStateChildren[] { new State(\"a\", new TimedTransition(100, \"b\")), new State(\"b\", new Shoot(8)) })");
var stateGroup = Root(stateArray).Children.Single(); var a = stateGroup.Children[0]; var b = stateGroup.Children[1];
Check(BehaviorAnalysis.Analyze(Root(stateArray)).Transitions.Single().OwnerId == a.Id && !BehaviorAnalysis.Analyze(Root(stateArray)).Diagnostics.Any(d => d.Message.Contains("incomplete")), "Phase analysis sees native array-held phases and transitions");
stateArray.RenameState(b.Id, "renamed", Validate);
Check(stateArray.Find(a.Id).Children.Single().Parameters.Single(p => p.Name == "targetState").Expression == "\"renamed\"", "Array-held phase rename updates actual inbound transition references");
var grouped = stateArray.GroupNodes([a.Id, b.Id], "pack", Validate);
Check(adapter.BuildState(stateArray, "Cave Pirate Brawler").States.Single().States.Select(s => s.Name).SequenceEqual(["a", "renamed"]), "Grouping array-held phases authors the actual native State hierarchy");
stateArray.UngroupState(grouped, Validate);
Check(stateArray.Find(stateGroup.Id).Children.Select(n => n.Id).SequenceEqual([a.Id, b.Id]), "Ungroup promotes array-held phases while retaining identities");
var duplicate = stateArray.DuplicateNode(a.Id, Validate);
Check(stateArray.StateName(duplicate) == "a-copy" && stateArray.Find(duplicate).Children.Single().Parameters.Single(p => p.Name == "targetState").Expression == "\"renamed\"", "Array-held branch copies reconcile names and preserve external targets");
stateArray.DeleteNode(b.Id, Validate);
Check(!Root(stateArray).Descendants().Any(n => n.Kind.EndsWith("Transition")), "Deleting an array-held phase removes all incoming literal transitions atomically");
var empty = Doc("new State(new[] { new State(\"only\") })"); var emptyGroup = Root(empty).Children.Single(); empty.DeleteNode(emptyGroup.Children.Single().Id, Validate);
Check(empty.Find(emptyGroup.Id).Children.Count == 0 && adapter.BuildState(empty, "Cave Pirate Brawler").States.Count == 0 && empty.Source.Contains("global::TKR.WorldServer.logic.State[]"), "Removing the last inferred-array element preserves its actual native type explicitly");
var single = Doc("new State(new OnDeathBehavior(behavior: new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable)))"); var wrapper = Root(single).Children.Single(); var effect = wrapper.Children.Single();
Reject(() => single.DuplicateNode(effect.Id, Validate), "Scalar callback wrapper cannot receive a second child");
Reject(() => single.DeleteNode(effect.Id, Validate), "Required scalar callback wrapper cannot lose its child");
Check(adapter.EffectiveCallbacks(single, effect).Select(c => c.Stage).SequenceEqual(["Death"]), "Death wrapper dispatches its child's entry callback at death without forwarding exit or ongoing");
var timed = Doc("new State(new Timed(300, new Shoot(8), new Wander(.1)))");
var session = new EncounterSession(adapter, timed, "Cave Pirate Brawler", 7, new());
var boss = (Enemy)typeof(EncounterSession).GetField("boss", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
session.Step(new());
var nativeTimed = boss.CurrentState.Behaviors.Single();
Check((int)boss.StateStorage[nativeTimed] == 100 && session.Shots.Count > 0, "Native Timed executes children and decrements its period once per child");
session.Step(new());
Check((int)boss.StateStorage[nativeTimed] == 200, "Native Timed retains its real within-loop reset behavior");
session = new EncounterSession(adapter, single, "Cave Pirate Brawler", 7, new());
boss = (Enemy)typeof(EncounterSession).GetField("boss", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
Check(!boss.HasConditionEffect(ConditionEffectIndex.Invulnerable), "Death child entry is not executed during encounter entry");
session.SetBossHpFraction(.001);
for (var i = 0; i < 15; i++) session.Step(new(Attack: true, AimX: 64.5, AimY: 64.5));
Check(session.Entities.Count == 0 && boss.HasConditionEffect(ConditionEffectIndex.Invulnerable), "Native weapon death invokes the wrapped child's actual entry callback");
var contextual = Doc("new State(new State(\"a\", new Follow(tuning.Speed)), new State(\"b\"))"); var contextualPhase = Root(contextual).Children[0];
contextual.MoveNode(contextualPhase.Id, 1, Validate);
Check(Root(contextual).Children.Last().Id == contextualPhase.Id && contextual.Source.Contains("tuning.Speed"), "Moving a known phase retains unresolved source-context values without guessing them");
var opaque = Doc("new State(new Prioritize(new CycleBehavior[] { MakeCycle(), new Wander(.1) }))"); var opaqueGroup = Root(opaque).Descendants().Single(n => n.Kind == "ArgumentGroup"); var visible = opaqueGroup.Children.Single(n => n.Kind == "Wander");
opaque.MoveNode(visible.Id, -1, Validate);
Check(opaque.Find(opaqueGroup.Id).Children.First().Id == visible.Id && opaque.Source.Contains("MakeCycle()") && !Validate(opaque).Any(d => d.IsError), "Typed-array edits preserve opaque factory expressions in their source order");
Reject(() => adapter.BuildState(opaque, "Cave Pirate Brawler"), "Unresolved array factories remain explicitly unavailable to the local interpreter");
var legacy = Doc(fixture); var legacyGroup = Root(legacy).Descendants().First(n => n.Kind == "ArgumentGroup");
var legacyIds = legacy.Identities.Where(i => !i.Path.StartsWith(legacyGroup.Path + "/", StringComparison.Ordinal)).Select(i => i.Id == legacyGroup.Id ? i with { Kind = "Opaque" } : i).ToArray();
var upgraded = new BehaviorDocument(legacy.Source, adapter, legacyIds); upgraded.LockedNodes.Add(legacyGroup.Id);
Check(upgraded.Find(legacyGroup.Id).Kind == "ArgumentGroup", "Former opaque-array identities upgrade into their typed argument container");
Reject(() => upgraded.DeleteNode(upgraded.Find(legacyGroup.Id).Children.Last().Id, Validate), "A restored legacy opaque-region lock protects the newly visible children");
var escaped = Doc("new @State(new @Shoot(8))");
Check(adapter.BuildState(escaped, "Cave Pirate Brawler").Behaviors.Single().GetType().Name == "Shoot", "Escaped native type identifiers import and execute using their actual C# names");
var keyword = Doc("new State(new[] { new global::TKR.WorldServer.logic.behaviors.@new.OrderedBehavior() })"); var keywordGroup = Root(keyword).Children.Single();
keyword.DeleteNode(keywordGroup.Children.Single().Id, Validate);
Check(keyword.Source.Contains("behaviors.@new.OrderedBehavior[]") && !Validate(keyword).Any(d => d.IsError), "Empty inferred arrays preserve escaped native namespace identifiers");
var artifact = Path.GetFullPath(".artifacts/arguments"); Directory.CreateDirectory(artifact);
var project = Path.Combine(artifact, "grouped.rbs"); BehaviorDocument.Save(project, new StudioProject { Source = doc.Source, Identities = doc.Identities }); var saved = BehaviorDocument.Load(project); var reopened = new BehaviorDocument(saved.Source, adapter, saved.Identities);
Check(reopened.Source == doc.Source && Ids(reopened) == Ids(doc) && reopened.Find(cycle.Id).Argument!.ElementType == "CycleBehavior", "Typed arrays and identities survive project save/reopen");
Check(SourceVerification.Check(adapter.ServerRoot, file, doc.Source).Accepted && SourceVerification.Check(adapter.ServerRoot, file, stateArray.Source).Accepted && SourceVerification.Check(adapter.ServerRoot, file, single.Source).Accepted, "Grouped moves, phase edits and callback wrappers compile against the original complete server");
File.WriteAllText(Path.Combine(artifact, "results.json"), JsonSerializer.Serialize(new { checks }, BehaviorDocument.JsonOptions));
Console.WriteLine($"{checks.Count} grouped argument checks passed.");
