using Studio.Core;

namespace Studio.Raple;

public record RuntimeLimits(int StartupSeconds = 30, int CommandSeconds = 3, long MemoryBytes = 1_073_741_824);
public record RuntimeSourceBinding(int Start, string Expression, SourceExpression Description);
public record RuntimeStart(string ServerRoot, StudioProject Project, string ResourceHash, string? Phase = null, RuntimeSourceBinding[]? SourceBindings = null);
public record RuntimeRequest(int Version, long Sequence, string Command, RuntimeStart? Start = null, EncounterInput? Input = null,
    int DurationMs = 100, double Fraction = 1, string? Breakpoint = null, CombatPreviewOptions? Preview = null, bool Density = true);
public record RuntimeResponse(int Version, long Sequence, RuntimeFrame? Frame = null, string? Error = null, CombatSample? Preview = null);
public record RuntimeFrame(long Ms, int PlayerHp, float PlayerX, float PlayerY, int BossHp, int BossMaxHp, int Seed, double StartMs,
    string PlayerClass, WeaponEntry Weapon, double PlayerFacing, double PlayerAttackAngle, double PlayerMoveSpeed,
    long PlayerAttackStart, double PlayerAttackPeriod, int[] PlayerStats, string Coverage, bool PauseRequested,
    EntityView[] Entities, BulletView[] Projectiles, EncounterEvent[] Events, Emission[] Shots, Emission[] PlayerShots, string Timers,
    string LoadedAssemblyHash, string[] EmbeddedResources, string RuntimeVersion, MovementSample[] MovementDelta,ArenaSceneView? ArenaDelta);
public record NativeRuntimeStamp(int Version, string BuildId, NativeSourceStamp Source, string HostHash, string ResourceHash,
    string AssemblyHash, string SymbolsHash, string RuntimeVersion, string SourceFramework = "net8.0", string HostFramework = "net10.0");

public static class RuntimeProtocol
{
    public const int Version = 4;
    public const int MaxLineLength = 8 * 1024 * 1024;
    public static RuntimeFrame Capture(EncounterSession session, ref int events, ref int shots, ref int playerShots, ref int movements,ref long arenaRevision)
    {
        var frame = new RuntimeFrame(session.Ms, session.PlayerHp, session.PlayerX, session.PlayerY, session.BossHp, session.BossMaxHp,
            session.Seed, session.StartMs, session.PlayerClass, session.Weapon, session.PlayerFacing, session.PlayerAttackAngle,
            session.PlayerMoveSpeed, session.PlayerAttackStart, session.PlayerAttackPeriod, session.PlayerStats.ToArray(), session.Coverage,
            session.PauseRequested, session.Entities.ToArray(), session.Projectiles.ToArray(), session.Events.Skip(events).ToArray(),
            session.Shots.Skip(shots).ToArray(), session.PlayerShots.Skip(playerShots).ToArray(), session.InspectTimers(), LoadedAssemblyHash, EmbeddedResources, Environment.Version.ToString(),session.Movements.Skip(movements).ToArray(),session.Arena.Revision==arenaRevision?null:session.Arena);
        events = session.Events.Count; shots = session.Shots.Count; playerShots = session.PlayerShots.Count;
        movements=session.Movements.Count;
        arenaRevision=session.Arena.Revision;
        return frame;
    }
    static readonly string LoadedAssemblyHash = SourceVerification.Hash([("TKR.WorldServer.dll", File.ReadAllBytes(typeof(TKR.WorldServer.logic.State).Assembly.Location))]);
    static readonly string[] EmbeddedResources = typeof(TKR.WorldServer.logic.State).Assembly.GetManifestResourceNames();
}
