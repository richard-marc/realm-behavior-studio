using BossPatternSim;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Studio.Core;

namespace Studio.Raple;
public sealed record TerrainWavePattern(string To,IReadOnlyList<string> From,double Start,double End,int Duration,int Delay,bool Restore,bool AllowBossLab);
public sealed partial class RapleAdapter
{
    public static double TerrainWaveFrontRadius(TerrainWavePattern pattern,long ageMs)=>(double)RuntimeType("TerrainWave").GetMethod("FrontRadius",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,[ageMs,pattern.Start,pattern.End,pattern.Duration,pattern.Delay])!;
    public bool IsTerrainWave(BehaviorDocument document,BehaviorNode node)=>node.Kind=="TerrainWave"&&NativeKind(document,node)==node.Kind;
    public IReadOnlyList<string>? TerrainWaveGrounds(BehaviorDocument document,BehaviorNode node)=>IsTerrainWave(document,node)?document.LiteralStringChoices(node.Id,"from"):null;
    public TerrainWavePattern? ReadTerrainWave(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsTerrainWave(document,node)||!ReadConstantParameter(document,node,"to",null,out var to)||to is not string target||TerrainWaveGrounds(document,node) is not{} from||!ReadNumericParameter(document,node,"startRadius",null,out var start)||start==null||!ReadNumericParameter(document,node,"endRadius",null,out var end)||end==null||!ReadNumericParameter(document,node,"durationMs",null,out var duration)||duration==null||!ReadNumericParameter(document,node,"delayMs",0,out var delay)||delay==null||!ReadConstantParameter(document,node,"restoreOnExit",false,out var restore)||restore is not bool restoring||!ReadConstantParameter(document,node,"allowBossLab",false,out var lab)||lab is not bool allowed)return null;
        if(!double.IsFinite(start.Value)||!double.IsFinite(end.Value)||start<0||end<0||duration<100||duration>int.MaxValue||duration!=Math.Truncate(duration.Value)||delay<0||delay>int.MaxValue||delay!=Math.Truncate(delay.Value))return null;
        return new(target,from,start.Value,end.Value,(int)duration.Value,(int)delay.Value,restoring,allowed);
    }
    public void EditTerrainWaveGround(BehaviorDocument document,string entityId,Guid id,long revision,PhaseChoiceEdit edit,int index,string? ground=null)
    {
        if(TerrainWaveGrounds(document,document.Find(id))==null)throw new ArgumentException("This terrain source list requires source editing.");
        if(edit is PhaseChoiceEdit.Add or PhaseChoiceEdit.Replace&&(ground==null||!SimEngine.Data.IdToTileType.ContainsKey(ground)))throw new ArgumentException("Choose an exact native ground XML ID.");
        document.EditLiteralStringChoice(id,revision,"from",1,edit,index,ground,d=>Validate(d,entityId));
    }
    public void SetTerrainWaveGround(BehaviorDocument document,string entityId,Guid id,long revision,string ground)
    {
        if(!IsTerrainWave(document,document.Find(id))||!SimEngine.Data.IdToTileType.ContainsKey(ground))throw new ArgumentException("Choose an exact native ground XML ID.");
        document.Apply(new(revision,"Set terrain wave ground",[new(id,"to",SyntaxFactory.Literal(ground).ToFullString())]),d=>Validate(d,entityId));
    }
    IEnumerable<DocumentDiagnostic> ValidateTerrainWaves(BehaviorDocument document,string? entityId)
    {
        foreach(var owner in document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId))))foreach(var node in owner.Root.Descendants().Where(n=>IsTerrainWave(document,n)))
        {
            foreach(var name in new[]{"startRadius","endRadius","durationMs","delayMs"})if(ReadNumericParameter(document,node,name,name=="delayMs"?0:null,out var value)&&value is{} number&&(!double.IsFinite(number)||number<(name=="durationMs"?100:0)||name is "durationMs" or "delayMs"&&(number>int.MaxValue||number!=Math.Truncate(number))))yield return new("TerrainWave requires finite nonnegative radii, duration >= 100 ms and a nonnegative integer delay.",node.Id);
            if(ReadConstantParameter(document,node,"to",null,out var target)&&(target is not string to||!SimEngine.Data.IdToTileType.ContainsKey(to)))yield return new("TerrainWave destination is not an exact native ground XML ID.",node.Id);
            var from=node.Parameters.FirstOrDefault(p=>p.Name=="from")?.Expression;
            if(from!=null)
            {
                var input=SyntaxFactory.ParseExpression(from);
                if(input.IsKind(SyntaxKind.NullLiteralExpression)||(input is Microsoft.CodeAnalysis.CSharp.Syntax.ArrayCreationExpressionSyntax or Microsoft.CodeAnalysis.CSharp.Syntax.ImplicitArrayCreationExpressionSyntax or Microsoft.CodeAnalysis.CSharp.Syntax.CollectionExpressionSyntax)&&input.DescendantNodes().Any(n=>n.IsKind(SyntaxKind.NullLiteralExpression)))yield return new("TerrainWave source grounds cannot be null.",node.Id);
            }
            var grounds=TerrainWaveGrounds(document,node);if(grounds is{Count:0})yield return new("TerrainWave requires at least one source ground XML ID.",node.Id);
            if(grounds!=null&&grounds.Any(g=>!SimEngine.Data.IdToTileType.ContainsKey(g)))yield return new("TerrainWave source grounds must be exact native XML IDs.",node.Id);
            if(ReadTerrainWave(document,node) is{AllowBossLab:false})yield return new("TerrainWave preserves its native Boss Lab exclusion. Enable allowBossLab explicitly to reshape the prototype arena.",node.Id,false);
        }
    }
}
