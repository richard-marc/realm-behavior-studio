namespace Studio.Raple;

public enum TelegraphShape { Circle,Diamond,Boundary,Dot }
public sealed record TelegraphMark(double X,double Y,double Radius,double Progress,uint Color,TelegraphShape Shape,bool Opening=false);

// Ground-space interpretation of the supplied client's gameplay warning packets.
// These are visual markers, never collision shapes or safe-space predictions.
public static class TelegraphGeometry
{
    public static TelegraphMark[] Sample(IReadOnlyList<NativeEffect> effects,double ms,int mapWidth=128,int mapHeight=128)
    {
        var active=new List<NativeEffect>();
        foreach(var e in effects)
        {
            if(e.Ms>ms)continue;
            if(e.Kind==22&&e.DurationMs<=0){active.RemoveAll(p=>p.Kind==22&&p.TargetObjectId==e.TargetObjectId);continue;}
            if(ms-e.Ms<Math.Max(1,e.DurationMs))active.Add(e);
        }
        var marks=new List<TelegraphMark>();
        foreach(var e in active)
        {
            if(!float.IsFinite(e.X)||!float.IsFinite(e.Y)||!float.IsFinite(e.X2)||!float.IsFinite(e.Y2))continue;
            var elapsed=ms-e.Ms;var progress=Math.Clamp(elapsed/Math.Max(1,e.DurationMs),0,1);
            var fuse=Math.Round(progress*30,MidpointRounding.AwayFromZero)/30;
            void At(double radius,double angle,uint color,bool opening=false)
            {
                var x=e.X+radius*Math.Cos(angle);var y=e.Y+radius*Math.Sin(angle);
                if(x>=0&&y>=0&&x<mapWidth&&y<mapHeight)marks.Add(new(x,y,.1,progress,color,TelegraphShape.Dot,opening));
            }
            switch(e.Kind)
            {
                case 19 when e.X2>0:
                    marks.Add(new(e.X,e.Y,e.X2,fuse,0xffffff,TelegraphShape.Circle));break;
                case 17:case 21:
                    marks.Add(new(e.X,e.Y,1.5*59/64,fuse,0xff2549,TelegraphShape.Circle));
                    var spokes=e.Kind==21?16:10;
                    for(var i=0;i<spokes;i++)for(var j=0;j<3;j++)At(1.76+j*.3,e.ObjectType/10000d+i*Math.Tau/spokes,0xff7285);
                    break;
                case 18:
                    var count=Math.Clamp((int)e.ObjectType,0,64);var pulse=(int)(elapsed/300)%2!=0;
                    for(var i=0;i<count;i++)for(var radius=2.5;radius<=14;radius+=1.25)At(radius,e.X2+e.Y2*i,pulse?0xffef99:e.Color&0xffffff);
                    var arc=e.Y2*(count-1);var gap=Math.Tau-arc;
                    if(count>1&&e.Y2>0&&gap>e.Y2*1.5&&gap<Math.PI)
                        for(var radius=5d;radius<=14;radius+=3)At(radius,e.X2+arc+gap/2,pulse?0xb8f8ffu:0x22e6ffu,true);
                    break;
                case 22:
                    var style=(int)e.Y2;style=style is >=0 and <=2?style:0;
                    var radiusFactor=style==1?251/256d:59/64d;
                    var ledgerFuse=Math.Round(progress*24,MidpointRounding.AwayFromZero)/24;
                    marks.Add(new(e.X,e.Y,Math.Clamp(e.X2,.1,16)*radiusFactor,ledgerFuse,e.Color&0xffffff,style==0?TelegraphShape.Diamond:style==1?TelegraphShape.Boundary:TelegraphShape.Circle));break;
            }
        }
        return marks.ToArray();
    }
}
