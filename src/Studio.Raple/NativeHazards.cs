using BossPatternSim;
using Studio.Core;
using TKR.Shared.resources;

namespace Studio.Raple;

public sealed record HazardProjectile(int Index,double AccelerationDelayMs,double ArmingDelayMs,double LifetimeMs,double SpeedCap);
public sealed record HazardPattern(string Layout,int Projectile,int IntervalMs,int StartupMs,int VolleyLimit,bool OutwardFirst)
{
    public long NativeIntervalMs => (long)Math.Ceiling(IntervalMs/100d)*100;
    public long NativeStartupMs => (long)Math.Ceiling(StartupMs/100d)*100;
}
public sealed partial class RapleAdapter
{
    public bool IsDelayedHazard(BehaviorDocument document,BehaviorNode node)=>node.Kind=="DelayedOriginHazards"&&NativeKind(document,node)==node.Kind;
    static bool HazardCompatible(ProjectileDesc p)=>p.Speed==0&&p.Acceleration>0&&p.AccelerationDelay>=500&&p.ArmingDelayMS>=p.AccelerationDelay&&p.ArmingDelayMS<p.LifetimeMS&&p.SpeedCap>0&&p.ZeroVelocityDelay<0&&!p.Radial&&!p.ReturnToOrigin&&!p.Homing&&!p.Orbit;
    public IReadOnlyList<HazardProjectile> HazardProjectiles(string entityId)
    {
        if(!SimEngine.Data.IdToObjectType.TryGetValue(entityId,out var type))return [];
        return SimEngine.Data.ObjectDescs[type].Projectiles.Where(pair=>pair.Key is >=0 and <=255&&pair.Key==pair.Value.BulletType&&HazardCompatible(pair.Value))
            .Select(pair=>new HazardProjectile(pair.Key,pair.Value.AccelerationDelay,pair.Value.ArmingDelayMS,pair.Value.LifetimeMS,pair.Value.SpeedCap)).OrderBy(p=>p.Index).ToArray();
    }
    public HazardPattern? ReadHazardPattern(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsDelayedHazard(document,node))return null;
        var names=new[]{"projectileIndex","intervalMs","startupMs","volleyLimit"};double?[] defaults=[null,null,0,int.MaxValue];var values=new int[4];
        for(var i=0;i<4;i++){if(!ReadNumericParameter(document,node,names[i],defaults[i],out var number)||number==null||!double.IsFinite(number.Value)||number!=Math.Truncate(number.Value)||number<int.MinValue||number>int.MaxValue)return null;values[i]=(int)number.Value;}
        if(values[0] is <0 or >255||values[1]<100||values[2]<0||values[3]<1||!ReadHazardLayout(document,node,out var layout)||layout is not (0 or 1)||!ReadConstantParameter(document,node,"outwardFirst",false,out var outward)||outward is not bool first)return null;
        return new(layout==0?"Trail":"Lattice",values[0],values[1],values[2],values[3],first);
    }
    bool ReadHazardLayout(BehaviorDocument document,BehaviorNode node,out int? layout)
    {
        layout=null;if(!ReadConstantParameter(document,node,"layout",null,out var constant)||constant==null)return false;
        if(constant is not int&&!constant.GetType().IsEnum)return false;
        layout=Convert.ToInt32(constant,System.Globalization.CultureInfo.InvariantCulture);return true;
    }
    IEnumerable<DocumentDiagnostic> ValidateDelayedHazards(BehaviorDocument document,string? entityId)
    {
        foreach(var owner in document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId))))
        foreach(var node in owner.Root.Descendants().Where(n=>IsDelayedHazard(document,n)))
        {
            foreach(var (name,fallback,min,max) in new[]{("projectileIndex",(double?)null,0d,255d),("intervalMs",(double?)null,100d,(double)int.MaxValue),("startupMs",(double?)0,0d,(double)int.MaxValue),("volleyLimit",(double?)int.MaxValue,1d,(double)int.MaxValue)})
                if(ReadNumericParameter(document,node,name,fallback,out var value)&&value is {} known&&(!double.IsFinite(known)||known<min||known>max||known!=Math.Truncate(known)))yield return new("DelayedOriginHazards: invalid native "+name+".",node.Id);
            if(ReadHazardLayout(document,node,out var layout)&&layout is not (0 or 1))yield return new("DelayedOriginHazards: choose the native Trail or Lattice layout.",node.Id);
            if(ReadProjectileIndex(document,node) is {} index)
                foreach(var caller in owner.Definition?.Encounters??new[]{owner.EntityId})
                    if(!HazardProjectiles(caller).Any(p=>p.Index==index))yield return new("DelayedOriginHazards: projectile #"+index+" in "+caller+" must use native dormant acceleration, positive speed cap and arming delay before expiry.",node.Id);
        }
    }
}
