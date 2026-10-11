using System.Collections.Immutable;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using BossPatternSim;
using Studio.Core;
using TKR.Shared;
using TKR.Shared.resources;
using TKR.Shared.terrain;
using TKR.WorldServer.core;
using TKR.WorldServer.core.worlds;

namespace Studio.Raple;

public readonly record struct NativeMapCell(ushort Ground, string ObjectId = "", string Config = "", byte Terrain = 0, byte Region = 0, byte Elevation = 0);
public sealed record NativeMapImage(int Width,int Height,ImmutableArray<NativeMapCell> Cells,int SourceVersion=2)
{
    public NativeMapCell this[int x,int y]=>x>=0&&y>=0&&x<Width&&y<Height?Cells[y*Width+x]:throw new ArgumentOutOfRangeException(nameof(x));
    public void Validate()
    {
        if(Width is <1 or >2048 || Height is <1 or >2048 || (long)Width*Height>1_048_576 || Cells.IsDefault || Cells.Length!=Width*Height || SourceVersion is <0 or >2)
            throw new ArgumentException("Map dimensions must fit 2048 tiles per axis and one million cells.");
        var unique=new HashSet<(ushort,string,string,byte,byte)>();
        foreach(var cell in Cells)
        {
            if(cell.ObjectId==null||cell.ObjectId.Length>256||cell.Config==null||cell.Config.Length>8192||!SimEngine.Data.Tiles.ContainsKey(cell.Ground)||
                cell.ObjectId!=""&&(!SimEngine.Data.IdToObjectType.TryGetValue(cell.ObjectId,out var type)||!SimEngine.Data.ObjectDescs.ContainsKey(type)))
                throw new ArgumentException("Map cells need available native ground/object XML and bounded configuration text.");
            unique.Add((cell.Ground,cell.ObjectId,cell.Config,cell.Terrain,cell.Region));
        }
        if(unique.Count>32767||unique.Sum(c=>(long)c.Item3.Length)>2_000_000)throw new ArgumentException("Map dictionary/configuration exceeds native format limits.");
    }
    public NativeMapImage Edit(IEnumerable<(int X,int Y,NativeMapCell Cell)> edits)
    {
        var next=Cells.ToBuilder();var changed=false;
        foreach(var (x,y,cell) in edits){var old=this[x,y];if(old!=cell){next[y*Width+x]=cell;changed=true;}}
        if(!changed)return this;var result=this with {Cells=next.ToImmutable()};result.Validate();return result;
    }
}
public sealed record NativeMapProbe(bool Passable,bool Spawnable,ushort EffectiveGround,ushort EffectiveObject,int StaticObjectId,string NativeConfig,string ObjectKind);

// A detached real World/Wmap supplies tile interpretation and passability without
// booting players, creating map entities, or reseeding a live encounter's RNG.
public sealed class NativeMapInspection
{
    readonly World world;
    readonly NativeMapImage image;
    readonly Dictionary<string,EntityResource> resources;
    public NativeMapInspection(NativeMapImage image)
    {
        this.image=image;image.Validate();
        var data=(Resources)RuntimeHelpers.GetUninitializedObject(typeof(Resources));SimEngine.Set(data,"GameData",SimEngine.Data);
        var server=(GameServer)RuntimeHelpers.GetUninitializedObject(typeof(GameServer));SimEngine.Set(server,"Resources",data);
        world=new(server,900,new WorldResource(XElement.Parse("<World id='StudioMapInspection'/>")));
        world.Map.Load(new MemoryStream(NativeMapFile.Export(image)),0);
        resources=EntityResources.Catalog(EntityResourceScope.Resolvable).ToDictionary(e=>e.Id,StringComparer.Ordinal);
    }
    public NativeMapProbe Probe(int x,int y)
    {
        var source=image[x,y];var tile=world.Map[x,y];
        return new(world.IsPassable(x+.5,y+.5),world.IsPassable(x+.5,y+.5,true),tile.TileId,tile.ObjType,tile.ObjId,tile.ObjCfg??"",source.ObjectId==""?"No object":resources[source.ObjectId].RuntimeKind);
    }
}

