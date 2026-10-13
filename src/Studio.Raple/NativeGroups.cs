using BossPatternSim;
using Microsoft.CodeAnalysis.CSharp;
using Studio.Core;

namespace Studio.Raple;
public sealed record NativeEntityGroup(string? Name,IReadOnlyList<EntityResource> Members)
{
    public string Title=>Name==null?"Null XML group":Name.Length==0?"Blank XML group":Name;
    public string Summary=>Members.Count+" native XML members · "+Members.Count(m=>m.RuntimeKind=="Enemy")+" enemies";
    public override string ToString()=>Title;
}
public sealed record GroupReference(bool Known,string? Name);
public static partial class NativeGroups
{
    static IReadOnlyList<NativeEntityGroup>? groups;
    public static IReadOnlyList<NativeEntityGroup> Catalog()=>groups??=SimEngine.Data.ObjectDescs.Values.GroupBy(d=>d.Group,StringComparer.Ordinal)
        .Select(group=>new NativeEntityGroup(group.Key,group.OrderBy(d=>d.IdName,StringComparer.Ordinal).Select(d=>EntityResources.FromDescriptor(d)).ToArray()))
        .OrderBy(g=>g.Name==null?1:0).ThenBy(g=>g.Name,StringComparer.OrdinalIgnoreCase).ToArray();
}
public sealed partial class RapleAdapter
{
    public bool IsGroupPrimitive(BehaviorDocument document,BehaviorNode node)=>node.Kind is "SpawnGroup" or "ReproduceGroup" or "GroupNotExistTransition"&&NativeKind(document,node)==node.Kind;
    public GroupReference ReadGroupReference(BehaviorDocument document,BehaviorNode node)=>IsGroupPrimitive(document,node)&&ReadConstantParameter(document,node,"group",null,out var value)&&value is null or string?new(true,(string?)value):new(false,null);
    public void ApplyGroupReference(BehaviorDocument document,string entityId,Guid nodeId,long revision,string? group)
    {
        var node=document.Find(nodeId);if(!IsGroupPrimitive(document,node)||!NativeGroups.Catalog().Any(g=>g.Name==group))throw new ArgumentException("Choose an exact group from the native XML descriptors.");
        document.Apply(new(revision,"Choose native entity group",[new(nodeId,"group",group==null?"null":SyntaxFactory.Literal(group).ToFullString())]),d=>Validate(d,entityId));
    }
    IEnumerable<DocumentDiagnostic> ValidateNativeGroups(BehaviorDocument document,string? entityId)
    {
        foreach(var owner in document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId))))
        foreach(var node in owner.Root.Descendants().Where(n=>IsGroupPrimitive(document,n)))
        {
            var group=ReadGroupReference(document,node);
            if(group.Known&&node.Kind!="GroupNotExistTransition"&&!NativeGroups.Catalog().Any(g=>g.Name==group.Name))yield return new(node.Kind+": this exact XML group has no members for native random selection.",node.Id);
            if(group.Known&&node.Kind=="GroupNotExistTransition"&&string.IsNullOrWhiteSpace(group.Name))yield return new("The native group-absence transition never matches a blank group.",node.Id,false);
            if(node.Kind=="ReproduceGroup"&&ReadConstantParameter(document,node,"region",0,out var region)&&region!=null&&Convert.ToInt32(region,System.Globalization.CultureInfo.InvariantCulture)!=0)
                yield return new("ReproduceGroup preserves the source's region-list filter: it compares the host's fractional position, not each tile's distance. Region cache/world ownership needs runtime review.",node.Id,false);
        }
    }
}
