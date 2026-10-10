using Studio.Core;

namespace Studio.Raple;

public sealed record MovementPattern(string Kind, double Range, double? AcquireRange, double RadiusVariance, double? Speed, double? SpeedVariance, bool? Clockwise, string? Target)
{
    public string PrimaryParameter => Kind == "Orbit" ? "radius" : "range";
    public bool IntegerRange => Kind == "StayCloseToSpawn";
    public double? ResumeRange => Kind == "Follow" ? Range + 1 : null;
}

public sealed partial class RapleAdapter
{
    public string? MovementKind(BehaviorDocument document, BehaviorNode node)
    {
        if (node.Kind is "State" or "ArgumentGroup" or "Factory" or "Reference" or "Opaque" or "Spread") return null;
        var kind = NativeKind(document, node); return kind is "Follow" or "Orbit" or "StayCloseToSpawn" ? kind : null;
    }
    public MovementPattern? ReadMovementPattern(BehaviorDocument document, BehaviorNode node)
    {
        var kind = MovementKind(document, node); if (kind == null || node.Children.Any(n => n.Kind == "Opaque")) return null;
        if (!ReadNumericParameter(document, node, kind == "Orbit" ? "radius" : "range", kind == "Follow" ? 6 : kind == "StayCloseToSpawn" ? 5 : null, out var range) || range is null or < 0) return null;
        double? acquire = null; double? variance = 0; double? speedVariance = 0; object? clockwise = false; object? target = null;
        if (kind != "StayCloseToSpawn" && (!ReadNumericParameter(document, node, "acquireRange", 10, out acquire) || acquire is null or < 0 || !ReadConstantParameter(document, node, "target", null, out target) || target is not (null or string))) return null;
        if (kind == "Orbit" && (!ReadNumericParameter(document, node, "radiusVariance", 1, out variance) || variance is null || !ReadConstantParameter(document, node, "orbitClockwise", false, out clockwise) || clockwise is not (null or bool))) return null;
        ReadNumericParameter(document, node, "speed", null, out var speed);
        if (kind == "Orbit" && !ReadNumericParameter(document, node, "speedVariance", 1, out speedVariance)) speedVariance = null;
        return new(kind, range.Value, acquire, variance!.Value, speed, speedVariance, (bool?)clockwise, (string?)target);
    }
}
