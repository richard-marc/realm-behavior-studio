using System.Globalization;
using Studio.Core;

namespace Studio.Raple;

public enum NativePathAnchor { Map, StateEntry, Spawn, CurrentPosition }
public sealed record NativePathPattern(string Kind, NativePathAnchor Anchor, double First, double Second, double? Speed, bool? Instant, double? Cooldown)
{
    public bool IsVector => Kind is "MoveLine" or "NewMove";
    public bool Continuous => Kind == "MoveLine" && Second == 0;
    public string FirstParameter => Kind switch { "MoveLine" => "direction", "NewMove" => "angle", "MoveTo2" or "MoveTo3" => "X", "SpawnRelativeMove" => "offsetX", _ => "x" };
    public string SecondParameter => IsVector ? "distance" : Kind is "MoveTo2" or "MoveTo3" ? "Y" : Kind == "SpawnRelativeMove" ? "offsetY" : "y";
    public (double X, double Y)? Destination(double anchorX, double anchorY)
    {
        if (Kind == "MoveLine" && Second <= 0) return null;
        if (IsVector) return (anchorX + Math.Cos(First * Math.PI / 180) * Second, anchorY + Math.Sin(First * Math.PI / 180) * Second);
        return Anchor == NativePathAnchor.Map ? (First, Second) : (anchorX + First, anchorY + Second);
    }
}

public sealed partial class RapleAdapter
{
    internal static bool IsPathKind(string? kind) => kind is "MoveTo" or "MoveTo2" or "MoveTo3" or "SpawnRelativeMove" or "MoveLine" or "NewMove";
    public string? PathKind(BehaviorDocument document, BehaviorNode node)
    {
        if (node.Kind is "State" or "ArgumentGroup" or "Factory" or "Reference" or "Opaque" or "Spread") return null;
        var kind = NativeKind(document, node); return IsPathKind(kind) ? kind : null;
    }
    public NativePathPattern? ReadPathPattern(BehaviorDocument document, BehaviorNode node)
    {
        var kind = PathKind(document, node); if (kind == null) return null;
        var pattern = new NativePathPattern(kind, kind switch { "MoveTo" => NativePathAnchor.Map, "SpawnRelativeMove" => NativePathAnchor.Spawn, "MoveLine" or "NewMove" => NativePathAnchor.CurrentPosition, _ => NativePathAnchor.StateEntry }, 0, 0, null, false, null);
        if (!ReadNumericParameter(document, node, pattern.FirstParameter, kind is "MoveLine" or "NewMove" ? 0 : null, out var first) || first == null ||
            !ReadNumericParameter(document, node, pattern.SecondParameter, kind == "MoveLine" ? 0 : kind == "NewMove" ? 3 : null, out var second) || second == null) return null;
        if (kind is "MoveTo2" or "MoveTo3")
        {
            if (!ReadConstantParameter(document, node, "isMapPosition", false, out var absolute) || absolute is not bool) return null;
            pattern = pattern with { Anchor = (bool)absolute ? NativePathAnchor.Map : NativePathAnchor.StateEntry };
            pattern = pattern with { Instant = ReadConstantParameter(document, node, "instant", false, out var instant) && instant is bool ? (bool)instant : null };
        }
        ReadNumericParameter(document, node, "speed", kind == "NewMove" ? 1 : kind is "MoveTo2" or "MoveTo3" ? 2 : null, out var speed);
        double? cooldown = null; if (kind == "NewMove") ReadNumericParameter(document, node, "cooldown", 10, out cooldown);
        return pattern with { First = first.Value, Second = second.Value, Speed = speed, Cooldown = cooldown };
    }
    public void ApplyPathValues(BehaviorDocument document, string entityId, Guid nodeId, long revision, IReadOnlyDictionary<string, double> values)
    {
        var pattern = ReadPathPattern(document, document.Find(nodeId)) ?? throw new InvalidOperationException("This native path needs known coordinate expressions.");
        var changes = new List<EditOperation>();
        foreach (var (parameter, value) in values)
        {
            if (parameter != pattern.FirstParameter && parameter != pattern.SecondParameter || !double.IsFinite(value)) throw new ArgumentException("Choose a finite native path coordinate.");
            var single = pattern.Kind != "MoveLine";
            if (single && !float.IsFinite((float)value)) throw new ArgumentException("This coordinate exceeds the native float range.");
            var literal = single ? ((float)value).ToString("R", CultureInfo.InvariantCulture) + "f" : value.ToString("R", CultureInfo.InvariantCulture);
            changes.Add(new(nodeId, parameter, literal));
        }
        if (changes.Count == 0) throw new ArgumentException("Choose a path value to change.");
        document.Apply(new(revision, "Adjust native movement path", changes), candidate => Validate(candidate, entityId));
    }
}
