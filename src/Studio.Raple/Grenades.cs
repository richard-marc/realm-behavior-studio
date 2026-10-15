using Studio.Core;

namespace Studio.Raple;
public sealed record GrenadeLanding(float X,float Y);
public sealed record GrenadeSettings(double Radius,int Damage,double Range,double? FixedAngle,int Count,double? Spacing,double Offset,double? Rotation,uint Color)
{
    public double NativeSpacing => Count==1 ? 0 : Spacing ?? 360/Count;
    public IReadOnlyList<GrenadeLanding> FixedTargets(float x,float y,int previousActivations=0)
    {
        if(FixedAngle==null)return [];
        var angle=(float)(FixedAngle.Value*Math.PI/180);
        angle+=(float)(Offset*Math.PI/180)+(float)((Rotation??0)*Math.PI/180)*previousActivations;
        var spacing=(float)(NativeSpacing*Math.PI/180);
        var points=Enumerable.Range(0,Count).Select(i=>new GrenadeLanding((float)(Range*Math.Cos(angle+spacing*i))+x,(float)(Range*Math.Sin(angle+spacing*i))+y)).ToArray();
        if(points.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)))throw new ArgumentException("Native grenade coordinates exceed the finite diagram range.");
        return points;
    }
}
public sealed partial class RapleAdapter
{
    public bool IsGrenade(BehaviorDocument document,BehaviorNode node)=>node.Kind=="Grenade"&&NativeKind(document,node)=="Grenade";
    public GrenadeSettings? ReadGrenade(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsGrenade(document,node))return null;
        var names=new[]{"radius","damage","range","fixedAngle","count","shootAngle","angleOffset","rotateAngle","color"};double?[] defaults=[null,null,5,null,1,null,0,null,0xffff0000u];var values=new double?[names.Length];
        for(var i=0;i<names.Length;i++)if(!ReadNumericParameter(document,node,names[i],defaults[i],out values[i]))return null;
        if(values[0] is not{} radius||!float.IsFinite((float)radius)||radius<0||values[1] is not{} damage||damage<int.MinValue||damage>int.MaxValue||damage!=Math.Truncate(damage)||
            values[2] is not{} range||values[4] is not{} count||count<1||count>255||count!=Math.Truncate(count)||values[6] is not{} offset||values[8] is not{} color||color<0||color>uint.MaxValue||color!=Math.Truncate(color))return null;
        return new(radius,(int)damage,range,values[3],(int)count,values[5],offset,values[7],(uint)color);
    }
    IEnumerable<DocumentDiagnostic> ValidateGrenades(BehaviorDocument document,string? entityId)
    {
        var owners=document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId)));
        foreach(var node in owners.SelectMany(e=>e.Root.Descendants()).Where(n=>IsGrenade(document,n)).DistinctBy(n=>n.Start))
        {
            foreach(var name in new[]{"radius","range","fixedAngle","rotateAngle","shootAngle","angleOffset"})
                if(ReadConstantParameter(document,node,name,null,out var value)&&value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal&&
                    !float.IsFinite((float)Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture)))yield return new("Grenade "+name+" exceeds finite native single-precision geometry.",node.Id);
        }
    }
}
