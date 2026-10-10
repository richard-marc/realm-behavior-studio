using System.Diagnostics;
using System.Text.Json;
using Studio.Core;
using Studio.Raple;

var root = Path.GetFullPath(args.FirstOrDefault() ?? "../raple-realms-7-server");
var adapter = new RapleAdapter(root);
var file = Path.Combine(root, "TKR.WorldServer/logic/db/BehaviorDb.PirateCave.cs");
var source = File.ReadAllText(file); var originalHash = SourceVerification.Fingerprint(Path.Combine(root, "TKR.WorldServer"), "*.cs");
var output = Path.GetFullPath(".artifacts/runtime"); Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); Console.WriteLine("PASS " + label); }
async Task Reject(Func<Task> action, string label) { try { await action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or TimeoutException or OperationCanceledException) { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
bool Running(int id) { try { using var process = Process.GetProcessById(id); return !process.HasExited; } catch (ArgumentException) { return false; } }
BehaviorDocument Doc(string expression, string helpers = "")
{
    var doc = new BehaviorDocument(source, adapter); var node = doc.Entities.Single(e => e.EntityId == "Cave Pirate Brawler").Root;
    doc.ReplaceSource(doc.Source.Remove(node.Start, node.Length).Insert(node.Start, expression) + helpers);
    return doc;
}
StudioProject Project(BehaviorDocument doc) => new() { SourceFile = file, Source = doc.Source, Identities = doc.Identities, EntityId = "Cave Pirate Brawler", Seed = 7, Player = new(1000, 50, 0) };
Task<CompiledRuntime> Start(BehaviorDocument doc, RuntimeLimits? limits = null, CancellationToken cancellation = default)
    => CompiledRuntime.StartAsync(root, Project(doc), artifactRoot: output, limits: limits, cancellation: cancellation);
if (args.Length == 3 && args[1] == "--orphan-probe")
{
    var orphan = await Start(Doc("new State(new Shoot(8))"));
    File.WriteAllText(args[2], orphan.ProcessId.ToString());
    Environment.Exit(0); // Intentionally bypass disposal to verify parent-death handling.
}
var context = Doc("new State(new State(\"studio\", new Wander(StudioSpeed), StudioFan(StudioCount)))", """

namespace TKR.WorldServer.logic {
    public partial class BehaviorDb {
        const double StudioSpeed = .3;
        const int StudioCount = 4;
        static TKR.WorldServer.logic.behaviors.Shoot StudioFan(int count) {
            System.Console.WriteLine("native helper output");
            return new TKR.WorldServer.logic.behaviors.Shoot(8, count: count, shootAngle: 12, fixedAngle: 90, coolDown: 200);
        }
    }
}
""");
Check(adapter.Validate(context, "Cave Pirate Brawler").Any(d => !d.IsError && d.Message.Contains("source context")), "Source-context fixture remains explicit in the restricted editor schema");
await using (var run = await Start(context))
{
    Check(run.ProcessId != Environment.ProcessId, "Compiled encounter runs in a separate native host process");
    Check(run.Frame.LoadedAssemblyHash == run.Stamp.AssemblyHash && run.Stamp.Source.SourceFiles > 500, "Host loads the edited complete original server assembly, not the baseline DLL");
    Check(run.Frame.EmbeddedResources.Order().SequenceEqual(new[] { "Oryx4.core.js", "Oryx4.game.js", "Oryx4.render.js", "Oryx4.bridge.js" }.Order()), "Compiled native runtime retains all original embedded JavaScript resources");
    Check(run.Diagnostics.Contains("native helper output"), "Native helper console output cannot corrupt the runtime protocol");
    for (var i = 0; i < 10; i++) await run.AdvanceAsync(new(Y: 1));
    Check(run.Shots.Any(s => s.Count == 4) && run.Frame.Entities.Single().Phase == "studio", "Native factories and source constants execute actual constructor values");
    Check(run.Frame.PlayerY > 64.5f && run.Frame.Ms == 1000 && run.Inputs.Count == 10, "Process host retains native timing and sampled player input");
    Check(run.Events.Where(e => e.Kind == "attack").All(e => e.NodeId == null), "Factory/grouped attack provenance is not guessed from projectile index");
    var phase = context.Entities.Single(e => e.EntityId == "Cave Pirate Brawler").Root.Descendants().Single(n => n.Label == "studio");
    Check(run.Events.Any(e => e.Kind == "transition" && e.NodeId == phase.Id), "Visible phase identity survives the process boundary");
    await run.SetBossHpAsync(.5); Check(run.Frame.BossHp == (int)(run.Frame.BossMaxHp * .5), "Native debugging commands update the authoritative host");
    await run.SetBreakpointAsync("attack"); await run.AdvanceAsync(new(), 1000);
    Check(run.Frame.PauseRequested, "Process host preserves actual attack breakpoint requests");
    run.ExportTrace(Path.Combine(output, "source-context-trace.json"));
    Check(JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "source-context-trace.json"))).RootElement.GetProperty("runtime").GetProperty("BuildId").GetString() == run.Stamp.BuildId, "Exported traces persist source/build/resource/runtime provenance");
}
var literal = Doc("new State(new State(\"native\", new Shoot(8, count: 3, shootAngle: 12, coolDown: 200), new Follow(.3, range: 1)))");
var local = new EncounterSession(adapter, literal, "Cave Pirate Brawler", 7, new(1000, 50, 0));
// The existing interpreter host is thread confined. Complete this reference run
// before awaiting IPC; continuations must not move its ThreadStatic RNG.
for (var i = 0; i < 50; i++) local.Step(new());
string literalBundle = "", literalBuild = "";
await using (var run = await Start(literal))
{
    literalBundle = run.ArtifactDirectory; literalBuild = run.Stamp.BuildId;
    for (var i = 0; i < 50; i++) await run.AdvanceAsync(new());
    File.WriteAllText(Path.Combine(output, "literal-comparison.json"), JsonSerializer.Serialize(new { local = local.Shots, compiled = run.Shots, localEntities = local.Entities, compiledEntities = run.Entities, localHp = local.PlayerHp, compiledHp = run.PlayerHp, localEvents = local.Events, compiledEvents = run.Events }, BehaviorDocument.JsonOptions));
    Check(JsonSerializer.Serialize(run.Shots) == JsonSerializer.Serialize(local.Shots), "Compiled original assembly and restricted native binding agree on seeded literal emissions");
    Check(run.Frame.Entities.Single().X == local.Entities.Single().X && run.Frame.Entities.Single().Y == local.Entities.Single().Y && run.Frame.PlayerHp == local.PlayerHp && run.Frame.PlayerHp < 1000 && run.Events.Any(e => e.Kind == "hit"),
        "Compiled process and current native session agree on movement and sampled damage");
    var pid = run.ProcessId; await run.DisposeAsync();
    Check(!Running(pid), "Disposing a runtime waits for native process exit");
}
await using (var run = await Start(literal))
{
    Check(run.Stamp.BuildId == literalBuild && run.ArtifactDirectory == literalBundle, "Unchanged compiled inputs reuse a verified private runtime bundle");
    var oldEvents = run.Events; var oldCount = oldEvents.Count;
    await run.AdvanceAsync(new(), 500);
    Check(oldEvents.Count == oldCount && run.Events.Count > oldCount, "Process presentation exposes immutable event snapshots across asynchronous ticks");
}
File.AppendAllText(Path.Combine(literalBundle, "TKR.WorldServer.pdb"), "invalidated symbols");
await using (var run = await Start(literal))
{
    Check(run.Stamp.BuildId == literalBuild && run.ArtifactDirectory != literalBundle, "Changed cached artifacts allocate a fresh bundle without overwriting loaded files");
}
await using (var run = await Start(Doc("new State(new State(\"native\", new Shoot(8, count: 3, shootAngle: 12, coolDown: 200), new Follow(.25, range: 1)))")))
    Check(run.Stamp.BuildId != literalBuild, "Edited source invalidates compiled build identity");
