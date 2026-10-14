using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Studio.Raple;
namespace Studio.Desktop;
public sealed record SnareSelection(Guid NodeId,long Revision,SnarePattern Pattern,double XmlReach,bool Editable);
public enum SnareHandle{Radius,ClampRange,Direction,Opening,Slots}
public sealed partial class EncounterViewport
{
    SnareSelection? snare;(SnareSelection Original,SnareHandle Handle,Point Start)? snareDrag;(double Scale,Vector Pan)? snareCamera;
    public SnareSelection? Snare
    {
        get=>snare;set
        {
            if(value!=null){Attack=null;Movement=null;Path=null;Sweep=null;Ring=null;CrossCut=null;}
            if(snare==null&&value!=null)snareCamera=(scale,pan);CancelSnareDrag();snare=value;
            if(value==null&&snareCamera is{} camera){scale=camera.Scale;pan=camera.Pan;snareCamera=null;}InvalidateVisual();
        }
    }
    public event Action<SnareSelection,IReadOnlyDictionary<string,double>>? SnareEdited;
    static bool SnareUsesClamp(SnarePattern p)=>p.Echo||p.Anchor=="Target";
    static bool SnareUsesDirection(SnarePattern p)=>!p.Echo&&p.Gap is "Fixed" or "Rotating";
    (double X,double Y) SnareAnchorPoint()=>SnareUsesClamp(snare!.Pattern)?(64.5+Math.Min(3,snare.Pattern.TargetRange),64.5):(64.5,64.5);
    double SnareDirection()=>SnareUsesDirection(snare!.Pattern)?snare.Pattern.GapAngle%360:snare!.Pattern.Gap=="TowardHost"?180:0;
    internal Point SnarePoint(SnareHandle handle,double value)
    {
        var p=snare!.Pattern;var (ax,ay)=SnareAnchorPoint();var direction=SnareDirection();
        return handle switch
        {
            SnareHandle.Radius=>HitScreen(ax,ay-value),
            SnareHandle.ClampRange=>HitScreen(64.5-value,64.5),
            SnareHandle.Slots=>new(34+(value-3)*5,ActualHeight-100),
            _=>HitScreen(ax+Math.Cos((handle==SnareHandle.Direction?value:direction+(value+1)*180/p.Count)*Math.PI/180)*(p.Radius+(handle==SnareHandle.Direction?24:48)/scale),ay+Math.Sin((handle==SnareHandle.Direction?value:direction+(value+1)*180/p.Count)*Math.PI/180)*(p.Radius+(handle==SnareHandle.Direction?24:48)/scale))
        };
    }
    internal Point SnareHandlePoint(SnareHandle handle)=>SnarePoint(handle,handle switch{SnareHandle.Radius=>snare!.Pattern.Radius,SnareHandle.ClampRange=>snare!.Pattern.TargetRange,SnareHandle.Direction=>snare!.Pattern.GapAngle%360,SnareHandle.Opening=>snare!.Pattern.GapSlots,_=>snare!.Pattern.Count});
    IEnumerable<SnareHandle> SnareHandles(){yield return SnareHandle.Radius;if(SnareUsesClamp(snare!.Pattern))yield return SnareHandle.ClampRange;if(SnareUsesDirection(snare.Pattern))yield return SnareHandle.Direction;yield return SnareHandle.Opening;yield return SnareHandle.Slots;}
    internal bool BeginSnareDrag(Point point)
    {if(snare is not{Editable:true} selected)return false;foreach(var handle in SnareHandles())if((SnareHandlePoint(handle)-point).Length<=10){snareDrag=(selected,handle,point);return true;}return false;}
    internal void MoveSnareDrag(Point point)
    {
        if(snareDrag is not{} edit)return;if((point-edit.Start).Length<.000001){snare=edit.Original;InvalidateVisual();return;}var world=HitWorld(point);if(!double.IsFinite(world.X)||!double.IsFinite(world.Y))return;var p=edit.Original.Pattern;var (ax,ay)=SnareAnchorPoint();var dx=world.X-ax;var dy=world.Y-ay;SnarePattern next;
        if(edit.Handle==SnareHandle.Radius){var limit=Math.Max(p.Radius,Math.Min(edit.Original.XmlReach,SnareUsesClamp(p)?14-p.TargetRange:14));next=p with{Radius=Math.Clamp(Math.Abs(dy),1,limit)};}
        else if(edit.Handle==SnareHandle.ClampRange){var limit=Math.Max(p.TargetRange,Math.Min(p.AcquireRange,14-p.Radius));next=p with{TargetRange=Math.Clamp(Math.Abs(world.X-64.5),Math.Min(p.TargetRange,Math.Min(.001,limit)),limit)};}
        else if(edit.Handle==SnareHandle.Slots){var count=(int)Math.Round(Math.Clamp((point.X-34)/5+3,3,32));next=p with{Count=count,GapSlots=Math.Min(p.GapSlots,count-2)};}
        else
        {
            if(Math.Abs(dx)+Math.Abs(dy)<.000001)return;var degrees=Math.Atan2(dy,dx)*180/Math.PI;
            if(edit.Handle==SnareHandle.Direction){var value=degrees+360*Math.Round((p.GapAngle-degrees)/360);next=p with{GapAngle=Math.Round(double.IsFinite(value)?value:degrees,3)};}
            else{var half=Math.Abs(Signed(degrees-SnareDirection()));next=p with{GapSlots=Math.Clamp((int)Math.Round(half*p.Count/180-1),0,p.Count-2)};}
        }
        snare=edit.Original with{Pattern=next};InvalidateVisual();
    }
    internal void EndSnareDrag()
    {
        if(snareDrag is not{} edit||snare==null)return;var p=snare.Pattern;var before=edit.Original.Pattern;snareDrag=null;var values=new Dictionary<string,double>();
        void Change(string name,double a,double b){if(Math.Abs(a-b)>.000001)values[name]=b;}
        Change("radius",before.Radius,p.Radius);Change(p.Echo?"clampRange":"targetRange",before.TargetRange,p.TargetRange);Change("gapAngle",before.GapAngle,p.GapAngle);Change("gapSlots",before.GapSlots,p.GapSlots);Change("count",before.Count,p.Count);if(values.Count>0)SnareEdited?.Invoke(edit.Original,values);InvalidateVisual();
    }
    internal void CancelSnareDrag(){if(snareDrag is{} edit){snare=edit.Original;snareDrag=null;InvalidateVisual();}}
    internal void FitSnare(){if(snare==null)return;scale=Math.Clamp(Math.Min(ActualWidth,ActualHeight)*.32/18,.001,32);pan=new();InvalidateVisual();}
    void DrawSnare(DrawingContext dc)
    {
        if(snare is not{} selected)return;var p=selected.Pattern;var (ax,ay)=SnareAnchorPoint();var center=HitScreen(ax,ay);var host=HitScreen(64.5,64.5);var dashed=new Pen(Brush("#40544B"),1){DashStyle=DashStyles.Dash};
        dc.DrawEllipse(null,dashed,host,14*scale,14*scale);dc.DrawEllipse(null,new Pen(Brush("#465B52"),1),center,p.Radius*scale,p.Radius*scale);
        foreach(var(x,y,heading)in SnarePattern.Origins(ax,ay,SnareDirection()*Math.PI/180,p.Count,p.GapSlots,p.Radius)){var from=HitScreen(x,y);dc.DrawEllipse(Brush("#A3C3B3"),null,from,3,3);dc.DrawLine(new Pen(Brush("#7B9B8C"),1.2),from,HitScreen(x+Math.Cos(heading)*Math.Min(2,p.Radius),y+Math.Sin(heading)*Math.Min(2,p.Radius)));}
        dc.DrawEllipse(Brush("#DCC793"),null,center,3,3);DrawMovementMarker(dc,PreviewEntity,64.5,64.5,0);
        dc.DrawLine(new Pen(Brush("#40544B"),2),SnarePoint(SnareHandle.Slots,3),SnarePoint(SnareHandle.Slots,32));
        foreach(var handle in SnareHandles()){var point=SnareHandlePoint(handle);dc.DrawEllipse(Brush("#182821"),new Pen(Brush(selected.Editable?"#A8CDBE":"#737D79"),1.5),point,6,6);Text(dc,handle switch{SnareHandle.Radius=>"Ring radius",SnareHandle.ClampRange=>"Anchor clamp",SnareHandle.Direction=>"Opening angle",SnareHandle.Opening=>"Omitted slots",_=>"Ring slots · "+p.Count},point+(handle==SnareHandle.ClampRange?new Vector(-96,-20):new Vector(10,-20)),11,Brush("#A4B8B0"));}
        Text(dc,p.Echo?"Echo snare layout":"Converging snare layout",new(18,18),13,Brush("#C4D5CF"));Text(dc,string.Create(CultureInfo.InvariantCulture,$"{p.Count-p.GapSlots} rays · {p.Radius:0.###} tile radius · {p.GapSlots} omitted"),new(18,41),11,Brush("#9FB0A8"));
        Text(dc,p.Echo?"Prototype history anchor · native travel/history determines the opening":SnareUsesClamp(p)?"Prototype target anchor · native targeting determines its actual position":"Prototype host/spawn anchor · native movement determines actual positions",new(18,61),11,Brush("#83958B"));
        Text(dc,selected.Editable?"Drag handles · Esc cancels · slot reductions also reduce an oversized gap":"This snare layout is locked",new(18,ActualHeight-48),11,Brush("#8D9D95"));Text(dc,"Dashed limit: native origin envelope · XML reach also bounds radius · warning cues are separate",new(18,ActualHeight-28),10,Brush("#74817D"));
    }
}
