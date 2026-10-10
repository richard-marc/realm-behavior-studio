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
    static readonly TKR.WorldServer.logic.behaviors.Shoot StudioContextSharedAction = new TKR.WorldServer.logic.behaviors.Shoot(8, count: 3, fixedAngle: 90, coolDown: 200);
    static TKR.WorldServer.logic.behaviors.Timed StudioContextWrap(params TKR.WorldServer.logic.Behavior[] children) => new TKR.WorldServer.logic.behaviors.Timed(1000, children);
    static TKR.WorldServer.logic.behaviors.Timed StudioContextLists(TKR.WorldServer.logic.Behavior[] first, TKR.WorldServer.logic.Behavior[] second, int period = 1000) => new TKR.WorldServer.logic.behaviors.Timed(period, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(first, second)));
    static TKR.WorldServer.logic.behaviors.Timed StudioContextSlots(TKR.WorldServer.logic.Behavior lead, int period = 1000, params TKR.WorldServer.logic.Behavior[] tail) => new TKR.WorldServer.logic.behaviors.Timed(period, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(new[] { lead }, tail)));
    static TKR.WorldServer.logic.behaviors.Timed StudioContextOptional(TKR.WorldServer.logic.Behavior left = null, TKR.WorldServer.logic.Behavior right = null, params TKR.WorldServer.logic.Behavior[] tail) => new TKR.WorldServer.logic.behaviors.Timed(1000, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(System.Linq.Enumerable.Where(new[] { left, right }, b => b != null), tail)));
    static TKR.WorldServer.logic.behaviors.Timed StudioContextKeywordSlot(TKR.WorldServer.logic.Behavior @event = null, params TKR.WorldServer.logic.Behavior[] tail) => StudioContextOptional(@event, tail: tail);
    static TKR.WorldServer.logic.behaviors.Timed StudioContextOptionalArrays(TKR.WorldServer.logic.Behavior[] first = null, TKR.WorldServer.logic.Behavior[] second = null) => StudioContextLists(first ?? new TKR.WorldServer.logic.Behavior[0], second ?? new TKR.WorldServer.logic.Behavior[0]);
    static readonly TKR.WorldServer.logic.Behavior[] StudioContextSharedList = new TKR.WorldServer.logic.Behavior[] { new TKR.WorldServer.logic.behaviors.Shoot(8) };
    static System.Collections.Generic.IEnumerable<TKR.WorldServer.logic.Behavior> StudioContextThrowingList { get { throw new System.InvalidOperationException("spread authoring must not execute this getter"); } }
    static TKR.WorldServer.logic.behaviors.Timed StudioContextCollections(System.Collections.Generic.IEnumerable<TKR.WorldServer.logic.Behavior> first, System.Collections.Generic.IReadOnlyList<TKR.WorldServer.logic.Behavior> second, System.Collections.Generic.List<TKR.WorldServer.logic.Behavior> third = null, int period = 1000) => new TKR.WorldServer.logic.behaviors.Timed(period, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(System.Linq.Enumerable.Concat(first, second), third ?? new System.Collections.Generic.List<TKR.WorldServer.logic.Behavior>())));
    static TKR.WorldServer.logic.behaviors.Timed StudioContextEnumerable(System.Collections.Generic.IEnumerable<TKR.WorldServer.logic.Behavior> children = null) => new TKR.WorldServer.logic.behaviors.Timed(1000, System.Linq.Enumerable.ToArray(children ?? System.Linq.Enumerable.Empty<TKR.WorldServer.logic.Behavior>()));
    static TKR.WorldServer.logic.behaviors.Prioritize StudioContextCycles(System.Collections.Generic.IReadOnlyList<TKR.WorldServer.logic.CycleBehavior> children) => new TKR.WorldServer.logic.behaviors.Prioritize(System.Linq.Enumerable.ToArray(children));
    [System.Runtime.CompilerServices.CollectionBuilder(typeof(StudioContextChildBuilder), "Create")]
    sealed class StudioContextBuiltChildren : System.Collections.Generic.IEnumerable<TKR.WorldServer.logic.Behavior> {
        readonly TKR.WorldServer.logic.Behavior[] children;
        public StudioContextBuiltChildren(TKR.WorldServer.logic.Behavior[] children) { this.children = children; }
        public System.Collections.Generic.IEnumerator<TKR.WorldServer.logic.Behavior> GetEnumerator() => ((System.Collections.Generic.IEnumerable<TKR.WorldServer.logic.Behavior>)children).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    static class StudioContextChildBuilder { public static StudioContextBuiltChildren Create(System.ReadOnlySpan<TKR.WorldServer.logic.Behavior> children) => new StudioContextBuiltChildren(children.ToArray()); }
    static TKR.WorldServer.logic.behaviors.Timed StudioContextBuilt(StudioContextBuiltChildren children) => StudioContextEnumerable(children);
    sealed class StudioContextUnbuildable : System.Collections.Generic.IEnumerable<TKR.WorldServer.logic.Behavior> {
        public StudioContextUnbuildable(int token) { }
        public System.Collections.Generic.IEnumerator<TKR.WorldServer.logic.Behavior> GetEnumerator() => System.Linq.Enumerable.Empty<TKR.WorldServer.logic.Behavior>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    static TKR.WorldServer.logic.behaviors.Timed StudioContextUnbuildableInput(StudioContextUnbuildable children = null) => StudioContextEnumerable(children);
    sealed class StudioContextInitializerChildren : System.Collections.Generic.IEnumerable<TKR.WorldServer.logic.Behavior> {
        readonly System.Collections.Generic.List<TKR.WorldServer.logic.Behavior> children;
        public StudioContextInitializerChildren(int capacity) { children = new System.Collections.Generic.List<TKR.WorldServer.logic.Behavior>(capacity); }
        public void Add(TKR.WorldServer.logic.Behavior child) => children.Insert(0, child);
        public System.Collections.Generic.IEnumerator<TKR.WorldServer.logic.Behavior> GetEnumerator() => children.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    sealed class StudioContextKeyedChildren : System.Collections.Generic.IEnumerable<TKR.WorldServer.logic.Behavior> {
        readonly System.Collections.Generic.List<TKR.WorldServer.logic.Behavior> children = new System.Collections.Generic.List<TKR.WorldServer.logic.Behavior>();
        public void Add(int key, TKR.WorldServer.logic.Behavior child) => children.Add(child);
        public System.Collections.Generic.IEnumerator<TKR.WorldServer.logic.Behavior> GetEnumerator() => children.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    sealed class StudioContextThrowingInitializer : System.Collections.Generic.List<TKR.WorldServer.logic.Behavior> {
        public StudioContextThrowingInitializer() { throw new System.InvalidOperationException("initializer authoring must not execute this constructor"); }
    }
    static State StudioContextMessages(params string[] text) => new State(string.Join("-", text));
    static State StudioContextKeyword(int @event = 1) => new State(@event.ToString());
    static State StudioContextAmbiguous(int count, double speed = 1) => new State(count.ToString());
    static State StudioContextAmbiguous(int count, float speed = 1) => new State(count.ToString());
    sealed class StudioContextDerivedState : State { public StudioContextDerivedState() : base("derived source") { } }
    static StudioContextDerivedState StudioContextDerived() => new StudioContextDerivedState();
    static State StudioContextCounter(int count = -1, string target = "label") => new State(target + count);
    sealed class StudioContextPairBehavior : TKR.WorldServer.logic.Behavior {
        readonly TKR.WorldServer.logic.Behavior first, second;
        public StudioContextPairBehavior(TKR.WorldServer.logic.Behavior first, TKR.WorldServer.logic.Behavior second) { this.first = first; this.second = second; }
        protected override void OnStateEntry(TKR.WorldServer.core.objects.Entity host, TKR.WorldServer.core.worlds.TickTime time, ref object state) { first.OnStateEntry(host, time); second.OnStateEntry(host, time); }
        protected override void TickCore(TKR.WorldServer.core.objects.Entity host, TKR.WorldServer.core.worlds.TickTime time, ref object state) { second.Tick(host, time); first.Tick(host, time); }
    }
    sealed class StudioContextCollectionBehavior : TKR.WorldServer.logic.Behavior {
        readonly System.Collections.Generic.IReadOnlyList<TKR.WorldServer.logic.Behavior> first;
        readonly System.Collections.Generic.List<TKR.WorldServer.logic.Behavior> second;
        public StudioContextCollectionBehavior(System.Collections.Generic.IReadOnlyList<TKR.WorldServer.logic.Behavior> first, System.Collections.Generic.List<TKR.WorldServer.logic.Behavior> second) { this.first = first; this.second = second; }
        protected override void OnStateEntry(TKR.WorldServer.core.objects.Entity host, TKR.WorldServer.core.worlds.TickTime time, ref object state) { foreach (var child in first) child.OnStateEntry(host, time); foreach (var child in second) child.OnStateEntry(host, time); }
        protected override void TickCore(TKR.WorldServer.core.objects.Entity host, TKR.WorldServer.core.worlds.TickTime time, ref object state) { foreach (var child in second) child.Tick(host, time); foreach (var child in first) child.Tick(host, time); }
    }
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
var multiArrays = Doc("new State(new State(\"multi lists\", StudioContextLists(second: new TKR.WorldServer.logic.Behavior[] { new Shoot(8, count: 5) }, first: new TKR.WorldServer.logic.Behavior[] { new Shoot(8, count: 2), new Wander(StudioContextSpeed) }, period: 800)))");
var listsCall = Root(multiArrays).Children.Single().Children.Single(); var firstList = listsCall.Children[0]; var secondList = listsCall.Children[1];
Check(listsCall.Children.Select(n => n.Argument?.Name).SequenceEqual(["first", "second"]) && !multiArrays.CanEditChildren(listsCall.Id) && listsCall.Children.All(n => multiArrays.CanEditChildren(n.Id)), "Multiple native array parameters expose independently addressable lists with actual formal names");
var movedWander = firstList.Children[1]; multiArrays.MoveNodes([movedWander.Id], secondList.Id, 1, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(multiArrays.Find(firstList.Id).Children.Count == 1 && multiArrays.Find(secondList.Id).Children.Last().Id == movedWander.Id && multiArrays.Find(listsCall.Id).Parameters.Single().Expression == "800", "Cross-slot moves preserve both list identities, unrelated scalar arguments and native subtype constraints");
var secondCopy = multiArrays.PasteNodes(secondList.Id, 0, multiArrays.CopyNodes([firstList.Children[0].Id]), d => adapter.Validate(d, "Cave Pirate Brawler")).Single();
Check(secondCopy != firstList.Children[0].Id && multiArrays.Find(secondList.Id).Children.Count == 3 && multiArrays.Source.Contains("second:") && multiArrays.Source.Contains("first:"), "Multi-list paste creates fresh identities while preserving out-of-order named array arguments");
var slotDoc = Doc("new State(new State(\"multi slots\", StudioContextSlots(new Shoot(8, count: 2), 600, new Shoot(8, count: 3), /* tail comment */ new Wander(StudioContextSpeed))))");
var slotsCall = Root(slotDoc).Children.Single().Children.Single(); var leadSlot = slotsCall.Children[0]; var tailSlot = slotsCall.Children[1]; var lead = leadSlot.Children.Single(); var tail = tailSlot.Children[0];
Check(leadSlot.ArgumentFromCall && tailSlot.ArgumentFromCall && slotDoc.IsScalarChildSlot(leadSlot.Id) && !slotDoc.IsScalarChildSlot(tailSlot.Id) && tailSlot.Children.Count == 2, "Scalar and expanded params child slots remain separate named containers");
slotDoc.MoveNode(tail.Id, 1, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(slotDoc.Find(tailSlot.Id).Children.Last().Id == tail.Id && slotDoc.Find(leadSlot.Id).Children.Single().Id == lead.Id && slotDoc.Source.Contains("/* tail comment */") && slotDoc.Find(slotsCall.Id).Parameters.Single().Expression == "600", "Expanded params reorder preserves another scalar child, argument comments and the helper's timer");
var beforeRequired = slotDoc.Source;
Reject(() => slotDoc.DeleteNode(lead.Id, d => adapter.Validate(d, "Cave Pirate Brawler")), "A required scalar source slot cannot lose its child");
Reject(() => slotDoc.AddChild(leadSlot.Id, "new Shoot(8)", d => adapter.Validate(d, "Cave Pirate Brawler")), "A scalar source slot cannot acquire a second child");
Check(slotDoc.Source == beforeRequired && slotDoc.AcceptsChild(leadSlot.Id, "StudioContextFan()", replace: true) && !slotDoc.AcceptsChild(leadSlot.Id, "new TimedTransition(100, \"multi slots\")", replace: true), "Scalar replacement preflight uses actual source child types without altering the document");
var replacementLead = slotDoc.ReplaceChild(leadSlot.Id, "StudioContextFan(count: 7)", d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(replacementLead != lead.Id && slotDoc.Find(leadSlot.Id).Children.Single().Id == replacementLead && slotDoc.Find(tailSlot.Id).Children.Last().Id == tail.Id, "Scalar replacement commits one native child with a fresh identity while retaining sibling slots");
Check(slotDoc.Undo() && slotDoc.Find(leadSlot.Id).Children.Single().Id == lead.Id && slotDoc.Redo() && slotDoc.Find(replacementLead).Kind == "Factory", "Scalar replacement restores complete source and identities through one undo/redo");
slotDoc.LockedNodes.Add(replacementLead); Reject(() => slotDoc.ReplaceChild(leadSlot.Id, "new Wander(.2)"), "Scalar replacement respects a lock on the existing child's region"); slotDoc.LockedNodes.Clear();
var optionalSlots = Doc("new State(new State(\"optional slots\", StudioContextOptional(new Shoot(8, count: 2), new Wander(.2), new Shoot(8, count: 3))))");
var optionalCall = Root(optionalSlots).Children.Single().Children.Single(); var leftSlot = optionalCall.Children[0]; var rightSlot = optionalCall.Children[1]; var optionalTail = optionalCall.Children[2]; var rightChild = rightSlot.Children.Single(); var optionalAttack = optionalTail.Children.Single();
optionalSlots.DeleteNode(leftSlot.Children.Single().Id, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(optionalSlots.Find(leftSlot.Id).Children.Count == 0 && optionalSlots.Find(rightSlot.Id).Children.Single().Id == rightChild.Id && optionalSlots.Source.Contains("right:") && optionalSlots.Source.Contains("tail:new global::TKR.WorldServer.logic.Behavior[]"), "Removing an optional positional child preserves the remaining arguments' actual binding with named slots");
optionalSlots.MoveNodes([rightChild.Id], leftSlot.Id, 0, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(optionalSlots.Find(leftSlot.Id).Children.Single().Id == rightChild.Id && optionalSlots.Find(rightSlot.Id).Children.Count == 0 && optionalSlots.Find(optionalTail.Id).Children.Single().Id == optionalAttack.Id, "Moving between two optional scalar slots sharing one call rewrites both atomically");
optionalSlots.MoveNodes([optionalAttack.Id], rightSlot.Id, 0, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(optionalSlots.Find(rightSlot.Id).Children.Single().Id == optionalAttack.Id && optionalSlots.Find(optionalTail.Id).Children.Count == 0, "Moving an array child into an omitted scalar slot retains nested array edits and every identity");
var namedComments = Doc("new State(StudioContextOptional(left: new Shoot(8), right /* retain slot comment */ : new Wander(.2), tail: new TKR.WorldServer.logic.Behavior[] { new Shoot(8) }))"); var commentCall = Root(namedComments).Children.Single();
namedComments.DeleteNode(commentCall.Children[0].Children.Single().Id, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(namedComments.Source.Contains("right /* retain slot comment */ :") && namedComments.Find(commentCall.Id).Children[1].Children.Single().Kind == "Wander", "Optional slot normalization preserves existing named-argument comments and their actual binding");
var emptySlots = Doc("new State(StudioContextOptional())"); var emptyCall = Root(emptySlots).Children.Single(); var emptyTail = emptyCall.Children[2];
emptySlots.AddChild(emptyTail.Id, "StudioContextFan(count: 6)", d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(emptySlots.Find(emptyTail.Id).Children.Single().Kind == "Factory" && emptySlots.Find(emptyCall.Id).Children.Take(2).All(n => n.Children.Count == 0), "An omitted params slot can be populated without accidentally binding to an earlier optional scalar");
var optionalArrays = Doc("new State(StudioContextOptionalArrays(second /* array slot note */ : null))"); var optionalArrayCall = Root(optionalArrays).Children.Single(); var missingArray = optionalArrayCall.Children[0]; var nullArray = optionalArrayCall.Children[1];
Check(optionalArrayCall.Children.All(n => n.Children.Count == 0 && optionalArrays.CanEditChildren(n.Id) && n.Label.Contains("unassigned")), "Missing and null optional array slots remain visibly unassigned and independently editable");
optionalArrays.AddChild(missingArray.Id, "StudioContextFan(count: 4)", d => adapter.Validate(d, "Cave Pirate Brawler")); optionalArrays.AddChild(nullArray.Id, "new Wander(.2)", d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(optionalArrays.Find(missingArray.Id).Children.Single().Kind == "Factory" && optionalArrays.Find(nullArray.Id).Children.Single().Kind == "Wander" && optionalArrayCall.Children.All(n => !optionalArrays.Find(n.Id).ArgumentFromCall) && optionalArrays.Source.Contains("second /* array slot note */ :"), "Filling missing and null array slots materializes actual typed arrays while preserving slot identities and named-argument comments");
var keywordSlots = Doc("new State(StudioContextKeywordSlot())"); var eventSlot = Root(keywordSlots).Children.Single().Children[0];
keywordSlots.AddChild(eventSlot.Id, "new Wander(.2)", d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(keywordSlots.Source.Contains("@event:") && keywordSlots.Find(eventSlot.Id).Children.Single().Kind == "Wander", "Inserting omitted scalar child parameters escapes actual C# keyword names");
var sharedLists = Doc("new State(StudioContextLists(StudioContextSharedList, new TKR.WorldServer.logic.Behavior[] { new Wander(.2) }))"); var sharedListCall = Root(sharedLists).Children.Single();
Check(sharedListCall.Children[0].ArgumentRequiresSource && !sharedLists.CanEditChildren(sharedListCall.Children[0].Id) && sharedLists.CanEditChildren(sharedListCall.Children[1].Id), "Shared generated collections stay explicit source regions while independent literal lists remain editable");
var identicalSlots = Doc("new State(StudioContextSlots(new Shoot(8), 1000, new Shoot(8), new Shoot(8)))"); var identicalCall = Root(identicalSlots).Children.Single();
var legacySlotIds = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
var legacySlotIdentities = identicalSlots.Identities.Where(i => !i.Path.StartsWith(identicalCall.Path + "/", StringComparison.Ordinal)).Concat(legacySlotIds.Select((legacyId, i) => new NodeIdentity(legacyId, identicalCall.Path + "/" + i, "Shoot", "new Shoot(8)"))).ToArray();
var legacySlots = new BehaviorDocument(identicalSlots.Source, adapter, legacySlotIdentities, file); var upgradedSlots = Root(legacySlots).Children.Single().Children;
Check(upgradedSlots.SelectMany(n => n.Children).Select(n => n.Id).SequenceEqual(legacySlotIds), "Older flat multi-slot identities upgrade correctly even for three identical child expressions");
legacySlots.LockedNodes.Add(legacySlotIds[1]); Reject(() => Edit(legacySlots, legacySlots.Find(legacySlotIds[1]), "count", "3"), "An upgraded flat child lock still protects its precise source region");
var oldSharedId = Guid.NewGuid(); var oldSharedIdentities = sharedLists.Identities.Where(i => !i.Path.StartsWith(sharedListCall.Path + "/", StringComparison.Ordinal)).Append(new NodeIdentity(oldSharedId, sharedListCall.Path + "/0", "Opaque", "StudioContextSharedList"));
var upgradedShared = new BehaviorDocument(sharedLists.Source, adapter, oldSharedIdentities, file);
Check(upgradedShared.Find(oldSharedId).Kind == "Opaque" && upgradedShared.Parent(oldSharedId)!.ArgumentRequiresSource, "Legacy opaque shared-list identities survive the new named slot boundary");
var cloneSlots = slotDoc.DuplicateNode(slotsCall.Id, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(slotDoc.Find(cloneSlots).Children.Select(n => n.Argument?.Name).SequenceEqual(["lead", "tail"]) && slotDoc.Find(cloneSlots).Descendants().All(n => !slotDoc.Find(slotsCall.Id).Descendants().Any(old => old.Id == n.Id)), "Duplicating a multi-slot helper retains every named container and gives the complete copy fresh identities");
slotDoc.DeleteNode(cloneSlots, d => adapter.Validate(d, "Cave Pirate Brawler"));
var constructorSlots = Doc("new State(new State(\"constructor slots\", new StudioContextPairBehavior(new Shoot(8, count: 2), new Shoot(8, count: 4))))");
var pairConstructor = Root(constructorSlots).Children.Single().Children.Single(); var firstScalar = pairConstructor.Children[0]; var secondScalar = pairConstructor.Children[1]; var secondScalarChild = secondScalar.Children.Single();
Check(pairConstructor.Kind.EndsWith(".StudioContextPairBehavior") && pairConstructor.Children.Select(n => n.Argument?.Name).SequenceEqual(["first", "second"]) && adapter.NeedsCompiledRuntime(constructorSlots, "Cave Pirate Brawler"), "Source-defined native constructors expose multiple scalar slots using their actual constructor symbols");
constructorSlots.ReplaceChild(firstScalar.Id, "new Shoot(8, count: 6)", d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(constructorSlots.Find(secondScalar.Id).Children.Single().Id == secondScalarChild.Id && constructorSlots.Find(firstScalar.Id).Children.Single().Parameters.Single(p => p.Name == "count").Expression == "6", "Replacing one source-constructor slot preserves the other slot's identity and argument");
var structural = Doc("new State(StudioContextWrap(children: new TKR.WorldServer.logic.Behavior[] { StudioContextFan(/* preserve count */ StudioContextCount), StudioContextSharedAction }), new Timed(500, new Wander(StudioContextSpeed)))");
var structuralRoot = Root(structural); var sourceWrap = structuralRoot.Children[0]; var sourceList = sourceWrap.Children.Single(); var leafFactory = sourceList.Children[0]; var sharedAction = sourceList.Children[1]; var timed = structuralRoot.Children[1];
Check(structural.CanOrganize(sourceWrap.Id) && structural.CanOrganize(leafFactory.Id) && structural.CanOrganize(sharedAction.Id) && !structural.CanOrganize(structuralRoot.Id), "Typed source helpers and shared references expose structural editing while encounter roots stay fixed");
var clonedWrap = structural.DuplicateNode(sourceWrap.Id, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(structural.Find(clonedWrap).Kind == "Factory" && structural.Find(clonedWrap).Descendants().All(n => !sourceWrap.Descendants().Any(old => old.Id == n.Id)) && structural.Find(clonedWrap).Children.Single().Children.Select(n => n.Kind).SequenceEqual(["Factory", "Reference"]), "Duplicating a factory preserves nested source-bound children with fresh identities");
Check(structural.Source.Contains("StudioContextFan(/* preserve count */ StudioContextCount)") && structural.Find(clonedWrap).Children.Single().Children[0].Parameters.Single().Expression == "StudioContextCount", "Factory copies retain argument comments and original constant expressions");
structural.MoveNodes([leafFactory.Id, sharedAction.Id], timed.Id, 1, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(structural.Find(sourceList.Id).Children.Count == 0 && structural.Find(timed.Id).Children.Skip(1).Select(n => n.Id).SequenceEqual([leafFactory.Id, sharedAction.Id]), "Moving helpers and shared references between native child slots preserves their identities and order");
var copiedReference = structural.DuplicateNode(sharedAction.Id, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(structural.Find(copiedReference).Kind == "Reference" && structural.Expression(structural.Find(copiedReference)) == structural.Expression(structural.Find(sharedAction.Id)), "Copying a shared reference preserves the original C# sharing semantics");
var beforeLock = structural.Source; structural.LockedNodes.Add(timed.Id);
Reject(() => structural.CopyNodes([leafFactory.Id]), "Source helper copy respects locked ancestors");
Reject(() => structural.AddChild(timed.Id, "StudioContextFan()"), "Source helper insertion respects locked destinations"); structural.LockedNodes.Clear();
Check(structural.Source == beforeLock, "Rejected locked source operations preserve the source");
var beforeInvalid = structural.Source; var beforeInvalidRevision = structural.Revision;
Reject(() => structural.AddChild(timed.Id, "StudioContextShared", d => adapter.Validate(d, "Cave Pirate Brawler")), "Native Behavior slots reject shared State references by their actual source type");
Reject(() => structural.AddChild(timed.Id, "StudioContextMissingHelper()", d => adapter.Validate(d, "Cave Pirate Brawler")), "Unresolved pasted helpers reject atomically in the destination source context");
Check(structural.Source == beforeInvalid && structural.Revision == beforeInvalidRevision, "Invalid source expression placements preserve the last valid revision");
structural.AddChild(timed.Id, "StudioContextFan(range: 7, count: StudioContextCount)", d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(structural.Find(timed.Id).Children.Last().Kind == "Factory" && structural.Find(timed.Id).Children.Last().Parameters.Select(p => p.Name).SequenceEqual(["range", "count"]), "Adding a helper binds its real overload and named arguments in the original file");
Check(structural.Undo() && structural.Source == beforeInvalid && structural.Redo() && structural.Find(timed.Id).Children.Last().Kind == "Factory", "Source-aware insertion restores source and node identities through undo and redo");
var otherContext = new BehaviorDocument(original, adapter, sourceFile: file); var otherSource = otherContext.Source;
Reject(() => otherContext.PasteNodes(Root(otherContext).Id, 0, structural.CopyNodes([leafFactory.Id]), d => adapter.Validate(d, "Cave Pirate Brawler")), "Cross-document paste rejects a helper absent from the destination's source scope");
Check(otherContext.Source == otherSource, "A missing destination helper leaves the other document untouched");
var referenceCopy = Doc("new State(StudioContextShared)"); var sharedStateId = Root(referenceCopy).Children.Single().Id;
Check(referenceCopy.DuplicateNode(sharedStateId, d => adapter.Validate(d, "Cave Pirate Brawler")) != sharedStateId && Root(referenceCopy).Children.All(n => n.Kind == "Reference"), "Shared State references duplicate without executing a throwing getter");
var stateShadowSource = "using State = TKR.WorldServer.logic.behaviors.Follow;\n" + Doc("new StudioStatePlaceholder(StudioContextFan(), new Wander(StudioContextSpeed))").Source.Replace("new State(", "new global::TKR.WorldServer.logic.State(").Replace("new StudioStatePlaceholder(", "new global::TKR.WorldServer.logic.State(");
var stateShadow = new BehaviorDocument(stateShadowSource, adapter, sourceFile: file);
stateShadow.DuplicateNode(Root(stateShadow).Children[0].Id, d => adapter.Validate(d, "Cave Pirate Brawler"));
var newGroup = stateShadow.GroupNodes(Root(stateShadow).Children.Select(n => n.Id), "grouped source", d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(stateShadow.Find(newGroup).Kind == "State" && stateShadow.Find(newGroup).Children.Count == 3 && stateShadow.Source.Contains("new global::TKR.WorldServer.logic.State(\"grouped source\""), "Source-aware copy and grouping use fully qualified native State constructors");
var duplicatedAttack = Doc("new State(new State(\"source duplicate\", StudioContextFan(count: 2)))");
var nativeLeaf = Root(duplicatedAttack).Children.Single().Children.Single(); duplicatedAttack.DuplicateNode(nativeLeaf.Id, d => adapter.Validate(d, "Cave Pirate Brawler"));
var phase = Doc("new State(new State(\"visible\", new Shoot(8)), StudioContextFan())");
Reject(() => phase.RenameState(Root(phase).Children[0].Id, "renamed"), "Visible phase rename refuses to guess hidden helper references");
Reject(() => factory.ReplaceSource(factory.Source.Replace("count: count", "count: StudioNoSuchCount")), "Compiler errors in a helper body reject complete source replacement");
var output = Path.GetFullPath(".artifacts/source-context"); Directory.CreateDirectory(output);
var collectionDoc = Doc("new State(children: [new State(\"source collections\", StudioContextWrap(children: [StudioContextFan(count: 2), .. /* spread note */ StudioContextSharedList, new Wander(StudioContextSpeed)]), new Timed(1000, behaviors: [new Shoot(8, count: 5)]))])");
var collectionPhase = Root(collectionDoc).StateChildren.Single(); var collectionWrap = collectionPhase.Children[0]; var collectionList = collectionWrap.Children.Single(); var targetCollection = collectionPhase.Children[1].Children.Single(); var spread = collectionList.Children.Single(n => n.Kind == "Spread"); var explicitChild = collectionList.Children[0];
Check(collectionList.Children.Select(n => n.Kind).SequenceEqual(["Factory", "Spread", "Wander"]) && collectionDoc.CanOrganize(spread.Id) && !adapter.Validate(collectionDoc, "Cave Pirate Brawler").Any(d => d.IsError), "Source collections retain explicit children and a typed spread input without executing or expanding the shared list");
collectionDoc.MoveNode(explicitChild.Id, 1, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(collectionDoc.Find(collectionList.Id).Children[0].Id == spread.Id && collectionDoc.Source.Contains(".. /* spread note */ StudioContextSharedList"), "Collection reorder preserves a spread's source expression, internal comments and identity");
var copiedSpread = collectionDoc.PasteNodes(targetCollection.Id, 0, collectionDoc.CopyNodes([spread.Id]), d => adapter.Validate(d, "Cave Pirate Brawler")).Single();
Check(copiedSpread != spread.Id && collectionDoc.Find(targetCollection.Id).Children[0].Kind == "Spread" && collectionDoc.Source.Contains("behaviors: ["), "Spread copy/paste binds the unchanged source input inside a destination collection with a fresh identity");
collectionDoc.MoveNodes([spread.Id], targetCollection.Id, 1, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(collectionDoc.Find(collectionList.Id).Children.All(n => n.Kind != "Spread") && collectionDoc.Find(targetCollection.Id).Children[1].Id == spread.Id, "Cross-collection spread moves preserve source inputs and stable selection identities");
var beforeSpreadError = collectionDoc.Source; var spreadRevision = collectionDoc.Revision;
Reject(() => collectionDoc.AddChild(targetCollection.Id, "..StudioContextNoSuchList", d => adapter.Validate(d, "Cave Pirate Brawler")), "An unavailable spread source rejects atomically in the destination's complete compiler context");
Reject(() => collectionDoc.AddChild(collectionPhase.Id, "..StudioContextSharedList", d => adapter.Validate(d, "Cave Pirate Brawler")), "Spread insertion requires an actual collection-expression destination");
Check(collectionDoc.Source == beforeSpreadError && collectionDoc.Revision == spreadRevision, "Rejected spread edits preserve the last valid source and revision");
var cycleCollections = Doc("new State(new Prioritize(children: [new Wander(.2)]))"); var cycleCollection = Root(cycleCollections).Children.Single().Children.Single(); var beforeBadSpread = cycleCollections.Source;
Reject(() => cycleCollections.AddChild(cycleCollection.Id, "..StudioContextSharedList", d => adapter.Validate(d, "Cave Pirate Brawler")), "A collection spread with incompatible native element types cannot enter a CycleBehavior list");
Check(cycleCollections.Source == beforeBadSpread, "Rejected spread subtype conversion preserves the destination source");
var throwingCollection = Doc("new State(new Timed(1000, behaviors: [..StudioContextThrowingList, new Wander(.2)]))"); var throwingCollectionList = Root(throwingCollection).Children.Single().Children.Single(); var throwingSpread = throwingCollectionList.Children[0];
var throwingCopy = throwingCollection.DuplicateNode(throwingSpread.Id, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(throwingCollection.Find(throwingCopy).Kind == "Spread" && throwingCollectionList.Children[0].Id == throwingSpread.Id, "Typed enumerable spreads bind and copy without executing a throwing source getter");
collectionDoc.LockedNodes.Add(spread.Id); Reject(() => collectionDoc.DeleteNode(spread.Id), "Spread region locks protect their complete source input"); collectionDoc.LockedNodes.Clear();
Check(collectionDoc.Undo() && collectionDoc.Find(collectionList.Id).Children.Any(n => n.Id == spread.Id) && collectionDoc.Redo() && collectionDoc.Find(targetCollection.Id).Children[1].Id == spread.Id, "Cross-list spread edits restore complete source and identities through undo and redo");
var multiCollections = Doc("new State(StudioContextLists(first: [StudioContextFan()], second: [new Wander(StudioContextSpeed)]))"); var multiCollectionCall = Root(multiCollections).Children.Single();
multiCollections.MoveNodes([multiCollectionCall.Children[0].Children.Single().Id], multiCollectionCall.Children[1].Id, 1, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(multiCollections.Find(multiCollectionCall.Id).Children[0].Children.Count == 0 && multiCollections.Find(multiCollectionCall.Id).Children[1].Children.Count == 2 && multiCollections.Source.Contains("first: []"), "Multiple source collection slots retain their distinct target types and empty bracket syntax");
var collectionProjectPath = Path.Combine(output, "collections.rbs"); BehaviorDocument.Save(collectionProjectPath, new StudioProject { SourceFile = file, Source = collectionDoc.Source, EntityId = "Cave Pirate Brawler", Identities = collectionDoc.Identities }); var collectionSaved = BehaviorDocument.Load(collectionProjectPath);
var collectionReopened = new BehaviorDocument(collectionSaved.Source, adapter, collectionSaved.Identities, collectionSaved.SourceFile);
Check(collectionReopened.Identities.SequenceEqual(collectionDoc.Identities) && collectionReopened.Find(spread.Id).Kind == "Spread", "Collection and spread source identities survive project save and reopen");
var typedCollections = Doc("new State(new State(\"typed collections\", StudioContextCollections(first: [StudioContextFan(count: 2), .. /* shared list */ StudioContextSharedList], second: [new Shoot(8, count: 3)], third: [new Shoot(8, count: 5)])))");
var typedCall = Root(typedCollections).StateChildren.Single().Children.Single(); var enumerable = typedCall.Children[0]; var readOnly = typedCall.Children[1]; var concrete = typedCall.Children[2]; var typedFan = enumerable.Children[0]; var typedSpread = enumerable.Children[1];
Check(typedCall.Children.Count == 3 && typedCall.Children.All(n => n.Argument is { IsArray: false, IsCollection: true, Minimum: 0 } && typedCollections.CanEditChildren(n.Id) && !typedCollections.IsScalarChildSlot(n.Id)) && enumerable.Argument!.CollectionType!.Contains("IEnumerable") && readOnly.Argument!.CollectionType!.Contains("IReadOnlyList") && concrete.Argument!.CollectionType!.Contains("List"), "Actual enumerable, read-only and concrete collection parameters expose independent typed lists rather than scalar children");
Check(typedCall.Parameters.Count == 0 && !adapter.Validate(typedCollections, "Cave Pirate Brawler").Any(d => d.IsError), "Collection arguments remain structural inputs and retain valid complete source binding");
typedCollections.MoveNodes([typedFan.Id], readOnly.Id, 1, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(typedCollections.Find(readOnly.Id).Children.Last().Id == typedFan.Id && typedCollections.Find(enumerable.Id).Children.Single().Id == typedSpread.Id, "Moving a source helper between different collection targets retains its actual binding and identity");
var typedSpreadCopy = typedCollections.PasteNodes(concrete.Id, 0, typedCollections.CopyNodes([typedSpread.Id]), d => adapter.Validate(d, "Cave Pirate Brawler")).Single();
Check(typedSpreadCopy != typedSpread.Id && typedCollections.Find(concrete.Id).Children.First().Kind == "Spread" && typedCollections.Source.Contains(".. /* shared list */ StudioContextSharedList"), "Spreads copy across native enumerable/read-only/concrete target types with retained source comments");
var typedBefore = typedCollections.Source; var typedRevision = typedCollections.Revision;
Reject(() => typedCollections.AddChild(readOnly.Id, "new Shoot(\"bad range\")", d => adapter.Validate(d, "Cave Pirate Brawler")), "An invalid collection child overload rejects through the actual source compiler");
Check(typedCollections.Source == typedBefore && typedCollections.Revision == typedRevision, "Rejected non-array collection edits preserve source and revision atomically");
typedCollections.LockedNodes.Add(readOnly.Id); Reject(() => typedCollections.DeleteNode(typedFan.Id), "Non-array collection locks protect their contained source children"); typedCollections.LockedNodes.Clear();
Check(typedCollections.Undo() && typedCollections.Find(concrete.Id).Children.Count == 1 && typedCollections.Redo() && typedCollections.Find(typedSpreadCopy).Kind == "Spread", "Non-array collection edits restore comments, source and identities through undo and redo");
var optionalEnumerable = Doc("new State(StudioContextEnumerable())"); var optionalList = Root(optionalEnumerable).Children.Single().Children.Single();
Check(optionalList.ArgumentFromCall && optionalList.Children.Count == 0 && optionalEnumerable.CanEditChildren(optionalList.Id), "A single omitted optional collection exposes an editable unassigned list");
optionalEnumerable.AddChild(optionalList.Id, "new Shoot(8, count: 6)", d => adapter.Validate(d, "Cave Pirate Brawler")); var optionalChild = optionalEnumerable.Find(optionalList.Id).Children.Single().Id;
Check(optionalEnumerable.Source.Contains("children:[") && optionalEnumerable.Find(optionalList.Id).Children.Single().Id == optionalChild && !optionalEnumerable.Source.Contains("new Behavior[]"), "Filling an omitted enumerable emits a target-typed collection and retains its slot identity");
optionalEnumerable.DeleteNode(optionalChild, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(optionalEnumerable.Source.Contains("children:[]") && optionalEnumerable.Find(optionalList.Id).Children.Count == 0, "Deleting the last enumerable child preserves a valid empty collection target");
var nullEnumerable = Doc("new State(StudioContextEnumerable(null))"); var nullList = Root(nullEnumerable).Children.Single().Children.Single(); nullEnumerable.AddChild(nullList.Id, "new Wander(.2)", d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(nullEnumerable.Source.Contains("StudioContextEnumerable([") && nullEnumerable.Find(nullList.Id).Children.Single().Kind == "Wander", "A null non-array collection becomes an actual populated collection without changing argument binding");
var sourceEnumerable = Doc("new State(StudioContextEnumerable(StudioContextThrowingList))"); var generatedEnumerableList = Root(sourceEnumerable).Children.Single().Children.Single();
Check(generatedEnumerableList.ArgumentRequiresSource && !sourceEnumerable.CanEditChildren(generatedEnumerableList.Id), "Shared/generated collection inputs keep an explicit source boundary without executing a throwing getter");
var cyclesInput = Doc("new State(StudioContextCycles(children: [new Wander(.2)]))"); var cyclesList = Root(cyclesInput).Children.Single().Children.Single();
Reject(() => cyclesInput.AddChild(cyclesList.Id, "new Taunt(\"test\")", d => adapter.Validate(d, "Cave Pirate Brawler")), "Read-only CycleBehavior targets reject incompatible native scalar children");
Reject(() => cyclesInput.AddChild(cyclesList.Id, "..StudioContextSharedList", d => adapter.Validate(d, "Cave Pirate Brawler")), "Read-only CycleBehavior targets reject incompatible spread element types");
var builtCollection = Doc("new State(new State(\"built collection\", StudioContextBuilt(children: [new Shoot(8, count: 4), ..StudioContextSharedList])))"); var builtList = Root(builtCollection).StateChildren.Single().Children.Single().Children.Single();
var builtCopy = builtCollection.DuplicateNode(builtList.Children[0].Id, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(builtCollection.Find(builtList.Id).Children.Select(n => n.Kind).SequenceEqual(["Shoot", "Shoot", "Spread"]) && builtCopy != builtList.Children[0].Id, "An actual source CollectionBuilder target supports type-checked duplication and retained spreads");
var unbuildable = Doc("new State(StudioContextUnbuildableInput())"); var unbuildableList = Root(unbuildable).Children.Single().Children.Single(); var unbuildableBefore = unbuildable.Source;
Reject(() => unbuildable.AddChild(unbuildableList.Id, "new Wander(.2)", d => adapter.Validate(d, "Cave Pirate Brawler")), "Enumerable shape alone cannot bypass an unsupported collection construction contract");
Check(unbuildable.Source == unbuildableBefore, "An unconstructible collection target leaves the original helper call untouched");
var typedProjectPath = Path.Combine(output, "typed-collections.rbs"); BehaviorDocument.Save(typedProjectPath, new StudioProject { SourceFile = file, Source = typedCollections.Source, EntityId = "Cave Pirate Brawler", Identities = typedCollections.Identities }); var typedSaved = BehaviorDocument.Load(typedProjectPath); var typedReopened = new BehaviorDocument(typedSaved.Source, adapter, typedSaved.Identities, typedSaved.SourceFile);
Check(typedReopened.Identities.SequenceEqual(typedCollections.Identities) && typedReopened.Find(readOnly.Id).Argument!.CollectionType == readOnly.Argument!.CollectionType, "Typed non-array collection slots and every child identity survive save and reopen");
var constructorCollections = Doc("new State(new State(\"constructor collections\", new StudioContextCollectionBehavior(first: [new Shoot(8, count: 6)], second: [new Shoot(8, count: 3)])))"); var constructorCollectionCall = Root(constructorCollections).StateChildren.Single().Children.Single(); var constructorFirst = constructorCollectionCall.Children[0]; var constructorSecond = constructorCollectionCall.Children[1];
Check(constructorCollectionCall.Children.Count == 2 && constructorCollectionCall.Children.All(n => n.Argument is { IsArray: false, IsCollection: true }) && constructorCollections.CanEditChildren(constructorFirst.Id), "Source-defined controller constructors expose their actual non-array collection parameters");
constructorCollections.AddChild(constructorSecond.Id, "new Shoot(8, count: 2)", d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(constructorCollections.Find(constructorFirst.Id).Children.Single().Parameters.Single(p => p.Name == "count").Expression == "6" && constructorCollections.Find(constructorSecond.Id).Children.Count == 2, "Editing one custom controller list preserves its other formal input and subtype binding");
var initializerDoc = Doc("new State(new State(\"initializer inputs\", StudioContextCollections(first: new System.Collections.Generic.List<Behavior>(capacity: StudioContextCount) { StudioContextFan(count: 2), /* initializer note */ new Shoot(8, count: 3) }, second: new System.Collections.Generic.List<Behavior>() { new Shoot(8, count: 5) }, third: new(capacity: StudioContextCount) { new Wander(.2) })))");
var initializerCall = Root(initializerDoc).StateChildren.Single().Children.Single(); var initializerFirst = initializerCall.Children[0]; var initializerSecond = initializerCall.Children[1]; var initializerThird = initializerCall.Children[2]; var initializerFan = initializerFirst.Children[0]; var initializerShoot = initializerFirst.Children[1];
Check(initializerCall.Children.All(n => !n.ArgumentFromCall && initializerDoc.CanEditChildren(n.Id)) && initializerThird.Children.Single().Kind == "Wander", "Explicit and target-typed collection initializers expose actual native child lists without constructing their collections");
initializerDoc.MoveNode(initializerShoot.Id, -1, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(initializerDoc.Find(initializerFirst.Id).Children.First().Id == initializerShoot.Id && initializerDoc.Source.Contains("capacity: StudioContextCount") && initializerDoc.Source.Contains("/* initializer note */"), "Initializer reorder retains constructor arguments, source comments and exact child identities");
var initializerCopy = initializerDoc.DuplicateNode(initializerFan.Id, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(initializerCopy != initializerFan.Id && initializerDoc.Find(initializerFirst.Id).Children.Count == 3, "Initializer duplication retains source-bound helpers and creates a fresh child identity");
initializerDoc.MoveNodes([initializerCopy], initializerSecond.Id, 1, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(initializerDoc.Find(initializerSecond.Id).Children.Last().Id == initializerCopy && initializerDoc.Find(initializerFirst.Id).Children.Count == 2, "Cross-initializer moves preserve both collection constructors and the moved helper's identity");
initializerDoc.DeleteNode(initializerThird.Children.Single().Id, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(initializerDoc.Find(initializerThird.Id).Children.Count == 0 && initializerDoc.Source.Contains("new(capacity: StudioContextCount) {"), "Deleting the last target-typed initializer child preserves the actual constructor and empty braces");
initializerDoc.AddChild(initializerThird.Id, "new Shoot(8, count: 6)", d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(initializerDoc.Find(initializerThird.Id).Children.Single().Kind == "Shoot" && initializerDoc.Source.Contains("third: new(capacity: StudioContextCount)"), "Native child creation fills a target-typed initializer without converting it to bracket syntax");
var initializerNarrow = Doc("new State(StudioContextEnumerable(new System.Collections.Generic.List<Shoot>() { new Shoot(8) }))"); var narrowInitializerList = Root(initializerNarrow).Children.Single().Children.Single();
Check(!initializerNarrow.AcceptsChild(narrowInitializerList.Id, "new Wander(.2)"), "Initializer preflight uses the concrete collection's narrower element type rather than the helper's wider enumerable type");
Reject(() => initializerNarrow.AddChild(narrowInitializerList.Id, "new Wander(.2)", d => adapter.Validate(d, "Cave Pirate Brawler")), "Incompatible concrete initializer children reject through actual native element binding");
var initializerBeforeSpread = initializerDoc.Source;
Reject(() => initializerDoc.AddChild(initializerThird.Id, "..StudioContextSharedList", d => adapter.Validate(d, "Cave Pirate Brawler")), "Spread syntax cannot enter an ordinary collection initializer");
Check(initializerDoc.Source == initializerBeforeSpread, "A rejected initializer spread preserves its complete constructor and source");
initializerDoc.LockedNodes.Add(initializerCall.Id); Reject(() => initializerDoc.DeleteNode(initializerFan.Id), "Ancestor locks protect initializer children and source-bound constructor arguments"); initializerDoc.LockedNodes.Clear();
Check(initializerDoc.Undo() && initializerDoc.Find(initializerThird.Id).Children.Count == 0 && initializerDoc.Redo() && initializerDoc.Find(initializerThird.Id).Children.Single().Kind == "Shoot", "Initializer edits restore empty braces, constructor arguments and identities through undo/redo");
var throwingInitializer = Doc("new State(StudioContextEnumerable(new StudioContextThrowingInitializer() { new Shoot(8) }))"); var throwingInitializerList = Root(throwingInitializer).Children.Single().Children.Single(); throwingInitializer.DuplicateNode(throwingInitializerList.Children.Single().Id, d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(throwingInitializer.Find(throwingInitializerList.Id).Children.Count == 2, "Collection initializer authoring and duplication never execute a throwing source constructor");
var reversedInitializer = Doc("new State(new State(\"reversed initializer\", StudioContextEnumerable(new StudioContextInitializerChildren(capacity: StudioContextCount) { new Shoot(8, count: 2), new Shoot(8, count: 4) })))"); var reverseList = Root(reversedInitializer).StateChildren.Single().Children.Single().Children.Single(); reversedInitializer.AddChild(reverseList.Id, "new Shoot(8, count: 6)", d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(reversedInitializer.Find(reverseList.Id).Children.Select(n => n.Parameters.Single(p => p.Name == "count").Expression).SequenceEqual(["2", "4", "6"]), "Custom Add-based initializers retain source input order without guessing their runtime enumeration order");
var keyedInitializer = Doc("new State(new State(\"keyed initializer\", StudioContextEnumerable(new StudioContextKeyedChildren() { { 11, new Shoot(8, count: 7) } })))"); var keyedInitializerList = Root(keyedInitializer).StateChildren.Single().Children.Single().Children.Single();
Check(keyedInitializerList.ArgumentRequiresSource && !keyedInitializer.CanEditChildren(keyedInitializerList.Id) && keyedInitializer.Source.Contains("{ 11, new Shoot"), "Multi-argument Add initializers retain an explicit source boundary and all original expressions");
var initializerProjectPath = Path.Combine(output, "initializers.rbs"); BehaviorDocument.Save(initializerProjectPath, new StudioProject { SourceFile = file, Source = initializerDoc.Source, EntityId = "Cave Pirate Brawler", Identities = initializerDoc.Identities }); var initializerSaved = BehaviorDocument.Load(initializerProjectPath); var initializerReopened = new BehaviorDocument(initializerSaved.Source, adapter, initializerSaved.Identities, initializerSaved.SourceFile);
Check(initializerReopened.Identities.SequenceEqual(initializerDoc.Identities) && initializerReopened.Source == initializerDoc.Source, "Collection initializer constructors, comments and every child identity survive save and reopen");
var emptyScalarInitializer = Doc("new State(new OnDeathBehavior(new Shoot(8) { }))"); var emptyScalarSlot = Root(emptyScalarInitializer).Children.Single();
Check(emptyScalarSlot.Children.Single().Kind == "Shoot" && emptyScalarInitializer.IsScalarChildSlot(emptyScalarSlot.Id), "Empty braces on a native scalar behavior remain its object initializer rather than becoming an empty child list");
var emptyExpandedInitializer = Doc("new State(new Shoot(8) { })");
Check(Root(emptyExpandedInitializer).Children.Single().Kind == "Shoot", "Expanded native children with empty object initializers retain their genuine behavior identity");
var initializedState = Doc("new State(\"source initialized\") { Behaviors = { new Shoot(8, count: 7) } }");
Check(Root(initializedState).Children.Single().Kind == "Opaque" && Root(initializedState).Children.Single().Label == "Source initializer" && !initializedState.CanEditChildren(Root(initializedState).Id) && BehaviorAnalysis.Analyze(Root(initializedState)).Diagnostics.Any(d => d.Message.StartsWith("Reachability analysis is incomplete")), "Native object initializers expose a source region and suppress guessed child editing and phase analysis");
Check(initializedState.SchemaAt(Root(initializedState))!.DescribeExpression(initializedState.Expression(Root(initializedState)))!.RequiresCompiled && !adapter.Validate(initializedState, "Cave Pirate Brawler").Any(d => d.IsError), "A native State object initializer selects compiled execution instead of silently losing initialized members");
var slotPath = Path.Combine(output, "multi-slot.rbs"); BehaviorDocument.Save(slotPath, new StudioProject { SourceFile = file, Source = optionalSlots.Source, EntityId = "Cave Pirate Brawler", Identities = optionalSlots.Identities }); var savedSlots = BehaviorDocument.Load(slotPath);
var reopenedSlots = new BehaviorDocument(savedSlots.Source, adapter, savedSlots.Identities, savedSlots.SourceFile);
Check(reopenedSlots.Identities.SequenceEqual(optionalSlots.Identities) && Root(reopenedSlots).Descendants().Where(n => n.Kind == "ArgumentGroup").All(n => reopenedSlots.CanEditChildren(n.Id)), "Mixed scalar and array slot layouts reopen with all identities and independent editability intact");
var structuralProject = new StudioProject { SourceFile = file, Source = structural.Source, EntityId = "Cave Pirate Brawler", Identities = structural.Identities, Seed = 7 };
var structuralPath = Path.Combine(output, "source-structure.rbs"); BehaviorDocument.Save(structuralPath, structuralProject); var structuralSaved = BehaviorDocument.Load(structuralPath);
var structuralReopened = new BehaviorDocument(structuralSaved.Source, adapter, structuralSaved.Identities, structuralSaved.SourceFile);
Check(structuralReopened.Source == structural.Source && structuralReopened.Identities.SequenceEqual(structural.Identities), "Saved duplicated helpers, nested lists and shared references reopen with every editor identity intact");
var reopenedBeforeDelete = structuralReopened.Source;
structuralReopened.DeleteNodes([copiedReference, leafFactory.Id], d => adapter.Validate(d, "Cave Pirate Brawler"));
Check(!structuralReopened.Identities.Any(i => i.Id == copiedReference || i.Id == leafFactory.Id) && structuralReopened.Undo() && structuralReopened.Source == reopenedBeforeDelete && structuralReopened.Find(copiedReference).Kind == "Reference", "Deleting a mixed helper/reference selection and undoing restores its complete source and identities");
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
await using (var native = await CompiledRuntime.StartAsync(server, project with { Source = duplicatedAttack.Source, Identities = duplicatedAttack.Identities }, artifactRoot: output))
{
    await native.AdvanceAsync(new(Y: 1));
    Check(native.Shots.Count == 2 && native.Shots.All(s => s.Count == 2) && native.Entities.Single().Phase == "source duplicate", "Duplicated source helpers execute two real native attacks inside the original phase");
}
await using (var native = await CompiledRuntime.StartAsync(server, project with { Source = slotDoc.Source, Identities = slotDoc.Identities }, artifactRoot: output))
{
    await native.AdvanceAsync(new(Y: 1));
    Check(native.Shots.Select(s => s.Count).SequenceEqual([7, 3]) && native.Entities.Single().Phase == "multi slots", "Replaced scalar and reordered params slots execute the actual helper's native callback and attack order");
    native.ExportTrace(Path.Combine(output, "multi-slot-trace.json"));
}
await using (var native = await CompiledRuntime.StartAsync(server, project with { Source = constructorSlots.Source, Identities = constructorSlots.Identities }, artifactRoot: output))
{
    await native.AdvanceAsync(new(Y: 1));
    Check(native.Shots.Select(s => s.Count).SequenceEqual([4, 6]), "Source-defined multi-slot constructor executes its actual reverse tick dispatch without an invented wrapper model");
    native.ExportTrace(Path.Combine(output, "constructor-slot-trace.json"));
}
await using (var native = await CompiledRuntime.StartAsync(server, project with { Source = collectionReopened.Source, Identities = collectionReopened.Identities }, artifactRoot: output))
{
    await native.AdvanceAsync(new(Y: 1));
    Check(native.Shots.Select(s => s.Count).SequenceEqual([2, 1, 5]) && native.Entities.Single().Phase == "source collections", "Reopened collections expand spreads in the actual native runtime while repeated references retain shared cooldown state");
    native.ExportTrace(Path.Combine(output, "collection-spread-trace.json"));
}
await using (var native = await CompiledRuntime.StartAsync(server, project with { Source = typedReopened.Source, Identities = typedReopened.Identities }, artifactRoot: output))
{
    await native.AdvanceAsync(new(Y: 1));
    Check(native.Shots.Select(s => s.Count).SequenceEqual([1, 3, 2, 5]) && native.Entities.Single().Phase == "typed collections", "Reopened enumerable/read-only/concrete inputs execute the actual helper order and retain shared spread cooldown state");
    native.ExportTrace(Path.Combine(output, "typed-collection-trace.json"));
}
await using (var native = await CompiledRuntime.StartAsync(server, project with { Source = builtCollection.Source, Identities = builtCollection.Identities }, artifactRoot: output))
{
    await native.AdvanceAsync(new(Y: 1));
    Check(native.Shots.Select(s => s.Count).SequenceEqual([4, 4, 1]), "Duplicated custom collection inputs execute the actual source CollectionBuilder and native attacks");
    native.ExportTrace(Path.Combine(output, "collection-builder-trace.json"));
}
await using (var native = await CompiledRuntime.StartAsync(server, project with { Source = constructorCollections.Source, Identities = constructorCollections.Identities }, artifactRoot: output))
{
    await native.AdvanceAsync(new(Y: 1));
    Check(native.Shots.Select(s => s.Count).SequenceEqual([3, 2, 6]), "A custom collection controller executes its actual reverse-list tick dispatch instead of a guessed authoring order");
    native.ExportTrace(Path.Combine(output, "collection-controller-trace.json"));
}
await using (var native = await CompiledRuntime.StartAsync(server, project with { Source = initializerReopened.Source, Identities = initializerReopened.Identities }, artifactRoot: output))
{
    await native.AdvanceAsync(new(Y: 1));
    Check(native.Shots.Select(s => s.Count).SequenceEqual([3, 2, 5, 2, 6]) && native.Entities.Single().Phase == "initializer inputs", "Reopened explicit and target-typed initializers execute their actual constructors and edited native child order");
    native.ExportTrace(Path.Combine(output, "initializer-trace.json"));
}
await using (var native = await CompiledRuntime.StartAsync(server, project with { Source = reversedInitializer.Source, Identities = reversedInitializer.Identities }, artifactRoot: output))
{
    await native.AdvanceAsync(new(Y: 1));
    Check(native.Shots.Select(s => s.Count).SequenceEqual([6, 4, 2]), "Custom initializer Add methods run natively and reverse source inputs according to their actual implementation");
    native.ExportTrace(Path.Combine(output, "initializer-add-trace.json"));
}
await using (var native = await CompiledRuntime.StartAsync(server, project with { Source = keyedInitializer.Source, Identities = keyedInitializer.Identities }, artifactRoot: output))
{
    await native.AdvanceAsync(new(Y: 1));
    Check(native.Shots.Single().Count == 7, "Source-retained multi-argument initializers still execute their actual Add overload in compiled preview");
}
await using (var native = await CompiledRuntime.StartAsync(server, project with { Source = initializedState.Source, Identities = initializedState.Identities }, artifactRoot: output))
{
    await native.AdvanceAsync(new(Y: 1));
    Check(native.Shots.Single().Count == 7 && native.Entities.Single().Phase == "source initialized", "Native State object initializers execute their actual collection-member additions in compiled preview");
    native.ExportTrace(Path.Combine(output, "state-initializer-trace.json"));
}
Check(File.ReadAllText(file) == original, "Source-context editing and compiled execution leave the original game file unchanged");
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { checks }, BehaviorDocument.JsonOptions));
Console.WriteLine($"{checks.Count} source-context checks passed.");
