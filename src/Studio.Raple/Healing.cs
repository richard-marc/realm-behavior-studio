using Studio.Core;

namespace Studio.Raple;
public sealed record HealingSettings(string Kind,double? Range,bool TargetKnown,string? Target,bool AmountKnown,int? Amount);
public sealed partial class RapleAdapter
{
    public bool IsHealing(BehaviorDocument document,BehaviorNode node)=>node.Kind is "HealEntity" or "HealGroup"&&NativeKind(document,node)==node.Kind;
    public HealingSettings? ReadHealing(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsHealing(document,node))return null;
        double? range=null;
        if(ReadNumericParameter(document,node,"range",null,out var value)&&value is{} radius&&float.IsFinite((float)radius))range=(float)radius;
        var targetKnown=ReadConstantParameter(document,node,node.Kind=="HealGroup"?"group":"name",null,out var target)&&target is null or string;
        var amountKnown=ReadConstantParameter(document,node,"healAmount",null,out var amount)&&amount is null or int;
        return new(node.Kind,range,targetKnown,targetKnown?(string?)target:null,amountKnown,amountKnown?(int?)amount:null);
    }
    IEnumerable<DocumentDiagnostic> ValidateHealing(BehaviorDocument document,string? entityId)
    {
        var owners=document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId)));
        foreach(var node in owners.SelectMany(e=>e.Root.Descendants()).Where(n=>IsHealing(document,n)).DistinctBy(n=>n.Start))
            if(ReadConstantParameter(document,node,"range",null,out var range)&&range is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal&&
                !float.IsFinite((float)Convert.ToDouble(range,System.Globalization.CultureInfo.InvariantCulture)))yield return new("Healing range exceeds finite native single-precision geometry.",node.Id);
    }
}
