using System.Collections.Immutable;
using Studio.Core;
using BossPatternSim;
using TKR.Shared.resources;
using TKR.Shared.terrain;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.terrain;

namespace Studio.Raple;
public static partial class NativeMapFile
{
    public static NativeMapImage FromArena(ArenaPreset arena,string hostId)
    {
        NativeArena.Validate(arena);var cells=Enumerable.Repeat(new NativeMapCell(NativeArena.GroundType(arena.GroundId)),16384).ToArray();
        cells[64*128+64]=cells[64*128+64] with {Region=(byte)TileRegion.Spawn};
        foreach(var tile in arena.Tiles)cells[tile.Y*128+tile.X]=new(NativeArena.GroundType(tile.GroundId),tile.ObjectId??"",tile.ObjectConfig,(byte)tile.Terrain,(byte)tile.Region,tile.Elevation);
        void Place(ArenaActor actor)
        {
            if(actor.X!=Math.Floor(actor.X)+.5||actor.Y!=Math.Floor(actor.Y)+.5)throw new ArgumentException("The native map format places objects at tile centers. Move this spawn to a tile center before export.");
            var index=(int)actor.Y*128+(int)actor.X;if(cells[index].ObjectId!="")throw new ArgumentException("The native map format stores one object per tile. Separate overlapping objects/enemies before export.");
            cells[index]=cells[index] with {ObjectId=actor.EntityId,Config=actor.Config};
        }
        Place(new(hostId,arena.BossX,arena.BossY,arena.BossConfig));foreach(var actor in arena.Actors)Place(actor);
        var result=new NativeMapImage(128,128,cells.ToImmutableArray());result.Validate();return result;
    }
    public static ArenaPreset ToArena(NativeMapImage image,string hostId,int hostX,int hostY)
    {
        image.Validate();if(image.Width!=128||image.Height!=128)throw new ArgumentException("This map's full dimensions are preserved. Its encounter needs a world bootstrap beyond the current 128×128 prototype; it can still be edited and exported.");
        if(!SimEngine.Data.IdToObjectType.TryGetValue(hostId,out var hostType)||image[hostX,hostY].ObjectId==""||SimEngine.Data.IdToObjectType[image[hostX,hostY].ObjectId]!=hostType)
            throw new ArgumentException("Select a map tile containing the current encounter enemy before using it as the host.");
        var resources=EntityResources.Catalog(EntityResourceScope.Resolvable).ToDictionary(e=>e.Id,StringComparer.Ordinal);
        var floor=image.Cells.GroupBy(c=>c.Ground).MaxBy(g=>g.Count())!.Key;var ground=GroundId(floor);var tiles=new List<ArenaTile>();var actors=new List<ArenaActor>();
        for(var y=0;y<128;y++)for(var x=0;x<128;x++)
        {
            var cell=image[x,y];string? obstacle=null;var config=cell.Config;
            if(cell.ObjectId!="")
            {
                var resource=resources[cell.ObjectId];
                if(resource.RuntimeKind=="Enemy"&&!SimEngine.Data.ObjectDescs[resource.ObjectType].Static)
                {if(x!=hostX||y!=hostY)actors.Add(new(cell.ObjectId,x+.5,y+.5,cell.Config));config="";}
                else
                {
                    try{NativeArena.ObstacleType(cell.ObjectId);obstacle=cell.ObjectId;}
                    catch(ArgumentException){throw new ArgumentException($"{cell.ObjectId} at ({x}, {y}) needs its native world/entity context. Its map data is preserved for editing/export; the prototype cannot execute this object yet.");}
                }
            }
            // Include every source cell, including empty regions/configurations;
            // no default spawn region may be invented during map conversion.
            tiles.Add(new(x,y,GroundId(cell.Ground),obstacle,config,cell.Elevation,cell.Terrain,cell.Region));
        }
        var result=new ArenaPreset{GroundId=ground,BossX=hostX+.5,BossY=hostY+.5,BossConfig=image[hostX,hostY].Config,Tiles=tiles.ToArray(),Actors=actors.ToArray()};NativeArena.Validate(result);return result;
    }
    internal static Enemy SpawnConfigured(SimEngine engine,ArenaActor actor)
    {
        if(actor.Config=="")return (Enemy)engine.World.CreateNewEntity(SimEngine.Data.IdToObjectType[actor.EntityId],(float)actor.X,(float)actor.Y);
        if(actor.X!=Math.Floor(actor.X)+.5||actor.Y!=Math.Floor(actor.Y)+.5)throw new ArgumentException("Configured map entities use native tile-center spawn positions.");
        var tiles=new TerrainTile[128,128];
        for(var y=0;y<128;y++)for(var x=0;x<128;x++)tiles[x,y]=new(){TileId=NativeArena.GroundType("Arena Dirt"),TileObj="",Name=""};
        tiles[(int)actor.X,(int)actor.Y].TileObj=actor.EntityId;tiles[(int)actor.X,(int)actor.Y].Name=actor.Config;
        var before=engine.World.Enemies.Keys.ToHashSet();var map=new Wmap(engine.World);map.Load(new MemoryStream(WorldMapExporter.Export(tiles)),0);
        // Native Wmap applies hp/name/size/effect configuration itself.
        map.CreateEntities();engine.FlushAdds();return engine.World.Enemies.Values.Single(e=>!before.Contains(e.Id)&&e.ObjectType==SimEngine.Data.IdToObjectType[actor.EntityId]);
    }
}
