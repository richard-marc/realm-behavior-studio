using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Studio.Raple;
using TKR.Shared;

namespace Studio.Desktop;
public sealed class NativeMapViewport:FrameworkElement
{
    NativeMapImage? model;
    BitmapSource? overview;
    public NativeMapImage? Model{get=>model;set{if(ReferenceEquals(value,model))return;model=value;overview=null;InvalidateVisual();}}
    public ClientSprites? Sprites{get;set;}
    public bool Painting{get;set;}
    public Point? Selection{get;set;}
    public event Action<int,int>? Selected;
    public event Action<Point[]>? Stroke;
    double scale=24;Vector pan;Point? panning;bool drawing;
    readonly List<Point> points=[];
    Dictionary<string,string>? spriteIds;
    static SolidColorBrush Brush(string hex){var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));b.Freeze();return b;}
    Point World(Point point)=>new((point.X-ActualWidth/2-pan.X)/scale+(model?.Width??128)/2d,(point.Y-ActualHeight/2-pan.Y)/scale+(model?.Height??128)/2d);
    Point Screen(double x,double y)=>new((x-(model?.Width??128)/2d)*scale+ActualWidth/2+pan.X,(y-(model?.Height??128)/2d)*scale+ActualHeight/2+pan.Y);
    internal Point TileScreen(int x,int y)=>Screen(x+.5,y+.5);
    public NativeMapViewport()
    {
        Focusable=true;RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.NearestNeighbor);
        MouseWheel+=(_,e)=>{var position=e.GetPosition(this);var before=World(position);scale=Math.Clamp(scale*(e.Delta>0?1.2:1/1.2),.05,96);pan+=position-Screen(before.X,before.Y);InvalidateVisual();e.Handled=true;};
        MouseDown+=(_,e)=>{Focus();if(e.ChangedButton==MouseButton.Middle){panning=e.GetPosition(this);CaptureMouse();}else if(e.ChangedButton==MouseButton.Left){BeginStroke(e.GetPosition(this));if(drawing)CaptureMouse();e.Handled=true;}};
        MouseMove+=(_,e)=>{if(panning is {} previous){var next=e.GetPosition(this);pan+=next-previous;panning=next;InvalidateVisual();}else if(drawing)ContinueStroke(e.GetPosition(this));};
        MouseUp+=(_,e)=>{if(e.ChangedButton==MouseButton.Middle)panning=null;if(e.ChangedButton==MouseButton.Left)FinishStroke();ReleaseMouseCapture();};
        LostMouseCapture+=(_,_)=>{panning=null;drawing=false;points.Clear();InvalidateVisual();};
    }
    public void Fit(){if(model!=null){scale=Math.Clamp(Math.Min(Math.Max(100,ActualWidth-40)/model.Width,Math.Max(100,ActualHeight-40)/model.Height),.05,32);pan=new();InvalidateVisual();}}
    public void FocusSelection(){if(Selection is {} point){scale=24;pan=new();var screen=Screen(point.X+.5,point.Y+.5);pan=new(ActualWidth/2-screen.X,ActualHeight/2-screen.Y);InvalidateVisual();}}
    internal void BeginStroke(Point screen)
    {
        if(model==null)return;var p=World(screen);var x=(int)Math.Floor(p.X);var y=(int)Math.Floor(p.Y);if(x<0||y<0||x>=model.Width||y>=model.Height)return;
        Selection=new(x,y);Selected?.Invoke(x,y);InvalidateVisual();if(!Painting)return;drawing=true;points.Clear();points.Add(new(x,y));
    }
    internal void ContinueStroke(Point screen)
    {
        if(!drawing||model==null)return;var p=World(screen);var point=new Point(Math.Floor(p.X),Math.Floor(p.Y));if(point.X<0||point.Y<0||point.X>=model.Width||point.Y>=model.Height)return;
        var previous=points[^1];var steps=(int)Math.Max(Math.Abs(point.X-previous.X),Math.Abs(point.Y-previous.Y));
        for(var i=1;i<=steps;i++)points.Add(new(Math.Round(previous.X+(point.X-previous.X)*i/steps),Math.Round(previous.Y+(point.Y-previous.Y)*i/steps)));InvalidateVisual();
    }
    internal void FinishStroke(){if(!drawing)return;drawing=false;var stroke=points.Distinct().ToArray();points.Clear();if(stroke.Length>0)Stroke?.Invoke(stroke);InvalidateVisual();}
    BitmapSource Overview()
    {
        if(overview!=null)return overview;var colors=new Dictionary<ushort,byte[]>();var pixels=new byte[model!.Width*model.Height*4];
        foreach(var ground in model.Cells.Select(c=>c.Ground).Distinct())
        {
            var color=new byte[]{25,28,26,255};
            if(Sprites?.GetGround(ground) is {} image)
            {
                var frame=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var raw=new byte[frame.PixelWidth*frame.PixelHeight*4];frame.CopyPixels(raw,frame.PixelWidth*4,0);
                for(var component=0;component<4;component++)color[component]=(byte)(Enumerable.Range(0,raw.Length/4).Sum(i=>(long)raw[i*4+component])/(raw.Length/4));
            }
            colors[ground]=color;
        }
        for(var i=0;i<model.Cells.Length;i++)Array.Copy(colors[model.Cells[i].Ground],0,pixels,i*4,4);
        var bitmap=BitmapSource.Create(model.Width,model.Height,96,96,PixelFormats.Bgra32,null,pixels,model.Width*4);bitmap.Freeze();return overview=bitmap;
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brush("#101215"),null,new(0,0,ActualWidth,ActualHeight));
        if(model==null){Text(dc,"Open a native .wmap or bring in the current arena.",new(20,20));return;}
        var start=World(new(0,0));var end=World(new(ActualWidth,ActualHeight));var minX=Math.Clamp((int)Math.Floor(start.X)-3,0,model.Width);var maxX=Math.Clamp((int)Math.Ceiling(end.X)+3,0,model.Width);var minY=Math.Clamp((int)Math.Floor(start.Y)-3,0,model.Height);var maxY=Math.Clamp((int)Math.Ceiling(end.Y)+3,0,model.Height);
        dc.PushOpacity(.35);
        if(scale<4)dc.DrawImage(Overview(),new(Screen(0,0),new Size(model.Width*scale,model.Height*scale)));
        else for(var y=minY;y<maxY;y++)for(var x=minX;x<maxX;x++)
        {var rect=new Rect(Screen(x,y),new Size(scale,scale));if(Sprites?.GetGround(model[x,y].Ground) is {} image)dc.DrawImage(image,rect);else dc.DrawRectangle(Brush("#292E29"),null,rect);}
        dc.Pop();spriteIds??=EntityResources.Catalog(EntityResourceScope.Resolvable).ToDictionary(e=>e.Id,e=>e.SpriteId,StringComparer.Ordinal);
        if(scale>=4)for(var y=minY;y<maxY;y++)for(var x=minX;x<maxX;x++)
        {
            var cell=model[x,y];if(cell.ObjectId==""||!spriteIds.TryGetValue(cell.ObjectId,out var id)||Sprites?.Get(id) is not {} image)continue;
            var size=Sprites.Size(id);
            // This is the same numeric parser and cap used by native Wmap configuration.
            foreach(var cfg in cell.Config.Split(';'))
            {
                var fields=cfg.Split(':');if(fields.Length<2||fields[0]!="size")continue;
                try{size=Math.Max(0,Math.Min(500,Utils.GetInt(fields[1])))/100d;}catch(Exception error) when(error is FormatException or OverflowException or ArgumentException){}
            }
            var ground=Screen(x+.5,y+.5);dc.DrawEllipse(Brush("#090A0C"),null,ground+new Vector(0,1),scale*.4,scale*.12);dc.DrawImage(image,EncounterViewport.CharacterBounds(ground,scale,size,image,Sprites.Elevation(id)));
        }
        if(Selection is {} selected)dc.DrawRectangle(null,new Pen(Brush("#8BD6B0"),1.5),new(Screen(selected.X,selected.Y),new Size(scale,scale)));
        foreach(var point in points)dc.DrawRectangle(Brush("#408BD6B0"),null,new(Screen(point.X,point.Y),new Size(scale,scale)));
        Text(dc,$"{model.Width} × {model.Height}  ·  middle-drag to pan  ·  scroll to zoom",new(16,ActualHeight-27));
    }
    void Text(DrawingContext dc,string text,Point point)=>dc.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,Brush("#D2DBD5"),VisualTreeHelper.GetDpi(this).PixelsPerDip),point);
}
