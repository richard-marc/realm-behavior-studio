using BossPatternSim;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Studio.Core;
namespace Studio.Raple;
public sealed partial class RapleAdapter
{
    public bool IsPresenceCondition(BehaviorDocument document,BehaviorNode node)=>node.Kind is "NoPlayerWithinTransition" or "AnyEntityWithinTransition" or "NoEntityWithinTransition" or "EntityWithinTransition" or "EntitiesNotExistsTransition" or "OnGroundTransition" or "NotMovingTransition"&&NativeKind(document,node)==node.Kind;
    public IReadOnlyList<string>? PresenceTargets(BehaviorDocument document,BehaviorNode node)=>node.Kind=="EntitiesNotExistsTransition"&&IsPresenceCondition(document,node)?document.LiteralStringChoices(node.Id,"targets"):null;
    public void EditPresenceTarget(BehaviorDocument document,string entityId,Guid nodeId,long revision,PhaseChoiceEdit edit,int index,string? target=null)
    {
        var node=document.Find(nodeId);if(PresenceTargets(document,node)==null)throw new ArgumentException("This target list uses source values.");
        if(edit is PhaseChoiceEdit.Add or PhaseChoiceEdit.Replace&&(target==null||!EntityResources.Catalog(EntityResourceScope.Queryable).Any(e=>e.Id==target)))throw new ArgumentException("Choose a native enemy/static XML type.");
        document.EditLiteralStringChoice(nodeId,revision,"targets",0,edit,index,target,d=>Validate(d,entityId));
    }
    public string? PresenceGround(BehaviorDocument document,BehaviorNode node)=>node.Kind=="OnGroundTransition"&&IsPresenceCondition(document,node)&&ReadConstantParameter(document,node,"ground",null,out var value)&&value is string name?name:null;
    public void ApplyPresenceGround(BehaviorDocument document,string entityId,Guid nodeId,long revision,string ground)
    {
        var node=document.Find(nodeId);if(!IsPresenceCondition(document,node)||node.Kind!="OnGroundTransition"||!SimEngine.Data.IdToTileType.ContainsKey(ground))throw new ArgumentException("Choose the native ground XML ID.");
        document.Apply(new(revision,"Choose phase ground",[new(nodeId,"ground",SyntaxFactory.Literal(ground).ToFullString())]),d=>Validate(d,entityId));
    }
    IEnumerable<DocumentDiagnostic> ValidatePresenceConditions(BehaviorDocument document,string? entityId)
    {
        foreach(var owner in document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId))))foreach(var node in owner.Root.Descendants().Where(n=>IsPresenceCondition(document,n)))
        {
            if(node.Kind=="OnGroundTransition"&&PresenceGround(document,node) is{} ground&&!SimEngine.Data.IdToTileType.ContainsKey(ground))yield return new("Native ground ID is missing; OnGroundTransition resolves this exact XML ID on its first tick.",node.Id);
            if(node.Kind!="EntitiesNotExistsTransition")continue;
            var targets=PresenceTargets(document,node);
            if(targets is{Count:0})yield return new("An empty native absence list never matches. Add at least one XML type to enable this condition.",node.Id,false);
            if(targets!=null)foreach(var target in targets.Where(t=>!SimEngine.Data.IdToObjectType.ContainsKey(t)))yield return new("Unknown XML target '"+target+"': the native constructor falls back to Pirate. Choose the intended XML type.",node.Id,false);
            foreach(var parameter in node.Parameters.Where(p=>p.Name.Split('[')[0]=="targets"))
            {
                var expression=SyntaxFactory.ParseExpression(parameter.Expression);IEnumerable<ExpressionSyntax> values=expression switch{ArrayCreationExpressionSyntax{Initializer:{} array}=>array.Expressions,ImplicitArrayCreationExpressionSyntax array=>array.Initializer.Expressions,CollectionExpressionSyntax collection=>collection.Elements.OfType<ExpressionElementSyntax>().Select(e=>e.Expression),_=>[expression]};
                if(values.Any(v=>v.IsKind(SyntaxKind.NullLiteralExpression)))yield return new("Native absence targets cannot contain null: object-type resolution requires an XML ID.",node.Id);
            }
        }
    }
}
