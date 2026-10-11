using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Studio.Core;

namespace Studio.Raple;

public sealed record MovementTracePoint(long Ms, float X, float Y, string Phase);
public sealed record NativeMovementTrace(string EntityId, int ActorId, string? ForcedPhase, int Seed, string SourceSha256, string Runtime, ImmutableArray<MovementTracePoint> Points, string? StoppedReason);
public static class NativeMovementPreview
{
    public static async Task<NativeMovementTrace> SampleAsync(RapleAdapter adapter, StudioProject project, string? phase = null, int durationMs = 3000, CancellationToken cancellation = default)
    {
        if (durationMs is < 100 or > 10000) throw new ArgumentOutOfRangeException(nameof(durationMs), "Movement previews run for 100–10000 ms.");
        cancellation.ThrowIfCancellationRequested();
        var document = new BehaviorDocument(project.Source, adapter, project.Identities, project.SourceFile);
        IEncounterSession native = adapter.NeedsCompiledRuntime(document, project.EntityId)
            ? await CompiledRuntime.StartAsync(adapter.ServerRoot, project, phase, cancellation: cancellation)
            : new EncounterSession(adapter, document, project.EntityId, project.Seed, project.Player, project.ArenaSize, phase, arena:project.Arena);
        try
        {
            var actor = native.Entities.MinBy(e => e.Id) ?? throw new InvalidOperationException("The native preview has no encounter actor.");
            var points = ImmutableArray.CreateBuilder<MovementTracePoint>(); points.Add(new(native.Ms, actor.X, actor.Y, actor.Phase));
            string? reason = null;
            while (native.Ms < durationMs)
            {
                cancellation.ThrowIfCancellationRequested();
                await native.AdvanceAsync(new(), (int)Math.Min(100, durationMs - native.Ms), cancellation);
                var moved = native.Entities.SingleOrDefault(e => e.Id == actor.Id);
                if (moved == null) { reason = "Host left the world"; break; }
                points.Add(new(native.Ms, moved.X, moved.Y, moved.Phase));
                if (native.Dead) { reason = native.PlayerHp <= 0 ? "Player died" : "Host died"; break; }
                await Task.Yield();
            }
            return new(project.EntityId, actor.Id, phase, project.Seed, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project.Source))), native is CompiledRuntime ? "Compiled source" : "Native constructors", points.ToImmutable(), reason);
        }
        finally { if (native is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync(); else native.Dispose(); }
    }
}
