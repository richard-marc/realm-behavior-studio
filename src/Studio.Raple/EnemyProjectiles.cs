using System.Globalization;
using BossPatternSim;
using Studio.Core;
using TKR.WorldServer.core.objects;

namespace Studio.Raple;

public sealed record EnemyProjectile(int Index, string TextureId, int MinDamage, int MaxDamage, double LifetimeMs, double Speed, string Traits, string? PreviewLimitation)
{
    public string Title => $"#{Index} · {TextureId}";
    public string Summary => string.Create(CultureInfo.InvariantCulture, $"{(MinDamage == MaxDamage ? MinDamage.ToString(CultureInfo.InvariantCulture) : MinDamage + "–" + MaxDamage)} damage · {LifetimeMs / 1000:0.###} s · base speed {Speed / 10:0.###} tiles/s");
}

public static class EnemyProjectiles
{
    public static IReadOnlyList<EnemyProjectile> ForEntity(string entityId)
    {
        if (!SimEngine.Data.IdToObjectType.TryGetValue(entityId, out var type) || !SimEngine.Data.ObjectDescs.TryGetValue(type, out var owner)) return [];
        return owner.Projectiles.OrderBy(pair => pair.Key).Select(pair =>
        {
            var p = pair.Value; var traits = new List<string>();
            if (p.ArmorPiercing) traits.Add("Armor piercing"); if (p.MultiHit) traits.Add("Multi-hit"); if (p.PassesCover) traits.Add("Passes cover");
            if (p.Parametric) traits.Add("Parametric"); if (p.Wavy) traits.Add("Wavy"); if (p.Boomerang) traits.Add("Boomerang"); if (p.ReturnToOrigin) traits.Add("Returns to origin");
            if (p.Radial) traits.Add("Radial"); if (p.Homing) traits.Add("Homing"); if (p.Orbit) traits.Add("Orbit"); if (p.Converging) traits.Add("Converging");
            if (p.Acceleration != 0) traits.Add("Accelerating"); if (p.AngleChange != 0 || p.AngleChangeAccel != 0) traits.Add("Curving"); if (p.ArmingDelayMS > 0) traits.Add($"Arming delay {p.ArmingDelayMS} ms");
            traits.AddRange(p.Effects.Select(effect => effect.Effect.ToString()));
            return new EnemyProjectile(pair.Key, p.ObjectId, p.MinDamage, p.MaxDamage, p.LifetimeMS, p.Speed, string.Join(" · ", traits),
                !Player.ValidatedProjectile.CanSampleBossLab(p) || p.Homing || p.Orbit ? "This motion is retained in source; local trajectory playback is incomplete." : null);
        }).ToArray();
    }
}

public sealed partial class RapleAdapter
{
    public bool IsNativeShoot(BehaviorDocument document, BehaviorNode node) => node.Kind == "Shoot" && NativeKind(document, node) == "Shoot";
    public int? ReadProjectileIndex(BehaviorDocument document, BehaviorNode node) => IsNativeShoot(document, node) && ReadNumericParameter(document, node, "projectileIndex", 0, out var value) && value is >= 0 and <= int.MaxValue && value == Math.Truncate(value.Value) ? (int)value.Value : null;
    string? NativeKind(BehaviorDocument document, BehaviorNode node)
    {
        var scoped = document.SchemaAt(node);
        return scoped?.HasSourceContext == true ? scoped.DescribeExpression(document.Expression(node))?.Kind : Binding(document.Expression(node)).Constructor?.DeclaringType?.Name;
    }
    public bool HasKnownProjectileOwner(BehaviorDocument document, string entityId, BehaviorNode node)
    {
        var root = document.Entities.SingleOrDefault(entity => entity.EntityId == entityId)?.Root;
        if (root == null || !root.Descendants().Any(child => child.Id == node.Id) || !IsNativeShoot(document, node)) return false;
        return root.Descendants().Where(parent => node.Path.StartsWith(parent.Path + "/", StringComparison.Ordinal))
            .All(parent => parent.Kind == "ArgumentGroup" || NativeKind(document, parent) is "State" or "Prioritize" or "Timed" or "OnDeathBehavior");
    }
}
