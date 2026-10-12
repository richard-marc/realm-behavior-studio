using System.Windows;
using System.Windows.Media;
using Studio.Raple;

namespace Studio.Desktop;
public sealed partial class EncounterViewport
{
    double effectMs=-1;int effectCount=-1;IEncounterSession? effectSession;
    TelegraphMark[] effectMarks=[];
    internal int NativeTelegraphCount=>effectMarks.Length;
    void DrawNativeEffects(DrawingContext dc,IEncounterSession current,double ms)
    {
        if(effectSession!=current||effectMs!=ms||effectCount!=current.Effects.Count)
        {effectSession=current;effectMs=ms;effectCount=current.Effects.Count;effectMarks=TelegraphGeometry.Sample(current.Effects,ms,current.Arena.Width,current.Arena.Height);}
        foreach(var mark in effectMarks)
        {
            var point=Screen(mark.X,mark.Y);var radius=mark.Radius*scale;
            var color=Color.FromRgb((byte)(mark.Color>>16),(byte)(mark.Color>>8),(byte)mark.Color);
            var rim=new SolidColorBrush(color);rim.Freeze();
            var fill=new SolidColorBrush(Color.FromArgb(35,color.R,color.G,color.B));fill.Freeze();
            var backing=new Pen(Brush("#19080D"),4);var pen=new Pen(rim,1.5);
            if(mark.Shape==TelegraphShape.Dot)
            {dc.DrawEllipse(rim,backing,point,Math.Max(mark.Opening?3:2,radius),Math.Max(mark.Opening?3:2,radius));continue;}
            if(mark.Shape==TelegraphShape.Boundary){pen.DashStyle=DashStyles.Dash;backing.DashStyle=DashStyles.Dash;}
            if(mark.Shape==TelegraphShape.Diamond)
            {
                StreamGeometry Diamond(double r){var geometry=new StreamGeometry();using(var path=geometry.Open()){path.BeginFigure(point-new Vector(0,r),true,true);path.LineTo(point+new Vector(r,0),true,false);path.LineTo(point+new Vector(0,r),true,false);path.LineTo(point-new Vector(r,0),true,false);}geometry.Freeze();return geometry;}
                dc.DrawGeometry(fill,backing,Diamond(radius));dc.DrawGeometry(null,pen,Diamond(radius));dc.DrawGeometry(fill,pen,Diamond(radius*(.1+.9*mark.Progress)));
            }
            else
            {
                dc.DrawEllipse(mark.Shape==TelegraphShape.Boundary?null:fill,backing,point,radius,radius);dc.DrawEllipse(null,pen,point,radius,radius);
                if(mark.Shape!=TelegraphShape.Boundary){var inner=radius*(4/59d+55/59d*mark.Progress);dc.DrawEllipse(fill,pen,point,inner,inner);}
            }
        }
    }
}
