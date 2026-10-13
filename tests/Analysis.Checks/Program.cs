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
var cycleDoc=Doc("new State(new State(\"a\",new TimedTransition(0,\"b\")),new State(\"b\",new TimedTransition(0,\"c\")),new State(\"c\",new TimedTransition(0,\"a\")))");var cycleSource=cycleDoc.Source;var cycleIds=cycleDoc.Identities;var cycleAnalysis=Analyze(cycleDoc);var cycle=cycleAnalysis.Cycles.Single();
Check(cycle.PhaseIds.Select(id=>cycleDoc.Find(id).Label).SequenceEqual(new[]{"a","b","c"})&&cycle.TransitionIds.Count==3&&cycle.ExitTransitionIds.Count==0&&cycle.Reachable,"A complete three-phase cycle retains ordered phase and transition identities with no literal graph exit");
Check(Warnings(cycleDoc,"Phase cycle:").Single() is {IsError:false,NodeId:var cycleDiagnostic}&&cycle.TransitionIds.Contains(cycleDiagnostic!.Value)&&cycleDoc.Source==cycleSource&&cycleDoc.Identities.SequenceEqual(cycleIds),"Cycle review is a navigable advisory and leaves source/history identities untouched");
using(var native=new EncounterSession(adapter,cycleDoc,"Cave Pirate Brawler",7,new(Invulnerable:true)))
{var observed=new List<string>();for(var i=0;i<6;i++){native.Step(new());observed.Add(native.Entities.Single().Phase);}Check(observed.SequenceEqual(new[]{"b","c","a","b","c","a"}),"The actual native zero-time transitions execute the analyzed repeating three-phase route");}
cycleDoc.RenameState(cycle.PhaseIds.Last(),"renamed",d=>adapter.Validate(d,"Cave Pirate Brawler"));Check(Analyze(cycleDoc).Cycles.Single().PhaseIds.SequenceEqual(cycle.PhaseIds)&&Warnings(cycleDoc,"Phase cycle:").Single().Message.Contains("renamed"),"Phase rename updates cycle labels and references without replacing member identities");
Check(cycleDoc.Undo()&&cycleDoc.Source==cycleSource&&Analyze(cycleDoc).Cycles.Single().TransitionIds.SequenceEqual(cycle.TransitionIds),"Cycle source history restores exact accepted routes and transition identities");
var exitDoc=Doc("new State(new State(\"a\",new TimedTransition(500,\"b\"),new HpLessTransition(.2,\"finish\")),new State(\"b\",new TimedTransition(500,\"a\")),new State(\"finish\"))");var exitCycle=Analyze(exitDoc).Cycles.Single();
Check(exitCycle.ExitTransitionIds.Count==1&&exitDoc.Find(exitCycle.ExitTransitionIds.Single()).Kind=="HpLessTransition"&&Warnings(exitDoc,"Phase cycle:").Single().Message.Contains("1 transition can leave"),"Conditional cycle exits retain their actual source condition and avoid a closed-loop claim");
Check(PhaseAfterStep(exitDoc,.1)=="finish","A native HP condition leaves the reviewed cycle before its timer advances");
var nestedCycle=Doc("new State(new State(\"branch\",new State(\"child\"),new TimedTransition(0,\"other\")),new State(\"other\",new State(\"other-child\"),new TimedTransition(0,\"branch\")))");
Check(Analyze(nestedCycle).Cycles.Single().PhaseIds.Select(id=>nestedCycle.Find(id).Label).SequenceEqual(new[]{"child","other-child"}),"Parent destinations normalize to actual first-child entry and inherited transitions connect active leaves");
using(var native=new EncounterSession(adapter,nestedCycle,"Cave Pirate Brawler",7,new(Invulnerable:true)))
{native.Step(new());var first=native.Entities.Single().Phase;native.Step(new());Check(first=="other-child"&&native.Entities.Single().Phase=="child","Native parent-target entry executes the two reviewed nested leaves");}
var shadowCycle=Doc("new State(new State(\"a\",new TimedTransition(0,\"finish\"),new TimedTransition(100,\"b\")),new State(\"b\",new TimedTransition(0,\"a\")),new State(\"finish\"))");
Check(Analyze(shadowCycle).Cycles.Count==0&&Warnings(shadowCycle,"Priority conflict:").Count()==1&&PhaseAfterStep(shadowCycle,1)=="finish","Provably shadowed sibling routes cannot invent a phase cycle");
var childShadowsParent=Doc("new State(new State(\"parent\",new State(\"child\",new TimedTransition(0,\"finish\")),new TimedTransition(0,\"other\")),new State(\"other\",new TimedTransition(0,\"parent\")),new State(\"finish\"))");
Check(Analyze(childShadowsParent).Cycles.Count==0&&PhaseAfterStep(childShadowsParent,1)=="finish","An always-matching native child transition suppresses ancestor cycle routes");
var choiceCycle=Doc("new State(new State(\"a\",new TimedRandomTransition(500,states:new[]{\"b\",\"b\",\"finish\"})),new State(\"b\",new TimedTransition(500,\"a\")),new State(\"finish\"))");var choiceAnalysis=Analyze(choiceCycle);var choiceEdge=choiceAnalysis.Transitions.First();
Check(choiceAnalysis.Cycles.Single().ExitTransitionIds.Single()==choiceEdge.Node.Id&&choiceEdge.Destinations.Single(d=>d.Name=="b").Slots==2,"Random choices retain repeated weighting slots and identify their actual conditional exit transition");
var disconnected=Doc("new State(new State(\"start\"),new State(\"a\",new TimedTransition(0,\"b\")),new State(\"b\",new TimedTransition(0,\"a\")))");
Check(!Analyze(disconnected).Cycles.Single().Reachable&&Warnings(disconnected,"Phase cycle:").Single().Message.Contains("outside known entry routes"),"A disconnected cycle is distinguished from one reachable through encounter entry");
Check(new[]{dynamic,factory,custom,duplicate,missing}.All(d=>Analyze(d).Cycles.Count==0),"Incomplete ownership, dynamic destinations, duplicate names and missing targets suppress unsupported cycle claims");
var collection=Doc("new State(children:new TKR.WorldServer.logic.IStateChildren[]{new State(\"a\",new TimedTransition(0,\"b\")),new State(\"b\",new TimedTransition(0,\"a\"))})");var collectionRoot=Root(collection).Children.Single();
Check(BehaviorAnalysis.Analyze(collectionRoot).Cycles.Count==0&&BehaviorAnalysis.Analyze(collectionRoot).Diagnostics.Any(d=>d.Message.StartsWith("Reachability analysis is incomplete")),"A detached collection view does not invent encounter entry or ancestor ownership");
var sweepCycle=Doc("new State(new State(\"a\",new SequentialAimedSweep(0),new TimedTransition(0,\"b\")),new State(\"b\",new TimedTransition(0,\"a\")))");
Check(Analyze(sweepCycle).Cycles.Count==1&&!Warnings(sweepCycle,"Reachability analysis is incomplete").Any(),"The inspected native sweep does not hide an otherwise complete literal phase cycle");
Check(Analyze(self).Cycles is [{PhaseIds.Count:1}]&&Warnings(self,"Self transition").Count()==1&&!Warnings(self,"Phase cycle:").Any(),"Existing self-transition notices retain a single diagnostic while cycle metadata remains available");
for(var seed=0;seed<20;seed++)
{
    var random=new Random(seed);const int size=12;var links=Enumerable.Range(0,size).Select(_=>Enumerable.Range(0,random.Next(1,4)).Select(_=>random.Next(size)).ToArray()).ToArray();
    var graph=Doc("new State("+string.Join(",",Enumerable.Range(0,size).Select(i=>"new State(\"p"+i+"\",new TimedRandomTransition(500,states:new[]{"+string.Join(",",links[i].Select(j=>"\"p"+j+"\""))+"}))"))+")");
    HashSet<int> Reach(int start){var found=new HashSet<int>();var pending=new Queue<int>();pending.Enqueue(start);while(pending.TryDequeue(out var n)){if(!found.Add(n))continue;foreach(var to in links[n])pending.Enqueue(to);}return found;}
    var reach=Enumerable.Range(0,size).Select(Reach).ToArray();var expected=new List<HashSet<int>>();var assigned=new HashSet<int>();
    for(var i=0;i<size;i++){if(assigned.Contains(i))continue;var group=Enumerable.Range(0,size).Where(j=>reach[i].Contains(j)&&reach[j].Contains(i)).ToHashSet();assigned.UnionWith(group);if(group.Count>1||links[i].Contains(i))expected.Add(group);}
    var actual=Analyze(graph).Cycles;var matches=actual.Count==expected.Count&&actual.All(c=>{var members=c.PhaseIds.Select(id=>int.Parse(graph.Find(id).Label[1..])).ToHashSet();return expected.Any(g=>g.SetEquals(members))&&c.Reachable==members.Any(reach[0].Contains)&&c.ExitTransitionIds.Count==members.Count(i=>links[i].Any(j=>!members.Contains(j)));});
    if(!matches)throw new Exception("Cycle graph disagrees with independent reachability oracle for seed "+seed);
}
Check(true,"Twenty branching random-choice graphs agree with independent mutual-reachability, exit and entry evidence");
var output = Path.GetFullPath(".artifacts/analysis"); Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { checks }, BehaviorDocument.JsonOptions));
Console.WriteLine($"{checks.Count} transition analysis checks passed.");
