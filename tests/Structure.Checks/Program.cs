using Studio.Core;
using Studio.Raple;
using System.Text.Json;

var adapter = new RapleAdapter(Path.GetFullPath(args.FirstOrDefault() ?? "../raple-realms-7-server"));
var file = Path.Combine(adapter.ServerRoot, "TKR.WorldServer/logic/db/BehaviorDb.PirateCave.cs");
var original = File.ReadAllText(file); var checks = new List<string>();
void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); Console.WriteLine("PASS " + label); }
void Reject(Action action, string label) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
const string fragment = """
new State(
    new State("opening",
        new Shoot(radius: 10, count: 2, coolDown: 1000),
        new Shoot(radius: 10, count: 2, coolDown: 1000),
        new TimedTransition(time: 200, targetState: @"loop"),
        new TimedTransition(time: 500, targetState: "inner")
    ),
    new State("loop",
        new State("inner",
            new Wander(speed: 0.2),
            new TimedTransition(time: 500, targetState: "opening")
        ),
        new TimedTransition(time: 500, targetState: "inner")
    )
)
""";
BehaviorDocument Fresh()
{
    var document = new BehaviorDocument(original, adapter);
    var root = document.Entities.Single(e => e.EntityId == "Cave Pirate Brawler").Root;
    document.ReplaceSource(document.Source.Remove(root.Start, root.Length).Insert(root.Start, fragment)); return document;
}
BehaviorNode Root(BehaviorDocument d) => d.Entities.Single(e => e.EntityId == "Cave Pirate Brawler").Root;
BehaviorNode Phase(BehaviorDocument d, string name) => Root(d).Descendants().Single(n => n.Kind == "State" && d.StateName(n.Id) == name);
IEnumerable<DocumentDiagnostic> Validate(BehaviorDocument d) => adapter.Validate(d, "Cave Pirate Brawler");
string IdentitySet(BehaviorDocument d) => string.Join(",", d.Identities.Select(i => i.Id).Order());
var renamed = Fresh(); var opening = Phase(renamed, "opening"); var ids = IdentitySet(renamed);
var otherEntity = renamed.Entities.First(e => e.EntityId != "Cave Pirate Brawler"); var otherExpression = renamed.Expression(otherEntity.Root);
renamed.RenameState(opening.Id, "phase \"two\" \\ path", Validate);
Check(renamed.Find(opening.Id).Label == "phase \"two\" \\ path" && Root(renamed).Descendants().SelectMany(n => n.Parameters).Any(p => p.Name == "targetState" && BehaviorDocument.LiteralString(p.Expression) == "phase \"two\" \\ path"), "Rename updates escaped phase names and inbound references");
Check(ids == IdentitySet(renamed) && renamed.Expression(renamed.Find(otherEntity.Root.Id)) == otherExpression, "Rename retains all identities and unrelated entity source");
var before = renamed.Source; var revision = renamed.Revision;
Reject(() => renamed.RenameState(opening.Id, "loop", Validate), "Duplicate phase name is rejected");
Reject(() => renamed.RenameState(opening.Id, " ", Validate), "Empty phase name is rejected");
Check(renamed.Source == before && renamed.Revision == revision, "Rejected renames are atomic");
var inbound = Root(renamed).Descendants().Single(n => n.Parameters.Any(p => p.Name == "targetState" && BehaviorDocument.LiteralString(p.Expression) == "phase \"two\" \\ path"));
renamed.LockedNodes.Add(inbound.Id);
Reject(() => renamed.RenameState(opening.Id, "changed", Validate), "Locked inbound transition blocks reference rewriting");
renamed.LockedNodes.Clear();
renamed.Apply(new(renamed.Revision, "Rename", [new(opening.Id, "name", "\"renamed\"")]), Validate);
Check(Root(renamed).Descendants().SelectMany(n => n.Parameters).Any(p => p.Name == "targetState" && BehaviorDocument.LiteralString(p.Expression) == "renamed"), "Generic parameter proposals use the same safe phase rename operation");
var duplicated = Fresh(); var branch = Phase(duplicated, "loop"); var originalBranchIds = branch.Descendants().Select(n => n.Id).ToHashSet(); var originalIds = duplicated.Identities.Select(i => i.Id).ToHashSet();
var duplicateId = duplicated.DuplicateNode(branch.Id, Validate); var copy = duplicated.Find(duplicateId);
Check(copy.Label == "loop-copy" && copy.Descendants().Any(n => n.Label == "inner-copy") && copy.Descendants().SelectMany(n => n.Parameters).Any(p => p.Name == "targetState" && BehaviorDocument.LiteralString(p.Expression) == "inner-copy"), "Duplicate branch renames nested phases and remaps its internal transitions");
Check(copy.Descendants().SelectMany(n => n.Parameters).Any(p => p.Name == "targetState" && BehaviorDocument.LiteralString(p.Expression) == "opening"), "Duplicate preserves references to phases outside its branch");
Check(!copy.Descendants().Any(n => originalIds.Contains(n.Id)) && originalIds.IsSubsetOf(duplicated.Identities.Select(i => i.Id)) && originalBranchIds.SetEquals(duplicated.Find(branch.Id).Descendants().Select(n => n.Id)), "Copied nodes receive new identities while original nodes retain theirs");
var secondCopy = duplicated.DuplicateNode(branch.Id, Validate);
Check(duplicated.Find(secondCopy).Label == "loop-copy-2" && duplicated.Find(secondCopy).Descendants().Any(n => n.Label == "inner-copy-2"), "Repeated duplication generates unique phase names");
var actions = Fresh(); var owner = Phase(actions, "opening"); var first = owner.Children[0]; var second = owner.Children[1]; var actionIds = IdentitySet(actions);
actions.MoveNode(second.Id, -1, Validate);
Check(actions.Find(owner.Id).Children[0].Id == second.Id && actions.Find(owner.Id).Children[1].Id == first.Id && IdentitySet(actions) == actionIds, "Reordering identical actions preserves their distinct stable identities");
var copiedAction = actions.DuplicateNode(second.Id, Validate);
Check(actions.Find(owner.Id).Children.Take(3).Select(n => n.Id).SequenceEqual(new[] { second.Id, copiedAction, first.Id }), "Identical action copy is inserted next to its source without stealing identities");
actions.DeleteNode(copiedAction, Validate);
Check(actions.Find(owner.Id).Children.Take(2).Select(n => n.Id).SequenceEqual(new[] { second.Id, first.Id }), "Deleting one identical action preserves its siblings");
var moved = Fresh(); var movedOpening = Phase(moved, "opening"); moved.MoveNode(movedOpening.Id, 1, Validate);
Check(Root(moved).Children.First().Label == "loop", "Phase order changes in the shared source model");
var run = new EncounterSession(adapter, moved, "Cave Pirate Brawler", 7, new(Invulnerable: true)); run.Step(new());
Check(run.Entities.Single(e => e.Name == "Cave Pirate Brawler").Phase == "inner", "Reordered first phase changes entry in the real runtime");
var deleted = Fresh(); var deletedBefore = deleted.Source; var deletedIds = IdentitySet(deleted); var loop = Phase(deleted, "loop"); var deletedParent = deleted.DeleteNode(loop.Id, Validate);
Check(deletedParent == Root(deleted).Id && Root(deleted).Descendants().All(n => n.Label is not ("loop" or "inner") && n.Kind != "TimedTransition"), "Deleting a branch removes external transitions to every deleted nested phase");
Check(deleted.Undo() && deleted.Source == deletedBefore && IdentitySet(deleted) == deletedIds, "One undo restores the entire deleted branch, references and identities");
Check(deleted.Redo() && Root(deleted).Children.Count == 1, "Redo repeats branch deletion atomically");
var locked = Fresh(); var lockedPhase = Phase(locked, "opening"); locked.LockedNodes.Add(lockedPhase.Children[0].Id); var lockedSource = locked.Source;
Reject(() => locked.DeleteNode(lockedPhase.Id, Validate), "Locked descendant prevents deleting its phase");
Reject(() => locked.DuplicateNode(lockedPhase.Id, Validate), "Locked branch cannot be duplicated");
Reject(() => locked.MoveNode(Phase(locked, "loop").Id, -1, Validate), "Moving across a locked branch is rejected");
Check(locked.Source == lockedSource, "Failed locked operations preserve source");
Reject(() => locked.DeleteNode(Root(locked).Id, Validate), "Encounter root cannot be deleted");
Reject(() => locked.DuplicateNode(Root(locked).Id, Validate), "Encounter root cannot be duplicated");
Reject(() => locked.MoveNode(Root(locked).Id, 1, Validate), "Encounter root cannot be reordered");
var rejected = Fresh(); var rejectedSource = rejected.Source; var rejectedIds = IdentitySet(rejected); var rejectedRevision = rejected.Revision;
Reject(() => rejected.DuplicateNode(Phase(rejected, "opening").Id, _ => [new("Rejected candidate")]), "Candidate validation rejects a structural edit before committing");
Check(rejected.Source == rejectedSource && IdentitySet(rejected) == rejectedIds && rejected.Revision == rejectedRevision, "Validation failure retains the prior source, revision and identities");
var opaque = Fresh(); opaque.AddChild(Phase(opaque, "opening").Id, "new Prioritize(new Wander(speed: 0.1))");
var grouped = Root(opaque).Descendants().Single(n => n.Kind == "Prioritize");
Reject(() => opaque.DeleteNode(grouped.Children[0].Id), "Prioritize must retain at least one native cycle child");
var dynamicReference = Fresh(); dynamicReference.ReplaceSource(dynamicReference.Source.Replace("targetState: @\"loop\"", "targetState: NextPhase"));
Reject(() => dynamicReference.RenameState(Phase(dynamicReference, "loop").Id, "changed", Validate), "Dynamic phase references require source editing instead of a partial rename");
var savedDir = Path.GetFullPath(".artifacts/structure"); Directory.CreateDirectory(savedDir);
var project = Path.Combine(savedDir, "organized.rbs"); BehaviorDocument.Save(project, new StudioProject { Source = duplicated.Source, Identities = duplicated.Identities });
var loaded = BehaviorDocument.Load(project); var reopened = new BehaviorDocument(loaded.Source, adapter, loaded.Identities);
Check(reopened.Source == duplicated.Source && IdentitySet(reopened) == IdentitySet(duplicated), "Organized phases and identities survive project save/reopen");
Check(SourceVerification.Check(adapter.ServerRoot, file, renamed.Source).Accepted && SourceVerification.Check(adapter.ServerRoot, file, duplicated.Source).Accepted && SourceVerification.Check(adapter.ServerRoot, file, deleted.Source).Accepted, "Renamed, duplicated and deleted phase exports compile against the original server");
File.WriteAllText(Path.Combine(savedDir, "results.json"), JsonSerializer.Serialize(new { checks }, BehaviorDocument.JsonOptions));
Console.WriteLine($"{checks.Count} structural authoring checks passed.");
