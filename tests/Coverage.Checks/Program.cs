using System.Diagnostics;
using System.Text.Json;
using Studio.Core;
using Studio.Raple;

var server = Path.GetFullPath(args.FirstOrDefault() ?? "../raple-realms-7-server"); var adapter = new RapleAdapter(server);
var output = Path.GetFullPath(".artifacts/coverage"); Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); Console.WriteLine("PASS " + label); }
void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
bool Running(int id) { try { using var p = Process.GetProcessById(id); return !p.HasExited; } catch (ArgumentException) { return false; } }
var source = "class Fixture { void Build() { db.Init(\"Cave Pirate Brawler\", new State(new Shoot(20, fixedAngle: 0, coolDown: 1000))); } }";
var project = new StudioProject { Source = source, Seed = 7, Player = new(Defense: 0, StartX: 67.5) };
var document = new BehaviorDocument(source, adapter);
using (var native = new EncounterSession(adapter, document, project.EntityId, 7, new(StartX: 68.5, StartY: 66.5)))
{
    Check(native.PlayerX == 68.5 && native.PlayerY == 66.5, "Player start uses the original host coordinates before encounter entry");
    native.Step(new()); Check(native.PlayerX == 68.5 && native.PlayerY == 66.5, "Stationary input retains a custom native player start");
}
Reject(() => new EncounterSession(adapter, document, project.EntityId, 7, new(StartX: double.NaN)), "Non-finite native player starts reject before initialization");
Reject(() => new EncounterSession(adapter, document, project.EntityId, 7, new(StartX: -1)), "Out-of-map native player starts reject");
Reject(() => new EncounterSession(adapter, document, project.EntityId, 7, new(StartX: 127.9999999)), "A double start that rounds outside the float map rejects before initialization");
Reject(() => new EncounterSession(adapter, document, project.EntityId, 7, new(StartX: 10)), "Native player starts respect the active prototype arena bounds");
var saved = Path.Combine(output, "start-position.rbs"); BehaviorDocument.Save(saved, project with { Player = new(StartX: 68.5, StartY: 66.5) });
Check(BehaviorDocument.Load(saved).Player is { StartX: 68.5, StartY: 66.5 } && JsonSerializer.Deserialize<PlayerPreset>("{}") is { StartX: 70.5, StartY: 64.5 }, "Start positions persist and older player presets retain their original defaults");
Reject(() => new CombatPreviewOptions(Columns: 2).Validate(), "Preview grids reject even dimensions");
Reject(() => new CombatPreviewOptions(DurationMs: 101).Validate(), "Preview durations retain the actual five-millisecond sample clock");
var options = new CombatPreviewOptions(Columns: 3, Spacing: 1, CenterX: 67.5);
var progress = new List<CombatPreviewProgress>();
var report = await CombatPreview.RunAsync(adapter, project, options: options, progress: new Sink(progress));
Check(report.Points.Length == 9 && progress.First().Completed == 0 && progress.Last() is { Completed: 10, Total: 10 }, "Area preview reports each completed native run and the reference density run");
Check(report.Reference.DensitySampled && report.Points.All(p => !p.Sample!.DensitySampled) && report.Reference.PeakIncoming > 0 && report.Reference.MeanIncoming > 0 && report.Reference.EmittedProjectiles > 0 && report.Reference.ExposureSamples.Any(n => n > 0), "Reference density uses actual sampled native projectiles and emitted packets; damage-only tests retain explicit density collection status");
Check(report.ResourceHash == adapter.LoadedResourceHash, "Literal preview provenance matches the resources actually loaded by its native host");
var near = report.Points.Single(p => p.X == 67.5 && p.Y == 64.5); var far = report.Points.Single(p => p.X == 68.5 && p.Y == 64.5); var offLine = report.Points.Single(p => p.X == 67.5 && p.Y == 65.5);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, BehaviorDocument.JsonOptions));
Check(near.Sample is { HealthLoss: > 0, ActualMs: 3000 } && offLine.Sample is { HealthLoss: 0, ActualMs: 3000 }, "Fresh native damage tests distinguish a hit position from an unhit position with the same scene, seed and duration");
Check(far.Sample?.HealthLoss > 0 && report.Reference.ExposureSamples[5] == 0, "Area damage reruns targeting and collision at each point instead of treating the reference player's consumed projectile as safety elsewhere");
Check(report.Points.All(p => p.Sample != null && p.Sample.StartX == p.X && p.Sample.StartY == p.Y && !p.Sample.PlayerMoved) && report.SourceSha256.Length == 64 && report.ResourceHash.Length == 64 && report.Runtime == "Native constructors", "Native reports retain actual starts, stationary input, source/resource fingerprints and execution path");
var repeated = await CombatPreview.RunAsync(adapter, project, options: options);
Check(report.Reference == repeated.Reference with { ExposureSamples = report.Reference.ExposureSamples } && report.Points.Select(p => p.Sample!.HealthLoss).SequenceEqual(repeated.Points.Select(p => p.Sample!.HealthLoss)), "Native area damage and density repeat the same seed and inputs");
using (var defended = new EncounterSession(adapter, document, project.EntityId, 7, new(StartX: 68.5)))
{
    var sample = NativeCombatSampling.Run(defended, options, true);
    Check(sample.HealthLoss == 0 && defended.Events.Any(e => e.Kind == "hit" && e.Message.Contains("−0 HP")), "Zero observed health loss preserves the actual defense formula even when projectiles hit");
}
using (var mortal = new EncounterSession(adapter, document, project.EntityId, 7, new(Hp: 1, Defense: 0, StartX: 68.5)))
{
    var sample = NativeCombatSampling.Run(mortal, options, true);
    Check(sample is { PlayerDied: true, HealthLoss: 1, RemainingHp: 0 } && sample.ActualMs < options.DurationMs, "Fatal tests stop at native death and preserve the actual shorter duration");
}
var border = await CombatPreview.RunAsync(adapter, project, options: new(Columns: 3, CenterX: 40.5, CenterY: 40.5));
Check(border.Points.Any(p => p.Sample == null && p.Error!.Contains("Player start")) && border.Points.Any(p => p.Sample != null), "Out-of-arena sample points remain explicitly unavailable beside valid native tests");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel(); var rejected = false; try { await CombatPreview.RunAsync(adapter, project, cancellation: cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
    Check(rejected, "Cancelled area previews do not initialize a native encounter");
}
var file = Path.Combine(server, "TKR.WorldServer/logic/db/BehaviorDb.PirateCave.cs"); var original = File.ReadAllText(file); var originalDoc = new BehaviorDocument(original, adapter); var root = originalDoc.Entities.Single(e => e.EntityId == project.EntityId).Root;
var factorySource = original.Remove(root.Start, root.Length).Insert(root.Start, "StudioCoverageRoot()") + "\nnamespace TKR.WorldServer.logic { public partial class BehaviorDb { static State StudioCoverageRoot() => new State(new Shoot(20, fixedAngle: 0, coolDown: 1000)); } }";
var factoryProject = project with { Source = factorySource, SourceFile = file };
var prepared = await CompiledRuntime.PrepareAsync(server, factoryProject, artifactRoot: Path.Combine(output, "runtime"));
int firstPid; string firstDirectory;
await using (var native = await prepared.StartAsync(new(Defense: 0, StartX: 68.5, StartY: 64.5)))
{
    firstPid = native.ProcessId; firstDirectory = native.ArtifactDirectory;
    var sample = await native.SampleCombatAsync(options);
    Check(sample.HealthLoss > 0 && sample.ActualMs == 3000 && sample.StartX == 68.5 && native.Inputs.Count == 600 && native.Inputs.All(i => i.DurationMs == 5), "Compiled workers sample actual native damage/density at custom starts and preserve replayable five-millisecond inputs");
}
Check(!Running(firstPid), "Compiled reference sampling disposes its native worker");
int secondPid;
await using (var native = await prepared.StartAsync(new(Defense: 0, StartX: 68.5, StartY: 68.5)))
{
    secondPid = native.ProcessId; var sample = await native.SampleCombatAsync(options, false);
    Check(native.ProcessId != firstPid && native.ArtifactDirectory == firstDirectory && native.Stamp.BuildId == prepared.Stamp.BuildId && sample.HealthLoss == 0, "Prepared source reuses verified compilation in a fresh process with independent native state and a different player position");
}
Check(!Running(secondPid), "Compiled point sampling disposes its independent native worker");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel(); var rejected = false;
    try { await prepared.StartAsync(cancellation: cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
    Check(rejected, "Cancelled prepared scenarios reject before launching a native worker");
}
var assemblyFile = Path.Combine(firstDirectory, "TKR.WorldServer.dll"); var assemblyBytes = File.ReadAllBytes(assemblyFile);
try
{
    File.WriteAllBytes(assemblyFile, [0]); var rejected = false;
    try { await prepared.StartAsync(); } catch (InvalidOperationException e) when (e.Message.Contains("Prepared runtime files changed")) { rejected = true; }
    Check(rejected, "Each prepared scenario rejects a modified private runtime bundle before process launch");
}
finally { File.WriteAllBytes(assemblyFile, assemblyBytes); }
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, BehaviorDocument.JsonOptions));
Check(File.ReadAllText(file) == original, "Native and compiled coverage previews leave the original game source untouched");
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { checks }, BehaviorDocument.JsonOptions));
Console.WriteLine($"{checks.Count} coverage preview checks passed.");

sealed class Sink(List<CombatPreviewProgress> values) : IProgress<CombatPreviewProgress> { public void Report(CombatPreviewProgress value) => values.Add(value); }
