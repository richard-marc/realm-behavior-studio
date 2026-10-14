using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Studio.Core;

namespace Studio.Raple;

public sealed record CombatInputStep(int DurationMs, EncounterInput Input);
public sealed record CombatPathPoint(long Ms, float X, float Y, int Hp, bool Attacking);
public sealed record CombatPreviewOptions(int DurationMs = 3000, int Columns = 9, double Spacing = 2, double CenterX = 64.5, double CenterY = 64.5)
{
    public ImmutableArray<CombatInputStep> Inputs { get; init; } = [];
    public ImmutableArray<int> Seeds { get; init; } = [];
    public string InputLabel { get; init; } = "Stationary";
    public double MinimumX => CenterX - (Columns - 1) * Spacing / 2;
    public double MinimumY => CenterY - (Columns - 1) * Spacing / 2;
    public void Validate()
    {
        if (DurationMs is < 100 or > 10000 || DurationMs % EncounterSession.SampleMs != 0 || Columns is < 1 or > 9 || Columns % 2 == 0 || !double.IsFinite(Spacing) || Spacing is < 1 or > 8 || !double.IsFinite(CenterX) || !double.IsFinite(CenterY) || CenterX is < 0 or >= 128 || CenterY is < 0 or >= 128)
            throw new ArgumentException("Preview needs 100–10000 ms, an odd 1–9 point grid and 1–8 tile spacing inside the prototype map.");
        if (Inputs.IsDefault || Seeds.IsDefault || Inputs.Length > 2000 || Seeds.Length > 8 || Seeds.Distinct().Count() != Seeds.Length || InputLabel == null || InputLabel.Length > 128) throw new ArgumentException("Preview needs a bounded input sequence and up to eight distinct seeds.");
        long duration = 0;
        foreach (var step in Inputs)
        {
            if (step == null || step.Input == null || step.DurationMs < 5 || step.DurationMs % EncounterSession.SampleMs != 0 ||
                !double.IsFinite(step.Input.X) || !double.IsFinite(step.Input.Y) || Math.Abs(step.Input.X) > 1 || Math.Abs(step.Input.Y) > 1 ||
                !double.IsFinite(step.Input.AimX) || !double.IsFinite(step.Input.AimY) || step.Input.AimX is < 0 or >= 128 || step.Input.AimY is < 0 or >= 128)
                throw new ArgumentException("Input steps need five-millisecond durations, valid movement axes and native map aim coordinates.");
            duration += step.DurationMs;
        }
        if (duration > DurationMs) throw new ArgumentException("The input sequence exceeds the selected preview duration.");
    }
}
public sealed record CombatSample(int ActualMs, int InitialHp, int RemainingHp, bool PlayerDied, int EmittedProjectiles, int PeakIncoming, double MeanIncoming, string Coverage, ImmutableArray<int> ExposureSamples, float StartX, float StartY, float EndX, float EndY, string PlayerClass, bool PlayerMoved, bool DensitySampled)
{
    public ImmutableArray<CombatPathPoint> Path { get; init; } = [];
    public int PlayerProjectiles { get; init; }
    public int HealthLoss => Math.Max(0, InitialHp - RemainingHp);
    public bool IncompleteMotion => Coverage.Contains("Unsupported", StringComparison.OrdinalIgnoreCase);
}
public sealed record CombatTestPoint(double X, double Y, CombatSample? Sample, string? Error);
public sealed record CombatPreviewProgress(int Completed, int Total, CombatTestPoint? Latest);
public sealed record CombatScenarioReport(int Seed, CombatSample Reference, ImmutableArray<CombatTestPoint> Points);
public sealed record CombatPreviewReport(string EntityId, string SourceSha256, string ResourceHash, string? BuildId, int Seed, int ArenaSize, PlayerPreset Player, string? ForcedPhase, string Runtime, CombatPreviewOptions Options, CombatSample Reference, ImmutableArray<CombatTestPoint> Points)
{
    public double BossX { get; init; } = 64.5;
    public double BossY { get; init; } = 64.5;
    public ImmutableArray<CombatScenarioReport> Scenarios { get; init; } = [];
    public CombatScenarioReport Scenario(int index = 0) => Scenarios.IsDefaultOrEmpty ? new(Seed, Reference, Points) : Scenarios[Math.Clamp(index, 0, Scenarios.Length - 1)];
    public bool NoDamageObserved(int index) => !Player.Invulnerable && !Player.Spectator &&
        (Scenarios.IsDefaultOrEmpty ? [new CombatScenarioReport(Seed, Reference, Points)] : Scenarios).All(s =>
            index >= 0 && index < s.Points.Length && s.Points[index].Sample is { PlayerDied: false, IncompleteMotion: false } sample &&
            sample.ActualMs == Options.DurationMs && sample.HealthLoss == 0);
}

