using System.Reflection;
using Studio.Core;
using BossPatternSim;

namespace Studio.Raple;
public sealed record SkyStrikePattern(string Layout,IReadOnlyDictionary<string,double> Values,bool BossLabOnly)
{
    public double this[string name]=>Values[name];
}
public sealed record SkyStrikePoint(double X,double Y,int DelayMs);
public sealed partial class RapleAdapter
{
    public bool IsSkyStrike(BehaviorDocument document,BehaviorNode node)=>node.Kind=="SkyStrike"&&NativeKind(document,node)==node.Kind;
    public SkyStrikePattern? ReadSkyStrike(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsSkyStrike(document,node))return null;var settings=ReadNativeAttackSettings(document,node,"SkyStrike");return settings==null?null:new(settings.Enums["layout"],settings.Values,settings.Flags["bossLabOnly"]);
    }
    public static IReadOnlyList<SkyStrikePoint> SkyStrikeLayout(SkyStrikePattern pattern,double targetX=6,double targetY=0,int volley=0)
    {
        // Use original pure layouts. Bound diagram work without replacing native Play semantics.
        if(pattern.Layout=="Grid"&&Math.Floor(pattern["halfLength"]/pattern["spacing"])>256)throw new ArgumentException("This native grid is too dense for the diagram. Inspect its source and native playback bounds.");
        var type=RuntimeType("SkyStrike");object[] args=pattern.Layout switch
        {
            "Grid"=>[0d,0d,(pattern["axisStartDeg"]+volley*pattern["axisStepDeg"])*Math.PI/180,(int)pattern["rows"],pattern["rowSpacing"],pattern["spacing"],pattern["halfLength"],(int)pattern["stepMs"]],
            "Chain"=>[0d,0d,targetX,targetY,(int)pattern["count"],pattern["spacing"],(int)pattern["stepMs"],pattern["maxRange"]],
            "Halo"=>[0d,0d,(pattern["axisStartDeg"]+volley*pattern["gapStepDeg"])*Math.PI/180,(int)pattern["count"],(int)pattern["gapSlots"],pattern["radius"]],
            _=>[targetX,targetY,(int)pattern["extra"],pattern["offsetRadius"],volley*2.399963229728653]
        };
        var name=pattern.Layout=="Targets"?"TargetLayout":pattern.Layout+"Layout";
        var points=(List<(double X,double Y,int Delay)>)type.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args)!;
        return points.Where(p=>p.X*p.X+p.Y*p.Y<=pattern["maxRange"]*pattern["maxRange"]).Select(p=>new SkyStrikePoint(p.X,p.Y,p.Delay)).ToArray();
    }
    public void SetSkyStrikeLayout(BehaviorDocument document,string entityId,Guid id,long revision,string layout)
    {
        if(!IsSkyStrike(document,document.Find(id))||!new[]{"Targets","Grid","Chain","Halo"}.Contains(layout))throw new ArgumentException("Choose the native strike layout.");
        document.Apply(new(revision,"Set sky-strike layout",[new(id,"layout","global::TKR.WorldServer.logic.behaviors.StrikeLayout."+layout)]),d=>Validate(d,entityId));
    }
    IEnumerable<DocumentDiagnostic> ValidateSkyStrikes(BehaviorDocument document,string? entityId)
    {
        foreach(var owner in document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId))))foreach(var node in owner.Root.Descendants().Where(n=>IsSkyStrike(document,n)))
        {
            var pattern=ReadSkyStrike(document,node);
            if(pattern==null){yield return new("SkyStrike inputs require a valid original constructor contract; dynamic source values retain compiled ownership.",node.Id, node.Parameters.All(p=>ReadConstantParameter(document,node,p.Name,null,out _)));continue;}
            foreach(var caller in owner.Definition?.Encounters??new[]{owner.EntityId})
            {
                var projectiles=EnemyProjectiles.ForEntity(caller);
                if(pattern["coreIndex"]>=0&&!projectiles.Any(p=>p.Index==pattern["coreIndex"]))yield return new("SkyStrike core projectile is missing from "+caller+" XML.",node.Id);
                if(pattern["shardCount"]>0&&(!SimEngine.Data.IdToObjectType.TryGetValue(caller,out var type)||!SimEngine.Data.ObjectDescs[type].Projectiles.TryGetValue((int)pattern["shardIndex"],out var shard)||shard.Speed<=0&&shard.Acceleration<=0))yield return new("SkyStrike shrapnel requires a moving native XML projectile.",node.Id);
            }
            if(pattern.Layout=="Grid"&&Math.Floor(pattern["halfLength"]/pattern["spacing"])>256)yield return new("SkyStrike grid exceeds the diagram's density bound; native generated work remains a runtime budget concern.",node.Id,false);
        }
    }
}
