using Studio.Core;
namespace Studio.Raple;
public sealed record NativeLoopContract(string Kind,int? Period,IReadOnlyList<BehaviorNode> Children,string Description);
public sealed partial class RapleAdapter
{
    public NativeLoopContract? ReadLoop(BehaviorDocument document,BehaviorNode node)
    {
        if(node.Kind is not ("Timed" or "Prioritize")||NativeKind(document,node)!=node.Kind)return null;
        var children=node.Children.Where(n=>!n.IsMemberInitializer).SelectMany(n=>n.Kind=="ArgumentGroup"?n.Children:[n]).ToArray();
        if(node.Kind=="Prioritize")return new(node.Kind,null,children,"Entry initializes every child. Selection ticks children in source order until one reports InProgress. Later ticks run only that selected child; Completed or NotStarted selects again on the following tick. Child exit/death callbacks are not forwarded.");
        int? period=ReadNumericParameter(document,node,"period",null,out var value)&&value is >=int.MinValue and <=int.MaxValue?(int)value.Value:null;
        return new(node.Kind,period,children,"Entry initializes every child and the loop counter. Every tick runs all children in source order, subtracting elapsed time once per child. Each expiry resets the counter and only a Prioritize child visited at that expiry is reset to selection. Empty lists do not advance the counter. Child exit/death callbacks are not forwarded.");
    }
}
