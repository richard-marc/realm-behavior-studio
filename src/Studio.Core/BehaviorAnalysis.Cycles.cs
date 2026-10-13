namespace Studio.Core;
public static partial class BehaviorAnalysis
{
    sealed record CycleRoute(Guid From,Guid To,PhaseTransition Transition);
    static bool Immediate(BehaviorNode node)=>node.Kind=="TimedTransition"&&Number(node,"time")<=0&&Value(node,"randomized","false")=="false"||node.Kind=="TimedRandomTransition"&&Number(node,"time")==0&&ChoiceTargets(node) is {Count:>0};
    static IReadOnlyList<PhaseCycle> FindCycles(BehaviorNode root,BehaviorNode[] states,IReadOnlyList<PhaseTransition> transitions)
    {
        var byId=states.ToDictionary(s=>s.Id);var parent=new Dictionary<Guid,BehaviorNode>();
        foreach(var state in states)foreach(var child in state.StateChildren.Where(n=>n.Kind=="State"))parent[child.Id]=state;
        var firstLeaves=new Dictionary<Guid,Guid>();
        Guid Entry(BehaviorNode state)
        {
            if(firstLeaves.TryGetValue(state.Id,out var leaf))return leaf;
            var chain=new List<Guid>();while(true){chain.Add(state.Id);var child=state.StateChildren.FirstOrDefault(n=>n.Kind=="State");if(child==null)break;state=child;}
            foreach(var id in chain)firstLeaves[id]=state.Id;return state.Id;
        }
        var leaves=states.Where(s=>!s.StateChildren.Any(n=>n.Kind=="State")).Select(s=>s.Id).ToArray();
        var groups=transitions.GroupBy(t=>t.OwnerId).ToDictionary(g=>g.Key,g=>g.OrderBy(t=>t.Priority).ToArray());var routes=new List<CycleRoute>();
        foreach(var leaf in leaves)
        {
            var blocked=false;
            for(BehaviorNode? state=byId[leaf];state!=null&&!blocked;state=parent.GetValueOrDefault(state.Id))
            {
                if(!groups.TryGetValue(state.Id,out var candidates))continue;
                for(var i=0;i<candidates.Length;i++)
                {
                    var transition=candidates[i];if(candidates.Take(i).Any(e=>Covers(e.Node,transition.Node)))continue;
                    foreach(var target in transition.Destinations)routes.Add(new(leaf,Entry(byId[target.Id!.Value]),transition));
                    if(Immediate(transition.Node)){blocked=true;break;}
                }
            }
        }
        var next=leaves.ToDictionary(id=>id,_=>new HashSet<Guid>());var reverse=leaves.ToDictionary(id=>id,_=>new HashSet<Guid>());
        foreach(var route in routes){next[route.From].Add(route.To);reverse[route.To].Add(route.From);}
        var reachable=new HashSet<Guid>();var pending=new Stack<Guid>();pending.Push(Entry(root));
        while(pending.TryPop(out var id)){if(!reachable.Add(id))continue;foreach(var target in next[id])pending.Push(target);}
        // Iterative strongly-connected-component discovery avoids adding a recursion limit to large phase graphs.
        var visited=new HashSet<Guid>();var order=new List<Guid>();var traversal=new Stack<(Guid Id,bool Exit)>();
        foreach(var start in leaves)
        {
            traversal.Push((start,false));while(traversal.TryPop(out var visit))
            {
                if(visit.Exit){order.Add(visit.Id);continue;}if(!visited.Add(visit.Id))continue;
                traversal.Push((visit.Id,true));foreach(var target in next[visit.Id])if(!visited.Contains(target))traversal.Push((target,false));
            }
        }
        var result=new List<PhaseCycle>();visited.Clear();var sourceOrder=leaves.Select((id,index)=>(id,index)).ToDictionary(p=>p.id,p=>p.index);var transitionOrder=transitions.ToDictionary(t=>t.Node.Id,t=>t.Node.Start);
        foreach(var start in order.AsEnumerable().Reverse())
        {
            if(visited.Contains(start))continue;var component=new HashSet<Guid>();pending.Push(start);
            while(pending.TryPop(out var id)){if(!visited.Add(id))continue;component.Add(id);foreach(var from in reverse[id])pending.Push(from);}
            if(component.Count==1&&!next[start].Contains(start))continue;
            var internalRoutes=routes.Where(r=>component.Contains(r.From)&&component.Contains(r.To)).ToArray();
            var exits=routes.Where(r=>component.Contains(r.From)&&!component.Contains(r.To)).Select(r=>r.Transition.Node.Id).Distinct().ToArray();
            result.Add(new(component.OrderBy(id=>sourceOrder[id]).ToArray(),internalRoutes.Select(r=>r.Transition.Node.Id).Distinct().OrderBy(id=>transitionOrder[id]).ToArray(),exits,component.Any(reachable.Contains)));
        }
        return result.OrderBy(c=>sourceOrder[c.PhaseIds[0]]).ToArray();
    }
}
