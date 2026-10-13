using System.Globalization;
using Studio.Core;
using BossPatternSim;

namespace Studio.Raple;
public sealed record SequentialSweep(int Projectile,int Count,int ShotsPerVolley,int IntervalMs,int RecoveryMs,double TriggerRange,double AimRange,double SweepDegrees,bool LockTarget)
{
    public int Volleys=>(Count+ShotsPerVolley-1)/ShotsPerVolley;
    public long NativeIntervalMs=>(long)(Math.Ceiling(IntervalMs/100d)*100);
    public int[] VolleyCounts=>Enumerable.Range(0,Volleys).Select(i=>Math.Min(ShotsPerVolley,Count-i*ShotsPerVolley)).ToArray();
}
public sealed partial class RapleAdapter
{
    public bool IsSequentialSweep(BehaviorDocument document,BehaviorNode node)=>node.Kind=="SequentialAimedSweep"&&NativeKind(document,node)=="SequentialAimedSweep";
    public SequentialSweep? ReadSequentialSweep(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsSequentialSweep(document,node))return null;
        var names=new[]{"projectile","count","shotsPerVolley","intervalMs","recoveryMs","triggerRange","aimRange","sweepDegrees"};double?[] defaults=[null,40,1,100,500,2,16,60];var values=new double[names.Length];
        for(var i=0;i<names.Length;i++){if(!ReadNumericParameter(document,node,names[i],defaults[i],out var value)||value==null)return null;values[i]=value.Value;}
        if(!ReadConstantParameter(document,node,"lockTarget",false,out var locked)||locked is not bool lockTarget)return null;
        if(values.Take(5).Any(v=>v!=Math.Truncate(v)||v>int.MaxValue)||values[0] is <0 or >255||values[1] is <2 or >255||values[2] is <1 or >12||values[2]>values[1]||values[3]<100||values[4]<0||values[5]<=0||values[6]<values[5]||values[7] is <0 or >360)return null;
        return new((int)values[0],(int)values[1],(int)values[2],(int)values[3],(int)values[4],values[5],values[6],values[7],lockTarget);
    }
    IEnumerable<DocumentDiagnostic> ValidateSequentialSweeps(BehaviorDocument document,string? entityId)
    {
        var roots=document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId)));
        foreach(var owner in roots)foreach(var node in owner.Root.Descendants().Where(n=>IsSequentialSweep(document,n)))
        {
            double? Number(string name,double? fallback)=>ReadConstantParameter(document,node,name,fallback,out var constant)&&constant is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal?Convert.ToDouble(constant,CultureInfo.InvariantCulture):null;
            var count=Number("count",40);var shots=Number("shotsPerVolley",1);var trigger=Number("triggerRange",2);var aim=Number("aimRange",16);
            foreach(var (name,value,valid) in new (string,double?,Func<double,bool>)[]{("projectile",Number("projectile",null),v=>v>=0&&v<=255&&v==Math.Truncate(v)),("count",count,v=>v>=2&&v<=255&&v==Math.Truncate(v)),("shotsPerVolley",shots,v=>v>=1&&v<=12&&v==Math.Truncate(v)),("intervalMs",Number("intervalMs",100),v=>v>=100), ("recoveryMs",Number("recoveryMs",500),v=>v>=0), ("triggerRange",trigger,v=>v>0), ("aimRange",aim,v=>v>0), ("sweepDegrees",Number("sweepDegrees",60),v=>v>=0&&v<=360)})
                if(value is {} number&&(!double.IsFinite(number)||!valid(number)))yield return new("Sequential sweep: invalid "+name+" for the native constructor.",node.Id);
            if(shots>count)yield return new("Sequential sweep: shotsPerVolley cannot exceed the activation's total count.",node.Id);
            if(aim<trigger)yield return new("Sequential sweep: aimRange must be at least triggerRange.",node.Id);
            var index=Number("projectile",null);var callers=owner.Definition?.Encounters??new[]{owner.EntityId};
            if(index is >=0 and <=255&&index==Math.Truncate(index.Value))foreach(var caller in callers)
                if(SimEngine.Data.IdToObjectType.TryGetValue(caller,out var type)&&!SimEngine.Data.ObjectDescs[type].Projectiles.ContainsKey((int)index.Value))yield return new("This entity has no projectile index "+index+": "+caller,node.Id);
        }
    }
}
