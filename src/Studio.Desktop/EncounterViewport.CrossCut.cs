using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Studio.Raple;
namespace Studio.Desktop;
public sealed record CrossCutSelection(Guid NodeId,long Revision,CrossCutPattern Pattern,bool Editable);
public enum CrossCutHandle{Radius,Spacing}
public sealed partial class EncounterViewport
{
    CrossCutSelection? crossCut; (CrossCutSelection Original,CrossCutHandle Handle)? crossCutDrag;(double Scale,Vector Pan)? crossCutCamera;
    public CrossCutSelection? CrossCut
    {
        get=>crossCut;set{if(value!=null){Attack=null;Movement=null;Path=null;Sweep=null;Ring=null;}if(crossCut==null&&value!=null)crossCutCamera=(scale,pan);CancelCrossCutDrag();crossCut=value;if(value==null&&crossCutCamera is{}camera){scale=camera.Scale;pan=camera.Pan;crossCutCamera=null;}InvalidateVisual();}
    }
    public event Action<CrossCutSelection,IReadOnlyDictionary<string,double>>? CrossCutEdited;
    internal Point CrossCutPoint(CrossCutHandle handle,double value)=>handle==CrossCutHandle.Radius?HitScreen(64.5-value,64.5):HitScreen(64.5-crossCut!.Pattern.Radius,64.5+value*Math.Max(1,(crossCut.Pattern.Count-1)/2d));
    internal Point CrossCutHandlePoint(CrossCutHandle handle)=>CrossCutPoint(handle,handle==CrossCutHandle.Radius?crossCut!.Pattern.Radius:crossCut!.Pattern.Spacing);
    internal bool BeginCrossCutDrag(Point point)
    {if(crossCut is not{Editable:true}selected)return false;foreach(var handle in new[]{CrossCutHandle.Radius,CrossCutHandle.Spacing}){if(handle==CrossCutHandle.Spacing&&selected.Pattern.Count==1)continue;if((CrossCutHandlePoint(handle)-point).Length<=10){crossCutDrag=(selected,handle);return true;}}return false;}
    internal void MoveCrossCutDrag(Point point)
    {
        if(crossCutDrag is not{} edit)return;var world=HitWorld(point);var p=edit.Original.Pattern;if(!double.IsFinite(world.X)||!double.IsFinite(world.Y))return;
        var maximum=edit.Handle==CrossCutHandle.Radius?Math.Max(p.Radius,p.MaximumRadius*(1-1e-10)):Math.Max(p.Spacing,p.MaximumSpacing*(1-1e-10));
        var next=edit.Handle==CrossCutHandle.Radius?p with{Radius=Math.Clamp(Math.Abs(world.X-64.5),Math.Min(.001,maximum),maximum)}:p with{Spacing=Math.Clamp(Math.Abs(world.Y-64.5)/Math.Max(1,(p.Count-1)/2d),Math.Min(.001,maximum),maximum)};
        crossCut=edit.Original with{Pattern=next};InvalidateVisual();
    }
    internal void EndCrossCutDrag(){if(crossCutDrag is not{} edit||crossCut==null)return;var p=crossCut.Pattern;crossCutDrag=null;var name=edit.Handle==CrossCutHandle.Radius?"radius":"spacing";var before=edit.Handle==CrossCutHandle.Radius?edit.Original.Pattern.Radius:edit.Original.Pattern.Spacing;var value=edit.Handle==CrossCutHandle.Radius?p.Radius:p.Spacing;if(Math.Abs(before-value)>.000001)CrossCutEdited?.Invoke(edit.Original,new Dictionary<string,double>{{name,value}});InvalidateVisual();}
    internal void CancelCrossCutDrag(){if(crossCutDrag is{} edit){crossCut=edit.Original;crossCutDrag=null;InvalidateVisual();}}
    internal void FitCrossCut(){if(crossCut==null)return;scale=Math.Clamp(Math.Min(ActualWidth,ActualHeight)*.32/15,.001,32);pan=new();InvalidateVisual();}
    void DrawCrossCut(DrawingContext dc)
    {
        if(crossCut is not{} selected)return;var p=selected.Pattern;var center=HitScreen(64.5,64.5);var dashed=new Pen(Brush("#40544B"),1){DashStyle=DashStyles.Dash};dc.DrawEllipse(null,dashed,center,14*scale,14*scale);
        foreach(var(x,y,heading)in p.Origins(64.5,64.5)){var from=HitScreen(x,y);dc.DrawEllipse(Brush("#9EBFAF"),null,from,3,3);dc.DrawLine(new Pen(Brush("#789A8B"),1.2),from,HitScreen(x+Math.Cos(heading)*3,y+Math.Sin(heading)*3));}
        DrawMovementMarker(dc,PreviewEntity,64.5,64.5,Math.PI);foreach(var handle in new[]{CrossCutHandle.Radius,CrossCutHandle.Spacing}){if(handle==CrossCutHandle.Spacing&&p.Count==1)continue;var point=CrossCutHandlePoint(handle);dc.DrawEllipse(Brush("#182821"),new Pen(Brush(selected.Editable?"#A8CDBE":"#737D79"),1.5),point,6,6);Text(dc,handle==CrossCutHandle.Radius?"Origin radius":"Lane spacing",point+(handle==CrossCutHandle.Radius?new Vector(-90,-30):new Vector(-85,12)),11,Brush("#A4B8B0"));}
        Text(dc,"Crosscut origin editor",new(18,18),13,Brush("#C4D5CF"));Text(dc,string.Create(CultureInfo.InvariantCulture,$"{p.Count} origins · {p.Radius:0.###} tile radius · {p.Spacing:0.###} tile spacing"),new(18,41),11,Brush("#9FB0A8"));Text(dc,"First edge shown · anchor captured at state entry",new(18,61),11,Brush("#83958B"));Text(dc,selected.Editable?"Drag origin radius or spacing · Esc cancels · Play runs the native sequence":"This origin layout is locked",new(18,ActualHeight-48),11,Brush("#8D9D95"));Text(dc,"Dashed limit: 14 tiles · passability and live host distance can suppress origins",new(18,ActualHeight-28),10,Brush("#74817D"));
    }
}
