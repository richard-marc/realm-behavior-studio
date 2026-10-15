using System.Reflection;
using Studio.Core;
using Studio.Raple;
using TKR.Shared.resources;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.worlds;

var adapter = new RapleAdapter(Path.GetFullPath(args.FirstOrDefault() ?? "../raple-realms-7-server")); var checks = 0;
void Check(bool passed, string label) { if (!passed) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
void Reject(Action action, string label) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
const string entity = "Cave Pirate Brawler";
BehaviorDocument Doc(string body) => new("class Fixture{void Build(){db.Init(\"" + entity + "\",new State(\"motion\"," + body + "));}}", adapter);
World World(EncounterSession session) { var engine = typeof(EncounterSession).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!; return (World)engine.GetType().GetField("World")!.GetValue(engine)!; }
Enemy Boss(EncounterSession session) => World(session).Enemies.Values.Single(e => e.ObjectDesc.IdName == entity);
object Stored(EncounterSession session) => Boss(session).StateStorage.Single(p => p.Key.GetType().Name == "Swirl").Value;
object Field(object value, string name) => value.GetType().GetField(name)!.GetValue(value)!;
int Timer(EncounterSession session) => (int)Field(Stored(session), "RemainingTime");
var doc = Doc("new Swirl()"); var swirl = doc.AllNodes.Single(n => n.Kind == "Swirl");
Check(adapter.Primitives.Where(p => p.Name is "Swirl" or "JumpToRandomOffset").All(p => p.Simulatable && p.Category == "Movement"), "Both original movement controllers expose native constructor editing");
Check(adapter.ReadSwirl(doc, swirl) is { Speed: 1, Radius: 8, AcquireRange: 10, Targeted: true }, "Swirl preserves all original optional defaults");
var jumpDoc = Doc("new JumpToRandomOffset(-2,3,4,4)"); var jump = jumpDoc.AllNodes.Single(n => n.Kind == "JumpToRandomOffset");
Check(adapter.ReadJumpOffset(jumpDoc, jump) is { Columns: 5, Rows: 1, LastX: 2, LastY: 4 } && adapter.ParametersFor(jumpDoc, jump).All(p => p.Unit == "integer tile offset"), "Jump preview retains exclusive maxima and equal-bound fixed offsets in native units");
var huge = Doc("new JumpToRandomOffset(int.MinValue,int.MaxValue,0,0)");
Check(adapter.ReadJumpOffset(huge, huge.AllNodes.Single(n => n.Kind == "JumpToRandomOffset")) is { Columns: 4294967295, LastX: int.MaxValue - 1 }, "Complete signed integer range computes without preview overflow");
Check(adapter.Validate(Doc("new JumpToRandomOffset(3,2,0,0)"), entity).Any(d => d.IsError), "Reversed bounds diagnose the original Random.Next entry failure");
Check(adapter.Validate(Doc("new Swirl(radius:0)"), entity).Any(d => !d.IsError && d.Message.Contains("non-finite")), "Zero native Swirl radius has an explicit movement hazard notice");
Check(!adapter.Validate(Doc("new Swirl(speed:-.3,radius:-8)"), entity).Any(d => d.IsError), "Native signed Swirl fields retain their original constructor domain");
var original = doc.Source; var ids = doc.Identities;
doc.Apply(new(doc.Revision, "Edit swirl", [new(swirl.Id, "radius", "5"), new(swirl.Id, "targeted", "false")]), d => adapter.Validate(d, entity));
Check(adapter.ReadSwirl(doc, doc.Find(swirl.Id)) is { Radius: 5, Targeted: false } && ids.All(i => doc.Identities.Any(n => n.Id == i.Id)), "Typed center/radius changes preserve native construction identities");
Check(doc.Undo() && doc.Source == original && doc.Redo(), "A complete movement edit is one Undo and Redo");
var appended = Doc("new Swirl(.3)"); var appendedNode = appended.AllNodes.Single(n=>n.Kind=="Swirl");
appended.Apply(new(appended.Revision,"Append two native defaults",[new(appendedNode.Id,"radius","5"),new(appendedNode.Id,"targeted","false")]),d=>adapter.Validate(d,entity));
Check(adapter.ReadSwirl(appended,appended.Find(appendedNode.Id)) is { Speed:.3,Radius:5,Targeted:false }, "Multiple omitted parameters append correctly beside an existing positional input");
doc.LockedNodes.Add(swirl.Id); Reject(() => doc.Apply(new(doc.Revision, "Locked", [new(swirl.Id, "radius", "6")]), d => adapter.Validate(d, entity)), "Movement edits retain region locks"); doc.LockedNodes.Clear();
Reject(() => doc.Apply(new(doc.Revision - 1, "Stale", [new(swirl.Id, "radius", "6")]), d => adapter.Validate(d, entity)), "Movement edits reject stale proposals");
jumpDoc.Apply(new(jumpDoc.Revision, "Fixed landing", [new(jump.Id, "minX", "2"), new(jump.Id, "maxX", "2"), new(jump.Id, "minY", "-1"), new(jump.Id, "maxY", "-1")]), d => adapter.Validate(d, entity));
Check(adapter.ReadJumpOffset(jumpDoc, jumpDoc.Find(jump.Id)) is { MinX: 2, MaxX: 2, MinY: -1, MaxY: -1 }, "Coupled jump bounds can change atomically without invalid intermediate ranges");
using (var native = new EncounterSession(adapter, jumpDoc, entity, 7, new(Invulnerable: true)))
{
    native.Step(new()); var host = Boss(native);
    Check(host.X == 66.5f && host.Y == 63.5f, "Native entry applies the fixed offset from current host coordinates");
    native.Advance(new(), 600); Check(host.X == 66.5f && host.Y == 63.5f, "Ordinary jump ticks do not repeat entry movement");
    var behavior = (TKR.WorldServer.logic.Behavior)Activator.CreateInstance(typeof(TKR.WorldServer.logic.State).Assembly.GetTypes().Single(t => t.Name == "JumpToRandomOffset"), 2, 2, -1, -1)!;
    host.ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Paralyzed, -1));
    behavior.OnStateEntry(host, new());
    Check(host.X == 68.5f && host.Y == 62.5f, "Original jump entry moves while paralyzed and adds to current position on re-entry");
}
var wall = NativeArena.Obstacles().First(o => o.EnemyOccupySquare && !o.Connects);
using (var native = new EncounterSession(adapter, Doc("new JumpToRandomOffset(2,2,0,0)"), entity, 7, new(Invulnerable: true), arena: new() { Tiles = [new(66, 64, "Arena Dirt", wall.Id)] }))
{ native.Step(new()); Check(Boss(native).X == 66.5f && !native.ProbeArena(66.5,64.5).Passable, "Native jump Move bypasses the original movement wall validation"); }
for (var seed = 1; seed <= 4; seed++)
{
    using var native = new EncounterSession(adapter, Doc("new JumpToRandomOffset(-2,3,-1,2)"), entity, seed, new(Invulnerable: true)); native.Step(new()); var host = Boss(native);
    Check(host.X - 64.5f is >= -2 and <= 2 && host.Y - 64.5f is >= -1 and <= 1 && host.X % 1 == .5f && host.Y % 1 == .5f, "Native random landing is an integer offset within exclusive bounds, seed " + seed);
}
using (var native = new EncounterSession(adapter, Doc("new Swirl()"), entity, 7, new(Invulnerable: true, StartX: 68.5, StartY: 67.5)))
{
    native.Step(new()); var state = Stored(native); var center = Field(state, "Center");
    Check((bool)Field(state, "Acquired") && Timer(native) > 0, "Diagonal visible player acquires a native Swirl center and period");
    var x = (float)Field(center, "X"); var y = (float)Field(center, "Y");
    var c = Math.Sqrt(Math.Abs(8 * 8 - 5 * 5) / 4d);
    Check(Math.Abs(x - (66.5 - c * 3 / 5)) < 1e-5 && Math.Abs(y - (66 + c * 4 / 5)) < 1e-5, "Observed native center retains its actual midpoint/signed perpendicular formula");
    var timer = Timer(native); native.Step(new()); Check(Timer(native) == timer - 100 && Field(Stored(native), "Center").Equals(center), "Acquired center remains fixed while native elapsed milliseconds consume its period");
    Stored(native).GetType().GetField("RemainingTime")!.SetValue(Stored(native), 100);
    native.Step(new()); Check(Timer(native)==0 && (bool)Field(Stored(native),"Acquired"), "Native period reaches zero on its decrement tick while retaining acquired state");
    native.Step(new()); Check(Timer(native)==0 && !(bool)Field(Stored(native),"Acquired"), "Following tick releases acquisition before constructing another center");
    native.Step(new()); Check(Timer(native)>0 && (bool)Field(Stored(native),"Acquired"), "Reacquisition uses the player's current native position on a later tick");
    var host = Boss(native); host.ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Paralyzed, -1)); var before = (host.X, host.Y); timer = Timer(native); native.Step(new());
    Check((host.X,host.Y) == before && Timer(native) == timer && native.NativeState.Single(s => s.Owner.EndsWith(".Swirl")).CycleStatus == "NotStarted", "Paralysis pauses Swirl position/timer and reports original NotStarted status");
}
using (var native = new EncounterSession(adapter, Doc("new Swirl()"), entity, 7, new(Invulnerable:true,StartX:68.5,StartY:67.5)))
{
    World(native).Players.Values.Single().ApplyConditionEffect(new ConditionEffect(ConditionEffectIndex.Hidden,-1));native.Step(new());
    Check(!(bool)Field(Stored(native),"Acquired"),"Hidden player is excluded by the actual native nearest-player query");
}
foreach (var position in new[] { (68.5,64.5), (64.5,68.5), (90.5,90.5) })
{
    using var native = new EncounterSession(adapter, Doc("new Swirl()"), entity, 7, new(Invulnerable: true, StartX: position.Item1, StartY: position.Item2), arenaSize: 80); native.Step(new());
    Check(!(bool)Field(Stored(native), "Acquired") && Boss(native).X < 64.5f && Boss(native).Y < 64.5f, "Failed native acquisition still steers around retained world-zero center at " + position);
}
using (var native = new EncounterSession(adapter, Doc("new Swirl(targeted:false)"), entity, 7, new(Invulnerable: true)))
{
    native.Step(new()); var center = Field(Stored(native), "Center");
    Check((float)Field(center, "X") == 64.5f && (float)Field(center, "Y") == 64.5f && Timer(native) == 5000 && (bool)Field(Stored(native), "Acquired"), "Untargeted entry stores its actual host position and native five-second refresh timer");
    native.Advance(new(), 700); Check(Field(Stored(native), "Center").Equals(center) && native.NativeState.Single(s=>s.Owner.EndsWith(".Swirl")).CycleStatus == "InProgress", "Untargeted steering retains entry center and never invents cycle completion");
}
using (var native = new EncounterSession(adapter, Doc("new Prioritize(new JumpToRandomOffset(2,2,0,0),new Swirl(targeted:false))"), entity, 7, new(Invulnerable: true)))
{
    native.Step(new());
    Check(native.NativeState.Single(s=>s.Owner.EndsWith(".Prioritize")).Value == "1" && (float)Field(Field(Stored(native),"Center"),"X") == 66.5f, "Prioritize enters every child, applies jump before Swirl entry, then selects InProgress Swirl");
}
var file = adapter.Catalog().Single(e=>e.Id==entity).SourceFile; var supplied = File.ReadAllText(file);
const string aliasesSource = "using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioSwirlAliases=()=>Behav().InitMany(\"Black Cat\",\"Snowman\",_=>new State(\"motion\",new Swirl()));}}";
var aliases = new BehaviorDocument(aliasesSource,adapter,sourceFile:file); var aliasNodes=aliases.Entities.Take(2).SelectMany(e=>e.Root.Descendants().Where(n=>n.Kind=="Swirl")).ToArray();
Check(aliasNodes.Length==2 && aliasNodes[0].Id!=aliasNodes[1].Id && aliasNodes[0].Start==aliasNodes[1].Start,"Two native range variants retain separate identities for one optional constructor span");
aliases.Apply(new(aliases.Revision,"Shared optional inputs",aliasNodes.SelectMany(n=>new[]{new EditOperation(n.Id,"radius","5"),new EditOperation(n.Id,"targeted","false")}).ToArray()));
Check(aliasNodes.All(n=>adapter.ReadSwirl(aliases,aliases.Find(n.Id)) is{Radius:5,Targeted:false}) && aliases.Undo() && aliases.Source==aliasesSource,"Repeated identical optional edits through aliases append each named argument once and Undo atomically");
Reject(()=>aliases.Apply(new(aliases.Revision,"Conflicting aliases",[new(aliasNodes[0].Id,"radius","5"),new(aliasNodes[1].Id,"radius","6")])) ,"Conflicting omitted values through native shared aliases reject atomically");
string Source(string body) => "using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{private _ StudioRandomMove=()=>Behav().Init(\""+entity+"\",new State(\"motion\","+body+"));}}";
if (!args.Contains("--shared-only"))
foreach (var body in new[] { "new Swirl()", "new JumpToRandomOffset(2,2,-1,-1)", "new Swirl(targeted:false)", "new JumpToRandomOffset(-2,3,-1,2)" })
{
    var project = new StudioProject { SourceFile=file,Source=Source(body),EntityId=entity,Seed=7,Player=new(Invulnerable:true,StartX:68.5,StartY:67.5) };
    using var literal = new EncounterSession(adapter,Doc(body),entity,7,project.Player);literal.Advance(new(),700);
    await using var compiled = await CompiledRuntime.StartAsync(adapter.ServerRoot,project);
    await using var plain = await CompiledRuntime.StartAsync(adapter.ServerRoot,project,captureStateOrigins:false);
    compiled.Recording = new(compiled);await compiled.AdvanceAsync(new(),700);await plain.AdvanceAsync(new(),700);
    if (body is "new Swirl()" or "new JumpToRandomOffset(2,2,-1,-1)")
        Check(literal.Movements.Select(m=>(m.Ms,m.FromX,m.FromY,m.ToX,m.ToY)).SequenceEqual(compiled.Movements.Select(m=>(m.Ms,m.FromX,m.FromY,m.ToX,m.ToY))), "Literal and compiled original complete movement samples agree for "+body);
    Check(compiled.Movements.Select(m=>(m.Ms,m.FromX,m.FromY,m.ToX,m.ToY)).SequenceEqual(plain.Movements.Select(m=>(m.Ms,m.FromX,m.FromY,m.ToX,m.ToY))) && compiled.Callbacks.Any(c=>c.Origin?.Start == new BehaviorDocument(project.Source,adapter,sourceFile:file).AllNodes.Single(n=>n.Kind==body.Split('(')[0][4..]).Start), "Source observation retains original motion and links native callbacks for "+body);
    var replay = EncounterReplay.Capture(project,null,EncounterReplay.Identify(adapter,project,compiled.Stamp.BuildId),compiled);var sought = await ReplayCursor.RestoreAsync(adapter,replay,700); using var soughtSession = sought.Session;
    Check(sought.Session.Movements.SequenceEqual(compiled.Movements), "Recorded native movement and state witnesses survive full replay for "+body);
    var partial = await ReplayCursor.RestoreAsync(adapter,replay,265); using var partialSession=partial.Session;
    Check(partial.Session.Ms==265 && partial.Session.Movements.All(m=>m.Ms<=265),"Partial native seek verifies the recorded movement/RNG prefix for "+body);
}
const string sharedSource = "using TKR.WorldServer.logic.behaviors;namespace TKR.WorldServer.logic{public partial class BehaviorDb{static readonly Swirl StudioSharedSwirl=new Swirl();private _ StudioSharedMove=()=>Behav().Init(\"Cave Pirate Brawler\",new State(\"motion\",StudioSharedSwirl));}}";
var sharedProject = new StudioProject{SourceFile=file,Source=sharedSource,EntityId=entity,Player=new(Invulnerable:true,StartX:68.5,StartY:67.5),Arena=new(){Actors=[new(entity,68.5,70.5)]}};
sharedProject=sharedProject with{Identities=new BehaviorDocument(sharedSource,adapter,sourceFile:file).Identities};
await using(var shared=await CompiledRuntime.StartAsync(adapter.ServerRoot,sharedProject))
{
    await shared.AdvanceAsync(new(),100);var states=shared.NativeState.Where(s=>s.Owner.EndsWith(".Swirl")).ToArray();
    File.WriteAllText(".artifacts/random-movement-shared-state.json",System.Text.Json.JsonSerializer.Serialize(new{shared.Entities,states},BehaviorDocument.JsonOptions));
    Check(states.Length==2 && states.Select(s=>s.ActorId).Distinct().Count()==2 && states.SelectMany(s=>s.Fields.Where(f=>f.Name=="Acquired")).Select(f=>f.Value).Order().SequenceEqual(new[]{"false","true"}),"One actual readonly Swirl retains distinct acquired state for two native actors");
    Check(states.Select(s=>s.Origin?.NodeId).Distinct().Count()==1 && states.All(s=>s.Origin?.NodeId!=null),"Shared actor storage retains one true source construction identity");
}
Check(File.ReadAllText(file)==supplied,"Movement authoring and compiled playback preserve supplied game source");
Console.WriteLine($"{checks} random movement checks passed.");
