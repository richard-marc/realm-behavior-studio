using Studio.Core;
using System.Reflection;
using TKR.WorldServer.logic;

namespace Studio.Raple;

public sealed record OwnedEmitterResource(string EntityId,EnemyProjectile Projectile);
public sealed record LaneAxisChoice(string Name,string Expression){public override string ToString()=>Name;}

public sealed partial class RapleAdapter
{
    internal static void ValidateLaneRuntime(State root,BehaviorDb database)
    {
        var seen=new HashSet<Behavior>();
        void BehaviorTree(Behavior behavior)
        {
            if(!seen.Add(behavior))return;var type=behavior.GetType();
            if(type.FullName==NativeTypeName("OwnedLaneEmitters"))
            {
                var helper=(string)type.GetField("childName",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(behavior)!;
                if(database.GameServer.Resources.GameData.IdToObjectType.TryGetValue(helper,out var helperType)&&database.Definitions.TryGetValue(helperType,out var definition)&&definition.Item1.States.Count>0)
                    throw new InvalidOperationException(helper+" starts inside a nested phase. The game's lane controller cannot replace that state tree. Choose a helper with a flat starting state.");
            }
            if(new[]{"Timed","OnDeathBehavior","OrderedBehavior"}.Any(name=>type.FullName==NativeTypeName(name)))
                foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public))
                    if(field.GetValue(behavior) is Behavior child)BehaviorTree(child);
                    else if(field.GetValue(behavior) is Behavior[] children)foreach(var nested in children)BehaviorTree(nested);
        }
        void StateTree(State state){foreach(var behavior in state.Behaviors)BehaviorTree(behavior);foreach(var child in state.States)StateTree(child);}
        StateTree(root);
    }
    public bool IsOwnedLaneEmitter(BehaviorDocument document,BehaviorNode node)=>node.Kind=="OwnedLaneEmitters"&&NativeKind(document,node)=="OwnedLaneEmitters";
    public IReadOnlyList<LaneAxisChoice> LaneAxes(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsOwnedLaneEmitter(document,node))return [];
        var type=RuntimeType("OwnedLaneEmitters").GetConstructors().Single().GetParameters().Single(p=>p.Name=="axes").ParameterType;
        return Enum.GetNames(type).Select(name=>new LaneAxisChoice(name,"global::"+CSharpTypeName(type)+"."+name)).ToArray();
    }
    public string? ReadLaneAxis(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsOwnedLaneEmitter(document,node)||!ReadNumericParameter(document,node,"axes",null,out var value)||value is not (0 or 1 or 2))return null;
        var type=RuntimeType("OwnedLaneEmitters").GetConstructors().Single().GetParameters().Single(p=>p.Name=="axes").ParameterType;
        return Enum.GetName(type,(int)value.Value);
    }
    public void ApplyLaneAxis(BehaviorDocument document,string entityId,Guid nodeId,long revision,string name)
    {
        var choice=LaneAxes(document,document.Find(nodeId)).SingleOrDefault(c=>c.Name==name)??throw new ArgumentException("Choose an actual native lane axis.");
        document.Apply(new(revision,"Choose lane axes",[new(nodeId,"axes",choice.Expression)]),candidate=>Validate(candidate,entityId));
    }
    public OwnedEmitterResource? ReadOwnedEmitterResource(BehaviorDocument document,BehaviorNode node)
    {
        if(!IsOwnedLaneEmitter(document,node))return null;
        var parameter=EntityParameterFor(document,node)!;var reference=ReadEntityReference(document,node,parameter);
        if(!reference.Known||reference.Id==null||!EntityResources.Accepts(parameter,reference.Id))return null;
        return new(reference.Id,EnemyProjectiles.ForEntity(reference.Id).Single(p=>p.Index==0));
    }
    IEnumerable<DocumentDiagnostic> ValidateOwnedEmitters(BehaviorDocument document,string? entityId)
    {
        var roots=document.Entities.Where(e=>entityId==null||e.EntityId==entityId).Concat(document.Definitions.Where(d=>d.Definition!.Encounters.Any(id=>entityId==null||id==entityId)));
        foreach(var node in roots.SelectMany(e=>e.Root.Descendants()).Where(n=>IsOwnedLaneEmitter(document,n)).DistinctBy(n=>n.Start))
        {
            var parameter=EntityParameterFor(document,node)!;var reference=ReadEntityReference(document,node,parameter);
            if(reference.Known&&ReadOwnedEmitterResource(document,node)==null)yield return new("Lane emitters require a native Enemy with XML projectile #0.",node.Id);
            if(ReadNumericParameter(document,node,"intervalMs",900,out var interval)&&interval<100)yield return new("Lane emission interval must be at least 100 ms.",node.Id);
            if(ReadNumericParameter(document,node,"startupMs",1200,out var startup)&&startup<0)yield return new("Lane startup must be nonnegative.",node.Id);
            if(ReadNumericParameter(document,node,"axes",null,out var axes)&&axes is {} value&&value is not (0 or 1 or 2))yield return new("Choose Horizontal, Vertical or Cross lane axes.",node.Id);
        }
    }
}
