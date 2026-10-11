using System.Windows;
using System.Windows.Media;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public sealed partial class EncounterViewport
{
    public bool ArenaEditing { get; set; }
    public ArenaPreset ArenaDraft { get; set; }=new();
    public int ArenaDraftSize { get; set; }=48;
    public Point ArenaPlayerStart { get; set; }=new(70.5,64.5);
    public event Action<Point[]>? ArenaStroke;
    readonly List<Point> arenaStroke=[];
    bool paintingArena;
    readonly Dictionary<ushort,ImageBrush> groundBrushes=[];
    Dictionary<ushort,string>? objectSprites;
    internal bool BeginArenaStroke(Point screen)
    {
        if(!ArenaEditing)return false;
        paintingArena=true;arenaStroke.Clear();AddArenaPoint(screen);return true;
    }
    void AddArenaPoint(Point screen)
    {
        var world=World(screen);var point=new Point(Math.Floor(world.X),Math.Floor(world.Y));
        if(point.X<0||point.Y<0||point.X>=128||point.Y>=128)return;
        if(arenaStroke.Count>0)
        {
            var previous=arenaStroke[^1];var steps=(int)Math.Max(Math.Abs(point.X-previous.X),Math.Abs(point.Y-previous.Y));
            for(var i=1;i<=steps;i++)arenaStroke.Add(new(Math.Round(previous.X+(point.X-previous.X)*i/steps),Math.Round(previous.Y+(point.Y-previous.Y)*i/steps)));
        }
        else arenaStroke.Add(point);
        InvalidateVisual();
    }
    internal void FinishArenaStroke()
    {
        if(!paintingArena)return;paintingArena=false;
        var points=arenaStroke.Distinct().ToArray();arenaStroke.Clear();if(points.Length>0)ArenaStroke?.Invoke(points);InvalidateVisual();
    }
    void CancelArenaStroke(){paintingArena=false;arenaStroke.Clear();InvalidateVisual();}
    internal void CancelArenaPainting()=>CancelArenaStroke();
    internal Point ArenaScreen(double x,double y)=>Screen(x,y);
    internal void ContinueArenaStroke(Point screen){if(paintingArena)AddArenaPoint(screen);}
    ImageBrush? GroundBrush(ushort type)
    {
        if(groundBrushes.TryGetValue(type,out var cached))return cached;
        if(Sprites?.GetGround(type) is not {} image)return null;
        var brush=new ImageBrush(image){ViewportUnits=BrushMappingMode.Absolute,Viewport=new(0,0,1,1),TileMode=TileMode.Tile,Stretch=Stretch.Fill};brush.Freeze();return groundBrushes[type]=brush;
    }
    void DrawArena(DrawingContext dc)
    {
        var scene=Session?.Arena;
        if(scene==null&&!ArenaEditing)return;
        var minimum=scene?.Geometry.Minimum??(128-ArenaDraftSize)/2+.5;
        var maximum=scene?.Geometry.Maximum??(128-ArenaDraftSize)/2+ArenaDraftSize-.5;
        var radius=scene?.Geometry.Radius??ArenaDraftSize/2f-.5;
        var shape=scene?.Geometry.Shape??ArenaDraft.Shape;
        var floor=scene?.GroundType??NativeArena.GroundType(ArenaDraft.GroundId);
        var topLeft=Screen(0,0);
        dc.PushTransform(new MatrixTransform(scale,0,0,scale,topLeft.X,topLeft.Y));
        dc.PushOpacity(.35);
        dc.DrawRectangle(GroundBrush(floor)??(Brush)Brush("#292E29"),null,new(0,0,128,128));
        if(scene!=null)
        {
            foreach(var cell in scene.Cells)
                if(cell.GroundType!=floor)dc.DrawRectangle(GroundBrush(cell.GroundType)??(Brush)Brush("#292E29"),null,new(cell.X,cell.Y,1,1));
        }
        else foreach(var cell in ArenaDraft.Tiles)
            dc.DrawRectangle(GroundBrush(NativeArena.GroundType(cell.GroundId))??(Brush)Brush("#292E29"),null,new(cell.X,cell.Y,1,1));
        dc.Pop();
        Geometry inside=shape=="Circle"?new EllipseGeometry(new(64,64),radius,radius):new RectangleGeometry(new(minimum,minimum,maximum-minimum,maximum-minimum));
        var outside=Geometry.Combine(new RectangleGeometry(new(0,0,128,128)),inside,GeometryCombineMode.Exclude,null);
        dc.DrawGeometry(Brush("#B8101215"),null,outside);dc.DrawGeometry(null,new Pen(Brush("#737F74"),1/scale),inside);dc.Pop();
        objectSprites??=NativeArena.Obstacles().GroupBy(o=>o.Type).ToDictionary(g=>g.Key,g=>g.First().SpriteId);
        if(scene!=null)
        {
            foreach(var cell in scene.Cells.Where(c=>c.ObjectType!=0).OrderBy(c=>c.Y))
                if(objectSprites.TryGetValue(cell.ObjectType,out var id))DrawArenaObject(dc,id,cell.X,cell.Y);
        }
        else foreach(var tile in ArenaDraft.Tiles.Where(t=>t.ObjectId!=null).OrderBy(t=>t.Y))
            DrawArenaObject(dc,objectSprites.GetValueOrDefault(NativeArena.ObstacleType(tile.ObjectId))??tile.ObjectId!,tile.X,tile.Y);
        if(!ArenaEditing)return;
        void Spawn(string id,double x,double y,string label)
        {var p=Screen(x,y);dc.DrawEllipse(null,new Pen(Brush("#8BD6B0"),1),p,scale*.42,scale*.42);DrawCharacter(dc,id,p,SpriteAction.Stand,0,0);Text(dc,label,new(p.X+scale*.45,p.Y-10),11,Brush("#D2DBD5"));}
        Spawn(PreviewEntity,ArenaDraft.BossX,ArenaDraft.BossY,"Enemy");Spawn("Wizard",ArenaPlayerStart.X,ArenaPlayerStart.Y,"Player");
        foreach(var actor in ArenaDraft.Actors)Spawn(actor.EntityId,actor.X,actor.Y,actor.EntityId);
        foreach(var point in arenaStroke)dc.DrawRectangle(Brush("#408BD6B0"),new Pen(Brush("#8BD6B0"),1),new(Screen(point.X,point.Y),new Size(scale,scale)));
        Text(dc,"Paint tiles  ·  middle-drag to pan  ·  scroll to zoom",new(16,ActualHeight-28),11,Brush("#D2DBD5"));
    }
    void DrawArenaObject(DrawingContext dc,string id,int x,int y)
    {
        var p=Screen(x+.5,y+.5);
        if(p.X< -scale*3||p.Y< -scale*3||p.X>ActualWidth+scale*3||p.Y>ActualHeight+scale*3)return;
        DrawCharacter(dc,id,p,SpriteAction.Stand,0,0);
    }
}
