using System.Reflection;
using System.Text.Json;
using Studio.Core;
using Studio.Raple;
using TKR.Shared.resources;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;

var adapter = new RapleAdapter(Path.GetFullPath(args.FirstOrDefault() ?? "../raple-realms-7-server")); var checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
void Reject(Action action, string label) { try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
const string entity = "Cave Pirate Brawler"; var file = adapter.Catalog().Single(e => e.Id == entity).SourceFile; var supplied = File.ReadAllText(file);
const string source = """
using TKR.WorldServer.logic.behaviors;
namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    private _ StudioDashFixture=()=>{var dash=new Dash(speed:2,distance:4,targetRange:30,initialDelayMs:0,fullDistance:true,cooldownAfterEndMs:300);
        return Behav().Init("Cave Pirate Brawler",new State("dash",dash,new DashFlankVolley(dash,0,countPerSide:3,spreadDeg:15,cooldownMs:100)));};
} }
""";
var doc = new BehaviorDocument(source, adapter, sourceFile: file); var dash = doc.AllNodes.Single(n => n.Kind == "Dash"); var flank = doc.AllNodes.Single(n => n.Kind == "DashFlankVolley"); var root = doc.Entities.Single().Root;
Check(adapter.Primitives.Single(p => p.Name == "Dash") is { Category: "Movement", Simulatable: true } && adapter.Primitives.Single(p => p.Name == "DashFlankVolley") is { Category: "Attacks", Simulatable: true }, "Original Dash movement and linked flank attack are editable native primitives");
Check(adapter.Overloads("Dash").Count == 2 && adapter.ParametersFor(doc, dash).Single(p => p.Name == "intervalMs").Unit == "ms" && adapter.ParametersFor(doc, flank).Single(p => p.Name == "spreadDeg").Unit == "degrees", "Both native Dash constructors and original timing/geometry units are retained");
Check(adapter.ReadDash(doc, dash) is { FullDistance: true, CooldownAfterEndMs: 300, Speed: 2 } && adapter.ReadDashFlank(doc, flank) is { CountPerSide: 3, CooldownMs: 100 }, "Known controller settings preserve native defaults and end-based scheduling");
Check(adapter.DashReferences(doc, root).SequenceEqual(new[] { "dash" }) && doc.ReferencedDefinitions(flank).Any(d => d.Root.Id == dash.Id), "The phase and volley retain the same source-local Dash definition");
var arraySource=source.Replace("new State(\"dash\",dash,new DashFlankVolley(dash,0,countPerSide:3,spreadDeg:15,cooldownMs:100))","new State(\"dash\",children:new IStateChildren[]{dash,new DashFlankVolley(dash,0,countPerSide:3,spreadDeg:15,cooldownMs:100)})");
var arrayDoc=new BehaviorDocument(arraySource,adapter,sourceFile:file);var arrayRoot=arrayDoc.Entities.Single().Root;
Check(adapter.DashReferences(arrayDoc,arrayRoot).SequenceEqual(new[]{"dash"}),"Named native child arrays expose the same executed shared Dash reference");
arrayDoc.AddChild(arrayRoot.Id,"new DashFlankVolley(dash,0)",d=>adapter.Validate(d,entity));
Check(arrayDoc.AllNodes.Count(n=>n.Kind=="DashFlankVolley")==2&&adapter.DashReferences(arrayDoc,arrayDoc.Find(arrayRoot.Id)).SequenceEqual(new[]{"dash"}),"Linked volley insertion preserves an explicit native child array and its shared controller");
var getterSource="using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{static Dash FreshDash=>new Dash();private _ StudioFreshDash=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"dash\",FreshDash));}}";
var getterDoc=new BehaviorDocument(getterSource,adapter,sourceFile:file);
Check(adapter.DashReferences(getterDoc,getterDoc.Entities.Single().Root).Count==0,"A getter constructing a new Dash on every read is excluded from shared-controller creation");
var fieldSource=getterSource.Replace("static Dash FreshDash=>", "static readonly Dash FreshDash=");var fieldDoc=new BehaviorDocument(fieldSource,adapter,sourceFile:file);
Check(adapter.DashReferences(fieldDoc,fieldDoc.Entities.Single().Root).SequenceEqual(new[]{"FreshDash"}),"An actual readonly native Dash field remains available as a shared controller");
var assignedSource=source.Replace("return Behav()", "dash=new Dash();return Behav()");var assignedDoc=new BehaviorDocument(assignedSource,adapter,sourceFile:file);
Check(adapter.DashReferences(assignedDoc,assignedDoc.Entities.Single().Root).Count==0,"Reassigned local Dash values retain a source boundary instead of an assumed shared construction");
Check(adapter.ProjectileParameter(doc, flank) == "projectileIndex" && adapter.HasKnownProjectileOwner(doc, entity, flank) && adapter.ReadProjectileIndex(doc, flank) == 0, "Flank XML projectile selection belongs to the actual native host");
var ids = doc.Identities; doc.Apply(new(doc.Revision, "Adjust flank", [new(flank.Id, "countPerSide", "4"), new(flank.Id, "sideAngleDeg", "60")]), d => adapter.Validate(d, entity));
Check(adapter.ReadDashFlank(doc, doc.Find(flank.Id)) is { CountPerSide: 4, SideDegrees: 60 } && ids.All(i => doc.Identities.Any(n => n.Id == i.Id)), "Atomic native fan edits preserve the shared controller and all source identities");
doc.Undo(); Check(doc.Source == source && doc.Redo(), "Dash/volley source history preserves one complete edit"); doc.Undo();
doc.LockedNodes.Add(flank.Id); Reject(() => doc.Apply(new(doc.Revision, "Locked", [new(flank.Id, "countPerSide", "4")]), d => adapter.Validate(d, entity)), "Region locks protect linked fan edits"); doc.LockedNodes.Clear();
foreach (var (node, name, value) in new[] { (dash, "speed", "0"), (dash, "distance", "101"), (dash, "trailMs", "2001"), (dash, "dodgeChance", "1.1"), (dash, "cooldownAfterEndMs", "-2"), (flank, "countPerSide", "17"), (flank, "spreadDeg", "-1"), (flank, "cooldownMs", "99"), (flank, "sideAngleDeg", "double.NaN") })
    Reject(() => doc.Apply(new(doc.Revision, "Invalid", [new(node.Id, name, value)]), d => adapter.Validate(d, entity)), "Original native constructor rejects " + node.Kind + "." + name);
var fans = RapleAdapter.DashFans(adapter.ReadDashFlank(doc, flank)!, 0);
Check(Math.Abs(fans.Left + 105 * Math.PI / 180) < 1e-12 && Math.Abs(fans.Right - 75 * Math.PI / 180) < 1e-12 && Math.Abs(fans.Increment - 15 * Math.PI / 180) < 1e-12, "Original pure fan helper centers both perpendicular fans around a locked heading");
Reject(() => RapleAdapter.DashFans(adapter.ReadDashFlank(doc, flank)! with { SideDegrees = double.MaxValue }, 0), "Native angular overflow stays outside the finite diagram without rewriting source values");
var literalSource = "class Fixture{void Build(){db.Init(\"Cave Pirate Brawler\",new State(\"dash\",new Dash(speed:2,distance:4,initialDelayMs:0)));}}";
var literalDoc = new BehaviorDocument(literalSource, adapter);
using (var native = new EncounterSession(adapter, literalDoc, entity, 7, new(Invulnerable: true)))
{
    native.Advance(new(), 300); Check(native.Entities.Single().X > 64.5 && native.Movements.Any(m => !m.Player), "Literal Dash executes original native movement rather than an editor path prediction");
    var engine = typeof(EncounterSession).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(native)!;
    var world = (World)engine.GetType().GetField("World")!.GetValue(engine)!; var boss = world.Enemies.Values.Single(); var x = boss.X;
    boss.ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Paralyzed, -1)); native.Advance(new(), 100);
    Check(boss.X == x && native.NativeState.Single(s => s.Owner.EndsWith(".Dash")).Fields.Any(f => f.Name == "Remaining" && f.Value == "0"), "Actual paralysis cancels dash movement and clears native remaining distance");
}
var project = new StudioProject { SourceFile = file, Source = source, EntityId = entity, Identities = doc.Identities, Player = new(Invulnerable: true), Seed = 7 };
var fallbackDoc = new BehaviorDocument("class Fixture{void Build(){db.Init(\"Cave Pirate Brawler\",new State(\"dash\",new Dash(speed:2,distance:4,initialDelayMs:300,movement:new Shoot(20,count:2,coolDown:100))));}}", adapter);
Check(adapter.HasKnownProjectileOwner(fallbackDoc,entity,fallbackDoc.AllNodes.Single(n=>n.Kind=="Shoot")),"Dash child dispatch retains the original actor for XML projectile selection");
using(var native=new EncounterSession(adapter,fallbackDoc,entity,7,new(Invulnerable:true)))
{
    native.Advance(new(),300);Check(native.Shots.Select(s=>s.Ms).SequenceEqual(new long[]{0}),"Original fallback child ticks before Dash acquisition and pauses on the first moving tick");
    native.Advance(new(),300);Check(native.Shots.Count==1&&native.Entities.Single().X>64.5,"Active Dash owns movement and suppresses fallback child ticks");
}
long firstForwardVolleyMs = -1;
await using (var native = await CompiledRuntime.StartAsync(adapter.ServerRoot, project))
await using (var plain = await CompiledRuntime.StartAsync(adapter.ServerRoot, project, captureStateOrigins: false))
{
    native.Recording = new(native); await native.AdvanceAsync(new(), 600); await plain.AdvanceAsync(new(), 600);
    firstForwardVolleyMs = native.Shots.Min(s => s.Ms);
    Check(native.Entities.Single().X > 64.5 && native.Shots.Count > 0 && native.Shots.Count % 2 == 0 && native.Shots.All(s => s.Count == 3 && s.Index == 0), "Shared native Dash moves while its flank action emits paired XML volleys");
    Check(native.Shots.SequenceEqual(plain.Shots) && native.Movements.Select(m => (m.Ms,m.FromX,m.FromY,m.ToX,m.ToY)).SequenceEqual(plain.Movements.Select(m => (m.Ms,m.FromX,m.FromY,m.ToX,m.ToY))), "Callback/source observation preserves complete original movement and flank packets");
    Check(native.Shots.Chunk(2).All(pair => pair.Length == 2 && pair[0].Ms == pair[1].Ms && Math.Abs(pair[1].Angle - pair[0].Angle - Math.PI) < 1e-5 && Math.Abs(pair[0].AngleIncrement - Math.PI/12) < 1e-6), "Native two-sided packets retain original fan order, centers and increments");
    var phase = native.Entities.Single(); Check(native.Shots.All(s => s.X > 64.5 && s.X <= phase.X), "Volley packets originate from the actual moving host after Dash dispatch");
    Check(native.Callbacks.Any(c => c.Origin?.NodeId == dash.Id) && native.Callbacks.Any(c => c.Origin?.NodeId == flank.Id), "Actual native callbacks preserve shared controller and volley construction provenance");
    var tape = EncounterReplay.Capture(project, null, EncounterReplay.Identify(adapter, project, native.Stamp.BuildId), native);
    var replay = await ReplayCursor.RestoreAsync(adapter, tape, 600); using (replay.Session) Check(replay.Session.Shots.SequenceEqual(native.Shots) && replay.Session.Entities.SequenceEqual(native.Entities), "Native replay reproduces shared Dash state and complete flank emissions");
}
var detached = source.Replace("new DashFlankVolley(dash,0", "new DashFlankVolley(new Dash(),0"); var detachedDoc = new BehaviorDocument(detached, adapter, sourceFile: file);
await using (var native = await CompiledRuntime.StartAsync(adapter.ServerRoot, project with { Source = detached, Identities = detachedDoc.Identities }))
{ await native.AdvanceAsync(new(), 400); Check(native.Entities.Single().X > 64.5 && native.Shots.Count == 0, "A distinct unexecuted Dash object cannot drive a volley despite matching its type"); }
var reverse = source.Replace("new State(\"dash\",dash,new DashFlankVolley(dash,0,countPerSide:3,spreadDeg:15,cooldownMs:100))", "new State(\"dash\",new DashFlankVolley(dash,0,countPerSide:3,spreadDeg:15,cooldownMs:100),dash)");
var reverseDoc = new BehaviorDocument(reverse, adapter, sourceFile:file);
await using(var native = await CompiledRuntime.StartAsync(adapter.ServerRoot, project with {Source=reverse,Identities=reverseDoc.Identities}))
{
    await native.AdvanceAsync(new(),300);
    Check(native.Shots.Count>0&&native.Shots.Min(s=>s.Ms)==firstForwardVolleyMs+100,"Native sibling order makes a preceding volley observe Dash state from the prior tick");
}
var stunned = source.Replace("new State(\"dash\",dash,", "new State(\"dash\",new ConditionEffectBehavior(global::TKR.Shared.resources.ConditionEffectIndex.Stunned,duration:300),dash,");
var stunnedDoc = new BehaviorDocument(stunned,adapter,sourceFile:file);
await using(var native = await CompiledRuntime.StartAsync(adapter.ServerRoot,project with{Source=stunned,Identities=stunnedDoc.Identities}))
{
    await native.AdvanceAsync(new(),100);
    Check(native.Shots.Count==0&&native.Entities.Single().X>64.5,"Native Stunned suppresses initial flank fire without suppressing Dash movement");
    await native.AdvanceAsync(new(),500);
    Check(native.Shots.Count==0&&native.NativeState.Single(s=>s.Owner.EndsWith(".Dash")).Fields.Any(f=>f.Name=="Remaining"&&float.Parse(f.Value,System.Globalization.CultureInfo.InvariantCulture)>0),"A stunned first observation retains the original minimum-long cadence boundary and suppresses the rest of that dash");
    foreach (var duration in new[] { 1000, 1000, 400 }) await native.AdvanceAsync(new(),duration);
    Check(native.Shots.Count>0&&native.Shots.Min(s=>s.Ms)>=2000&&native.Shots.GroupBy(s=>s.Ms).All(g=>g.Count()==2),"A later native dash resets observation and resumes paired volleys without a backlog");
}
await using(var native = await CompiledRuntime.StartAsync(adapter.ServerRoot,project with{Arena=new(){Actors=[new(entity,64.5,67.5)]}}))
{
    await native.AdvanceAsync(new(),300);var states=native.NativeState.Where(s=>s.Owner.EndsWith(".Dash")).ToArray();
    Check(states.Length==2&&states.Select(s=>s.ActorId).Distinct().Count()==2&&states.Select(s=>s.Origin!.Start).Distinct().Count()==1,"Shared Dash objects keep separate native motion storage for each actual actor");
    Check(native.Shots.Select(s=>s.OwnerId).Distinct().Count()==2&&states.Select(s=>s.Fields.Single(f=>f.Name=="Y").Value).Distinct().Count()==2,"Each actor locks its actual target heading and emits from its own movement");
}
var igrisFile=adapter.Catalog().Single(e=>e.Id=="Igris").SourceFile;var igrisSource=File.ReadAllText(igrisFile);var igrisDoc=new BehaviorDocument(igrisSource,adapter,sourceFile:igrisFile);var igrisPhase=igrisDoc.AllNodes.Single(n=>n.Kind=="State"&&igrisDoc.StateName(n.Id)=="p1-thrust");
Check(adapter.DashReferences(igrisDoc,igrisPhase).SequenceEqual(new[]{"thrust"})&&adapter.ReadDashFlank(igrisDoc,igrisDoc.AllNodes.Single(n=>n.Kind=="DashFlankVolley")) is{Projectile:5,CountPerSide:3},"Supplied Igris source exposes its actual shared thrust and owned flank projectile");
var igrisProject=new StudioProject{SourceFile=igrisFile,Source=igrisSource,EntityId="Igris",Identities=igrisDoc.Identities,Player=new(Invulnerable:true),Seed=7};
await using(var native=await CompiledRuntime.StartAsync(adapter.ServerRoot,igrisProject,phase:"p1-thrust"))
{
    await native.AdvanceAsync(new(),400);
    Check(native.Entities.Single(e=>e.Name=="Igris").X>64.5&&native.Shots.Count>=2&&native.Shots.All(s=>s.Index==5&&s.Count==3),"Supplied Igris thrust phase executes real shared Dash movement and original XML flank volleys");
}
Check(File.ReadAllText(igrisFile)==igrisSource,"Complex-boss authoring and forced-phase testing preserve supplied Igris source");
var output = Path.GetFullPath(".artifacts/dash"); Directory.CreateDirectory(output); var saved = Path.Combine(output, "dash.rbs"); BehaviorDocument.Save(saved, project); var reopened = BehaviorDocument.Load(saved);
Check(reopened.Source == source && reopened.Identities.SequenceEqual(project.Identities), "Saved source retains shared Dash references and accepted identities");
Check(File.ReadAllText(file) == supplied, "Dash authoring and playback preserve supplied game source");
Console.WriteLine($"{checks} native dash-controller checks passed.");
