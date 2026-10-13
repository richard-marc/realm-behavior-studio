using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Studio.Raple;

namespace Studio.Desktop;
public sealed record RingSelection(Guid NodeId,long Revision,RingPattern Pattern,bool Editable);
public enum RingHandle { Direction,Opening,Divisions }
public sealed partial class EncounterViewport
{
    RingSelection? ring;
    (RingSelection Original,RingHandle Handle)? ringDrag;
    (double Scale,Vector Pan)? ringCamera;
    public RingSelection? Ring
    {
        get=>ring;set
        {
            if(value!=null){if(crossCut!=null)CrossCut=null;if(attack!=null)Attack=null;if(movement!=null)Movement=null;if(path!=null)Path=null;if(sweep!=null)Sweep=null;}
            if(ring==null&&value!=null)ringCamera=(scale,pan);CancelRingDrag();ring=value;
            if(value==null&&ringCamera is {} camera){scale=camera.Scale;pan=camera.Pan;ringCamera=null;}InvalidateVisual();
        }
    }
    public event Action<RingSelection,IReadOnlyDictionary<string,double>>? RingEdited;
    internal Point RingPoint(double degrees,double distance)=>HitScreen(64.5+Math.Cos(degrees%360*Math.PI/180)*distance,64.5+Math.Sin(degrees%360*Math.PI/180)*distance);
    double RingDirection()=>ring!.Pattern.Angle-(ring.Pattern.Kind=="GapRing"?180:0);
    internal Point RingHandlePoint(RingHandle handle)
    {
        var p=ring!.Pattern;var direction=RingDirection();return handle switch
        {
            RingHandle.Direction=>RingPoint(direction,4+16/scale),
            RingHandle.Opening=>RingPoint(p.Kind=="GapRing"?direction+(p.Missing!.Value+1)*p.Spacing/2:p.Angle+p.Spacing*(p.Count-1)/2,5+16/scale),
            _=>RingPoint(direction-90,p.Slots!.Value/4d+16/scale)
        };
    }
    IEnumerable<RingHandle> RingHandles(){yield return RingHandle.Direction;if(ring!.Pattern.Kind=="GapRing"||ring.Pattern.Count>1)yield return RingHandle.Opening;if(ring.Pattern.Kind=="GapRing")yield return RingHandle.Divisions;}
    internal bool BeginRingDrag(Point point)
    {if(ring is not {Editable:true} selected)return false;foreach(var handle in RingHandles()){if((RingHandlePoint(handle)-point).Length>10)continue;ringDrag=(selected,handle);InvalidateVisual();return true;}return false;}
    internal void MoveRingDrag(Point point)
    {
        if(ringDrag is not {} edit)return;var world=HitWorld(point);var x=world.X-64.5;var y=world.Y-64.5;if(!double.IsFinite(x)||!double.IsFinite(y)||Math.Abs(x)+Math.Abs(y)<.000001)return;
        var p=edit.Original.Pattern;var degrees=Math.Atan2(y,x)*180/Math.PI;RingPattern next;
        if(edit.Handle==RingHandle.Direction)
        {
            var original=p.Angle-(p.Kind=="GapRing"?180:0);var value=degrees+360*Math.Round((original-degrees)/360);next=p with{Angle=Math.Round(value+(p.Kind=="GapRing"?180:0),3)};
        }
        else if(p.Kind=="GapRing"&&edit.Handle==RingHandle.Opening)
        {var half=Math.Abs(Signed(degrees-(p.Angle-180)));var missing=Math.Clamp((int)Math.Round(2*half/p.Spacing-1),1,p.Slots!.Value-2);next=p with{Missing=missing,Count=p.Slots.Value-missing};}
        else if(p.Kind=="GapRing")
        {var slots=Math.Clamp((int)Math.Round(Math.Max(0,double.Hypot(x,y)-16/scale)*4),4,64);var missing=Math.Min(p.Missing!.Value,slots-2);next=p with{Slots=slots,Missing=missing,Count=slots-missing,Spacing=360d/slots};}
        else
        {var delta=Signed(degrees-p.Angle);var half=p.Spacing*(p.Count-1)/2;delta+=360*Math.Round((half-delta)/360);next=p with{Spacing=Math.Round(delta*2/(p.Count-1),3)};}
        ring=edit.Original with{Pattern=next};InvalidateVisual();
    }
    internal void EndRingDrag()
    {
        if(ringDrag is not {} edit||ring==null)return;var p=ring.Pattern;var before=edit.Original.Pattern;ringDrag=null;var values=new Dictionary<string,double>();
        void Change(string name,double a,double b){if(Math.Abs(a-b)>.000001)values[name]=b;}
        Change(p.Kind=="GapRing"?"gapAngle":"angle",before.Angle-(p.Kind=="GapRing"?180:0),p.Angle-(p.Kind=="GapRing"?180:0));
        if(p.Kind=="GapRing"){Change("slots",before.Slots!.Value,p.Slots!.Value);Change("missing",before.Missing!.Value,p.Missing!.Value);}else Change("spacing",before.Spacing,p.Spacing);
        if(values.Count>0)RingEdited?.Invoke(edit.Original,values);InvalidateVisual();
    }
    internal void CancelRingDrag(){if(ringDrag is {} edit){ring=edit.Original;ringDrag=null;InvalidateVisual();}}
    internal void FitRing(){if(ring==null)return;scale=Math.Clamp(Math.Min(ActualWidth,ActualHeight)*.34/Math.Max(8,ring.Pattern.Slots/4d??8),.001,32);pan=new();InvalidateVisual();}
    void DrawRing(DrawingContext dc)
    {
        if(ring is not {} selected)return;var p=selected.Pattern;var origin=HitScreen(64.5,64.5);var rim=new Pen(Brush("#465B52"),1){DashStyle=DashStyles.Dash};dc.DrawEllipse(null,rim,origin,4*scale,4*scale);
        var ray=new Pen(Brush("#8EAEA1"),1.2);for(var i=0;i<p.Count;i++)dc.DrawLine(ray,origin,RingPoint(p.Angle%360-p.Spacing*(p.Count-1)/2+p.Spacing*i,4));
        DrawMovementMarker(dc,PreviewEntity,64.5,64.5,RingDirection()*Math.PI/180);
        foreach(var handle in RingHandles()){var point=RingHandlePoint(handle);dc.DrawEllipse(Brush("#182821"),new Pen(Brush(selected.Editable?"#A8CDBE":"#737D79"),1.5),point,6,6);Text(dc,handle switch{RingHandle.Direction=>p.Kind=="GapRing"?"Gap direction":"Heading",RingHandle.Opening=>p.Kind=="GapRing"?"Omitted divisions":"Spacing",_=>"Ring divisions"},point+new Vector(10,-9),11,Brush("#A4B8B0"));}
        Text(dc,p.Kind=="GapRing"?"Gap ring editor":"Spiral volley editor",new(18,18),13,Brush("#C4D5CF"));
        Text(dc,string.Create(CultureInfo.InvariantCulture,$"{p.Count} projectiles · {p.Spacing:0.###}° spacing")+(p.Slots is {} slots?$" · {slots} divisions · {p.Missing} omitted":""),new(18,41),11,Brush("#9FB0A8"));
        Text(dc,selected.Editable?"Drag layout handles · Esc cancels · Play executes the ring":"This ring is locked",new(18,ActualHeight-48),11,Brush("#8D9D95"));
        Text(dc,"Base layout · RNG, drift, rotation and conditions apply during Play",new(18,ActualHeight-28),10,Brush("#74817D"));
    }
}