public static class NativeCombatSampling
{
    // Called on the same thread as native initialization, including in workers.
    public static CombatSample Run(IEncounterSession native, CombatPreviewOptions options, bool density, CancellationToken cancellation = default)
    {
        options.Validate(); cancellation.ThrowIfCancellationRequested();
        var began = native.Ms; var hp = native.PlayerHp; var shots = native.Shots.Count;
        var startX = native.PlayerX; var startY = native.PlayerY; var moved = false;
        var friendly = native.PlayerShots.Count;
        var path = ImmutableArray.CreateBuilder<CombatPathPoint>(); path.Add(new(0, startX, startY, hp, false));
        var inputIndex = 0; long inputEnd = options.Inputs.Length > 0 ? options.Inputs[0].DurationMs : 0;
        var exposure = new int[options.Columns * options.Columns]; var samples = 0; var peak = 0; long incomingTotal = 0;
        while (native.Ms - began < options.DurationMs && !native.Dead)
        {
            cancellation.ThrowIfCancellationRequested(); var elapsed = native.Ms - began;
            while (inputIndex < options.Inputs.Length && elapsed >= inputEnd)
            {
                inputIndex++;
                if (inputIndex < options.Inputs.Length) inputEnd += options.Inputs[inputIndex].DurationMs;
            }
            var input = inputIndex < options.Inputs.Length ? options.Inputs[inputIndex].Input : new EncounterInput();
            native.Advance(input, EncounterSession.SampleMs);
            if ((native.Ms - began) % 100 == 0 || native.Dead || native.Ms - began == options.DurationMs)
                path.Add(new(native.Ms - began, native.PlayerX, native.PlayerY, native.PlayerHp, input.Attack));
            moved |= native.PlayerX != startX || native.PlayerY != startY;
            if (!density) continue;
            var incoming = native.Projectiles.Where(p => !p.Friendly).ToArray(); peak = Math.Max(peak, incoming.Length); incomingTotal += incoming.Length; samples++;
            foreach (var bullet in incoming)
            {
                if (!float.IsFinite(bullet.X) || !float.IsFinite(bullet.Y)) throw new InvalidOperationException("Native projectile position is not finite; the preview is unavailable.");
                // Sample the same inclusive square player footprint at grid
                // centers. Only nearby centers are visited, never all cells.
                var fromX = Math.Max(0, Math.Ceiling((bullet.X - .5 - options.MinimumX) / options.Spacing));
                var toX = Math.Min(options.Columns - 1, Math.Floor((bullet.X + .5 - options.MinimumX) / options.Spacing));
                var fromY = Math.Max(0, Math.Ceiling((bullet.Y - .5 - options.MinimumY) / options.Spacing));
                var toY = Math.Min(options.Columns - 1, Math.Floor((bullet.Y + .5 - options.MinimumY) / options.Spacing));
                if (fromX > toX || fromY > toY) continue;
                for (var y = (int)fromY; y <= toY; y++) for (var x = (int)fromX; x <= toX; x++) exposure[y * options.Columns + x]++;
            }
        }
        return new CombatSample((int)(native.Ms - began), hp, native.PlayerHp, native.Dead, native.Shots.Skip(shots).Sum(s => s.Count), peak, samples == 0 ? 0 : (double)incomingTotal / samples, native.Coverage, exposure.ToImmutableArray(), startX, startY, native.PlayerX, native.PlayerY, native.PlayerClass, moved, density) { Path = path.ToImmutable(), PlayerProjectiles = native.PlayerShots.Skip(friendly).Sum(s => s.Count) };
    }
}

