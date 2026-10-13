using Studio.Core;

namespace Studio.Raple;
public sealed partial class RapleAdapter
{
    IReadOnlyList<HashSet<string>>? KnownLocalPhaseScopes(BehaviorDocument document,EntityBehavior owner)
    {
        if(owner.Definition==null)return null;
        var scopes=new List<HashSet<string>>();
        foreach(var caller in owner.Definition.Encounters)
        {
            var root=document.Entities.SingleOrDefault(e=>SameEntity(e.EntityId,caller));if(root==null)return null;
            var queue=new Queue<EntityBehavior>([root]);var seen=new HashSet<string>();var names=new HashSet<string>(StringComparer.Ordinal);
            while(queue.TryDequeue(out var body))
            {
                if(!seen.Add(body.EntityId))continue;if(body.Definition is {StructurallyComplete:false})return null;
                foreach(var node in body.Root.Descendants())
                {
                    if(node.Kind=="Opaque")return null;
                    if(node.Kind=="State")
                    {
                        var name=document.StateName(node.Id);if(name==null&&node.Parameters.Any(p=>p.Name=="name"))return null;
                        names.Add(name??"");
                    }
                    if(node.IsMemberInitializer||node.Kind is not ("Factory" or "Reference" or "Spread")&&!(node.Kind=="ArgumentGroup"&&node.ArgumentRequiresSource))continue;
                    if(document.ExternalDefinitions(node).Count>0)return null;
                    var definitions=document.ReferencedDefinitions(node);if(definitions.Count==0)return null;foreach(var definition in definitions)queue.Enqueue(definition);
                }
            }
            scopes.Add(names);
        }
        return scopes.Count==0?null:scopes;
    }
}
