using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    async Task RunArenaUiSmoke(string source,BehaviorNode root,string output,Action<bool,string> check)
    {
        var previous=Snapshot();var originalSource=File.ReadAllText(selectedEntity!.SourceFile);
        var fixture=source.Remove(root.Start,root.Length).Insert(root.Start,"new State(new Shoot(20, fixedAngle: 0, coolDown: 1000))");
        document=new BehaviorDocument(fixture,adapter,sourceFile:selectedEntity.SourceFile);Changed();
        arenaPreset=new();arenaUndo.Clear();arenaRedo.Clear();PlayerStartX.Text="70.5";PlayerStartY.Text="64.5";ArenaSize.Text="48";
        StartSession();session!.Advance(new(),100);UpdateSession();
        ArenaEditButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        check(Viewport.ArenaEditing&&session==null&&ArenaControls.Visibility==Visibility.Visible&&TimelineVisual.Replay==null,"Opening arena editing stops the native run and clears its replay");
        void Paint(int mode,string? resource,params Point[] points)
        {
            arenaMode.SelectedIndex=mode;arenaSearch.Text=resource??"";
            if(resource!=null)arenaChoices.SelectedItem=arenaChoices.Items.Cast<ArenaChoice>().Single(c=>c.Id==resource);
            Viewport.BeginArenaStroke(Viewport.ArenaScreen(points[0].X+.5,points[0].Y+.5));
            foreach(var point in points.Skip(1))Viewport.ContinueArenaStroke(Viewport.ArenaScreen(point.X+.5,point.Y+.5));Viewport.FinishArenaStroke();
        }
        var wall=NativeArena.Obstacles().First(o=>!o.Connects&&o.EnemyOccupySquare&&sprites.Get(o.SpriteId)!=null);
        Paint(1,wall.Id,new(61,64),new(63,64));
        check(arenaPreset.Tiles.Count==3&&arenaPreset.Tiles.All(t=>t.ObjectId==wall.Id),"Viewport brush strokes fill skipped pointer samples with real XML obstacles");
        Undo(this,new());check(arenaPreset.Tiles.Count==0,"A whole arena brush stroke is one Undo");Redo(this,new());check(arenaPreset.Tiles.Count==3,"Arena Redo restores the full obstacle stroke");
        var slow=NativeArena.Grounds().First(g=>g.Speed>0&&g.Speed<1&&!g.NoWalk&&g.CompletePlayerSupport&&sprites.GetGround(g.Type)!=null);
        Paint(0,slow.Id,new Point(61,64));check(arenaPreset.Tiles.Single(t=>t.X==61).ObjectId==wall.Id&&arenaPreset.Tiles.Single(t=>t.X==61).GroundId==slow.Id,"Ground painting preserves the obstacle on the same native cell");
        check(arenaChoices.SelectedItem is ArenaChoice{Image:not null}&&sprites.GetGround(NativeArena.GroundType("Arena Dirt"))!=null,"Ground palette resolves actual client texture frames");
        Paint(3,null,new Point(65,66));Paint(4,null,new Point(69,65));Paint(5,selectedEntity.Id,new Point(64,69));
        check(arenaPreset is {BossX:65.5,BossY:66.5}&&ReadPreset() is {StartX:69.5,StartY:65.5}&&arenaPreset.Actors.Count==1,"Encounter brush modes place primary, player and additional enemy starts");
        arenaShape.SelectedItem="Circle";arenaSizeChoice.SelectedItem=64;
        check(arenaPreset.Shape=="Circle"&&ArenaSize.Text=="64"&&Viewport.ArenaDraftSize==64,"Styled arena controls edit native shape and size and refresh the canvas");
        var saved=Path.Combine(output,"native-arena.rbs");BehaviorDocument.Save(saved,Snapshot());LoadProject(saved);
        check(arenaPreset.Tiles.Count==3&&arenaPreset.Actors.Count==1&&arenaPreset.Shape=="Circle"&&ReadPreset().StartX==69.5,"Desktop save/open retains terrain, obstacles, bounds and all authored starts");
        ArenaEditButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Width=1500;Height=1000;WorkspaceTabs.SelectedIndex=0;UpdateLayout();CaptureView(this,Path.Combine(output,"native-arena-editor.png"));
        check(Viewport.ActualHeight>150&&ArenaControls.ActualWidth>400,"Arena controls wrap while preserving a usable encounter viewport");
        Play(this,new());await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        check(session is EncounterSession native&&native.Arena.Geometry is {Shape:"Circle",Size:64}&&native.Entities.Count==2&&native.Entities.MinBy(e=>e.Id) is {X:65.5f,Y:66.5f}&&!native.ProbeArena(61.5,64.5).Passable,"Desktop Play loads the native edited map and actual extra actors");
        session!.Advance(new(),100);check(session.Shots.Count>=2,"Desktop edited arenas retain active native attacks from the primary and extra enemies");
        running=false;UpdateSession();UpdateLayout();CaptureView(Viewport,Path.Combine(output,"native-arena-runtime.png"));
        ArenaEditButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Paint(2,null,new Point(64,69));check(arenaPreset.Actors.Count==0,"Erase removes extra enemy placements on the painted cell");
        check(File.ReadAllText(selectedEntity.SourceFile)==originalSource,"Desktop arena authoring preserves the private game source");
        ApplyProject(previous,null);arenaUndo.Clear();arenaRedo.Clear();ClearArenaEditor();
    }
}
