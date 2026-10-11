using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using Studio.Core;
using Studio.Raple;

var server=Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server");var adapter=new RapleAdapter(server);var output=Path.GetFullPath(".artifacts/maps");Directory.CreateDirectory(output);var checks=new List<string>();
void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);Console.WriteLine("PASS "+text);}
void Reject(Action action,string text){try{action();}catch(ArgumentException){Check(true,text);return;}throw new Exception("Expected rejection: "+text);}
var floor=NativeArena.GroundType("Arena Dirt");var slow=NativeArena.Grounds().First(g=>g.CompletePlayerSupport&&!g.NoWalk&&g.Speed<1&&g.Speed>0);var wall=NativeArena.Obstacles().First(o=>o.EnemyOccupySquare&&!o.Connects);var connected=NativeArena.Obstacles().First(o=>o.Connects);
var resources=EntityResources.Catalog(EntityResourceScope.Resolvable);var portal=resources.First(r=>r.RuntimeKind=="Portal");var enemy="Cave Pirate Brawler";
var image=new NativeMapImage(37,19,Enumerable.Repeat(new NativeMapCell(floor),37*19).ToImmutableArray());
image=image.Edit([(3,4,new(slow.Type,wall.Id,"name:stone;",1,1,9)),(6,7,new(floor,enemy,"hp:80;name:Captain;size:150;",2,3,17)),(8,7,new(floor,portal.Id,"name:portal;",0,2,2)),(12,12,new(floor,connected.Id))]);
var bytes=NativeMapFile.Export(image);var imported=NativeMapFile.Read(bytes);
Check(bytes[0]==2&&imported.Width==37&&imported.Height==19&&imported.Cells.SequenceEqual(image.Cells),"Native exporter and bounded reader round-trip non-square dimensions and every source tile field");
var inspection=new NativeMapInspection(imported);var probe=inspection.Probe(3,4);
Check(probe is {Passable:false,Spawnable:false,StaticObjectId:>0}&&probe.EffectiveGround==slow.Type&&probe.EffectiveObject==wall.Type,"Detached native World/Wmap retains static objects and actual passability");
Check(inspection.Probe(6,7) is {EffectiveObject:0,ObjectKind:"Enemy"}&&inspection.Probe(8,7) is {EffectiveObject:0,ObjectKind:"Portal"},"Map authoring retains dynamic enemy/portal records that native Wmap moves out of its tile object layer");
Check(inspection.Probe(12,12).NativeConfig.Contains("conn:")&&imported[12,12].Config==""&&imported[12,12].Ground==floor,"Native connected-object interpretation is observable without overwriting its original map ground/configuration");
Check(NativeMapFile.Regions().Any(v=>v.Name=="Spawn")&&NativeMapFile.Terrains().Count>1,"Map metadata choices come from native region and terrain enums");
var path=Path.Combine(output,"non-square.wmap");NativeMapFile.Save(path,image);Check(NativeMapFile.Load(path).Cells.SequenceEqual(image.Cells),"Atomic native map save/open preserves entity configurations and elevation");
var before=image;var changed=image.Edit([(3,4,image[3,4] with {Elevation=10,Config="name:edited;"})]);Check(before[3,4].Elevation==9&&changed[3,4].Elevation==10&&ReferenceEquals(before,before.Edit([(3,4,before[3,4])])),"Map edits are immutable and unchanged transactions retain the original image");
byte[] Legacy(byte version,short index=0,int width=2,int height=1,string objectId="",ushort? ground=null)
{
    using var raw=new MemoryStream();using(var writer=new BinaryWriter(raw,System.Text.Encoding.UTF8,true)){writer.Write((short)1);writer.Write(ground??floor);writer.Write(objectId);writer.Write("name:legacy;");writer.Write((byte)2);writer.Write((byte)3);if(version==1)writer.Write((byte)7);writer.Write(width);writer.Write(height);if(width<10000&&height<10000)for(var i=0;i<width*height;i++)writer.Write(index);}
    using var result=new MemoryStream();result.WriteByte(version);using(var zipped=new ZLibStream(result,CompressionLevel.Optimal,true))zipped.Write(raw.ToArray());return result.ToArray();
}
foreach(var version in new byte[]{0,1})
{
    var old=NativeMapFile.Read(Legacy(version));var native=new NativeMapInspection(old);
    Check(old.SourceVersion==version&&old.Width==2&&old.Cells.All(c=>c.Config=="name:legacy;"&&c.Elevation==(version==1?7:0))&&native.Probe(0,0).EffectiveGround==floor,$"Legacy native version {version} reads through native tile interpretation without inventing elevation");
    Check(NativeMapFile.Read(NativeMapFile.Export(old)).Cells.SequenceEqual(old.Cells),$"Legacy version {version} exports native version 2 with equivalent logical cells");
}
Reject(()=>NativeMapFile.Read([3,0]),"Unknown map versions reject");Reject(()=>NativeMapFile.Read([2,0]),"Truncated compressed maps reject");Reject(()=>NativeMapFile.Read(Legacy(0,index:1)),"Out-of-range dictionary indices reject before native loading");
Reject(()=>NativeMapFile.Read(Legacy(0,width:3000)),"Oversized axes reject before allocating a native tile grid");Reject(()=>NativeMapFile.Read(Legacy(0,objectId:"missing source XML")),"Unknown objects reject instead of accepting native loader's object-dropping fallback");
Reject(()=>NativeMapFile.Read(Legacy(0,ground:65535)),"Unknown ground types reject");Reject(()=>NativeMapFile.Read(bytes[..^8]),"Truncated version-2 payloads reject");
Reject(()=>(image with {Cells=[]}).Validate(),"Mismatched grid dimensions reject");Reject(()=>image.Edit([(40,4,new(floor))]),"Out-of-map edits reject atomically");
Reject(()=>image.Edit([(1,1,new(floor,Config:new string('x',8193)))]),"Oversized entity configuration rejects atomically");
var hazard=NativeArena.Grounds().First(g=>!g.CompletePlayerSupport);
Check(NativeArena.Limitations(new ArenaPreset{GroundId=$"#0x{hazard.Type:X4}"})!=null&&NativeArena.Limitations(new ArenaPreset{Tiles=[new(60,60,$"#0x{hazard.Type:X4}")]})!=null,"Typed native ground references retain hazard limitations for floors and individual cells");
Reject(()=>(image with {Width=2048,Height=1024}).Validate(),"Total native cell budget rejects before a large grid can be allocated");
Reject(()=>new NativeMapImage(256,128,Enumerable.Range(0,32768).Select(i=>new NativeMapCell(floor,Config:$"name:{i};")).ToImmutableArray()).Validate(),"Native signed-short dictionary capacity rejects rather than wrapping exported indices");
Reject(()=>new NativeMapImage(512,1,Enumerable.Range(0,512).Select(i=>new NativeMapCell(floor,Config:i.ToString()+new string('x',4096))).ToImmutableArray()).Validate(),"Combined unique configuration budget rejects before native export");
Reject(()=>NativeMapFile.Read(new byte[16_000_001]),"Compressed file budget rejects before decompression");
var large=new NativeMapImage(256,192,Enumerable.Repeat(new NativeMapCell(floor),256*192).ToImmutableArray());var largeRead=NativeMapFile.Read(NativeMapFile.Export(large));Check(largeRead.Width==256&&largeRead.Height==192&&new NativeMapInspection(largeRead).Probe(255,191).Passable,"Maps beyond prototype arena dimensions retain full native grids and edge passability");
Reject(()=>NativeMapFile.ToArena(image,enemy,6,7),"Non-prototype map transfer rejects without resizing or discarding its authored map");
var arena=new ArenaPreset{BossConfig="hp:0x50;name:Captain;size:100;size:150:ignored;",Tiles=[new(60,60,"Arena Dirt",wall.Id,"name:stone;",9,1,2)],Actors=[new(enemy,67.5,66.5,"hp:50;name:Guard;size:75;")]};
var arenaMap=NativeMapFile.FromArena(arena,enemy);var restored=NativeMapFile.ToArena(arenaMap,enemy,64,64);
Check(restored.BossConfig==arena.BossConfig&&restored.Actors.Single().Config==arena.Actors.Single().Config&&restored.Tiles.Single(c=>c.X==60&&c.Y==60) is {Elevation:9,Terrain:1,Region:2},"Prototype map conversion retains primary/extra configurations and every tile attribute");
var absentRegion=arenaMap.Edit([(64,64,arenaMap[64,64] with {Region=0})]);Check(NativeMapFile.FromArena(NativeMapFile.ToArena(absentRegion,enemy,64,64),enemy).Cells.SequenceEqual(absentRegion.Cells),"Map transfer never invents a default spawn region over an explicitly empty source cell");
Reject(()=>NativeMapFile.FromArena(arena with {Actors=[new(enemy,64.5,64.5)]},enemy),"Native map export rejects overlapping objects rather than dropping them");
Reject(()=>NativeMapFile.FromArena(arena with {BossX=64.25},enemy),"Native map export rejects unsupported fractional object placement");
Reject(()=>NativeMapFile.ToArena(arenaMap.Edit([(70,70,new(floor,portal.Id))]),enemy,64,64),"Prototype execution keeps an explicit world-context gate for portal objects while preserving the full map");
var entry=adapter.Catalog().Single(e=>e.Id==enemy);var original=File.ReadAllText(entry.SourceFile);var doc=new BehaviorDocument(original,adapter,sourceFile:entry.SourceFile);var root=doc.Entities.Single(e=>e.EntityId==enemy).Root;var source=original.Remove(root.Start,root.Length).Insert(root.Start,"new State(new Shoot(20, fixedAngle: 0, coolDown: new Cooldown(100, 500)))");doc=new(source,adapter,sourceFile:entry.SourceFile);
var project=new StudioProject{Source=source,SourceFile=entry.SourceFile,EntityId=enemy,Identities=doc.Identities,Seed=7,ArenaSize=128,Arena=restored};EncounterReplay tape;
using(var native=new EncounterSession(adapter,doc,enemy,7,project.Player,128,arena:restored))
{
    var primary=native.Entities.MinBy(e=>e.Id)!;var helper=native.Entities.MaxBy(e=>e.Id)!;
    Check(primary is {Hp:80,MaxHp:80,Size:150,DisplayName:"Captain"}&&helper is {Hp:50,MaxHp:50,Size:75,DisplayName:"Guard"},"Real Wmap.CreateEntities applies native hp/name/size configuration to primary and additional enemies");
    native.Recording=new(native);native.Advance(new(),250);_ = new NativeMapInspection(large);native.Advance(new(),750);Check(native.Shots.Count>2,"Configured native actors keep executing their actual behavior trees after detached map inspection");
    tape=EncounterReplay.Capture(project,null,EncounterReplay.Identify(adapter,project),native);
}
var cursor=await ReplayCursor.RestoreAsync(adapter,tape,175);using(cursor.Session){await cursor.AdvanceToAsync(1000);Check(cursor.Session.Entities.MinBy(e=>e.Id) is {Size:150,DisplayName:"Captain"},"Map inspection leaves live native thread RNG intact and configured actor state survives verified replay");}
var prepared=await CompiledRuntime.PrepareAsync(server,project,artifactRoot:Path.Combine(output,"compiled"));
await using(var native=await prepared.StartAsync()){Check(native.Entities.MinBy(e=>e.Id) is {Hp:80,MaxHp:80,Size:150,DisplayName:"Captain"},"Compiled workers execute native map configuration and transmit actual actor size/name");await native.AdvanceAsync(new(),100);Check(native.Shots.Count>=2,"Configured compiled primary and helper both execute real attacks");}
Check(File.ReadAllText(entry.SourceFile)==original,"Map tooling and configured playback preserve supplied game source");
File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(checks,BehaviorDocument.JsonOptions));Console.WriteLine($"{checks.Count} native map checks passed");