public static partial class NativeMapFile
{
    const int MaxFile=16_000_000,MaxExpanded=64_000_000;
    public static NativeMapImage Load(string path)
    {if(new FileInfo(path).Length>MaxFile)throw new ArgumentException("Map file exceeds 16 MB.");return Read(File.ReadAllBytes(path));}
    public static NativeMapImage Read(byte[] bytes)
    {
        if(bytes.Length is <2 or >MaxFile || bytes[0]>2)throw new ArgumentException("Unsupported or oversized native map file.");
        try
        {
            using var compressed=new MemoryStream(bytes,1,bytes.Length-1,false);using var zlib=new ZLibStream(compressed,CompressionMode.Decompress);using var expanded=new MemoryStream();
            var buffer=new byte[65536];int count;
            while((count=zlib.Read(buffer))>0){if(expanded.Length+count>MaxExpanded)throw new ArgumentException("Expanded map exceeds 64 MB.");expanded.Write(buffer,0,count);}
            expanded.Position=0;using var reader=new BinaryReader(expanded,Encoding.UTF8,true);var version=bytes[0];var entries=reader.ReadInt16();
            if(entries<1)throw new ArgumentException("Native map dictionary is empty or invalid.");
            string BoundedString(int limit)
            {
                uint length=0;var shift=0;
                while(true){var part=reader.ReadByte();if(shift>=28&&(part&0xF8)!=0)throw new ArgumentException("Invalid map string length.");length|=(uint)(part&127)<<shift;if((part&128)==0)break;shift+=7;if(shift>28)throw new ArgumentException("Invalid map string length.");}
                if(length>limit*4)throw new ArgumentException("Map string exceeds its limit.");var data=reader.ReadBytes((int)length);if(data.Length!=length)throw new EndOfStreamException();
                var value=new UTF8Encoding(false,true).GetString(data);if(value.Length>limit)throw new ArgumentException("Map string exceeds its limit.");return value;
            }
            var dictionary=new NativeMapCell[entries];
            for(var i=0;i<entries;i++)dictionary[i]=new(reader.ReadUInt16(),BoundedString(256),BoundedString(8192),reader.ReadByte(),reader.ReadByte(),version==1?reader.ReadByte():(byte)0);
            var width=reader.ReadInt32();var height=reader.ReadInt32();
            if(width is <1 or >2048 || height is <1 or >2048 || (long)width*height>1_048_576)throw new ArgumentException("Map dimensions exceed the editor's native map limits.");
            var cells=ImmutableArray.CreateBuilder<NativeMapCell>(width*height);
            for(var i=0;i<width*height;i++)
            {var index=reader.ReadInt16();if(index<0||index>=entries)throw new ArgumentException("Map cell references an invalid dictionary entry.");var cell=dictionary[index];cells.Add(version==2?cell with {Elevation=reader.ReadByte()}:cell);}
            if(expanded.Position!=expanded.Length)throw new ArgumentException("Unexpected trailing native map data.");
            var image=new NativeMapImage(width,height,cells.MoveToImmutable(),version);image.Validate();return image;
        }
        catch(Exception e) when(e is InvalidDataException or EndOfStreamException or DecoderFallbackException or OverflowException)
        {throw new ArgumentException("Invalid native map data: "+e.Message,e);}
    }
    public static byte[] Export(NativeMapImage image)
    {
        image.Validate();var cells=new TerrainTile[image.Width,image.Height];
        for(var y=0;y<image.Height;y++)for(var x=0;x<image.Width;x++)
        {var cell=image[x,y];cells[x,y]=new(){TileId=cell.Ground,TileObj=cell.ObjectId,Name=cell.Config,Terrain=(TerrainType)cell.Terrain,Region=(TileRegion)cell.Region,Elevation=cell.Elevation};}
        // The game writes the actual version-2 dictionary/zlib payload.
        var bytes=WorldMapExporter.Export(cells);if(bytes.Length>MaxFile)throw new ArgumentException("Exported map exceeds 16 MB.");return bytes;
    }
    public static void Save(string path,NativeMapImage image)
    {
        var bytes=Export(image);var target=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(target)!);var temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temporary,bytes);File.Move(temporary,target,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static IReadOnlyList<(int Value,string Name)> Regions()=>Enum.GetValues<TileRegion>().Select(v=>((int)v,v.ToString())).ToArray();
    public static IReadOnlyList<(int Value,string Name)> Terrains()=>Enum.GetValues<TerrainType>().Select(v=>((int)v,v.ToString())).ToArray();
    public static string GroundId(ushort type)
    {
        if(!SimEngine.Data.Tiles.ContainsKey(type))throw new ArgumentException("Missing native ground type.");
        var name=SimEngine.Data.Tiles[type].ObjectId;
        return SimEngine.Data.IdToTileType.TryGetValue(name,out var actual)&&actual==type?name:$"#0x{type:X4}";
    }
}
