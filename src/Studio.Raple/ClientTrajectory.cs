using TKR.Shared.resources;
using TKR.WorldServer.core.objects;
using TKR.WorldServer.core.structures;

namespace Studio.Raple;
public static class ClientTrajectory
{
    public static Position Sample(Player.ValidatedProjectile path, int age, int id, ProjectileDesc desc)
    {
        if (desc.Parametric && !desc.Wavy && !desc.ReturnToOrigin && !desc.Radial)
        {
            // Projectile.as uses the opposite X parity to the server's validation formula.
            // Render and hit-test the CLIENT path, which is authoritative for hit reporting.
            var t = age / (double)desc.LifetimeMS * 2 * Math.PI;
            var x = Math.Sin(t) * (id % 2 == 0 ? -1 : 1);
            var y = Math.Sin(2 * t) * (id % 4 < 2 ? 1 : -1);
            var sin = Math.Sin(path.Angle); var cos = Math.Cos(path.Angle);
            return new Position(path.StartX + (float)((x * cos - y * sin) * desc.Magnitude),
                path.StartY + (float)((x * sin + y * cos) * desc.Magnitude));
        }
        return path.GetPosition(age, id, desc);
    }
}
