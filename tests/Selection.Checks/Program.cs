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
        new Shoot(radius: 10, count: 2, coolDown: 1000), // retained attack comment
        new Shoot(radius: 10, count: 2, coolDown: 1000),
        new TimedTransition(time: 200, targetState: "loop")),
    new State("loop",
        new State("inner", new Wander(speed: .2), new TimedTransition(time: 500, targetState: "opening")),
        new TimedTransition(time: 500, targetState: "inner")))
""";
BehaviorDocument Fresh()
{
    var d = new BehaviorDocument(original, adapter); var root = Root(d);
    d.ReplaceSource(d.Source.Remove(root.Start, root.Length).Insert(root.Start, fragment)); return d;
}
BehaviorNode Root(BehaviorDocument d) => d.Entities.Single(e => e.EntityId == "Cave Pirate Brawler").Root;
BehaviorNode Phase(BehaviorDocument d, string name) => Root(d).Descendants().Single(n => n.Kind == "State" && d.StateName(n.Id) == name);
IEnumerable<DocumentDiagnostic> Validate(BehaviorDocument d) => adapter.Validate(d, "Cave Pirate Brawler");
string Ids(BehaviorDocument d) => string.Join(",", d.Identities.Select(i => i.Id).Order());
var moved = Fresh(); var opening = Phase(moved, "opening"); var loop = Phase(moved, "loop"); var first = opening.Children[0]; var second = opening.Children[1]; var baseline = moved.Source; var ids = Ids(moved);
moved.MoveNodes([second.Id, first.Id], loop.Id, 1, Validate);
Check(moved.Find(loop.Id).Children.Skip(1).Take(2).Select(n => n.Id).SequenceEqual(new[] { first.Id, second.Id }), "Multi-node cross-parent move preserves source order and distinct identical-action identities");
Check(moved.Find(opening.Id).Children.Count == 1 && ids == Ids(moved) && moved.Source.Contains("retained attack comment"), "Move removes source entries, retains all IDs and preserves separator comments");
Check(moved.Undo() && moved.Source == baseline && Ids(moved) == ids && moved.Redo(), "Cross-parent move is one atomic undo/redo");
var nested = Fresh(); var inner = Phase(nested, "inner"); var nestedIds = Ids(nested);
nested.MoveNodes([inner.Id], Root(nested).Id, 1, Validate);
Check(nested.Find(Root(nested).Id).Children[1].Id == inner.Id && nested.Find(Phase(nested, "loop").Id).Children.All(n => n.Id != inner.Id) && Ids(nested) == nestedIds, "Moving a nested phase to its ancestor does not restore the removed source child");
var downward = Fresh(); var downOpening = Phase(downward, "opening"); var downLoop = Phase(downward, "loop");
downward.MoveNodes([downOpening.Id], downLoop.Id, 1, Validate);
Check(downward.Find(Root(downward).Id).Children.Single().Id == downLoop.Id && downward.Find(downLoop.Id).Children[1].Id == downOpening.Id, "Moving a phase into a sibling retains its branch and literal references");
Reject(() => downward.MoveNodes([downLoop.Id], downOpening.Id, 0, Validate), "Moving a branch into its descendant is rejected");
Reject(() => downward.MoveNodes([Root(downward).Id], downOpening.Id, 0, Validate), "Root is excluded from multi-node moves");
var locked = Fresh(); var lockedOpening = Phase(locked, "opening"); var lockedLoop = Phase(locked, "loop"); locked.LockedNodes.Add(lockedLoop.Id); var lockedBefore = locked.Source;
Reject(() => locked.MoveNodes([lockedOpening.Children[0].Id], lockedLoop.Id, 0, Validate), "Locked destination rejects cross-parent movement");
locked.LockedNodes.Clear(); locked.LockedNodes.Add(lockedOpening.Children[1].Id);
Reject(() => locked.MoveNodes([lockedOpening.Children[0].Id], lockedOpening.Id, 2, Validate), "Reorder cannot cross a locked sibling");
Check(locked.Source == lockedBefore, "Rejected selection operations leave the source unchanged");
var copies = Fresh(); var copyOpening = Phase(copies, "opening"); var copyLoop = Phase(copies, "loop");
Check(copies.NormalizeSelection([copyLoop.Id, Phase(copies, "inner").Id]).Single().Id == copyLoop.Id, "Ancestor/descendant selection is normalized to one branch");
var clipboard = copies.CopyNodes([copyLoop.Id]); var encoded = JsonSerializer.Serialize(clipboard, BehaviorDocument.JsonOptions); var decoded = JsonSerializer.Deserialize<BehaviorClipboard>(encoded)!; var copyIds = Ids(copies);
var pasted = copies.PasteNodes(Root(copies).Id, 2, decoded, Validate).Single();
Check(copies.Find(pasted).Label == "loop-copy" && copies.Find(pasted).Descendants().Any(n => n.Label == "inner-copy"), "Clipboard paste generates unique nested phase names");
Check(copies.Find(pasted).Descendants().SelectMany(n => n.Parameters).Any(p => BehaviorDocument.LiteralString(p.Expression) == "inner-copy") && copies.Find(pasted).Descendants().SelectMany(n => n.Parameters).Any(p => p.Name == "targetState" && BehaviorDocument.LiteralString(p.Expression) == "opening"), "Clipboard remaps internal targets and retains valid external destinations");
Check(!copies.Find(pasted).Descendants().Any(n => copyIds.Split(',').Contains(n.Id.ToString())) && copies.Undo() && Ids(copies) == copyIds, "Pasted branches use independent identities and one undo restores the destination");
copies.PasteNodes(copyOpening.Id, 1, copies.CopyNodes([copyOpening.Children[0].Id, copyOpening.Children[1].Id]), Validate);
Check(copies.Find(copyOpening.Id).Children.Take(4).Select(n => n.Id).Distinct().Count() == 4, "Multi-action paste preserves independent identities for identical expressions");
var external = Fresh(); var externalBefore = external.Source;
Reject(() => external.PasteNodes(Root(external).Id, 0, new(1, ["new State(\"external\", new TimedTransition(100, \"absent\"))"]), Validate), "Paste with an unresolved external phase reference is rejected atomically");
Reject(() => external.PasteNodes(Root(external).Id, 0, new(2, ["new Wander(.1)"]), Validate), "Unknown clipboard version is rejected");
Reject(() => external.PasteNodes(Root(external).Id, 0, new(1, ["Execute()"]), Validate), "Clipboard rejects arbitrary invocation entries");
Check(external.Source == externalBefore, "Failed clipboard candidates do not mutate the document");
var grouped = Fresh(); var groupOpening = Phase(grouped, "opening"); var groupFirst = groupOpening.Children[0]; var groupSecond = groupOpening.Children[1]; var groupBaseline = grouped.Source; var groupIds = Ids(grouped);
var group = grouped.GroupNodes([groupSecond.Id, groupFirst.Id], "attacks", Validate);
Check(grouped.Find(group).Children.Select(n => n.Id).SequenceEqual(new[] { groupFirst.Id, groupSecond.Id }) && grouped.Find(groupOpening.Id).Children[0].Id == group, "Grouping adjacent actions creates a real nested phase with stable child IDs");
Check(grouped.Source.Contains("retained attack comment"), "Grouping retains comments belonging to selected actions");
var groupedRun = new EncounterSession(adapter, grouped, "Cave Pirate Brawler", 7, new(Invulnerable: true)); groupedRun.Step(new());
Check(groupedRun.Entities.Single(e => e.Name == "Cave Pirate Brawler").Phase == "attacks", "Grouping changes native first-child entry to the authored nested phase");
Check(grouped.Undo() && grouped.Source == groupBaseline && Ids(grouped) == groupIds && grouped.Redo(), "Phase grouping is a single undo/redo preserving original identities");
grouped.UngroupState(group, Validate);
Check(grouped.Find(groupOpening.Id).Children.Take(2).Select(n => n.Id).SequenceEqual(new[] { groupFirst.Id, groupSecond.Id }) && Ids(grouped) == groupIds && grouped.Undo(), "Ungroup promotes contents in source order, preserves identities and supports undo");
var referencedGroup = Fresh();
Reject(() => referencedGroup.UngroupState(Phase(referencedGroup, "loop").Id, Validate), "Ungroup refuses to leave dangling incoming phase transitions");
var deleted = Fresh(); var deleteOpening = Phase(deleted, "opening"); var deleteLoop = Phase(deleted, "loop"); var deleteBaseline = deleted.Source;
deleted.DeleteNodes([deleteOpening.Id, deleteLoop.Id], Validate);
Check(Root(deleted).Children.Count == 0 && deleted.Undo() && deleted.Source == deleteBaseline, "Multi-branch deletion and inbound reference cleanup form one undo");
var selectActions = Fresh(); var actionOpening = Phase(selectActions, "opening"); var actionIds = actionOpening.Children.Take(2).Select(n => n.Id).ToArray();
selectActions.DeleteNodes(actionIds, Validate);
Check(Phase(selectActions, "opening").Children.Single().Kind == "TimedTransition", "Multi-action deletion keeps unselected siblings");
var rejectedCandidate = Fresh(); var candidateBefore = rejectedCandidate.Source; var candidateIds = Ids(rejectedCandidate);
Reject(() => rejectedCandidate.MoveNodes([Phase(rejectedCandidate, "opening").Children[0].Id], Phase(rejectedCandidate, "loop").Id, 0, _ => [new("Candidate rejected")]), "Move candidate validation fails before committing");
Check(rejectedCandidate.Source == candidateBefore && Ids(rejectedCandidate) == candidateIds, "Failed move validation preserves source and identity layout");
Check(SourceVerification.Check(adapter.ServerRoot, file, moved.Source).Accepted && SourceVerification.Check(adapter.ServerRoot, file, nested.Source).Accepted && SourceVerification.Check(adapter.ServerRoot, file, grouped.Source).Accepted && SourceVerification.Check(adapter.ServerRoot, file, copies.Source).Accepted, "Moved, grouped and pasted exports compile against the original full server");
var output = Path.GetFullPath(".artifacts/selection"); Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { checks }, BehaviorDocument.JsonOptions));
Console.WriteLine($"{checks.Count} selection authoring checks passed.");
