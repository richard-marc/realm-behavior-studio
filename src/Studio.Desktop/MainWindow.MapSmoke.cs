using System.Collections.Immutable;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async Task RunMapUiSmoke(string source,BehaviorNode root,string output,Action<bool,string> check)
    {
        var previous=Snapshot();var privateSource=File.ReadAllText(selectedEntity!.SourceFile);var fixture=source.Remove(root.Start,root.Length).Insert(root.Start,"new State(new Shoot(20, fixedAngle: 0, coolDown: 1000))");document=new(fixture,adapter,sourceFile:selectedEntity.SourceFile);Changed();
        var arena=new ArenaPreset{BossConfig="hp:80;name:Captain;size:150;",Actors=[new(selectedEntity.Id,66.5,66.5,"hp:50;name:Guard;size:75;")]};var nativeImage=NativeMapFile.FromArena(arena,selectedEntity.Id);var path=Path.Combine(output,"native-map.wmap");SetMapImage(nativeImage,path);UpdateLayout();
        check(MapWorkspace.IsSelected&&MapCanvas.Model?.Cells.Length==16384&&ParametersTab.Header.ToString()=="Map tile","Map workspace shares the native XML context and shows its own tile inspector");
        var slow=NativeArena.Grounds().First(g=>g.CompletePlayerSupport&&!g.NoWalk&&g.Speed>0&&g.Speed<1&&sprites.GetGround(g.Type)!=null);
        mapSearch.Text=slow.Id;mapMode.SelectedIndex=1;mapPalette.SelectedItem=mapPalette.Items.Cast<ArenaChoice>().Single(c=>c.Id==slow.Id);
        var before=mapImage;MapCanvas.BeginStroke(MapCanvas.TileScreen(60,60));MapCanvas.ContinueStroke(MapCanvas.TileScreen(62,60));MapCanvas.FinishStroke();
        check(mapImage![60,60].Ground==slow.Type&&mapImage[61,60].Ground==slow.Type&&mapImage[62,60].Ground==slow.Type&&before![60,60].Ground!=slow.Type,"Map canvas fills skipped brush samples and preserves immutable earlier cells");
        Undo(this,new());check(ReferenceEquals(before,mapImage),"Top-level Undo routes a whole native map stroke to the map history");Redo(this,new());check(mapImage![61,60].Ground==slow.Type,"Top-level Redo restores the native map stroke");
        mapMode.SelectedIndex=4;mapNumber.Text="7";MapCanvas.BeginStroke(MapCanvas.TileScreen(60,60));MapCanvas.FinishStroke();
        mapMode.SelectedIndex=5;mapNumber.Text="2";MapCanvas.BeginStroke(MapCanvas.TileScreen(60,60));MapCanvas.FinishStroke();
        mapMode.SelectedIndex=6;mapNumber.Text="1";MapCanvas.BeginStroke(MapCanvas.TileScreen(60,60));MapCanvas.FinishStroke();check(mapImage![60,60] is {Elevation:7,Region:2,Terrain:1},"Native map metadata brushes edit elevation, region and terrain without changing object data");
        mapMode.SelectedIndex=0;MapCanvas.BeginStroke(MapCanvas.TileScreen(64,64));
        check(mapPalette.Visibility==Visibility.Collapsed&&mapSearch.Visibility==Visibility.Collapsed,"Inspect mode hides unrelated brush controls and preserves canvas space");
        var inputs=Inspector.Children.OfType<TextBox>().ToArray();check(inputs.Length==6&&inputs[1].Text==selectedEntity.Id&&inputs[2].Text.Contains("hp:80"),"Actual tile inspector exposes the original object XML and native configuration");
        inputs[2].Text="hp:90;name:Captain;size:100;size:150:ignored;";inputs[3].Text="3";inputs[4].Text="2";inputs[5].Text="4";
        Inspector.Children.OfType<Button>().Single(b=>b.Content.ToString()=="Apply tile").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(mapImage![64,64] is {Config:"hp:90;name:Captain;size:100;size:150:ignored;",Elevation:3,Terrain:2,Region:4},"Inspector Apply atomically commits native object configuration and tile metadata");
        await FlushMapRecovery(Path.Combine(output,"map-recovery.wmap"));check(NativeMapFile.Load(Path.Combine(output,"map-recovery.wmap"))[64,64].Config==mapImage[64,64].Config,"Map recovery saves the complete native file and current configuration");
        MapSave.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(mapSaveTask!=null)await mapSaveTask;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        check(!mapDirty&&NativeMapFile.Load(path).Cells.SequenceEqual(mapImage.Cells),"Save map control writes actual native .wmap and clears the saved revision's dirty state");
        if(mapInspectionTask!=null)await mapInspectionTask;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        check(mapInspection?.Probe(64,64) is {ObjectKind:"Enemy",Passable:true},"Desktop inspector reads genuine native tile passability and dynamic object classification");
        MapCanvas.FocusSelection();Width=1500;Height=1000;UpdateLayout();CaptureView(this,Path.Combine(output,"native-map-editor.png"));
        check(MapCanvas.ActualWidth>400&&MapCanvas.ActualHeight>150,"Map tools and inspector retain a usable native sprite canvas");
        MapUse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(arenaPreset.BossConfig.Contains("hp:90")&&arenaPreset.Actors.Single().Config.Contains("hp:50")&&ArenaSize.Text=="128","Use in arena retains native primary/helper settings and exact tile coordinates");
        check(await StartSessionAsync()&&session!.Entities.MinBy(e=>e.Id) is {MaxHp:90,Size:150,DisplayName:"Captain"}&&session.Entities.MaxBy(e=>e.Id) is {MaxHp:50,Size:75},"Desktop Play applies actual native Wmap configuration to both placed enemies");session!.Advance(new(),100);check(session.Shots.Count>=2,"Configured desktop enemies both execute original native attacks");
        running=false;UpdateSession();UpdateLayout();CaptureView(Viewport,Path.Combine(output,"native-map-configured-runtime.png"));Stop(this,new());
        var invalidConfig=nativeImage.Edit([(64,64,nativeImage[64,64] with {Config="size:999999999999999999999999;"})]);SetMapImage(invalidConfig);MapCanvas.Selection=new(64,64);MapCanvas.FocusSelection();UpdateLayout();CaptureView(MapCanvas,Path.Combine(output,"native-map-invalid-config.png"));check(mapImage![64,64].Config==invalidConfig[64,64].Config,"Oversized native size text remains editable and does not crash sprite rendering");
        var wide=new NativeMapImage(256,192,Enumerable.Repeat(new NativeMapCell(NativeArena.GroundType("Arena Dirt")),256*192).ToImmutableArray());SetMapImage(wide);UpdateLayout();MapCanvas.Fit();CaptureView(MapCanvas,Path.Combine(output,"native-map-wide.png"));check(mapImage.Width==256&&mapImage.Height==192&&MapCanvas.Model==wide,"Map editor keeps non-prototype dimensions and its full overview instead of resizing a map to the arena");
        check(mapDirty,"An unsaved map is eligible for recovery before any further painting");
        var tall=new NativeMapImage(128,2048,Enumerable.Repeat(new NativeMapCell(NativeArena.GroundType("Arena Dirt")),128*2048).ToImmutableArray());SetMapImage(tall);UpdateLayout();MapCanvas.Fit();check(MapCanvas.TileScreen(0,0).Y>=0&&MapCanvas.TileScreen(127,2047).Y<=MapCanvas.ActualHeight,"Fit includes both ends of a native map at the supported 2048-tile axis limit");
        var beforeArena=arenaPreset;var rejected=false;try{NativeMapFile.ToArena(wide,selectedEntity.Id,64,64);}catch(ArgumentException){rejected=true;}check(rejected&&ReferenceEquals(beforeArena,arenaPreset),"Unsupported world transfer leaves the map and current encounter unchanged");
        check(PrivateMapTarget(Path.Combine(adapter.ServerRoot,"TKR.Shared/resources/worlds/test.wmap"))&&File.ReadAllText(selectedEntity.SourceFile)==privateSource,"Map save isolation and playback preserve supplied game checkouts");
        var closeWindow=new MainWindow(["--smoke","--verify-map-close"]){mapImage=nativeImage,mapDirty=true,mapRevision=1};var closed=new TaskCompletionSource();closeWindow.Closed+=(_,_)=>closed.SetResult();closeWindow.Close();await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        check(NativeMapFile.Load(closeWindow.MapRecoveryPath()).Cells.SequenceEqual(nativeImage.Cells),"Actual window close waits for a complete native map recovery before disposal");
        var recoveredWindow=new MainWindow(["--smoke","--verify-map-close"]){mapImage=nativeImage,mapDirty=true,mapRevision=2,recoveredMapRevision=2};var recoveredClosed=new TaskCompletionSource();recoveredWindow.Closed+=(_,_)=>recoveredClosed.SetResult();recoveredWindow.Close();var deferred=!recoveredClosed.Task.IsCompleted;await recoveredClosed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        check(deferred,"An already recovered dirty map closes after the original Closing event returns without reentrant Close");
        ApplyProject(previous,null);WorkspaceTabs.SelectedIndex=0;mapImage=null;mapInspection=null;mapInspectionTask=null;MapCanvas.Model=null;mapDirty=false;mapUndo.Clear();mapRedo.Clear();
    }
}
