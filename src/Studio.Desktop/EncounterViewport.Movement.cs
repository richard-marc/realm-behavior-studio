using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Studio.Raple;

namespace Studio.Desktop;

public sealed record MovementSelection(Guid NodeId, long Revision, MovementPattern Pattern, bool Editable);
public enum MovementHandle { Range, Acquire, Variance }

public sealed partial class EncounterViewport
{
    MovementSelection? movement;
    (MovementSelection Original, MovementHandle Handle)? movementDrag;
    (double Scale, Vector Pan)? movementCamera;
    public MovementSelection? Movement
    {
        get => movement;
        set
        {
            if (value != null && attack != null) Attack = null;
            if (value != null && path != null) Path = null;
            if (movement == null && value != null) movementCamera = (scale, pan);
            CancelMovementDrag(); movement = value;
            if (value == null && movementCamera is { } camera) { scale = camera.Scale; pan = camera.Pan; movementCamera = null; }
            InvalidateVisual();
        }
    }
    public event Action<MovementSelection, string, double>? MovementEdited;
    internal Point MovementPoint(MovementHandle handle, double distance)
    {
        var pattern = movement!.Pattern;
        var centerX = pattern.Kind == "StayCloseToSpawn" || handle == MovementHandle.Acquire ? 64.5 : 70.5;
        return handle switch { MovementHandle.Acquire => HitScreen(centerX, 64.5 - distance), MovementHandle.Variance => HitScreen(centerX + distance, 64.5), _ => HitScreen(centerX, 64.5 + distance) };
    }
    internal Point MovementHandlePoint(MovementHandle handle)
    {
        var pattern = movement!.Pattern;
        return MovementPoint(handle, handle switch { MovementHandle.Acquire => pattern.AcquireRange!.Value, MovementHandle.Variance => pattern.Range + Math.Abs(pattern.RadiusVariance), _ => pattern.Range }) + (handle == MovementHandle.Variance ? new Vector(16, 0) : new Vector());
    }
    IEnumerable<MovementHandle> MovementHandles()
    {
        yield return MovementHandle.Range;
        if (movement!.Pattern.AcquireRange != null) yield return MovementHandle.Acquire;
        if (movement.Pattern.Kind == "Orbit") yield return MovementHandle.Variance;
    }
    internal bool BeginMovementDrag(Point point)
    {
        if (movement is not { Editable: true } selected) return false;
        foreach (var handle in MovementHandles())
            if ((MovementHandlePoint(handle) - point).Length <= 10) { movementDrag = (selected, handle); InvalidateVisual(); return true; }
        return false;
    }
    internal void MoveMovementDrag(Point point)
    {
        if (movementDrag is not { } edit) return;
        var pattern = edit.Original.Pattern; var center = HitWorld(MovementPoint(edit.Handle, 0)); var world = HitWorld(point - (edit.Handle == MovementHandle.Variance ? new Vector(16, 0) : new Vector())); var distance = (world - center).Length;
        var next = edit.Handle switch { MovementHandle.Acquire => pattern with { AcquireRange = distance }, MovementHandle.Variance => pattern with { RadiusVariance = (pattern.RadiusVariance < 0 ? -1 : 1) * Math.Max(0, distance - pattern.Range) }, _ => pattern with { Range = pattern.IntegerRange ? Math.Min(int.MaxValue, Math.Round(distance, MidpointRounding.AwayFromZero)) : distance } };
        movement = edit.Original with { Pattern = next }; InvalidateVisual();
    }
    internal void EndMovementDrag()
    {
        if (movementDrag is not { } edit || movement == null) return;
        var pattern = movement.Pattern; movementDrag = null;
        var parameter = edit.Handle switch { MovementHandle.Acquire => "acquireRange", MovementHandle.Variance => "radiusVariance", _ => pattern.PrimaryParameter };
        var original = edit.Handle switch { MovementHandle.Acquire => edit.Original.Pattern.AcquireRange!.Value, MovementHandle.Variance => edit.Original.Pattern.RadiusVariance, _ => edit.Original.Pattern.Range };
        var value = edit.Handle switch { MovementHandle.Acquire => pattern.AcquireRange!.Value, MovementHandle.Variance => pattern.RadiusVariance, _ => pattern.Range };
        if (Math.Abs(original - value) > .000001) MovementEdited?.Invoke(edit.Original, parameter, Math.Round(value, 3));
        InvalidateVisual();
    }
    internal void CancelMovementDrag()
    {
        if (movementDrag is { } edit) { movement = edit.Original; movementDrag = null; InvalidateVisual(); }
    }
    internal void FitMovement()
    {
        if (movement == null) return;
        var pattern = movement.Pattern; var extent = Math.Max(pattern.AcquireRange ?? 0, pattern.Range + Math.Abs(pattern.RadiusVariance) + (pattern.Kind == "StayCloseToSpawn" ? 0 : 6) + 1);
        scale = Math.Clamp(Math.Min(ActualWidth, ActualHeight) * .32 / Math.Max(6, extent), .001, 32); pan = new(); InvalidateVisual();
    }
    void DrawMovementMarker(DrawingContext dc, string id, double x, double y, double facing)
    {
        // These are prototype markers, not runtime collision bounds. Keep the
        // original sprite readable when a large parameter boundary zooms out.
        var tile = Math.Max(40, scale);
        DrawCharacter(dc, id, HitScreen(x, y) + new Vector(0, tile * .5), SpriteAction.Stand, 0, facing, tile);
    }
    void DrawMovement(DrawingContext dc)
    {
        if (movement is not { } selected) return;
        var pattern = selected.Pattern; var home = pattern.Kind == "StayCloseToSpawn"; var origin = HitScreen(64.5, 64.5); var center = MovementPoint(MovementHandle.Range, 0);
        var boundary = new Pen(Brush("#8EAEA1"), 1.2); var dashed = new Pen(Brush("#465B52"), 1) { DashStyle = DashStyles.Dash };
        if (pattern.AcquireRange is { } acquire) dc.DrawEllipse(null, dashed, origin, acquire * scale, acquire * scale);
        if (pattern.Kind == "Orbit" && pattern.RadiusVariance != 0)
        {
            var outer = new EllipseGeometry(center, (pattern.Range + Math.Abs(pattern.RadiusVariance)) * scale, (pattern.Range + Math.Abs(pattern.RadiusVariance)) * scale);
            var inner = new EllipseGeometry(center, Math.Max(0, pattern.Range - Math.Abs(pattern.RadiusVariance)) * scale, Math.Max(0, pattern.Range - Math.Abs(pattern.RadiusVariance)) * scale);
            dc.DrawGeometry(Brush("#15211C"), null, new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner));
            dc.DrawGeometry(null, dashed, outer); dc.DrawGeometry(null, dashed, inner);
        }
        dc.DrawEllipse(null, boundary, center, pattern.Range * scale, pattern.Range * scale);
        if (pattern.ResumeRange is { } resume) dc.DrawEllipse(null, dashed, center, resume * scale, resume * scale);
        DrawMovementMarker(dc, PreviewEntity, 64.5, 64.5, 0);
        if (!home) DrawMovementMarker(dc, pattern.Target ?? "Wizard", 70.5, 64.5, Math.PI);
        if (home) { dc.DrawEllipse(Brush("#B6CDC1"), null, center, 3, 3); Text(dc, "State-entry position", center + new Vector(10, -30), 11, Brush("#A4B8B0")); }
        foreach (var handle in MovementHandles())
        {
            var point = MovementHandlePoint(handle);
            if (handle == MovementHandle.Variance) dc.DrawLine(dashed, point - new Vector(16, 0), point);
            dc.DrawEllipse(Brush("#182821"), new Pen(Brush(selected.Editable ? "#A8CDBE" : "#737D79"), 1.5), point, 6, 6);
            Text(dc, handle switch { MovementHandle.Acquire => "Acquire target", MovementHandle.Variance => "Radius variance", _ => home ? "Home range" : pattern.Kind == "Orbit" ? "Orbit radius" : "Stop range" }, point + new Vector(10, -9), 11, Brush("#A4B8B0"));
        }
        Text(dc, pattern.Kind == "Orbit" ? "Orbit editor" : home ? "Home-range editor" : "Follow editor", new(18, 18), 13, Brush("#C4D5CF"));
        Text(dc, string.Create(CultureInfo.InvariantCulture, $"{pattern.Range:0.###} tile {pattern.PrimaryParameter}") + (pattern.AcquireRange is { } a ? string.Create(CultureInfo.InvariantCulture, $" · {a:0.###} tile acquisition") : ""), new(18, 41), 11, Brush("#9FB0A8"));
        var detail = pattern.Kind == "Follow" ? "Dashed stop boundary: resume beyond range + 1 tile" : home ? "Anchor is captured on state entry; it can differ from the spawn position" : "Direction: " + (pattern.Clockwise == null ? "seeded choice" : pattern.Clockwise.Value ? "clockwise" : "counterclockwise") + string.Create(CultureInfo.InvariantCulture, $" · radius variance ±{Math.Abs(pattern.RadiusVariance):0.###}");
        Text(dc, detail, new(18, 61), 11, Brush("#869C91"));
        if (pattern.Kind == "Orbit" && Math.Abs(pattern.RadiusVariance) >= pattern.Range) Text(dc, "Native sampled radius can reach or cross zero", new(18, 81), 11, Brush("#C6AF85"));
        Text(dc, selected.Editable ? "Drag a boundary · Esc cancels · Play tests native movement" : "This movement is locked", new(18, ActualHeight - 48), 11, Brush("#8D9D95"));
        Text(dc, home ? "Parameter boundaries · native entry and movement apply during Play" : "Target prototype: " + (pattern.Target ?? "player") + " · seeded motion and effects apply during Play", new(18, ActualHeight - 28), 10, Brush("#74817D"));
    }
}
