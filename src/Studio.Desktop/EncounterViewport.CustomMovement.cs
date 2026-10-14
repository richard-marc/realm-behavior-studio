using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Studio.Raple;
namespace Studio.Desktop;
public sealed record CustomMovementSelection(Guid NodeId,long Revision,CustomMovement Pattern,bool Editable);
public enum CustomMovementHandle{Range,Acquire,Arrival}
public sealed partial class EncounterViewport
{
    CustomMovementSelection? customMove;(CustomMovementSelection Original,CustomMovementHandle Handle,Point Start)? customMoveDrag;(double Scale,Vector Pan)? customMoveCamera;
    public CustomMovementSelection? CustomMove
    {
        get=>customMove;set{if(value!=null){Attack=null;Movement=null;Path=null;Sweep=null;Ring=null;CrossCut=null;Snare=null;}if(customMove==null&&value!=null)customMoveCamera=(scale,pan);CancelCustomMoveDrag();customMove=value;if(value==null&&customMoveCamera is{} camera){scale=camera.Scale;pan=camera.Pan;customMoveCamera=null;}InvalidateVisual();}
    }
    public event Action<CustomMovementSelection,IReadOnlyDictionary<string,double>>? CustomMoveEdited;
    internal Point CustomMovePoint(CustomMovementHandle handle,double value)
    {
        var p=customMove!.Pattern;return p.Kind=="BackAndForth"?HitScreen(64.5+value,64.5):handle==CustomMovementHandle.Acquire?HitScreen(64.5,64.5-value):handle==CustomMovementHandle.Arrival?HitScreen(70.5-value,64.5):HitScreen(p.Kind=="Protect"?70.5:64.5,64.5+value);
    }
    internal Point CustomMoveHandlePoint(CustomMovementHandle handle)=>CustomMovePoint(handle,handle==CustomMovementHandle.Acquire?customMove!.Pattern.Acquire!.Value:handle==CustomMovementHandle.Arrival?customMove!.Pattern.Arrival!.Value:customMove!.Pattern.Radius);
    IEnumerable<CustomMovementHandle> CustomMoveHandles(){yield return CustomMovementHandle.Range;if(customMove!.Pattern.Kind=="Protect"){yield return CustomMovementHandle.Acquire;yield return CustomMovementHandle.Arrival;}}
    internal bool BeginCustomMoveDrag(Point point){if(customMove is not{Editable:true} selected)return false;foreach(var handle in CustomMoveHandles())if((CustomMoveHandlePoint(handle)-point).Length<=10){customMoveDrag=(selected,handle,point);return true;}return false;}
    internal void MoveCustomMoveDrag(Point point)
    {
        if(customMoveDrag is not{} edit)return;if((point-edit.Start).Length<.000001){customMove=edit.Original;InvalidateVisual();return;}var world=HitWorld(point);if(!double.IsFinite(world.X)||!double.IsFinite(world.Y))return;var p=edit.Original.Pattern;CustomMovement next;
        if(p.Kind=="BackAndForth")next=p with{Radius=Math.Round(Math.Clamp(world.X-64.5,int.MinValue,int.MaxValue),MidpointRounding.AwayFromZero)};
        else if(edit.Handle==CustomMovementHandle.Acquire)next=p with{Acquire=Math.Min(float.MaxValue,Math.Abs(world.Y-64.5))};
        else if(edit.Handle==CustomMovementHandle.Arrival)next=p with{Arrival=Math.Min(float.MaxValue,Math.Abs(world.X-70.5))};
        else next=p with{Radius=p.Kind=="PatrolCircle"?Math.Max(Math.Min(p.Radius,.001),Math.Abs(world.Y-64.5)):Math.Min(float.MaxValue,Math.Abs(world.Y-64.5))};
        customMove=edit.Original with{Pattern=next};InvalidateVisual();
    }
    internal void EndCustomMoveDrag()
    {
        if(customMoveDrag is not{} edit||customMove==null)return;var p=customMove.Pattern;customMoveDrag=null;var name=edit.Handle==CustomMovementHandle.Acquire?"acquireRange":edit.Handle==CustomMovementHandle.Arrival?"reprotectRange":p.Kind=="Protect"?"protectionRange":p.Kind=="BackAndForth"?"distance":"radius";var before=edit.Handle==CustomMovementHandle.Acquire?edit.Original.Pattern.Acquire!.Value:edit.Handle==CustomMovementHandle.Arrival?edit.Original.Pattern.Arrival!.Value:edit.Original.Pattern.Radius;var value=edit.Handle==CustomMovementHandle.Acquire?p.Acquire!.Value:edit.Handle==CustomMovementHandle.Arrival?p.Arrival!.Value:p.Radius;if(Math.Abs(before-value)>.000001)CustomMoveEdited?.Invoke(edit.Original,new Dictionary<string,double>{{name,value}});InvalidateVisual();
    }
    internal void CancelCustomMoveDrag(){if(customMoveDrag is{} edit){customMove=edit.Original;customMoveDrag=null;InvalidateVisual();}}
    internal void FitCustomMove(){if(customMove==null)return;var p=customMove.Pattern;var extent=Math.Max(Math.Abs(p.Radius),Math.Max(p.Acquire??0,p.Arrival??0))+(p.Kind=="Protect"?7:1);scale=Math.Clamp(Math.Min(ActualWidth,ActualHeight)*.32/Math.Max(6,extent),double.Epsilon,32);pan=new();InvalidateVisual();}
    void DrawCustomMove(DrawingContext dc)
    {
        if(customMove is not{} selected)return;var p=selected.Pattern;var origin=HitScreen(64.5,64.5);var target=HitScreen(70.5,64.5);var line=new Pen(Brush("#8EAEA1"),1.2);var dashed=new Pen(Brush("#465B52"),1){DashStyle=DashStyles.Dash};
        if(p.Kind=="BackAndForth")dc.DrawLine(line,origin,CustomMovePoint(CustomMovementHandle.Range,p.Radius));
        else if(p.Kind=="Protect"){dc.DrawEllipse(null,dashed,origin,p.Acquire!.Value*scale,p.Acquire.Value*scale);dc.DrawEllipse(null,line,target,p.Radius*scale,p.Radius*scale);dc.DrawEllipse(null,dashed,target,p.Arrival!.Value*scale,p.Arrival.Value*scale);DrawMovementMarker(dc,p.Target!,70.5,64.5,Math.PI);}
        else dc.DrawEllipse(null,line,origin,p.Radius*scale,p.Radius*scale);
        DrawMovementMarker(dc,PreviewEntity,64.5,64.5,0);foreach(var handle in CustomMoveHandles()){var point=CustomMoveHandlePoint(handle);dc.DrawEllipse(Brush("#182821"),new Pen(Brush(selected.Editable?"#A8CDBE":"#737D79"),1.5),point,6,6);Text(dc,handle==CustomMovementHandle.Acquire?"Acquire range":handle==CustomMovementHandle.Arrival?"Arrival distance":p.Kind=="Protect"?"Restart distance":p.Kind=="BackAndForth"?"Signed segment":"Patrol radius",point+(handle==CustomMovementHandle.Arrival?new Vector(-115,-40):handle==CustomMovementHandle.Acquire?new Vector(-90,-25):new Vector(10,12)),11,Brush("#A4B8B0"));}
        Text(dc,p.Kind=="BackAndForth"?"Back and forth movement":p.Kind=="PatrolCircle"?"Spawn circle patrol":"Protect movement",new(18,18),13,Brush("#C4D5CF"));Text(dc,string.Create(CultureInfo.InvariantCulture,$"{p.Radius:0.###} tiles")+(p.Kind=="PatrolCircle"?(p.Clockwise?" · clockwise":" · counterclockwise"):""),new(18,41),11,Brush("#9FB0A8"));Text(dc,selected.Editable?"Drag native distance handles · Esc cancels · Play runs the original controller":"This movement is locked",new(18,ActualHeight-48),11,Brush("#8D9D95"));Text(dc,p.Kind=="Protect"?"Prototype target · native nearest-type selection and fixed movement steps apply during Play":p.Kind=="BackAndForth"?"Attempted travel budget · blocked steps and signed segments retain native behavior":"Prototype spawn circle · off-circle steering and map passability execute during Play",new(18,ActualHeight-28),10,Brush("#74817D"));
    }
}
