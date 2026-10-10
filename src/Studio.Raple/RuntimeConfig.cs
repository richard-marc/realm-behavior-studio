namespace BossPatternSim;
// Only configuration consumed by the linked, unchanged upstream SimEngine host.
sealed record SimConfig
{
    public string Boss { get; init; } = "Cave Pirate Brawler";
    public int Seed { get; init; } = 1;
    public string Map { get; init; } = "bosslab";
    public float PlayerX { get; init; } = 70.5f;
    public float PlayerY { get; init; } = 64.5f;
}
