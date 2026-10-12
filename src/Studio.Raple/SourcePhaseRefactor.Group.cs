using Studio.Core;

namespace Studio.Raple;
public static partial class SourcePhaseRefactor
{
    public static SourcePhaseEdit Group(RapleAdapter adapter,StudioProject project,string sourceFile,IEnumerable<Guid> selected,string name,bool chooseUniqueName=false)
    {
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Enter a non-empty phase name.");
        var ids=selected.ToArray();var scope=PrepareStructureScope(adapter,project,sourceFile,ids.FirstOrDefault());
        var nodes=scope.Target.NormalizeSelection(ids);
        if(scope.PhaseNames.Contains(name))
        {
            if(!chooseUniqueName)throw new ArgumentException("A caller already contains that phase name.");
            var prefix=name;var suffix=2;while(scope.PhaseNames.Contains(name))name=prefix+"-"+suffix++;
        }
        var before=scope.Target.Source;var beforeIds=scope.Target.Identities;
        var group=scope.Target.GroupNodes(nodes.Select(n=>n.Id),name);
        return FinishStructure(adapter,project,scope,before,beforeIds,[group]);
    }
}