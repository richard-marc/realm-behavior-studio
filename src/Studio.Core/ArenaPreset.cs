namespace Studio.Core;

public record ArenaTile(int X, int Y, string GroundId, string? ObjectId = null, string ObjectConfig = "", byte Elevation = 0, int Terrain = 0, int Region = 0);
public record ArenaActor(string EntityId, double X, double Y);
public sealed record ArenaPreset
{
    public int Version { get; init; } = 1;
    public string Shape { get; init; } = "Square";
    public string GroundId { get; init; } = "Arena Dirt";
    public double BossX { get; init; } = 64.5;
    public double BossY { get; init; } = 64.5;
    public IReadOnlyList<ArenaTile> Tiles { get; init; } = [];
    public IReadOnlyList<ArenaActor> Actors { get; init; } = [];
    public void Validate()
    {
        static bool Coordinate(double value)=>double.IsFinite(value)&&value>=0&&(float)value<128;
        static bool Id(string? value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=256;
        if(Version!=1 || Shape is not ("Square" or "Circle") || !Id(GroundId) || !Coordinate(BossX) || !Coordinate(BossY) ||
            Tiles==null || Tiles.Count>16384 || Actors==null || Actors.Count>64)throw new ArgumentException("Unsupported arena settings.");
        if(Tiles.Any(t=>t==null || t.X<0 || t.X>=128 || t.Y<0 || t.Y>=128 || !Id(t.GroundId) || t.ObjectId!=null&&!Id(t.ObjectId) ||
            t.ObjectConfig==null || t.ObjectConfig.Length>8192 || t.Terrain<0 || t.Terrain>255 || t.Region<0 || t.Region>255) ||
            Tiles.Select(t=>(t.X,t.Y)).Distinct().Count()!=Tiles.Count || Tiles.Sum(t=>(long)t.ObjectConfig.Length)>1_000_000)throw new ArgumentException("Arena cells need unique map coordinates and bounded resource/configuration data.");
        if(Actors.Any(a=>a==null || !Id(a.EntityId) || !Coordinate(a.X) || !Coordinate(a.Y)))throw new ArgumentException("Arena actors need a resource ID and a bounded spawn position.");
    }
}
