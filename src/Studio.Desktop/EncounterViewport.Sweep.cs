using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Studio.Raple;

namespace Studio.Desktop;
public sealed record SweepSelection(Guid NodeId,long Revision,SequentialSweep Pattern,bool Editable);
public enum SweepHandle { Width,Trigger,AimRange }
public sealed partial class EncounterViewport
{
    SweepSelection? sweep;
    (SweepSelection Original,SweepHandle Handle)? sweepDrag;
    (double Scale,Vector Pan)? sweepCamera;
    public SweepSelection? Sweep
    {
        get=>sweep;
        set
        {
            if(value!=null){if(attack!=null)Attack=null;if(movement!=null)Movement=null;if(path!=null)Path=null;}
            if(sweep==null&&value!=null)sweepCamera=(scale,pan);
            CancelSweepDrag();sweep=value;
            if(value==null&&sweepCamera is {} camera){scale=camera.Scale;pan=camera.Pan;sweepCamera=null;}
            InvalidateVisual();
        }
    }
    public event Action<SweepSelection,IReadOnlyDictionary<string,double>>? SweepEdited;
    internal Point SweepPoint(double degrees,double distance)=>HitScreen(64.5+Math.Cos(degrees*Math.PI/180)*distance,64.5+Math.Sin(degrees*Math.PI/180)*distance);
    internal Point SweepHandlePoint(SweepHandle handle)=>handle switch
    {
        SweepHandle.Width=>SweepPoint(sweep!.Pattern.SweepDegrees/2,4+16/scale),
        SweepHandle.Trigger=>SweepPoint(90,sweep!.Pattern.TriggerRange),
        _=>SweepPoint(-90,sweep!.Pattern.AimRange)
    };
    internal bool BeginSweepDrag(Point point)
    {
        if(sweep is not {Editable:true} selected)return false;
        foreach(var handle in Enum.GetValues<SweepHandle>())
        {
            if((SweepHandlePoint(handle)-point).Length>10)continue;
            sweepDrag=(selected,handle);InvalidateVisual();return true;
        }
        return false;
    }
    internal void MoveSweepDrag(Point point)
    {
        if(sweepDrag is not {} edit)return;
        var world=HitWorld(point);var x=world.X-64.5;var y=world.Y-64.5;var pattern=edit.Original.Pattern;
        if(!double.IsFinite(x)||!double.IsFinite(y))return;
        if(edit.Handle==SweepHandle.Width&&Math.Abs(x)+Math.Abs(y)<.000001)return;
        var length=double.Hypot(x,y);if(!double.IsFinite(length))return;
        var range=Math.Max(.001,Math.Round(length,3));
        var next=edit.Handle switch
        {
            SweepHandle.Width=>pattern with{SweepDegrees=Math.Round(Math.Abs(Signed(Math.Atan2(y,x)*180/Math.PI))*2,3)},
            SweepHandle.Trigger=>pattern with{TriggerRange=range,AimRange=Math.Max(pattern.AimRange,range)},
            _=>pattern with{AimRange=Math.Max(pattern.TriggerRange,range)}
        };
        sweep=edit.Original with{Pattern=next};InvalidateVisual();
    }
    internal void EndSweepDrag()
    {
        if(sweepDrag is not {} edit||sweep==null)return;
        var next=sweep.Pattern;var before=edit.Original.Pattern;sweepDrag=null;var values=new Dictionary<string,double>();
        void Change(string name,double original,double value){if(Math.Abs(original-value)>.000001)values[name]=value;}
        Change("sweepDegrees",before.SweepDegrees,next.SweepDegrees);Change("triggerRange",before.TriggerRange,next.TriggerRange);Change("aimRange",before.AimRange,next.AimRange);
        if(values.Count>0)SweepEdited?.Invoke(edit.Original,values);InvalidateVisual();
    }
    internal void CancelSweepDrag(){if(sweepDrag is {} edit){sweep=edit.Original;sweepDrag=null;InvalidateVisual();}}
    internal void FitSweep(){if(sweep==null)return;scale=Math.Clamp(Math.Min(ActualWidth,ActualHeight)*.36/Math.Max(6,sweep.Pattern.AimRange),double.Epsilon,32);pan=new();InvalidateVisual();}
    void DrawSweep(DrawingContext dc)
    {
        if(sweep is not {} selected)return;
        var pattern=selected.Pattern;var origin=HitScreen(64.5,64.5);var color=Brush("#8EAEA1");var boundary=new Pen(color,1.2);var quiet=new Pen(Brush("#465B52"),1){DashStyle=DashStyles.Dash};
        dc.DrawEllipse(null,quiet,origin,pattern.AimRange*scale,pattern.AimRange*scale);dc.DrawEllipse(null,boundary,origin,pattern.TriggerRange*scale,pattern.TriggerRange*scale);
        if(pattern.SweepDegrees>=360)dc.DrawEllipse(Brush("#192320"),quiet,origin,4*scale,4*scale);
        else if(pattern.SweepDegrees>0)
        {
            var cone=new StreamGeometry();using(var shape=cone.Open()){shape.BeginFigure(origin,true,true);shape.LineTo(SweepPoint(-pattern.SweepDegrees/2,4),true,false);shape.ArcTo(SweepPoint(pattern.SweepDegrees/2,4),new(4*scale,4*scale),0,pattern.SweepDegrees>180,SweepDirection.Clockwise,true,false);}
            dc.DrawGeometry(Brush("#192320"),quiet,cone);
        }
        var volley=Math.Min(pattern.ShotsPerVolley,pattern.Count);var ray=new Pen(color,1.2);
        for(var i=0;i<volley;i++){var angle=volley==1?-pattern.SweepDegrees/2:-pattern.SweepDegrees/2+pattern.SweepDegrees*i/(volley-1);dc.DrawLine(ray,origin,SweepPoint(angle,4));}
        DrawMovementMarker(dc,PreviewEntity,64.5,64.5,0);DrawMovementMarker(dc,"Wizard",70.5,64.5,Math.PI);
        foreach(var handle in Enum.GetValues<SweepHandle>())
        {
            var point=SweepHandlePoint(handle);dc.DrawEllipse(Brush("#182821"),new Pen(Brush(selected.Editable?"#A8CDBE":"#737D79"),1.5),point,6,6);
            Text(dc,handle switch{SweepHandle.Width=>"Cone width",SweepHandle.Trigger=>"Trigger range",_=>pattern.LockTarget?"Aim range · target locked":"Reacquisition range"},point+new Vector(10,-9),11,Brush("#A4B8B0"));
        }
        Text(dc,"Tracked burst sweep",new(18,18),13,Brush("#C4D5CF"));
        Text(dc,string.Create(CultureInfo.InvariantCulture,$"{pattern.SweepDegrees:0.###}° cone · {pattern.TriggerRange:0.###} tile trigger · {pattern.AimRange:0.###} tile aim range"),new(18,41),11,Brush("#9FB0A8"));
        if(pattern.TriggerRange<=6)Text(dc,"Prototype player is outside activation range",new(18,61),11,Brush("#C6AF85"));
        Text(dc,selected.Editable?"Drag cone or ranges · Esc cancels · Play runs the native burst":"This sweep is locked",new(18,ActualHeight-48),11,Brush("#8D9D95"));
        Text(dc,"Opening volley and targeting boundaries · the active sweep follows its acquired player",new(18,ActualHeight-28),10,Brush("#74817D"));
    }
}
