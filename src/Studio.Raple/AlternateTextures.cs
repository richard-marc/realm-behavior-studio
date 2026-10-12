using System.Globalization;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using BossPatternSim;

namespace Studio.Raple;

// GameObject.as:setAltTexture retains the previous texture for an undefined ID.
// Observed every simulation sample, so batched advances do not lose valid intermediate changes.
public static class AlternateTextures
{
    static readonly ConcurrentDictionary<string,ImmutableArray<int>> indices=new(StringComparer.Ordinal);
    public static IReadOnlyList<int> Indices(string entity)
    {
        return indices.GetOrAdd(entity,id=>
        {
            var xml=SimEngine.Data.ObjectCombinedXML.Elements("Object").LastOrDefault(e=>(string?)e.Attribute("id")==id);
            return new[]{0}.Concat(xml?.Elements("AltTexture").Select(e=>Parse((string?)e.Attribute("id"))).OfType<int>()??[]).Distinct().Order().ToImmutableArray();
        });
    }
    static int? Parse(string? text)=>text!=null&&text.StartsWith("0x",StringComparison.OrdinalIgnoreCase)&&int.TryParse(text[2..],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var hex)?hex:int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var number)?number:null;
    public static int Resolve(string entity,int requested,int previous=0)=>Indices(entity).Contains(requested)?requested:Indices(entity).Contains(previous)?previous:0;
}
