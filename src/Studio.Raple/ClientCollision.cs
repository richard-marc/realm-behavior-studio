using System.Globalization;
using BossPatternSim;
using TKR.Shared;
using TKR.Shared.resources;
using TKR.WorldServer.core.objects;

namespace Studio.Raple;

public readonly record struct HitGeometry(double Radius = .5, bool Circular = false)
{
    public HitGeometry() : this(.5, false) { }
    // Projectile.as:getHit tests a projectile POINT, with inclusive target boundaries.
    public bool Contains(double dx, double dy) => Math.Abs(dx) <= Radius && Math.Abs(dy) <= Radius &&
        (!Circular || dx * dx + dy * dy <= Radius * Radius);
}
public record HitTarget(int Id, ushort Type, double X, double Y, HitGeometry Geometry, bool Eligible = true);
public static class ClientCollision
{
    static Dictionary<ushort, HitGeometry>? geometry;
    public static HitGeometry Geometry(ushort type)
    {
        geometry ??= SimEngine.Data.ObjectCombinedXML.Elements("Object")
            .GroupBy(e => e.GetAttribute<ushort>("type")).ToDictionary(g => g.Key, g =>
            {
                var radius = g.Last().Element("Oryx4HitRadius");
                return radius == null ? new HitGeometry(.5) : new HitGeometry(double.Parse(radius.Value, CultureInfo.InvariantCulture), true);
            });
        return geometry.GetValueOrDefault(type, new HitGeometry(.5));
    }
    public static bool Eligible(Entity e) => !e.Dead && (e is not Enemy enemy || enemy.HP > 0) &&
        !e.HasConditionEffect(ConditionEffectIndex.Invincible) && !e.HasConditionEffect(ConditionEffectIndex.Paused) &&
        !e.HasConditionEffect(ConditionEffectIndex.Hidden) && !e.HasConditionEffect(ConditionEffectIndex.Stasis);
    public static HitTarget? Select(double x, double y, IEnumerable<HitTarget> targets, ISet<int>? alreadyHit = null)
    {
        HitTarget? closest = null; var distance = double.PositiveInfinity;
        foreach (var target in targets)
        {
            if (!target.Eligible || alreadyHit?.Contains(target.Id) == true) continue;
            var dx = target.X - x; var dy = target.Y - y;
            if (!target.Geometry.Contains(dx, dy)) continue;
            // Matches the client's explicit priority and stable equal-distance ordering.
            if (target.Type == 0x6931) return target;
            var next = dx * dx + dy * dy;
            if (next < distance) { closest = target; distance = next; }
        }
        return closest;
    }
}