public static class CombatPreview
{
    public static async Task<CombatPreviewReport> RunAsync(RapleAdapter adapter, StudioProject project, string? phase = null, CombatPreviewOptions? options = null, IProgress<CombatPreviewProgress>? progress = null, CancellationToken cancellation = default)
    {
        options ??= new(); options.Validate(); cancellation.ThrowIfCancellationRequested();
        adapter = adapter.ForProject(project);
        var document = new BehaviorDocument(project.Source, adapter, project.Identities, project.SourceFile);
        var compiled = adapter.NeedsCompiledRuntime(document, project.EntityId);
        var prepared = compiled ? await CompiledRuntime.PrepareAsync(adapter.ServerRoot, project, limits: new(CommandSeconds: 30), cancellation: cancellation).ConfigureAwait(false) : null;
        var resourceRoot = Path.Combine(adapter.ServerRoot, "TKR.Shared/resources");
        var resourceHash = prepared?.Stamp.ResourceHash ?? await Task.Run(() => SourceVerification.Fingerprint(resourceRoot, "*"), cancellation).ConfigureAwait(false);
        if (!compiled && resourceHash != adapter.LoadedResourceHash) throw new InvalidOperationException("Loaded game resources differ from the current files. Restart Studio before previewing attacks.");
        async Task<CombatSample> Sample(PlayerPreset player, bool density, int seed)
        {
            cancellation.ThrowIfCancellationRequested();
            if (prepared != null)
            {
                await using var native = await prepared.StartAsync(player, phase, cancellation, seed).ConfigureAwait(false);
                return await native.SampleCombatAsync(options, density, cancellation).ConfigureAwait(false);
            }
            return await Task.Run(() =>
            {
                using var native = new EncounterSession(adapter, document, project.EntityId, seed, player, project.ArenaSize, phase, arena: project.Arena);
                return NativeCombatSampling.Run(native, options, density, cancellation);
            }, cancellation).ConfigureAwait(false);
        }
        var seeds = options.Seeds.IsEmpty ? ImmutableArray.Create(project.Seed) : options.Seeds; var scenarios = ImmutableArray.CreateBuilder<CombatScenarioReport>();
        var total = (options.Columns * options.Columns + 1) * seeds.Length; var completed = 0; progress?.Report(new(0, total, null));
        foreach (var seed in seeds)
        {
            var reference = await Sample(project.Player, true, seed).ConfigureAwait(false); progress?.Report(new(++completed, total, null));
            var points = ImmutableArray.CreateBuilder<CombatTestPoint>();
            for (var y = 0; y < options.Columns; y++) for (var x = 0; x < options.Columns; x++)
            {
                var px = options.MinimumX + x * options.Spacing; var py = options.MinimumY + y * options.Spacing;
                CombatTestPoint point;
                try { point = new(px, py, await Sample(project.Player with { StartX = px, StartY = py }, false, seed).ConfigureAwait(false), null); }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException && error.Message.Contains("Player start", StringComparison.Ordinal))
                { point = new(px, py, null, error.Message); }
                points.Add(point); progress?.Report(new(++completed, total, point));
            }
            scenarios.Add(new(seed, reference, points.ToImmutable()));
        }
        if (await Task.Run(() => SourceVerification.Fingerprint(resourceRoot, "*"), cancellation).ConfigureAwait(false) != resourceHash) throw new InvalidOperationException("Game resources changed during the preview. Run it again with the current resources.");
        return new CombatPreviewReport(project.EntityId, SourceVerification.EditedSourceHash(project), resourceHash, prepared?.Stamp.BuildId, seeds[0], project.ArenaSize, project.Player, phase, compiled ? "Compiled source" : "Native constructors", options, scenarios[0].Reference, scenarios[0].Points) { Scenarios = scenarios.ToImmutable(), BossX = project.Arena.BossX, BossY = project.Arena.BossY };
    }
}
