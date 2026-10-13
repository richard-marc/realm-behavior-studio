using Studio.Core;

namespace Studio.Raple;

public sealed record MovementPattern(string Kind, double Range, double? AcquireRange, double RadiusVariance, double? Speed, double? SpeedVariance, bool? Clockwise, string? Target, double? TravelDistance = null)
{
    public string PrimaryParameter => Kind switch {"Orbit"=>"radius","ReturnToSpawn"=>"returnWithinRadius","StayBack"=>"distance","FollowDistance" or "LockedFollowDistance"=>"stopRange",_=>"range"};
    public bool IntegerRange => Kind == "StayCloseToSpawn";
    public double? ResumeRange => Kind == "Follow" ? Range + 1 : null;
}

public sealed partial class RapleAdapter
{
    public string? MovementKind(BehaviorDocument document, BehaviorNode node)
    {
        if (node.Kind is "State" or "ArgumentGroup" or "Factory" or "Reference" or "Opaque" or "Spread") return null;
        var kind = NativeKind(document, node); return kind is "Follow" or "Orbit" or "StayCloseToSpawn" or "ReturnToSpawn" or "StayBack" or "Charge" or "FollowDistance" or "LockedFollowDistance" ? kind : null;
    }
    public MovementPattern? ReadMovementPattern(BehaviorDocument document, BehaviorNode node)
    {
        var kind = MovementKind(document, node); if (kind == null || node.Children.Any(n => n.Kind == "Opaque")) return null;
        if (kind is "FollowDistance" or "LockedFollowDistance")
        {
            if (!ReadNumericParameter(document,node,"stopRange",kind=="FollowDistance"?.5:4,out var stop) || stop is null or < 0 ||
                !ReadNumericParameter(document,node,"acquireRange",null,out var acquireRange) || acquireRange is null or <= 0 ||
                !ReadNumericParameter(document,node,"distance",null,out var travel) || travel is null or <= 0 ||
                new[]{stop.Value,acquireRange.Value,travel.Value}.Any(value=>!double.IsFinite(value)||!float.IsFinite((float)value)) ||
                kind=="LockedFollowDistance" && stop>=acquireRange) return null;
            ReadNumericParameter(document,node,"speed",null,out var followSpeed);
            return new(kind,stop.Value,acquireRange.Value,0,followSpeed,0,false,null,travel.Value);
        }
        if(kind is "ReturnToSpawn" or "StayBack" or "Charge")
        {
            var parameter=kind=="ReturnToSpawn"?"returnWithinRadius":kind=="StayBack"?"distance":"range";var fallback=kind=="ReturnToSpawn"?1:kind=="StayBack"?8:10;
            if(!ReadNumericParameter(document,node,parameter,fallback,out var distance)||distance==null||distance<0||!float.IsFinite((float)distance.Value))return null;
            object? retreatTarget=null;if(kind=="StayBack"&&(!ReadConstantParameter(document,node,"entity",null,out retreatTarget)||retreatTarget is not (null or string)))return null;
            ReadNumericParameter(document,node,"speed",kind=="Charge"?4:null,out var nativeSpeed);return new(kind,distance.Value,null,0,nativeSpeed,0,false,(string?)retreatTarget);
        }
        if (!ReadNumericParameter(document, node, kind == "Orbit" ? "radius" : "range", kind == "Follow" ? 6 : kind == "StayCloseToSpawn" ? 5 : null, out var range) || range is null or < 0) return null;
        double? acquire = null; double? variance = 0; double? speedVariance = 0; object? clockwise = false; object? target = null;
        if (kind != "StayCloseToSpawn" && (!ReadNumericParameter(document, node, "acquireRange", 10, out acquire) || acquire is null or < 0 || !ReadConstantParameter(document, node, "target", null, out target) || target is not (null or string))) return null;
        if (kind == "Orbit" && (!ReadNumericParameter(document, node, "radiusVariance", 1, out variance) || variance is null || !ReadConstantParameter(document, node, "orbitClockwise", false, out clockwise) || clockwise is not (null or bool))) return null;
        ReadNumericParameter(document, node, "speed", null, out var speed);
        if (kind == "Orbit" && !ReadNumericParameter(document, node, "speedVariance", 1, out speedVariance)) speedVariance = null;
        return new(kind, range.Value, acquire, variance!.Value, speed, speedVariance, (bool?)clockwise, (string?)target);
    }
}
