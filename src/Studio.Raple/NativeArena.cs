using System.Reflection;
using System.Collections.Immutable;
using BossPatternSim;
using Studio.Core;
using TKR.Shared.resources;
using TKR.Shared.terrain;
using TKR.WorldServer.core.worlds;
using TKR.WorldServer.core.worlds.impl;

namespace Studio.Raple;

public record GroundResource(string Id, ushort Type, float Speed, bool NoWalk, int MinDamage, int MaxDamage, bool Push, bool Sink, bool Sinking)
{
    public string Title=>Id;
    public bool CompletePlayerSupport=>MinDamage==0&&MaxDamage==0&&!Push&&!Sink&&!Sinking&&Speed>0;
    public string Summary=>$"0x{Type:X4} · speed {Speed:0.##}"+(NoWalk?" · no walk":"")+(CompletePlayerSupport?"":" · player hazard mechanics pending");
}
public record ObstacleResource(string Id, ushort Type, string SpriteId, bool FullOccupy, bool OccupySquare, bool EnemyOccupySquare, bool Connects)
{
    public string Title=>Id;
    public string Summary=>$"0x{Type:X4} · native static tile"+(FullOccupy||EnemyOccupySquare?" · blocks movement":"")+(OccupySquare?" · cover":"")+(Connects?" · connected":"");
}
public record ArenaGeometry(string Shape, int Size, float Center, float Minimum, float Maximum, float Radius);
public record ArenaCellView(int X,int Y,ushort GroundType,ushort ObjectType,int ObjectId,byte Elevation,int Terrain,int Region,string Config);
public record ArenaSceneView(long Revision,int Width,int Height,ushort GroundType,ArenaGeometry Geometry,ImmutableArray<ArenaCellView> Cells);
public record ArenaTileProbe(bool Inside,bool Passable,bool Spawnable,bool BlocksShot,bool BlocksCoverPassingShot,float Speed);

