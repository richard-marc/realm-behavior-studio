using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Studio.Raple;

namespace Studio.Desktop;

public sealed record AttackSelection(Guid NodeId, long Revision, ShootPattern Pattern, bool Editable);
public enum AttackHandle { Aim, Spread, Radius }
public sealed partial class EncounterViewport
{
    AttackSelection? attack;
    (AttackSelection Original, AttackHandle Handle)? attackDrag;
    (double Scale, Vector Pan)? attackCamera;
    public AttackSelection? Attack
    {
        get => attack;
        set
        {
            if(value!=null&&crossCut!=null)CrossCut=null;
            if(value!=null&&snare!=null)Snare=null;
            if(value!=null&&customMove!=null)CustomMove=null;
            if (value != null && movement != null) Movement = null;
            if (value != null && path != null) Path = null;
            if(value!=null&&ring!=null)Ring=null;
            if (value != null && sweep != null) Sweep = null;
            if (attack == null && value != null) attackCamera = (scale, pan);
            CancelAttackDrag(); attack = value;
            if (value == null && attackCamera is { } camera) { scale = camera.Scale; pan = camera.Pan; attackCamera = null; }
            InvalidateVisual();
        }
    }
    public event Action<AttackSelection, string, double>? AttackEdited;
    const double DirectionLength = 4;
    // Native nearest-player acquisition uses a strict distance < search radius.
    internal static double? TargetDirection(ShootPattern pattern) => pattern.Radius > 6 ? 0 : null;
    internal Point AttackPoint(double degrees, double distance) => HitScreen(64.5 + Math.Cos(degrees * Math.PI / 180) * distance, 64.5 + Math.Sin(degrees * Math.PI / 180) * distance);
    internal Point AttackHandlePoint(AttackHandle handle)
    {
        var pattern = attack!.Pattern; var target = TargetDirection(pattern); var center = pattern.Center(target);
        return handle switch { AttackHandle.Aim => AttackPoint(center, DirectionLength), AttackHandle.Spread => AttackPoint(pattern.Angles(target).Last(), DirectionLength + 16 / scale), _ => AttackPoint(center + (pattern.Spacing < 0 ? 90 : -90), pattern.Radius) };
    }
    internal bool BeginAttackDrag(Point point)
    {
        if (attack is not { Editable: true } selected) return false;
        foreach (var handle in Enum.GetValues<AttackHandle>())
        {
            if (handle == AttackHandle.Spread && selected.Pattern.Count == 1) continue;
            if ((AttackHandlePoint(handle) - point).Length > 10) continue;
            attackDrag = (selected, handle); InvalidateVisual(); return true;
        }
        return false;
    }
    internal void MoveAttackDrag(Point point)
    {
        if (attackDrag is not { } edit) return;
        var world = HitWorld(point); var x = world.X - 64.5; var y = world.Y - 64.5;
        if (edit.Handle != AttackHandle.Radius && Math.Abs(x) + Math.Abs(y) < .0001) return;
        var degrees = Math.Atan2(y, x) * 180 / Math.PI; var pattern = edit.Original.Pattern;
        var delta = Signed(degrees - pattern.Center(TargetDirection(pattern))); var originalHalf = pattern.Spacing * (pattern.Count - 1) / 2;
        delta += 360 * Math.Round((originalHalf - delta) / 360);
        var next = edit.Handle switch
        {
            AttackHandle.Aim => pattern with { FixedAngle = Normalize(degrees - pattern.Offset) },
            AttackHandle.Spread => pattern with { Spacing = delta * 2 / (pattern.Count - 1) },
            _ => pattern with { Radius = Math.Sqrt(x * x + y * y) }
        };
        attack = edit.Original with { Pattern = next }; InvalidateVisual();
    }
    internal void EndAttackDrag()
    {
        if (attackDrag is not { } edit || attack == null) return;
        var next = attack.Pattern; attackDrag = null;
        var parameter = edit.Handle switch { AttackHandle.Aim => "fixedAngle", AttackHandle.Spread => "shootAngle", _ => "radius" };
        var original = edit.Handle switch { AttackHandle.Aim => edit.Original.Pattern.FixedAngle, AttackHandle.Spread => edit.Original.Pattern.Spacing, _ => edit.Original.Pattern.Radius };
        var value = edit.Handle switch { AttackHandle.Aim => next.FixedAngle, AttackHandle.Spread => next.Spacing, _ => next.Radius };
        if (value is { } number && (original == null || Math.Abs(original.Value - number) > .000001)) AttackEdited?.Invoke(edit.Original, parameter, Math.Round(number, 3));
        InvalidateVisual();
    }
    internal void CancelAttackDrag()
    {
        if (attackDrag is { } edit) { attack = edit.Original; attackDrag = null; InvalidateVisual(); }
    }
    static double Normalize(double degrees) => (degrees % 360 + 360) % 360;
    static double Signed(double degrees) => Normalize(degrees + 180) - 180;
    internal void FitAttack()
    {
        if (attack == null) return;
        scale = Math.Clamp(Math.Min(ActualWidth, ActualHeight) * .38 / Math.Max(6, attack.Pattern.Radius), .001, 32); pan = new(); InvalidateVisual();
    }
    void DrawAttack(DrawingContext dc)
    {
        if (attack is not { } selected) return;
        var pattern = selected.Pattern; var target = TargetDirection(pattern); var canFire = pattern.CanFire(target); var origin = HitScreen(64.5, 64.5);
        var circle = new Pen(Brush("#485B56"), 1) { DashStyle = DashStyles.Dash };
        dc.DrawEllipse(null, circle, origin, pattern.Radius * scale, pattern.Radius * scale);
        DrawCharacter(dc, "Wizard", Screen(70.5, 64.5), SpriteAction.Stand, 0, Math.PI);
        var span = pattern.Spacing * (pattern.Count - 1);
        if (canFire && pattern.Count > 1 && Math.Abs(span) > .001)
        {
            if (Math.Abs(span) >= 360) dc.DrawEllipse(null, circle, origin, DirectionLength * scale, DirectionLength * scale);
            else
            {
                var angles = pattern.Angles(target); var arc = new StreamGeometry();
                using (var path = arc.Open()) { path.BeginFigure(AttackPoint(angles[0], DirectionLength), false, false); path.ArcTo(AttackPoint(angles[^1], DirectionLength), new Size(DirectionLength * scale, DirectionLength * scale), 0, Math.Abs(span) > 180, span > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false); }
                dc.DrawGeometry(null, circle, arc);
            }
        }
        var ray = new Pen(Brush("#789D91"), 1.2);
        if (canFire) foreach (var angle in pattern.Angles(target)) dc.DrawLine(ray, origin, AttackPoint(angle, DirectionLength));
        var center = AttackPoint(pattern.Center(target), DirectionLength);
        dc.DrawLine(new Pen(Brush(canFire ? "#C3DBD3" : "#485B56"), 1.5) { DashStyle = canFire ? DashStyles.Solid : DashStyles.Dash }, origin, center);
        dc.DrawEllipse(Brush("#A8C5BA"), null, origin, 3, 3);
        foreach (var handle in Enum.GetValues<AttackHandle>())
        {
            if (handle == AttackHandle.Spread && pattern.Count == 1) continue;
            var point = AttackHandlePoint(handle);
            dc.DrawEllipse(Brush("#182821"), new Pen(Brush(selected.Editable ? "#A8CDBE" : "#737D79"), 1.5), point, 6, 6);
            Text(dc, handle switch { AttackHandle.Aim => pattern.FixedAngle != null ? "Aim" : target != null ? "Aim · targets player" : pattern.DefaultAngle != null ? "Aim · fallback" : "Aim · no target", AttackHandle.Spread => "Spacing", _ => "Search radius" }, point + new Vector(10, -9), 11, Brush("#A4B8B0"));
        }
        Text(dc, "Attack editor", new(18, 18), 13, Brush("#C4D5CF"));
        Text(dc, string.Create(CultureInfo.InvariantCulture, $"{pattern.Count} shots · {pattern.Spacing:0.###}° spacing · {pattern.Radius:0.###} tile search radius"), new(18, 41), 11, Brush("#9FB0A8"));
        if (!canFire) Text(dc, "Target outside search radius · this attack does not fire", new(18, 61), 11, Brush("#C6AF85"));
        Text(dc, selected.Editable ? (pattern.Count == 1 ? "Drag aim or radius" : "Drag aim, spacing or radius") + " · Esc cancels a drag · Play tests the attack" : "This attack is locked", new(18, ActualHeight - 48), 11, Brush("#8D9D95"));
        Text(dc, "First-volley directions · prediction, rotation and effects apply during Play", new(18, ActualHeight - 28), 10, Brush("#74817D"));
    }
}
