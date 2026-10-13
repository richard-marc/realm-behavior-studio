using Studio.Core;
namespace Studio.Raple;
public sealed record CrossCutPattern(int Projectile,double Radius,int Count,double Spacing,int IntervalMs,int StartupMs,bool BossLabOnly)
{
    public double HalfWidth=>(Count-1)*Spacing/2;
    public double MaximumRadius=>Math.Sqrt(Math.Max(0,196-HalfWidth*HalfWidth));
    public double MaximumSpacing=>Count==1?double.PositiveInfinity:2*Math.Sqrt(Math.Max(0,196-Radius*Radius))/(Count-1);
    public static readonly double[] EdgeDegrees=[180,0,-90,90,-135,45,-45,135];
    public (double X,double Y,double Heading)[] Origins(double x,double y,int volley=0)
    {
        var edge=EdgeDegrees[(volley%8+8)%8]*Math.PI/180;
        return Enumerable.Range(0,Count).Select(i=>{var offset=(i-(Count-1)/2d)*Spacing;return(x+Radius*Math.Cos(edge)+offset*Math.Sin(edge),y+Radius*Math.Sin(edge)-offset*Math.Cos(edge),edge+Math.PI);}).ToArray();
    }
}
public sealed partial class RapleAdapter
{
    public bool IsCrossCut(BehaviorDocument document,BehaviorNode node)=>node.Kind=="EdgeCrossCut"&&NativeKind(document,node)==node.Kind;
    public CrossCutPattern? ReadCrossCut(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsCrossCut(document,node))return null;string[] names=["projectileIndex","radius","count","spacing","intervalMs","startupMs"];double?[] defaults=[null,10.5,4,1.6,5200,3200];var values=new double[6];
        for(var i=0;i<6;i++){if(!ReadNumericParameter(document,node,names[i],defaults[i],out var value)||value==null)return null;values[i]=value.Value;}
        if(new[]{0,2,4,5}.Any(i=>values[i]!=Math.Truncate(values[i])||values[i]>int.MaxValue)||values[0] is <0 or >255||values[1]<=0||values[2] is <1 or >8||values[3]<=0||values[4]<100||values[5]<0||Math.Sqrt(values[1]*values[1]+Math.Pow((values[2]-1)*values[3]/2,2))>14||!ReadConstantParameter(document,node,"bossLabOnly",false,out var only)||only is not bool lab)return null;
        return new((int)values[0],values[1],(int)values[2],values[3],(int)values[4],(int)values[5],lab);
    }
    IEnumerable<DocumentDiagnostic> ValidateCrossCuts(BehaviorDocument document,string? entityId)
    {
        foreach(var owner in document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId))))foreach(var node in owner.Root.Descendants().Where(n=>IsCrossCut(document,n)))
        {
            double? Number(string name,double? fallback)=>ReadConstantParameter(document,node,name,fallback,out var constant)&&constant is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal?Convert.ToDouble(constant,System.Globalization.CultureInfo.InvariantCulture):null;
            var radius=Number("radius",10.5);var count=Number("count",4);var spacing=Number("spacing",1.6);
            foreach(var (name,value,min,max,integer)in new[]{("projectileIndex",Number("projectileIndex",null),0d,255d,true),("radius",radius,double.Epsilon,double.MaxValue,false),("count",count,1d,8d,true),("spacing",spacing,double.Epsilon,double.MaxValue,false),("intervalMs",Number("intervalMs",5200),100d,(double)int.MaxValue,true),("startupMs",Number("startupMs",3200),0d,(double)int.MaxValue,true)})
                if(value is{} known&&(!double.IsFinite(known)||known<min||known>max||integer&&known!=Math.Truncate(known)))yield return new("EdgeCrossCut: invalid native "+name+".",node.Id);
            if(radius!=null&&count!=null&&spacing!=null&&Math.Sqrt(radius.Value*radius.Value+Math.Pow((count.Value-1)*spacing.Value/2,2))>14)yield return new("EdgeCrossCut origins must fit within the native 14-tile envelope.",node.Id);
        }
    }
}
