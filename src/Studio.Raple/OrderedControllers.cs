using System.Reflection;
using Studio.Core;
using TKR.WorldServer.logic;

namespace Studio.Raple;
public sealed record OrderedChild(BehaviorNode Node,bool NativeOverride,string Description);
public sealed partial class RapleAdapter
{
    public bool IsOrderedMovement(BehaviorDocument document,BehaviorNode node)=>(node.Kind is "NewFollow" or "NewCharge" or "NewWander" or "NewStayAbove" or "NewMove")&&NativeKind(document,node)==node.Kind;
    public bool UsesOrderedDispatch(BehaviorDocument document,BehaviorNode node)
    {
        var parent=document.Parent(node.Id);while(parent?.Kind=="ArgumentGroup")parent=document.Parent(parent.Id);return parent?.Kind=="OrderedBehavior"&&NativeKind(document,parent)=="OrderedBehavior";
    }
    public IReadOnlyList<OrderedChild>? OrderedChildren(BehaviorDocument document,BehaviorNode node)
    {
        if(node.Kind!="OrderedBehavior"||NativeKind(document,node)!="OrderedBehavior")return null;
        var children=node.Children.SelectMany(n=>n.Kind=="ArgumentGroup"?n.Children:[n]).ToArray();var result=new List<OrderedChild>();
        foreach(var child in children)
        {
            var kind=NativeKind(document,child);var type=kind==null?null:EngineAssembly.GetTypes().FirstOrDefault(t=>t.Name==kind&&typeof(Behavior).IsAssignableFrom(t));if(type==null)return null;
            var ordered=type.GetMethod("TickCoreOrdered",BindingFlags.Instance|BindingFlags.NonPublic)?.DeclaringType!=typeof(Behavior);
            var description=kind switch
            {
                "NewWander"=>"Attempts native movement and always returns true, even when blocked.",
                "NewStayAbove"=>"RealmWorld elevation prerequisite. Other worlds return true without movement and stop the list.",
                "NewFollow"=>"Native acquisition, range and duration/cooldown return rules. False permits the next child.",
                "NewCharge"=>"Charging returns true, including its finishing tick. Cooldown/no-target returns false.",
                "NewMove"=>"Returns attempted displacement, even when collision rejects movement; exact target equality and cooldown use original fields.",
                _=>ordered?"Invokes this native ordered override; its implementation decides whether later children run.":"Inherited no-op returns true and blocks every later child; ordinary callbacks are not forwarded."
            };result.Add(new(child,ordered,description));
        }
        return result;
    }
    IEnumerable<DocumentDiagnostic> ValidateOrderedControllers(BehaviorDocument document,string? entityId)
    {
        foreach(var owner in document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId))))foreach(var node in owner.Root.Descendants())
        {
            if(IsOrderedMovement(document,node)&&!UsesOrderedDispatch(document,node))yield return new(node.Kind+" has an ordered movement callback. A direct phase action does not execute it; place it in native OrderedBehavior.",node.Id,false);
            if(OrderedChildren(document,node) is{} children)foreach(var child in children.Where(c=>!c.NativeOverride))yield return new(child.Node.Kind+" inherits a no-op TickOrdered result of true and blocks later OrderedBehavior children.",child.Node.Id,false);
            if(IsOrderedMovement(document,node)&&node.Kind=="NewStayAbove")yield return new("NewStayAbove uses RealmWorld elevation. In this Boss Lab prototype it returns true without movement.",node.Id,false);
        }
    }
}