await Reject(() => Start(Doc("new State(new Shoot(\"invalid\"))")), "Compiler rejects invalid source before a native runtime starts");
await Reject(() => Start(Doc("new State(new TimedTransition(100, \"missing phase\"))")), "Original native state resolver rejects an encounter that compiles but cannot load");
await using (var run = await Start(literal))
{
    await Reject(() => run.AdvanceAsync(new(), 7), "Invalid tick command fails the native session rather than resuming partial state");
    await Reject(() => run.AdvanceAsync(new()), "A failed native process cannot resume");
}
var startupMarker = Path.Combine(output, "native-factory-entered.txt");
if (File.Exists(startupMarker)) File.Delete(startupMarker);
var hanging = Doc("new State(StudioHang())", $$"""

namespace TKR.WorldServer.logic { public partial class BehaviorDb {
    static TKR.WorldServer.logic.Behavior StudioHang() {
        System.IO.File.WriteAllText({{JsonSerializer.Serialize(startupMarker)}}, "native factory entered");
        while (true) System.Threading.Thread.SpinWait(10000);
    }
} }
""");
var watch = Stopwatch.StartNew();
await Reject(() => Start(hanging, new(StartupSeconds: 2)), "A nonreturning native factory is terminated by the startup time limit");
Check(File.Exists(startupMarker) && watch.Elapsed.TotalSeconds < 30, "Startup timeout evidence confirms the native factory entered and control returned within the recorded bound including compilation");
var tickMarker = Path.Combine(output, "native-tick-entered.txt");
if (File.Exists(tickMarker)) File.Delete(tickMarker);
var stalled = Doc("new State(new StudioStalledTick())", $$"""

namespace TKR.WorldServer.logic {
    public sealed class StudioStalledTick : Behavior {
        protected override void TickCore(TKR.WorldServer.core.objects.Entity host, TKR.WorldServer.core.worlds.TickTime time, ref object state) {
            System.IO.File.WriteAllText({{JsonSerializer.Serialize(tickMarker)}}, "native tick entered");
            while (true) System.Threading.Thread.SpinWait(10000);
        }
    }
}
""");
await using (var run = await Start(stalled, new(CommandSeconds: 1)))
{
    await Reject(() => run.AdvanceAsync(new()), "A nonreturning native tick is stopped by its command time limit");
    Check(File.Exists(tickMarker), "Tick timeout evidence confirms the actual custom native callback executed");
    await Reject(() => run.AdvanceAsync(new()), "A timed-out native tick requires a fresh session");
}
await Reject(() => Start(literal, new(MemoryBytes: 64 * 1024 * 1024)), "Native host startup is bounded by the configured process memory limit");
var parentMarker = Path.Combine(output, "parent-exit-host-pid.txt");
if (File.Exists(parentMarker)) File.Delete(parentMarker);
var probeInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
{ UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
foreach (var argument in new[] { System.Reflection.Assembly.GetExecutingAssembly().Location, root, "--orphan-probe", parentMarker }) probeInfo.ArgumentList.Add(argument);
using (var probe = Process.Start(probeInfo)!)
{
    var stdout = probe.StandardOutput.ReadToEndAsync(); var stderr = probe.StandardError.ReadToEndAsync();
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await probe.WaitForExitAsync(deadline.Token); await stdout; await stderr;
    Check(probe.ExitCode == 0 && File.Exists(parentMarker), "Parent-exit probe creates a real native process then exits without cleanup");
    var orphanPid = int.Parse(File.ReadAllText(parentMarker));
    try { using var orphan = Process.GetProcessById(orphanPid); using var exitBound = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await orphan.WaitForExitAsync(exitBound.Token); }
    catch (ArgumentException) { }
    Check(!Running(orphanPid), "Native host shuts down when its owning application process exits");
}
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel(); await Reject(() => Start(literal, cancellation: cancelled.Token), "Cancelled runtime construction leaves no executing encounter");
}
Check(SourceVerification.Fingerprint(Path.Combine(root, "TKR.WorldServer"), "*.cs") == originalHash && File.ReadAllText(file) == source, "Compiled execution never edits the supplied game source");
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { checks }, BehaviorDocument.JsonOptions));
Console.WriteLine($"{checks.Count} compiled runtime checks passed.");
