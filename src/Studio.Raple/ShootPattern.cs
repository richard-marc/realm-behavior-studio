using System.Globalization;
using Studio.Core;

namespace Studio.Raple;

public sealed record ShootPattern(int Count, double? FixedAngle, double Spacing, double Offset, double Radius, double? Rotation, double Predictive, int ProjectileIndex, double? DefaultAngle = null)
{
    // Shoot centers the fan, then applies instance rotation and the angle offset.
    public bool CanFire(double? targetDegrees) => FixedAngle != null || targetDegrees != null || DefaultAngle != null;
    public double Center(double? targetDegrees, int volley = 0) => (FixedAngle ?? targetDegrees ?? DefaultAngle ?? 0) + Offset + (Rotation ?? 0) * volley;
    public double[] Angles(double? targetDegrees, int volley = 0) => Enumerable.Range(0, Count).Select(i => Center(targetDegrees, volley) + Spacing * (i - (Count - 1) / 2d)).ToArray();
}

public sealed partial class RapleAdapter
{
    public ShootPattern? ReadShootPattern(BehaviorDocument document, BehaviorNode node)
    {
        if (node.Kind != "Shoot" || node.Children.Any(n => n.Kind == "Opaque")) return null;
        if (!IsNativeShoot(document, node)) return null;
        if (!Number("count", 1, out var count) || count is < 1 or > 255 || count != Math.Truncate(count!.Value) ||
            !Number("fixedAngle", null, out var fixedAngle) || !Number("shootAngle", null, out var spacing) || !Number("angleOffset", 0, out var offset) ||
            !Number("radius", null, out var radius) || radius is null or < 0 || !Number("rotateAngle", null, out var rotation) ||
            !Number("defaultAngle", null, out var defaultAngle) || !Number("predictive", 0, out var predictive) || !Number("projectileIndex", 0, out var projectile) || projectile is null || projectile != Math.Truncate(projectile.Value)) return null;
        return new((int)count.Value, fixedAngle, count == 1 ? 0 : spacing ?? 360 / count.Value, offset!.Value, radius.Value, rotation, predictive!.Value, (int)projectile.Value, defaultAngle);

        bool Number(string name, double? fallback, out double? value) => ReadNumericParameter(document, node, name, fallback, out value);
    }
    bool ReadNumericParameter(BehaviorDocument document, BehaviorNode node, string name, double? fallback, out double? value)
    {
        value = fallback;
        if (!ReadConstantParameter(document, node, name, fallback, out var constant)) return false;
        if (constant == null) { value = null; return fallback == null; }
        if (constant is not (byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)) return false;
        value = Convert.ToDouble(constant, CultureInfo.InvariantCulture); return double.IsFinite(value.Value);
    }
    bool ReadConstantParameter(BehaviorDocument document, BehaviorNode node, string name, object? fallback, out object? constant)
    {
        constant = fallback;
        var parameter = node.Parameters.SingleOrDefault(p => p.Name == name); if (parameter == null) return true;
        if (document.SchemaAt(node)?.HasSourceContext == true)
        {
            if (document.SchemaAt(node)!.At(parameter.Start) is not SourceBehaviorSchema source || !source.TryConstant(parameter.Expression, out constant)) return false;
        }
        else { var info = ConstructorSchema.Value.ValueInfo(parameter.Expression); if (!info.Constant) return false; constant = info.Value; }
        return true;
    }
}
