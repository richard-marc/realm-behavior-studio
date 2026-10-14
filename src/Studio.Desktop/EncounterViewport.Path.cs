using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Studio.Raple;

namespace Studio.Desktop;

public sealed record PathSelection(Guid NodeId, long Revision, NativePathPattern Pattern, bool Editable);
public enum PathHandle { Destination, Direction }
public sealed partial class EncounterViewport
{
    PathSelection? path;
    (PathSelection Original, PathHandle Handle, Vector Offset)? pathDrag;
    (double Scale, Vector Pan)? pathCamera;
    NativeMovementTrace? pathTrace;
    public PathSelection? Path
    {
        get => path;
        set
        {
            if(value!=null&&crossCut!=null)CrossCut=null;
            if(value!=null&&snare!=null)Snare=null;
            if(value!=null&&customMove!=null)CustomMove=null;
            if (value != null) { if (attack != null) Attack = null; if (movement != null) Movement = null; if(sweep!=null)Sweep=null;if(ring!=null)Ring=null; }
            if (path == null && value != null) pathCamera = (scale, pan);
            CancelPathDrag(); path = value; pathTrace = null;
            if (value == null && pathCamera is { } camera) { scale = camera.Scale; pan = camera.Pan; pathCamera = null; }
            InvalidateVisual();
        }
    }
    public NativeMovementTrace? PathTrace { get => pathTrace; set { pathTrace = value; InvalidateVisual(); } }
    public event Action<PathSelection, IReadOnlyDictionary<string, double>>? PathEdited;
    Point PathDestination()
    {
        var pattern = path!.Pattern;
        var point = pattern.Destination(64.5, 64.5) ?? (64.5 + Math.Cos(pattern.First * Math.PI / 180) * 4, 64.5 + Math.Sin(pattern.First * Math.PI / 180) * 4);
        return HitScreen(point.Item1, point.Item2);
    }
    internal Point PathPoint(double x, double y) => HitScreen(x, y);
    Vector PathLeader() => path!.Pattern.IsVector ? new(-Math.Sin(path.Pattern.First * Math.PI / 180) * 16, Math.Cos(path.Pattern.First * Math.PI / 180) * 16) : new();
    Point PathHandleBase(PathHandle handle) => handle == PathHandle.Destination ? PathDestination() : HitScreen(64.5 + Math.Cos(path!.Pattern.First * Math.PI / 180) * 4, 64.5 + Math.Sin(path.Pattern.First * Math.PI / 180) * 4);
    internal Point PathHandlePoint(PathHandle handle) => PathHandleBase(handle) + (pathDrag is { } edit && edit.Handle == handle ? edit.Offset : handle == PathHandle.Destination ? PathLeader() : -PathLeader());
    internal Point PathDestinationDragPoint(double x, double y) => HitScreen(x, y) + (pathDrag?.Offset ?? PathLeader());
    internal Point PathDirectionDragPoint(double degrees) => HitScreen(64.5 + Math.Cos(degrees * Math.PI / 180) * 4, 64.5 + Math.Sin(degrees * Math.PI / 180) * 4) + (pathDrag?.Offset ?? -PathLeader());
    internal bool BeginPathDrag(Point point)
    {
        if (path is not { Editable: true } selected) return false;
        foreach (var handle in selected.Pattern.IsVector ? new[] { PathHandle.Destination, PathHandle.Direction } : [PathHandle.Destination])
            if ((PathHandlePoint(handle) - point).Length <= 10) { pathDrag = (selected, handle, point - PathHandleBase(handle)); pathTrace = null; InvalidateVisual(); return true; }
        return false;
    }
    internal void MovePathDrag(Point point)
    {
        if (pathDrag is not { } edit) return;
        var pattern = edit.Original.Pattern; var world = HitWorld(point - edit.Offset);
        if (pattern.IsVector)
        {
            var delta = world - new Point(64.5, 64.5); var negative = pattern.Kind == "NewMove" && pattern.Second < 0 && edit.Handle == PathHandle.Destination;
            var angle = (Math.Atan2(delta.Y, delta.X) * 180 / Math.PI + (negative ? 180 : 0) + 360) % 360;
            pattern = pattern with { First = angle, Second = edit.Handle == PathHandle.Direction ? pattern.Second : delta.Length * (negative ? -1 : 1) };
        }
        else pattern = pattern with { First = world.X - (pattern.Anchor == NativePathAnchor.Map ? 0 : 64.5), Second = world.Y - (pattern.Anchor == NativePathAnchor.Map ? 0 : 64.5) };
        path = edit.Original with { Pattern = pattern }; InvalidateVisual();
    }
    internal void EndPathDrag()
    {
        if (pathDrag is not { } edit || path == null) return;
        var pattern = path.Pattern; pathDrag = null; var values = new Dictionary<string, double>();
        if (Math.Abs(pattern.First - edit.Original.Pattern.First) > .000001) values.Add(pattern.FirstParameter, Math.Round(pattern.First, 3));
        if (Math.Abs(pattern.Second - edit.Original.Pattern.Second) > .000001) values.Add(pattern.SecondParameter, Math.Round(pattern.Second, 3));
        if (values.Count > 0) PathEdited?.Invoke(edit.Original, values);
        InvalidateVisual();
    }
    internal void CancelPathDrag() { if (pathDrag is { } edit) { path = edit.Original; pathDrag = null; InvalidateVisual(); } }
    internal void FitPath()
    {
        if (path == null) return;
        var points = new List<Point> { new(64.5, 64.5), HitWorld(PathDestination()) };
        if (pathTrace != null) points.AddRange(pathTrace.Points.Select(p => new Point(p.X, p.Y)));
        var minX = points.Min(p => p.X); var maxX = points.Max(p => p.X); var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y);
        scale = Math.Clamp(Math.Min(Math.Max(100, ActualWidth - 150) / Math.Max(8, maxX - minX + 4), Math.Max(100, ActualHeight - 200) / Math.Max(8, maxY - minY + 4)), 1e-36, 32);
        pan = new(-(minX + maxX - 129) * .5 * scale, -(minY + maxY - 129) * .5 * scale + scale * .5); InvalidateVisual();
    }
    void DrawPath(DrawingContext dc)
    {
        if (path is not { } selected) return;
        var pattern = selected.Pattern; var origin = HitScreen(64.5, 64.5); var destination = PathDestination();
        var requested = new Pen(Brush("#53645C"), 1.2) { DashStyle = DashStyles.Dash };
        dc.DrawLine(requested, origin, destination); dc.DrawEllipse(null, requested, destination, 4, 4);
        DrawMovementMarker(dc, PreviewEntity, 64.5, 64.5, Math.Atan2(destination.Y - origin.Y, destination.X - origin.X));
        if (pathTrace is { } trace)
        {
            var pen = new Pen(Brush("#99DBC1"), 2);
            for (var i = 1; i < trace.Points.Length; i++) dc.DrawLine(pen, HitScreen(trace.Points[i - 1].X, trace.Points[i - 1].Y), HitScreen(trace.Points[i].X, trace.Points[i].Y));
            var last = trace.Points.Last(); dc.DrawEllipse(Brush("#99DBC1"), null, HitScreen(last.X, last.Y), 4, 4);
            Text(dc, string.Create(CultureInfo.InvariantCulture, $"Native phase preview · {last.Ms / 1000d:0.#} s · seed {trace.Seed}") + (trace.StoppedReason == null ? "" : " · " + trace.StoppedReason), new(18, 81), 11, Brush("#99DBC1"));
        }
        foreach (var handle in pattern.IsVector ? new[] { PathHandle.Destination, PathHandle.Direction } : [PathHandle.Destination])
        {
            var point = PathHandlePoint(handle); dc.DrawEllipse(Brush("#182821"), new Pen(Brush(selected.Editable ? "#A8CDBE" : "#737D79"), 1.5), point, 6, 6);
            Text(dc, handle == PathHandle.Direction ? "Direction" : pattern.Continuous ? "Set finite distance" : "Destination", point + new Vector(10, -9), 11, Brush("#A4B8B0"));
        }
        Text(dc, pattern.Kind + " path editor", new(18, 18), 13, Brush("#C4D5CF"));
        Text(dc, string.Create(CultureInfo.InvariantCulture, $"{pattern.FirstParameter} {pattern.First:0.###} · {pattern.SecondParameter} {pattern.Second:0.###}") + (pattern.IsVector ? " · degrees / tiles" : " · tiles"), new(18, 41), 11, Brush("#9FB0A8"));
        var anchor = pattern.Anchor switch { NativePathAnchor.Map => "Absolute map destination", NativePathAnchor.StateEntry => "Offset from state-entry position", NativePathAnchor.Spawn => "Offset from the native spawn point", _ => pattern.Kind == "NewMove" ? "Destination resampled from current position on native cooldown" : pattern.Continuous ? "Continuous native steps; zero distance has no fixed endpoint" : pattern.Second < 0 ? "Negative distance does not move this native MoveLine" : "Requested distance; native ticks may overshoot" };
        Text(dc, anchor + (pattern.Instant == true ? " · instant on entry" : ""), new(18, 61), 11, Brush("#869C91"));
        Text(dc, selected.Editable ? "Drag a handle · Esc cancels · one Undo per gesture" : "This path is locked", new(18, ActualHeight - 48), 11, Brush("#8D9D95"));
        Text(dc, "Dashed: requested parameters · green: native whole-phase samples · prototype anchor 64.5, 64.5", new(18, ActualHeight - 28), 10, Brush("#74817D"));
    }
}
