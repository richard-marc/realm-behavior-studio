using Studio.Core;
using Studio.Raple;
using System.Text.Json;

var adapter = new RapleAdapter(Path.GetFullPath(args.FirstOrDefault() ?? "../raple-realms-7-server"));
var checks = new List<string>();
void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); Console.WriteLine("PASS " + label); }
BehaviorDocument Doc(string tree) => new("class Fixture { void Build() { db.Init(\"Cave Pirate Brawler\", " + tree + "); } }", adapter);
BehaviorNode Root(BehaviorDocument d) => d.Entities.Single().Root;
PhaseAnalysis Analyze(BehaviorDocument d) => BehaviorAnalysis.Analyze(Root(d));
IEnumerable<DocumentDiagnostic> Warnings(BehaviorDocument d, string prefix) => Analyze(d).Diagnostics.Where(d => d.Message.StartsWith(prefix));
string PhaseAfterStep(BehaviorDocument d, double hp)
{
    var session = new EncounterSession(adapter, d, "Cave Pirate Brawler", 7, new(Invulnerable: true));
    session.SetBossHpFraction(hp); session.Step(new());
    return session.Entities.Single(e => e.Name == "Cave Pirate Brawler").Phase;
}
var priority = Doc("""
new State(new State("opening",
    new HpLessTransition(threshold: 0.8, targetState: "wide"), new Wander(speed: 0.1),
    new HpLessTransition(threshold: .3f, targetState: "low")), new State("wide"), new State("low"))
""");
var edges = Analyze(priority).Transitions;
Check(edges.Select(e => e.Priority).SequenceEqual(new[] { 1, 2 }), "Transition priority ignores interleaved action positions");
Check(edges[0].Condition == "HP < 80%" && edges[1].Condition == "HP < 30%", "HP labels preserve strict comparison and C# numeric suffixes");
Check(Warnings(priority, "Priority conflict").Single().NodeId == edges[1].Node.Id, "Broader earlier HP condition diagnoses the later shadowed transition");
Check(PhaseAfterStep(priority, .2) == "wide", "Native runtime chooses the first matching sibling transition");
Check(PhaseAfterStep(priority, .8) == "opening", "Exact HP boundary retains the native strict less-than rule");
priority.MoveNode(edges[1].Node.Id, -1); priority.MoveNode(edges[1].Node.Id, -1);
Check(!Warnings(priority, "Priority conflict").Any() && Analyze(priority).Transitions.First().Node.Id == edges[1].Node.Id, "Source reorder recalculates priority and resolves the HP conflict");
Check(PhaseAfterStep(priority, .2) == "low", "Reordered transition priority changes the actual native destination");
var inheritance = Doc("""
new State(new State("parent", new State("child", new HpLessTransition(.9, "child-choice")),
    new HpLessTransition(.9, "parent-choice")), new State("child-choice"), new State("parent-choice"))
""");
Check(PhaseAfterStep(inheritance, .5) == "child-choice" && !Warnings(inheritance, "Priority conflict").Any(), "Native child precedence is separate from sibling priority conflicts");
var hierarchy = Doc("""
new State(new State("opening", new TimedTransition(500, "branch")),
    new State("branch", new State("first", new TimedTransition(500, "opening")), new State("unused")), new State("orphan"))
""");
var unreachable = Warnings(hierarchy, "Unreachable phase").Select(d => hierarchy.Find(d.NodeId!.Value).Label).Order().ToArray();
Check(unreachable.SequenceEqual(new[] { "orphan", "unused" }), "Parent-target entry reaches its first child and ancestors while excluding unentered siblings");
Check(!Warnings(hierarchy, "Reachability analysis is incomplete").Any(), "A reachable conditional cycle terminates analysis without a false incomplete result");
var inheritedEdge = Doc("""
new State(new State("parent", new State("child"), new TimedTransition(500, "reachable")), new State("reachable"))
""");
Check(!Warnings(inheritedEdge, "Unreachable phase").Any(), "Transitions inherited from active ancestors contribute to reachability");
var self = Doc("new State(new State(\"loop\", new TimedTransition(500, \"loop\")))");
Check(Warnings(self, "Self transition").Single().IsError == false && !Warnings(self, "Unreachable phase").Any(), "Self transition is an advisory with no infinite traversal or simulation block");
var dynamic = Doc("new State(new State(\"start\", new TimedTransition(500, NextPhase)), new State(\"unknown\"))");
Check(Warnings(dynamic, "Reachability analysis is incomplete").Any() && !Warnings(dynamic, "Unreachable phase").Any(), "Dynamic destinations suppress unsupported unreachable claims");
var factory = Doc("new State(new State(\"start\", Factory()), new State(\"unknown\"))");
Check(Warnings(factory, "Reachability analysis is incomplete").Any() && !Warnings(factory, "Unreachable phase").Any(), "Opaque factories suppress unsupported unreachable claims");
var custom = Doc("new State(new State(\"start\", new CustomAction()), new State(\"unknown\"))");
Check(Warnings(custom, "Reachability analysis is incomplete").Any() && !Warnings(custom, "Unreachable phase").Any(), "Unknown actions can change phases and make reachability incomplete");
var missing = Doc("new State(new State(\"start\", new TimedTransition(500, @\"missing\")))");
Check(adapter.Validate(missing).Single(d => d.Message.StartsWith("Missing transition target")).NodeId == Analyze(missing).Transitions.Single().Node.Id && Analyze(missing).Transitions.Single().TargetId == null, "Missing targets retain blocking validation and the exact transition identity");
var immediate = Doc("new State(new State(\"start\", new TimedTransition(0, \"first\"), new HpLessTransition(.9, \"second\")), new State(\"first\"), new State(\"second\"))");
Check(Warnings(immediate, "Priority conflict").Count() == 1 && PhaseAfterStep(immediate, .5) == "first", "Zero-time native timer shadows all later transitions in its owner");
var timers = Doc("new State(new State(\"start\", new TimedTransition(500, \"first\"), new TimedTransition(1000, \"second\")), new State(\"first\"), new State(\"second\"))");
Check(!Warnings(timers, "Priority conflict").Any(), "Stateful nonzero timers are not analyzed as stateless overlapping conditions");
var distance = Doc("new State(new State(\"start\", new PlayerWithinTransition(10, \"one\"), new PlayerWithinTransition(5, \"two\")), new State(\"one\"), new State(\"two\"))");
Check(Warnings(distance, "Priority conflict").Count() == 1 && Analyze(distance).Transitions[0].Condition == "Player within 10 tiles", "Broader player-distance condition shadows a narrower condition with the same visibility rule");
distance.ReplaceSource(distance.Source.Replace("new PlayerWithinTransition(5, \"two\")", "new PlayerWithinTransition(5, \"two\", seeInvis: true)"));
Check(!Warnings(distance, "Priority conflict").Any(), "Different invisible-player visibility prevents an unsupported conflict claim");
var resources = Doc("new State(new State(\"start\", new EntityExistsTransition(\"Cave Pirate Brawler\", 10, \"start\"), new EntityNotExistsTransition(null, 10, \"start\", checkAttackTarget: true), new EntityNotExistsTransition(null, 10, \"start\")))");
Check(Analyze(resources).Transitions[0].Condition == "Cave Pirate Brawler within 10 tiles" && Analyze(resources).Transitions[1].Condition == "Attack target absent" && Analyze(resources).Transitions[2].Condition == "No player within 10 tiles", "Entity condition labels respect checkAttackTarget mode and native null-player lookup");
var duplicate = Doc("new State(new State(\"same\"), new State(\"same\"))");
Check(Warnings(duplicate, "Reachability analysis is incomplete").Any() && !Warnings(duplicate, "Unreachable phase").Any(), "Duplicate state names disable definitive reachability");
Check(Analyze(hierarchy).Diagnostics.All(d => !d.IsError) && adapter.BuildState(hierarchy, "Cave Pirate Brawler") != null, "Authoring warnings remain separate from runtime validation gates");
var output = Path.GetFullPath(".artifacts/analysis"); Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { checks }, BehaviorDocument.JsonOptions));
Console.WriteLine($"{checks.Count} transition analysis checks passed.");
