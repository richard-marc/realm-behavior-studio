using BossPatternSim;
using Microsoft.CodeAnalysis.CSharp;
using Studio.Core;
using TKR.Shared.resources;
namespace Studio.Raple;
public sealed record ScalingWeapon(string Id,bool Legendary,bool Mythical,int NativeGearPoints);
public sealed partial class RapleAdapter
{
    public bool IsConditionAction(BehaviorDocument document,BehaviorNode node)=>node.Kind is "ConditionEffectBehavior" or "RemoveConditionalEffect"&&NativeKind(document,node)==node.Kind;
    public ConditionEffectIndex? ReadConditionAction(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsConditionAction(document,node)||!ReadConstantParameter(document,node,"effect",null,out var value)||value==null||value is not(byte or int)&&!value.GetType().IsEnum)return null;
        var index=Convert.ToInt32(value,System.Globalization.CultureInfo.InvariantCulture);return index is >=0 and <=255&&Enum.IsDefined((ConditionEffectIndex)index)?(ConditionEffectIndex)index:null;
    }
    public void ApplyConditionAction(BehaviorDocument document,string entityId,Guid nodeId,long revision,ConditionEffectIndex effect)
    {
        var node=document.Find(nodeId);if(!IsConditionAction(document,node)||!Enum.IsDefined(effect))throw new ArgumentException("Choose the native host condition.");
        document.Apply(new(revision,"Choose host condition",[new(nodeId,"effect","global::TKR.Shared.resources.ConditionEffectIndex."+effect)]),d=>Validate(d,entityId));
    }
    public bool IsScalingAction(BehaviorDocument document,BehaviorNode node)=>node.Kind is "ScaleHP" or "ScaleHP2" or "ScaleDefense"&&NativeKind(document,node)==node.Kind;
    public static ScalingWeapon? ScalingWeaponInfo(string id)
    {
        if(!SimEngine.Data.IdToObjectType.TryGetValue(id,out var type)||!SimEngine.Data.Items.TryGetValue(type,out var item))return null;
        return new(id,item.Legendary,item.Mythical,item.Legendary||item.Mythical?23:0);
    }
}
