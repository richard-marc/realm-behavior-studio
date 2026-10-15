using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using TKR.WorldServer.core.objects;

namespace Studio.Raple;
public sealed record NativeStateField(string Name,string Type,string Value);
public sealed record NativeStateSlot(int ActorId,string Actor,int Slot,string Owner,string ValueType,string Value,string Meaning,NativeStateField[] Fields,NativeStateOrigin? Origin,bool Truncated=false)
{
    public string? CycleStatus {get;init;}
}
internal static class NativeRuntimeState
{
    const int MaxSlots=1024,MaxActorSlots=128,MaxFields=12;
    static readonly ConcurrentDictionary<Type,FieldInfo[]> fields=new();
    static readonly FieldInfo? cycleStatus=typeof(TKR.WorldServer.logic.State).Assembly.GetType("TKR.WorldServer.logic.CycleBehavior")?.GetField("<Status>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic);
    static string Bound(string text)=>text.Length<=128?text:text[..125]+"…";
    static string TypeName(Type? type)=>Bound(type?.FullName??"null");
    static string? Scalar(object? value)=>value switch
    {
        null=>"null",string text=>Bound(text),char c=>c.ToString(),bool b=>b?"true":"false",
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal=>((IFormattable)value).ToString(null,CultureInfo.InvariantCulture),
        Enum e=>Bound(e.ToString()),_=>null
    };
    static FieldInfo[] Fields(Type type)=>fields.GetOrAdd(type,t=>
    {
        var found=new List<FieldInfo>();
        for(var current=t;current!=null&&current!=typeof(object)&&found.Count<=128;current=current.BaseType)
            found.AddRange(current.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly));
        return found.OrderBy(f=>f.DeclaringType?.FullName,StringComparer.Ordinal).ThenBy(f=>f.Name,StringComparer.Ordinal).ToArray();
    });
    internal static NativeStateSlot[] Capture(IEnumerable<Enemy> actors)
    {
        var result=new List<NativeStateSlot>();
        foreach(var actor in actors.Where(e=>!e.Dead).OrderBy(e=>e.Id))
        {
            var slot=0;
            foreach(var pair in actor.StateStorage)
            {
                if(result.Count>=MaxSlots||slot>=MaxActorSlots)
                {
                    result.Add(new(actor.Id,actor.ObjectDesc.IdName,slot,"Observation limit","","Additional native state is not shown.","",[],null,true));break;
                }
                var value=pair.Value;var type=value?.GetType();var scalar=Scalar(value);var entries=new List<NativeStateField>();var truncated=false;
                if(scalar==null&&type!=null)
                {
                    var nativeFields=Fields(type);truncated=nativeFields.Length>MaxFields;
                    foreach(var field in nativeFields.Take(MaxFields))
                    {
                        var stored=field.GetValue(value);entries.Add(new(field.Name,TypeName(field.FieldType),Scalar(stored)??(stored is Array array?"Array · "+array.Length+" elements":"Reference · "+TypeName(stored?.GetType()))));
                    }
                }
                var owner=pair.Key.GetType();var meaning=owner.FullName switch
                {
                    "TKR.WorldServer.logic.behaviors.Grenade" when value is int=>"Remaining launch cooldown � milliseconds; rotation counter belongs to the shared behavior object",
                    "TKR.WorldServer.logic.behaviors.Timed" when value is int=>"Remaining loop counter · milliseconds",
                    "TKR.WorldServer.logic.behaviors.Prioritize" when value is int=>"Selected child index · -1 selects again",
                    "TKR.WorldServer.logic.transitions.TimedTransition" or "TKR.WorldServer.logic.transitions.TimedRandomTransition" when value is int=>"Remaining transition counter · milliseconds",
                    _=>"Native stored value · units depend on its source"
                };
                result.Add(new NativeStateSlot(actor.Id,actor.ObjectDesc.IdName,slot++,TypeName(owner),TypeName(type),scalar??"Stored object · fields only",meaning,entries.ToArray(),NativeStateOrigins.Find(pair.Key),truncated){CycleStatus=cycleStatus?.DeclaringType?.IsInstanceOfType(pair.Key)==true?Scalar(cycleStatus.GetValue(pair.Key)):null});
            }
            if(result.Count>=MaxSlots)break;
        }
        return result.ToArray();
    }
}
