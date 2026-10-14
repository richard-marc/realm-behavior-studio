using Studio.Core;
namespace Studio.Raple;
public sealed record CustomMovement(string Kind,double Speed,double Radius,double? Acquire,double? Arrival,string? Target,bool Clockwise);
public sealed partial class RapleAdapter
{
    public bool IsCustomMovement(BehaviorDocument document,BehaviorNode node)=>node.Kind is "Protect" or "BackAndForth" or "PatrolCircle"&&NativeKind(document,node)==node.Kind;
    public CustomMovement? ReadCustomMovement(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsCustomMovement(document,node)||!ReadNumericParameter(document,node,"speed",null,out var speed)||speed==null||!double.IsFinite(speed.Value))return null;
        if(node.Kind=="Protect")
        {
            if(!ReadNumericParameter(document,node,"protectionRange",2,out var protect)||protect==null||!ReadNumericParameter(document,node,"reprotectRange",1,out var arrival)||arrival==null||!ReadNumericParameter(document,node,"acquireRange",10,out var acquire)||acquire==null||new[]{speed.Value,protect.Value,arrival.Value,acquire.Value}.Any(v=>!float.IsFinite((float)v))||!ReadConstantParameter(document,node,"protectee",null,out var target)||target is not string name)return null;
            return new(node.Kind,speed.Value,protect.Value,acquire,arrival,name,false);
        }
        if(!ReadNumericParameter(document,node,node.Kind=="BackAndForth"?"distance":"radius",node.Kind=="BackAndForth"?5:null,out var radius)||radius==null||!double.IsFinite(radius.Value))return null;
        if(node.Kind=="BackAndForth")return radius.Value>=int.MinValue&&radius.Value<=int.MaxValue&&radius.Value==Math.Truncate(radius.Value)&&float.IsFinite((float)speed.Value)?new(node.Kind,speed.Value,radius.Value,null,null,null,false):null;
        if(radius<=0||speed<=0||!ReadConstantParameter(document,node,"clockwise",true,out var clockwise)||clockwise is not bool direction)return null;return new(node.Kind,speed.Value,radius.Value,null,null,null,direction);
    }
    public void ApplyCustomMovement(BehaviorDocument document,string entityId,Guid nodeId,long revision,IReadOnlyDictionary<string,double> values)
    {
        var node=document.Find(nodeId);if(!IsCustomMovement(document,node)||values.Count==0||values.Values.Any(v=>!double.IsFinite(v))||values.Keys.Any(k=>node.Kind switch{"Protect"=>k is not("acquireRange" or "protectionRange" or "reprotectRange"),"BackAndForth"=>k!="distance",_=>k!="radius"}))throw new ArgumentException("Choose the selected movement's native distance fields.");
        if(node.Kind=="Protect"&&values.Values.Any(v=>!float.IsFinite((float)v))||node.Kind=="BackAndForth"&&values.Values.Any(v=>v<int.MinValue||v>int.MaxValue||v!=Math.Truncate(v))||node.Kind=="PatrolCircle"&&values.Values.Any(v=>v<=0))throw new ArgumentException("These values exceed the native movement field contract.");
        document.Apply(new(revision,"Adjust native movement",values.Select(p=>new EditOperation(nodeId,p.Key,p.Value.ToString("R",System.Globalization.CultureInfo.InvariantCulture))).ToArray()),d=>Validate(d,entityId));
    }
    IEnumerable<DocumentDiagnostic> ValidateCustomMovement(BehaviorDocument document,string? entityId)
    {
        foreach(var owner in document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId))))foreach(var node in owner.Root.Descendants().Where(n=>IsCustomMovement(document,n)&&n.Kind=="PatrolCircle"))
            foreach(var name in new[]{"radius","speed"})if(ReadNumericParameter(document,node,name,null,out var value)&&value is{} known&&!(known>0))yield return new("PatrolCircle requires positive native "+name+".",node.Id);
    }
}
