using BossPatternSim;
using Studio.Core;
using TKR.Shared.resources;
namespace Studio.Raple;

public sealed record SnareProjectile(int Index,double Speed,double LifetimeMs)
{
    public double ArrivalMs(double radius)=>radius*10000/Speed;
}
public sealed record SnarePattern(bool Echo,int Projectile,int Count,double Radius,int GapSlots,string Anchor,string Gap,double GapAngle,double GapStep,double TargetRange,double AcquireRange,int IntervalMs,int StartupMs,int LagMs,bool Telegraph,double MarkerRadius)
{
    // Source-derived diagnostic layout. Gameplay always executes the native class.
    public static (double X,double Y,double Heading)[] Origins(double ax,double ay,double gap,int count,int missing,double radius)
    {
        var step=Math.Tau/count;var half=missing%2==0?.5:0;var result=new List<(double,double,double)>();
        for(var j=0;j<count;j++){var c=j<=count/2?j:j-count;var offset=c+half;if(Math.Abs(offset)<missing/2d-1e-9)continue;var a=gap+offset*step;result.Add((ax+radius*Math.Cos(a),ay+radius*Math.Sin(a),a+Math.PI));}return result.ToArray();
    }
    public static (double X,double Y,double Gap) EchoAim(double hx,double hy,double px,double py,double nx,double ny,double clamp,double fallback)
    {
        var dx=px-hx;var dy=py-hy;var dist=Math.Sqrt(dx*dx+dy*dy);var scale=dist>clamp?clamp/dist:1;var ax=hx+dx*scale;var ay=hy+dy*scale;var mx=nx-ax;var my=ny-ay;var moved=(nx-px)*(nx-px)+(ny-py)*(ny-py)>=.25;
        return(ax,ay,moved&&mx*mx+my*my>=1e-6?Math.Atan2(my,mx):(ax-hx)*(ax-hx)+(ay-hy)*(ay-hy)>=.25?Math.Atan2(ay-hy,ax-hx):fallback);
    }
}
public sealed partial class RapleAdapter
{
    public bool IsSnare(BehaviorDocument document,BehaviorNode node)=>node.Kind is "ConvergingSnare" or "EchoSnare"&&NativeKind(document,node)==node.Kind;
    public double? ReadSnareRadius(BehaviorDocument document,BehaviorNode node)=>IsSnare(document,node)?SnareNumber(document,node,"radius",node.Kind=="EchoSnare"?5.5:6):null;
    static bool SnareStraight(ProjectileDesc p)=>p.Speed>0&&p.Acceleration==0&&!p.Wavy&&!p.Boomerang&&!p.Converging&&!p.Parametric&&!p.Radial&&!p.ReturnToOrigin&&!p.Homing&&!p.Orbit&&p.Amplitude==0&&p.AngleChange==0&&p.AngleChangeAccel==0&&p.ZeroVelocityDelay<0;
    public IReadOnlyList<SnareProjectile> SnareProjectiles(string entityId,double radius)
    {
        if(!SimEngine.Data.IdToObjectType.TryGetValue(entityId,out var type))return[];
        return SimEngine.Data.ObjectDescs[type].Projectiles.Where(p=>p.Key is >=0 and <=255&&p.Key==p.Value.BulletType&&SnareStraight(p.Value)&&p.Value.LifetimeMS*p.Value.Speed/10000>=radius).OrderBy(p=>p.Key).Select(p=>new SnareProjectile(p.Key,p.Value.Speed,p.Value.LifetimeMS)).ToArray();
    }
    double? SnareNumber(BehaviorDocument document,BehaviorNode node,string name,double? fallback)=>ReadConstantParameter(document,node,name,fallback,out var value)&&value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal?Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture):null;
    string? SnareEnum(BehaviorDocument document,BehaviorNode node,string name,string[] names)
    {
        if(!ReadConstantParameter(document,node,name,0,out var value)||value==null||value is not int&&!value.GetType().IsEnum)return null;var index=Convert.ToInt32(value,System.Globalization.CultureInfo.InvariantCulture);return index>=0&&index<names.Length?names[index]:null;
    }
    public SnarePattern? ReadSnare(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsSnare(document,node))return null;var echo=node.Kind=="EchoSnare";
        string[] names=["projectileIndex","count","radius","gapSlots","gapAngle","gapStep",echo?"clampRange":"targetRange","acquireRange","intervalMs","startupMs","lagMs",echo?"markRadius":"markerRadius"];
        double?[] defaults=[null,echo?14:16,echo?5.5:6,3,0,45,8,14,echo?3600:2400,echo?2500:800,2400,echo?.9:.8];var values=new double[12];
        for(var i=0;i<names.Length;i++){var value=SnareNumber(document,node,names[i],defaults[i]);if(value==null||!double.IsFinite(value.Value))return null;values[i]=value.Value;}
        if(new[]{0,1,3,8,9,10}.Any(i=>values[i]!=Math.Truncate(values[i])||values[i]>int.MaxValue)||values[0] is <0 or >255||values[1] is <3 or >32||values[2]<1||values[3]<0||values[3]>values[1]-2||values[6]<=0||values[7]<values[6]||values[8]<100||values[9]<0||echo&&values[10]<100||values[11]<(echo?double.Epsilon:0))return null;
        var anchor=echo?"History":SnareEnum(document,node,"anchor",["Target","Host","Spawn"]);var gap=echo?"Travel":SnareEnum(document,node,"gap",["AwayFromHost","TowardHost","Fixed","Rotating"]);
        if(anchor==null||gap==null||values[2]>14||(echo||anchor=="Target")&&values[2]+values[6]>14||!ReadConstantParameter(document,node,"telegraph",true,out var marker)||marker is not bool telegraph)return null;
        return new(echo,(int)values[0],(int)values[1],values[2],(int)values[3],anchor,gap,values[4],values[5],values[6],values[7],(int)values[8],(int)values[9],(int)values[10],echo||telegraph,values[11]);
    }
    IEnumerable<DocumentDiagnostic> ValidateSnares(BehaviorDocument document,string? entityId)
    {
        foreach(var owner in document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId))))foreach(var node in owner.Root.Descendants().Where(n=>IsSnare(document,n)))
        {
            var echo=node.Kind=="EchoSnare";double? N(string name,double? fallback)=>SnareNumber(document,node,name,fallback);
            var radius=N("radius",echo?5.5:6);var count=N("count",echo?14:16);var missing=N("gapSlots",3);var range=N(echo?"clampRange":"targetRange",8);var acquire=N("acquireRange",14);var anchor=echo?"History":SnareEnum(document,node,"anchor",["Target","Host","Spawn"]);
            foreach(var(name,value,min,max)in new[]{("projectileIndex",N("projectileIndex",null),0d,255d),("count",count,3d,32d),("gapSlots",missing,0d,30d),("intervalMs",N("intervalMs",echo?3600:2400),100d,(double)int.MaxValue),("startupMs",N("startupMs",echo?2500:800),0d,(double)int.MaxValue),("lagMs",echo?N("lagMs",2400):null,100d,(double)int.MaxValue)})
                if(value is{} v&&(!double.IsFinite(v)||v<min||v>max||v!=Math.Truncate(v)))yield return new(node.Kind+": invalid native "+name+".",node.Id);
            if(radius is{} r&&(!(r>=1)||!echo&&(!double.IsFinite(r)||r>14))||range is{} t&&(!(t>0)||!echo&&!double.IsFinite(t))||acquire is{} a&&(!echo&&!double.IsFinite(a)||range is{} lower&&!(a>=lower))||radius!=null&&range!=null&&(echo||anchor=="Target")&&radius+range>14||count!=null&&missing!=null&&missing>count-2)yield return new(node.Kind+": origins, gap slots or acquisition ranges violate the native constructor contract.",node.Id);
            if(!echo&&ReadConstantParameter(document,node,"anchor",0,out var av)&&av!=null&&anchor==null||!echo&&ReadConstantParameter(document,node,"gap",0,out var gv)&&gv!=null&&SnareEnum(document,node,"gap",["AwayFromHost","TowardHost","Fixed","Rotating"])==null)yield return new("Choose an original SnareAnchor and SnareGap value.",node.Id);
            foreach(var name in echo?new[]{"gapAngle","markRadius"}:new[]{"gapAngle","gapStep","markerRadius"})
                if(N(name,name=="gapStep"?45:name.Contains("Radius")?echo?.9:.8:0) is{} v&&((name=="gapAngle"||!echo)&&!double.IsFinite(v)||name.Contains("Radius")&&(echo?!(v>0):v<0)))yield return new(node.Kind+": invalid native "+name+".",node.Id);
            if(ReadProjectileIndex(document,node) is{} index&&radius is{} known)
                foreach(var caller in owner.Definition?.Encounters??new[]{owner.EntityId})if(!SnareProjectiles(caller,known).Any(p=>p.Index==index))yield return new(node.Kind+": owner XML projectile #"+index+" must be straight, constant-speed and live long enough to reach the anchor.",node.Id);
        }
    }
}
