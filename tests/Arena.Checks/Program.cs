using System.Text.Json;
using Studio.Core;
using Studio.Raple;

var server=Path.GetFullPath(args.FirstOrDefault()??"../raple-realms-7-server");var adapter=new RapleAdapter(server);
var output=Path.GetFullPath(".artifacts/arena");Directory.CreateDirectory(output);var checks=new List<string>();
void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(ArgumentException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
var entry=adapter.Catalog().Single(e=>e.Id=="Cave Pirate Brawler");var original=File.ReadAllText(entry.SourceFile);var originalDoc=new BehaviorDocument(original,adapter,sourceFile:entry.SourceFile);var root=originalDoc.Entities.Single(e=>e.EntityId==entry.Id).Root;
var source=original.Remove(root.Start,root.Length).Insert(root.Start,"new State(new Shoot(20, fixedAngle: 0, coolDown: 1000))");
var document=new BehaviorDocument(source,adapter,sourceFile:entry.SourceFile);
var floor=NativeArena.Grounds().First(g=>g.Id=="Arena Dirt");
var slow=NativeArena.Grounds().First(g=>g.Speed>0&&g.Speed<floor.Speed&&!g.NoWalk&&g.CompletePlayerSupport);
var blocked=NativeArena.Grounds().First(g=>g.NoWalk);
var wall=NativeArena.Obstacles().First(o=>!o.Connects&&o.EnemyOccupySquare);
var cover=NativeArena.Obstacles().First(o=>!o.Connects&&!o.EnemyOccupySquare&&o.OccupySquare);
Console.WriteLine($"RESOURCES slow={slow.Id}; blocked={blocked.Id}; wall={wall.Id}; cover={cover.Id}");
var arena=new ArenaPreset{BossX=65.5,BossY=64.5,Tiles=[new(68,64,floor.Id,wall.Id),new(68,66,slow.Id),new(68,67,blocked.Id),new(68,68,floor.Id,cover.Id,Elevation:7,Terrain:1,Region:1)],Actors=[new(entry.Id,66.5,69.5)]};
var project=new StudioProject{Source=source,SourceFile=entry.SourceFile,Identities=document.Identities,EntityId=entry.Id,Seed=7,Player=new(Defense:0,StartX:70.5),Arena=arena};
EncounterSession Start(ArenaPreset? model=null,PlayerPreset? player=null,int size=48)=>new(adapter,document,entry.Id,7,player??project.Player,size,arena:model??arena);
using(var native=Start())
{
    Check(native.Entities.Count==2&&native.Entities.MinBy(e=>e.Id) is {X:65.5f,Y:64.5f}&&native.Entities.Any(e=>e.X==66.5f&&e.Y==69.5f),"Primary and extra enemies enter the real world at authored positions");
    Check(native.Arena.Geometry is {Shape:"Square",Size:48,Minimum:40.5f,Maximum:87.5f},"Presentation receives the actual native square bounds");
    Check(native.ProbeArena(68.5,64.5) is {Passable:false,Spawnable:false,BlocksShot:true,BlocksCoverPassingShot:true},"Native EnemyOccupySquare blocks walking, spawning and both projectile cover modes");
    Check(native.ProbeArena(68.5,68.5) is {Spawnable:false,BlocksShot:true,BlocksCoverPassingShot:false},"Native OccupySquare cover preserves its different cover-passing projectile rule");
    Check(!native.ProbeArena(68.5,67.5).Passable&&native.ProbeArena(68.5,66.5).Speed==slow.Speed,"Loaded ground XML determines NoWalk and positive movement-speed modifiers");
    var cell=native.Arena.Cells.Single(c=>c.X==68&&c.Y==68);
    Check(cell.ObjectType==cover.Type&&cell.ObjectId>0&&cell.Elevation==7&&cell.Terrain==1&&cell.Region==1,"Game map exporter/loader retains actual static object IDs, elevation, terrain and region");
    var snapshot=native.Arena;native.Advance(new(),100);Check(ReferenceEquals(snapshot,native.Arena),"Unchanged native maps retain an immutable snapshot without rebuilding presentation data");
    Check(native.Shots.Count>=2&&native.Events.Count(e=>e.Kind=="attack")>=2,"Reloaded arena collision indexes keep the player active so both authored enemies execute native attacks");
    native.Advance(new(-1,0),1000);Check(native.PlayerX>=69&&native.PlayerX<70.5,"Native player movement stops against the loaded wall");
}
using(var circle=Start(arena with {Shape="Circle"}))Check(circle.Arena.Geometry.Shape=="Circle"&&!circle.ProbeArena(41,41).Inside&&circle.ProbeArena(64,64).Inside,"Circle geometry uses the native radius rather than a square collision approximation");
using(var fast=Start(new()))using(var slower=Start(new(){GroundId=slow.Id}))
{
    fast.Advance(new(0,1),100);slower.Advance(new(0,1),100);
    Check(Math.Abs((slower.PlayerY-64.5)/(fast.PlayerY-64.5)-slow.Speed/floor.Speed)<.02,"Actual movement consumes the selected XML floor speed");
}
Reject(()=>Start(arena with {BossX=68.5}),"Primary enemy starts reject native occupied tiles");
Reject(()=>Start(arena with {Actors=[new(entry.Id,68.5,64.5)]}),"Extra enemy starts reject native occupied tiles");
Reject(()=>Start(player:project.Player with {StartX=68.5}),"Player starts reject native occupied tiles");
Reject(()=>Start(arena with {Shape="Circle",BossX=41,BossY=41}),"Enemy starts reject outside circular native bounds");
Reject(()=>Start(size:31),"Out-of-range arena sizes reject instead of silently clamping");
Reject(()=>NativeArena.Validate(arena with {GroundId="missing ground"}),"Unknown ground IDs reject before map initialization");
Reject(()=>NativeArena.Validate(arena with {Tiles=[new(60,60,floor.Id,"Cave Pirate Brawler")]}),"Dynamic enemies cannot be painted as static obstacles");
Reject(()=>NativeArena.Validate(arena with {Tiles=[new(60,60,floor.Id),new(60,60,floor.Id)]}),"Duplicate tile coordinates reject");
Reject(()=>NativeArena.Validate(arena with {Actors=[new("Wizard",60,60)]}),"Player XML cannot masquerade as an extra enemy");
var hazard=NativeArena.Grounds().First(g=>!g.CompletePlayerSupport&&!g.NoWalk&&g.Speed>0);
using(var unsupported=Start(new(){GroundId=hazard.Id}))Check(unsupported.Events.Any(e=>e.Kind=="unsupported"&&e.Message.Contains("ground")),"Unimplemented full-player ground hazards remain explicitly unsupported");
var path=Path.Combine(output,"arena.rbs");BehaviorDocument.Save(path,project);var loaded=BehaviorDocument.Load(path);
Check(JsonSerializer.Serialize(loaded.Arena)==JsonSerializer.Serialize(arena),"Arena tiles and actors survive project save/reopen");
Check(JsonSerializer.Deserialize<StudioProject>("{}")!.Arena is {GroundId:"Arena Dirt",BossX:64.5,Shape:"Square"},"Legacy projects retain the original native arena defaults");
var identity=EncounterReplay.Identify(adapter,project);EncounterReplay tape;
using(var native=Start()){native.Recording=new(native);native.Advance(new(0,1),500);tape=EncounterReplay.Capture(project,null,identity,native);}
var cursor=await ReplayCursor.RestoreAsync(adapter,tape,175);using(cursor.Session){await cursor.AdvanceToAsync(500);Check(cursor.Session.Arena.Cells.Any(c=>c.ObjectType==wall.Type)&&cursor.Session.Entities.Count==2,"Replay seeks and verifies the same native terrain and extra actors");}
var movement=await NativeMovementPreview.SampleAsync(adapter,project,durationMs:100);Check(movement.Points.First() is {X:65.5f,Y:64.5f},"Native path sampling starts at the authored primary enemy position");
var dynamicSource=original.Remove(root.Start,root.Length).Insert(root.Start,$"new State(new State(\"A\", new GroundTransform({JsonSerializer.Serialize(slow.Id)}, radius: 1), new TimedTransition(300, \"B\")), new State(\"B\", new TimedTransition(300, \"A\")))");
var dynamicDoc=new BehaviorDocument(dynamicSource,adapter,sourceFile:entry.SourceFile);var dynamicProject=project with {Source=dynamicSource,Identities=dynamicDoc.Identities,Arena=arena with {Actors=[]}};EncounterReplay dynamicTape;
using(var changing=new EncounterSession(adapter,dynamicDoc,entry.Id,7,project.Player,arena:dynamicProject.Arena))
{
    var before=changing.Arena;changing.Recording=new(changing);changing.Advance(new(),100);
    Check(changing.Arena.Cells.Any(c=>c.GroundType==slow.Type&&c.X==65),"Native ground-transform entry callbacks appear in actual map snapshots");
    changing.Advance(new(),300);Check(!ReferenceEquals(before,changing.Arena)&&!changing.Arena.Cells.Any(c=>c.GroundType==slow.Type&&c.X==65),"Ground-transform exit restores the native earlier floor and refreshes the scene");changing.Advance(new(),600);
    dynamicTape=EncounterReplay.Capture(dynamicProject,null,EncounterReplay.Identify(adapter,dynamicProject),changing);
}
var changingCursor=await ReplayCursor.RestoreAsync(adapter,dynamicTape,175);using(changingCursor.Session){await changingCursor.AdvanceToAsync(1000);Check(changingCursor.Session.Recording!.ObserveCurrent(changingCursor.Session).Signature==dynamicTape.Commands[^1].Result!.Signature,"Replay map witnesses ignore presentation revision counts while certifying split native map updates");}
var prepared=await CompiledRuntime.PrepareAsync(server,project,artifactRoot:Path.Combine(output,"compiled"));
EncounterReplay compiledTape;
await using(var compiled=await prepared.StartAsync())
{
    Check(compiled.Arena.Cells.Any(c=>c.ObjectType==wall.Type)&&compiled.Entities.Count==2&&compiled.Entities.MinBy(e=>e.Id)!.X==65.5f,"Compiled workers load authored maps and actors and transmit the initial native scene");
    compiled.Recording=new(compiled);var initial=compiled.Arena;await compiled.AdvanceAsync(new(-1,0),1000);
    Check(compiled.PlayerX>=69&&compiled.PlayerX<70.5&&ReferenceEquals(initial,compiled.Arena),"Compiled movement obeys the same wall and unchanged scene deltas retain their snapshot");
    Check(compiled.Shots.Count>=2,"Compiled edited arenas keep both enemies executing native attacks");
    compiledTape=EncounterReplay.Capture(project,null,EncounterReplay.Identify(adapter,project,prepared.Stamp.BuildId),compiled);
}
var compiledCursor=await ReplayCursor.RestoreAsync(adapter,compiledTape,175,prepared);using(compiledCursor.Session){await compiledCursor.AdvanceToAsync(1000);Check(compiledCursor.Session.Arena.Cells.Any(c=>c.ObjectType==wall.Type)&&compiledCursor.Session.Entities.Count==2,"Compiled seeking verifies native map snapshots, attacks and additional actors with the exact prepared build");}
var dynamicPrepared=await CompiledRuntime.PrepareAsync(server,dynamicProject,artifactRoot:Path.Combine(output,"compiled"));
await using(var changing=await dynamicPrepared.StartAsync())
{
    await changing.AdvanceAsync(new(),100);Check(changing.Arena.Cells.Any(c=>c.GroundType==slow.Type&&c.X==65),"Compiled workers transmit native ground entry effects after the actual first tick");
    var initial=changing.Arena;await changing.AdvanceAsync(new(),300);
    Check(!ReferenceEquals(initial,changing.Arena)&&!changing.Arena.Cells.Any(c=>c.GroundType==slow.Type&&c.X==65),"Compiled workers transmit actual map deltas after native ground exit callbacks");
}
Check(File.ReadAllText(entry.SourceFile)==original,"Arena authoring and compiled verification preserve the private source file");
File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(checks,BehaviorDocument.JsonOptions));Console.WriteLine($"{checks.Count} native arena checks passed");
