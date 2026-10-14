using System.Reflection;
using BossPatternSim;
using Studio.Core;

namespace Studio.Raple;
public sealed record RecountPoint(long RecordedMs,double X,double Y,long FuseMs);
public sealed partial class RapleAdapter
{
    public bool IsTrailLedger(BehaviorDocument document,BehaviorNode node)=>node.Kind is "TrailLedger" or "TrailRecount"&&NativeKind(document,node)==node.Kind;
    public NativeAttackSettings? ReadTrailLedger(BehaviorDocument document,BehaviorNode node)=>IsTrailLedger(document,node)?ReadNativeAttackSettings(document,node,node.Kind):null;
    public IReadOnlyList<EnemyProjectile> TrailShardProjectiles(string entityId)=>SimEngine.Data.IdToObjectType.TryGetValue(entityId,out var type)?EnemyProjectiles.ForEntity(entityId).Where(p=>p.Index is >=0 and <=255&&SimEngine.Data.ObjectDescs[type].Projectiles[p.Index] is{} xml&&xml.BulletType==p.Index&&(xml.Speed>0||xml.Acceleration>0)).ToArray():[];
    public static bool OutsideLedgerBounds(double x,double y,double spawnX,double spawnY,double radius)=>(bool)RuntimeType("TrailLedger").GetMethod("Outside",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[x,y,spawnX,spawnY,radius])!;
    public static IReadOnlyList<RecountPoint> RecountLayout(NativeAttackSettings settings,IReadOnlyList<(long T,float X,float Y)> path,long now)
    {
        if(settings.Kind!="TrailRecount"||path.Count>4096)throw new ArgumentException("Use a bounded native recount history.");
        var type=RuntimeType("TrailRecount");var spacing=(double)type.GetMethod("SpacingFor",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[path,settings["spacing"],(int)settings["perPlayerCap"]])!;
        if(settings["perPlayerCap"]>4096)throw new ArgumentException("Recount diagram exceeds its point budget; native caps remain in source.");
        var history=EngineAssembly.GetType("TKR.WorldServer.logic.behaviors.PositionHistory")!;
        var points=(List<(long T,double X,double Y)>)history.GetMethod("Resample",BindingFlags.Static|BindingFlags.Public)!.Invoke(null,[path,spacing])!;
        return points.Select(p=>new RecountPoint(p.T,p.X,p.Y,(long)type.GetMethod("FuseFor",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[p.T,now,(int)settings["windowMs"],(int)settings["minFuseMs"],(int)settings["maxFuseMs"]])!)).ToArray();
    }
    IEnumerable<DocumentDiagnostic> ValidateTrailLedgers(BehaviorDocument document,string? entityId)
    {
        foreach(var owner in document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId))))foreach(var node in owner.Root.Descendants().Where(n=>IsTrailLedger(document,n)))
        {
            var settings=ReadTrailLedger(document,node);
            if(settings==null){yield return new(node.Kind+" requires its original native constructor contract; dynamic values retain compiled ownership.",node.Id,node.Parameters.All(p=>ReadConstantParameter(document,node,p.Name,null,out _)));continue;}
            foreach(var caller in owner.Definition?.Encounters??new[]{owner.EntityId})
            {
                if(!SimEngine.Data.IdToObjectType.TryGetValue(caller,out var type))continue;var xml=SimEngine.Data.ObjectDescs[type].Projectiles;
                if(!xml.TryGetValue((int)settings["shardIndex"],out var shard)||shard.Speed<=0&&shard.Acceleration<=0)yield return new(node.Kind+" requires a moving shard projectile from "+caller+" XML.",node.Id);
                foreach(var name in settings.Kind=="TrailLedger"?new[]{"coreIndex","interestIndex"}:new[]{"coreIndex"})if(settings[name]>=0&&!xml.ContainsKey((int)settings[name]))yield return new(node.Kind+" "+name+" is missing from "+caller+" XML.",node.Id);
            }
        }
    }
}
