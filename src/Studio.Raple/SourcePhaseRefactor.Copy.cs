using Studio.Core;

namespace Studio.Raple;
public static partial class SourcePhaseRefactor
{
    public static SourcePhaseEdit Paste(RapleAdapter adapter,StudioProject project,string sourceFile,Guid parentId,int index,BehaviorClipboard clipboard)
    {
        var scope=PrepareStructureScope(adapter,project,sourceFile,parentId);
        var before=scope.Target.Source;var beforeIds=scope.Target.Identities;
        var pasted=scope.Target.PasteNodes(parentId,index,clipboard,reservedPhaseNames:scope.PhaseNames);
        var internalNames=pasted.SelectMany(id=>scope.Target.Find(id).Descendants()).Where(n=>n.Kind=="State").Select(n=>scope.Target.StateName(n.Id)).ToHashSet(StringComparer.Ordinal);
        foreach(var node in pasted.SelectMany(id=>scope.Target.Find(id).Descendants()).Where(n=>n.Kind is "Factory" or "Reference" or "Spread"))
            if((node.Kind=="Factory"||internalNames.Count>0)&&(scope.Target.SchemaAt(node)?.DescribeExpression(scope.Target.Expression(node))?.IsState==true||scope.Target.ReferencedDefinitions(node).Any(d=>d.Root.Descendants().Any(n=>n.Kind=="State"))))
                throw new ArgumentException("Copying a shared phase factory/value requires independent definition ownership. Extract or clone its source body first.");
        foreach(var node in pasted.SelectMany(id=>scope.Target.Find(id).Descendants()))foreach(var parameter in node.Parameters)
        {
            if(node.Kind is "Factory" or "Reference" or "Spread")continue;
            var binding=scope.Target.SchemaAt(node)!;
            var users=scope.Callers.Where(to=>scope.Callers.Any(from=>binding.TargetsPhaseOf(scope.Target.Expression(node),parameter.Name,from,to)==true)).ToArray();
            if(users.Length==0)
            {
                if(scope.Callers.Any(id=>binding.TargetsPhaseOf(scope.Target.Expression(node),parameter.Name,id,id)==null))throw new ArgumentException("A copied order's phase ownership cannot be resolved.");
                continue;
            }
            var targets=BehaviorDocument.PhaseLiterals(parameter.Expression)??throw new ArgumentException("A pasted phase destination needs source reconciliation.");
            if(targets.Any(t=>!internalNames.Contains(t.Name)&&!scope.UniqueTarget(t.Name,users)))throw new ArgumentException("A copied external phase target must resolve uniquely in every destination caller.");
        }
        return FinishStructure(adapter,project,scope,before,beforeIds,pasted);
    }
}
