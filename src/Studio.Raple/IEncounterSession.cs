namespace Studio.Raple;

// Presentation consumes sampled state, never engine objects across a process boundary.
public interface IEncounterSession : IDisposable
{
    long Ms { get; }
    int PlayerHp { get; }
    bool Dead { get; }
    float PlayerX { get; }
    float PlayerY { get; }
    int BossHp { get; }
    int BossMaxHp { get; }
    int Seed { get; }
    double StartMs { get; }
    IReadOnlyList<EncounterEvent> Events { get; }
    IReadOnlyList<Emission> Shots { get; }
    IReadOnlyList<Emission> PlayerShots { get; }
    IReadOnlyList<NativeEffect> Effects { get; }
    IReadOnlyList<ReplayInput> Inputs { get; }
    ReplayJournal? Recording { get; set; }
    IReadOnlyList<MovementSample> Movements { get; }
    ArenaSceneView Arena { get; }
    bool PauseRequested { get; }
    string? BreakpointKind { get; set; }
    string PlayerClass { get; }
    WeaponEntry Weapon { get; }
    double PlayerFacing { get; }
    double PlayerAttackAngle { get; }
    double PlayerMoveSpeed { get; }
    long PlayerAttackStart { get; }
    double PlayerAttackPeriod { get; }
    IReadOnlyList<int> PlayerStats { get; }
    string Coverage { get; }
    IReadOnlyList<EntityView> Entities { get; }
    IReadOnlyList<WorldObjectView> WorldObjects { get; }
    IReadOnlyList<BulletView> Projectiles { get; }
    void SetBossHpFraction(double fraction);
    void RemoveMinionsForTest();
    void Advance(EncounterInput input, int durationMs);
    void Step(EncounterInput input) => Advance(input, 100);
    Task AdvanceAsync(EncounterInput input, int durationMs, CancellationToken cancellation = default)
    { cancellation.ThrowIfCancellationRequested(); Advance(input, durationMs); return Task.CompletedTask; }
    void ExportTrace(string path);
    string InspectTimers();
}
