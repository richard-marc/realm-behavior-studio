using System.Globalization;
using BossPatternSim;
using Studio.Core;

namespace Studio.Raple;
public sealed record RingPattern(string Kind,int Projectile,int Count,double Spacing,double Angle,double Rotation,int Interval,int Delay,int RandomStartStep,bool WarnFirstVolley,bool AlternateSides,int ReverseEvery,double DegreesPerSecond,int? Slots=null,int? Missing=null);
public sealed partial class RapleAdapter
{
    public bool IsRingPattern(BehaviorDocument document,BehaviorNode node)=>node.Kind is "GordoSpiral" or "GapRing"&&NativeKind(document,node)==node.Kind;
    public RingPattern? ReadRingPattern(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsRingPattern(document,node))return null;var gap=node.Kind=="GapRing";
        var names=new[]{"projectile",gap?"slots":"count",gap?"missing":"spacing",gap?"gapAngle":"angle","rotation","interval","delay","randomStartStep","reverseEvery","degreesPerSecond"};double?[] defaults=[null,gap?24:null,gap?5:null,0,gap?0:null,gap?2000:null,600,0,0,0];var values=new double[names.Length];
        for(var i=0;i<names.Length;i++){if(!ReadNumericParameter(document,node,names[i],defaults[i],out var value)||value==null)return null;values[i]=value.Value;}
        if(!ReadConstantParameter(document,node,"warnFirstVolley",false,out var warning)||warning is not bool warn||!ReadConstantParameter(document,node,"alternateSides",false,out var alternating)||alternating is not bool alternate)return null;
        var integerIndices=gap?new[]{0,1,2,5,6,7,8}:new[]{0,1,5,6,7,8};if(integerIndices.Any(i=>values[i]!=Math.Truncate(values[i])||values[i]<int.MinValue||values[i]>int.MaxValue))return null;
        var count=gap?values[1]-values[2]:values[1];if(count is <1 or >64||gap&&(values[1] is <4 or >64||values[2]<1||values[2]>=values[1]-1)||values[5]<100||values[6]<0||values[7] is <0 or >360||values[7]>0&&360%values[7]!=0||values[8]<0||warn&&values[6]==0)return null;
        return new(node.Kind,(int)values[0],(int)count,gap?360/values[1]:values[2],gap?values[3]+180:values[3],values[4],(int)values[5],(int)values[6],(int)values[7],warn,alternate,(int)values[8],values[9],gap?(int)values[1]:null,gap?(int)values[2]:null);
    }
    IEnumerable<DocumentDiagnostic> ValidateRingPatterns(BehaviorDocument document,string? entityId)
    {
        var owners=document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId)));
        foreach(var owner in owners)foreach(var node in owner.Root.Descendants().Where(n=>IsRingPattern(document,n)))
        {
            double? Number(string name,double? fallback)=>ReadConstantParameter(document,node,name,fallback,out var constant)&&constant is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal?Convert.ToDouble(constant,CultureInfo.InvariantCulture):null;
            var gap=node.Kind=="GapRing";var slots=Number("slots",24);var missing=Number("missing",5);var delay=Number("delay",600);var random=Number("randomStartStep",0);
            foreach(var (name,value,valid) in new (string,double?,Func<double,bool>)[]{("count",gap?null:Number("count",null),v=>v>=1&&v<=64), ("slots",gap?slots:null,v=>v>=4&&v<=64), ("missing",gap?missing:null,v=>v>=1), ("interval",Number("interval",gap?2000:null),v=>v>=100), ("delay",delay,v=>v>=0), ("reverseEvery",gap?null:Number("reverseEvery",0),v=>v>=0), ("randomStartStep",random,v=>v>=0&&v<=360&&(v==0||360%v==0)), ("spacing",gap?null:Number("spacing",null),_=>true), (gap?"gapAngle":"angle",Number(gap?"gapAngle":"angle",0),_=>true), ("rotation",Number("rotation",gap?0:null),_=>true), ("degreesPerSecond",gap?null:Number("degreesPerSecond",0),_=>true)})
                if(value is {} number&&(!double.IsFinite(number)||!valid(number)))yield return new(node.Kind+": invalid "+name+" for the native ring constructor.",node.Id);
            if(gap&&missing>=slots-1)yield return new("GapRing: missing must be less than slots - 1.",node.Id);
            if(delay==0&&ReadConstantParameter(document,node,"warnFirstVolley",false,out var warn)&&warn is true)yield return new("A native ring warning needs positive initial delay.",node.Id);
            var projectile=Number("projectile",null);if(projectile is {} index&&double.IsFinite(index)&&index==Math.Truncate(index))foreach(var caller in owner.Definition?.Encounters??new[]{owner.EntityId})
                if(SimEngine.Data.IdToObjectType.TryGetValue(caller,out var type)&&!SimEngine.Data.ObjectDescs[type].Projectiles.ContainsKey((int)index))yield return new("This entity has no projectile index "+index+": "+caller,node.Id);
        }
    }
}