public static class NativeArena
{
    public static IReadOnlyList<GroundResource> Grounds()=>SimEngine.Data.IdToTileType.Where(p=>SimEngine.Data.Tiles.ContainsKey(p.Value))
        .Select(p=>{var d=SimEngine.Data.Tiles[p.Value];return new GroundResource(p.Key,p.Value,d.Speed,d.NoWalk,d.MinDamage,d.MaxDamage,d.Push,d.Sink,d.Sinking);}).OrderBy(g=>g.Id).ToArray();
    public static IReadOnlyList<ObstacleResource> Obstacles()=>SimEngine.Data.IdToObjectType.Where(p=>IsObstacle(p.Value)).Select(p=>{
        var d=SimEngine.Data.ObjectDescs[p.Value];return new ObstacleResource(p.Key,p.Value,d.IdName,d.FullOccupy,d.OccupySquare,d.EnemyOccupySquare,d.Connects);
    }).OrderBy(o=>o.Id).ToArray();
    static bool IsObstacle(ushort type)=>SimEngine.Data.ObjectDescs.TryGetValue(type,out var d)&&d.Static&&!d.Enemy&&!d.Container&&d.Class!="Character"&&
        d.Class is not ("GuildHallPortal" or "ArenaPortal" or "Portal" or "ClosedVaultChest" or "Merchant" or "GuildMerchant" or "ClosedVaultChestGold" or "VaultChest" or "MarketNPC" or "SkillTree" or "Forge" or "StatNPC" or "Engine");
    public static ushort GroundType(string id)
    {
        if(id.StartsWith("#0x",StringComparison.Ordinal)&&ushort.TryParse(id.AsSpan(3),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out var direct)&&SimEngine.Data.Tiles.ContainsKey(direct))return direct;
        return SimEngine.Data.IdToTileType.TryGetValue(id,out var type)&&SimEngine.Data.Tiles.ContainsKey(type)?type:throw new ArgumentException("Unknown ground XML: "+id);
    }
    public static ushort ObstacleType(string? id)
    {if(id==null)return 0;return SimEngine.Data.IdToObjectType.TryGetValue(id,out var type)&&IsObstacle(type)?type:throw new ArgumentException("Choose a native static tile object: "+id);}
    public static void Validate(ArenaPreset arena)
    {
        ArgumentNullException.ThrowIfNull(arena);
        arena.Validate(); GroundType(arena.GroundId);
        foreach(var tile in arena.Tiles){GroundType(tile.GroundId);ObstacleType(tile.ObjectId);}
        if(arena.Actors.Count>0)
        {
            var enemies=EntityResources.Catalog(EntityResourceScope.Enemy).Select(e=>e.Id).ToHashSet(StringComparer.Ordinal);
            if(arena.Actors.Any(a=>!enemies.Contains(a.EntityId)))throw new ArgumentException("Arena actor XML must resolve to a native Enemy.");
        }
    }
    internal static void Apply(SimEngine engine,ArenaPreset arena,int size)
    {
        Validate(arena);
        if(size<32||size>128)throw new ArgumentException("Arena size must be between 32 and 128 tiles.");
        var lab=(BossLabWorld)engine.World;
        if(!lab.TrySetArena(engine.Actor,SimEngine.Time(0),size,Enum.Parse<BossLabArenaShape>(arena.Shape)))throw new InvalidOperationException("Native prototype rejected the arena geometry.");
        if(arena.GroundId!="Arena Dirt"||arena.Tiles.Count>0)
        {
            var tiles=new TerrainTile[128,128];var floor=GroundType(arena.GroundId);
            for(var y=0;y<128;y++)for(var x=0;x<128;x++)tiles[x,y]=new(){TileId=floor,TileObj="",Name="",Region=x==64&&y==64?TileRegion.Spawn:TileRegion.None};
            foreach(var cell in arena.Tiles)tiles[cell.X,cell.Y]=new(){TileId=GroundType(cell.GroundId),TileObj=cell.ObjectId??"",Name=cell.ObjectConfig,Elevation=cell.Elevation,Terrain=(TerrainType)cell.Terrain,Region=(TileRegion)cell.Region};
            lab.Map.Regions.Clear();
            // Native Insert ignores actors that still carry a node from the old index.
            lab.PlayersCollision.Remove(engine.Actor);engine.Actor.CollisionNode=null;
            // Use the game's own map exporter, loader and collision-index initialization.
            typeof(World).GetMethod("FromWorldMap",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(lab,[new MemoryStream(WorldMapExporter.Export(tiles))]);
            lab.PlayersCollision.Insert(engine.Actor,engine.Actor.X,engine.Actor.Y);
            engine.FlushAdds();
        }
    }
    internal static void ValidateSpawn(SimEngine engine,double x,double y,string label,bool enemy=false)
    {
        var lab=(BossLabWorld)engine.World;
        if(!double.IsFinite(x)||!double.IsFinite(y)||x<0||y<0||(float)x>=128||(float)y>=128||!lab.ContainsArenaPoint((float)x,(float)y)||!lab.IsPassable((float)x,(float)y,enemy))
            throw new ArgumentException(label+" must start on a passable tile inside the native arena.");
    }
    public static string? Limitations(ArenaPreset arena)
    {
        var types=arena.Tiles.Select(t=>GroundType(t.GroundId)).Append(GroundType(arena.GroundId)).ToHashSet();
        return types.Any(type=>{var d=SimEngine.Data.Tiles[type];return d.MinDamage!=0||d.MaxDamage!=0||d.Push||d.Sink||d.Sinking||d.Speed<=0;})
            ?"Unsupported player ground damage/push/sink/zero-speed mechanics present; full player lifecycle remains pending.":null;
    }
}

// Sparse immutable presentation snapshots are rebuilt only when native cells change.
internal sealed class NativeArenaObserver
{
    readonly SimEngine engine;
    readonly ushort ground;
    ArenaSceneView? current;
    readonly ArenaCellView?[] previous=new ArenaCellView?[16384];
    public NativeArenaObserver(SimEngine engine,string groundId){this.engine=engine;ground=NativeArena.GroundType(groundId);}
    public ArenaSceneView Current=>current??Capture();
    public ArenaSceneView Capture()
    {
        var lab=(BossLabWorld)engine.World;var changed=current==null;
        for(var y=0;y<128;y++)for(var x=0;x<128;x++)
        {
            var tile=lab.Map[x,y];
            var index=y*128+x;var old=previous[index];
            if(tile.TileId==ground&&tile.ObjType==0&&tile.ObjId==0&&string.IsNullOrEmpty(tile.ObjCfg)&&tile.Elevation==0&&tile.Terrain==0&&tile.Region==0)
            {if(old!=null){previous[index]=null;changed=true;}continue;}
            if(old!=null&&old.GroundType==tile.TileId&&old.ObjectType==tile.ObjType&&old.ObjectId==tile.ObjId&&old.Elevation==tile.Elevation&&old.Terrain==(int)tile.Terrain&&old.Region==(int)tile.Region&&old.Config==(tile.ObjCfg??""))continue;
            previous[index]=new(x,y,tile.TileId,tile.ObjType,tile.ObjId,tile.Elevation,(int)tile.Terrain,(int)tile.Region,tile.ObjCfg??"");changed=true;
        }
        var geometry=new ArenaGeometry(lab.ActiveArenaShape.ToString(),lab.ActiveArenaSize,BossLabWorld.ArenaCenter,lab.ArenaMin,lab.ArenaMax,lab.ArenaRadius);
        if(current?.Geometry!=geometry)changed=true;
        if(changed)current=new((current?.Revision??-1)+1,128,128,ground,geometry,previous.OfType<ArenaCellView>().ToImmutableArray());
        return current!;
    }
}
