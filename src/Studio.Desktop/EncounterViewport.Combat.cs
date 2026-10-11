using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Studio.Raple;

namespace Studio.Desktop;

public enum CombatPreviewView { Density, HealthLoss, NoDamage }
public sealed partial class EncounterViewport
{
    bool combatOpen;
    (double Scale, Vector Pan)? combatCamera;
    CombatPreviewReport? combatReport;
    int combatSelected = -1;
    public event Action<int>? CombatPointSelected;
    public CombatPreviewView CombatView { get; set; }
    public string CombatProgress { get; set; } = "Preparing native runs…";
    public bool CombatPreviewOpen
    {
        get => combatOpen;
        set { if (value == combatOpen) return; if (value) combatCamera = (scale, pan); else { combatReport = null; if (combatCamera is { } camera) { scale = camera.Scale; pan = camera.Pan; combatCamera = null; } } combatOpen = value; InvalidateVisual(); }
    }
    public CombatPreviewReport? CombatReport
    {
        get => combatReport;
        set
        {
            combatSelected = -1;
            combatReport = value;
            if (value != null)
            {
                var options = value.Options; var span = Math.Max(4, (options.Columns - 1) * options.Spacing + 2);
                scale = Math.Clamp(Math.Min(Math.Max(80, ActualWidth - 100), Math.Max(80, ActualHeight - 150)) / span, 4, 48);
                pan = new(-(options.CenterX - 64.5) * scale, -(options.CenterY - 64.5) * scale + scale * .5 + 12);
            }
            InvalidateVisual();
        }
    }
    internal Point CombatPoint(double x, double y) => HitScreen(x, y);
    internal bool SelectCombatPoint(Point cursor)
    {
        if (!combatOpen || combatReport == null) return false;
        var nearest = combatReport.Points.Select((test, index) => (index, Distance: (HitScreen(test.X, test.Y) - cursor).Length)).OrderBy(p => p.Distance).FirstOrDefault();
        if (nearest.Distance > 12) return false;
        combatSelected = nearest.index; CombatPointSelected?.Invoke(combatSelected); InvalidateVisual(); return true;
    }
    void DrawCombatPreview(DrawingContext dc)
    {
        Text(dc, "Attack preview", new(18, 18), 13, Brush("#C4D5CF"));
        if (combatReport is not { } report) { Text(dc, CombatProgress, new(18, 42), 12, Brush("#9FB0A8")); return; }
        var options = report.Options; var count = options.Columns; var values = report.Reference.ExposureSamples;
        var max = CombatView == CombatPreviewView.Density ? Math.Max(1, values.Max()) : Math.Max(1, report.Points.Max(p => p.Sample?.HealthLoss ?? 0));
        DrawMovementMarker(dc, report.EntityId, 64.5, 64.5, 0);
        DrawMovementMarker(dc, report.Reference.PlayerClass, report.Reference.StartX, report.Reference.StartY, Math.PI);
        for (var i = 0; i < report.Points.Length; i++)
        {
            var test = report.Points[i]; var point = HitScreen(test.X, test.Y); var sample = test.Sample;
            var complete = sample is { PlayerDied: false, IncompleteMotion: false } && sample.ActualMs == options.DurationMs;
            var zeroObserved = complete && sample!.HealthLoss == 0 && !report.Player.Invulnerable && !report.Player.Spectator;
            var value = CombatView == CombatPreviewView.Density ? values[i] : sample?.HealthLoss ?? 0;
            var intensity = Math.Clamp((double)value / max, 0, 1);
            var fill = sample == null ? Brush("#353C39") : CombatView == CombatPreviewView.NoDamage ? Brush(zeroObserved ? "#7AB39B" : "#3D4A43") : value == 0 ? Brush("#35423B") : Brush(intensity > .66 ? "#D39177" : intensity > .33 ? "#A97E68" : "#725F51");
            dc.DrawEllipse(fill, new Pen(Brush("#65746C"), .5), point, 6, 6);
            if (i == combatSelected) dc.DrawEllipse(null, new Pen(Brush("#C8DDD1"), 1.5), point, 10, 10);
            if (sample == null) dc.DrawLine(new Pen(Brush("#747D77"), 1), point + new Vector(-4, -4), point + new Vector(4, 4));
        }
        Text(dc, string.Create(CultureInfo.InvariantCulture, $"{options.DurationMs / 1000d:0.#} s per run · seed {report.Seed} · {report.Points.Length} start positions") + (report.ForcedPhase == null ? " · full encounter" : " · forced " + report.ForcedPhase), new(18, 42), 11, Brush("#9FB0A8"));
        var legend = CombatView switch { CombatPreviewView.Density => "Density at point centers · reference run only · five-millisecond projectile samples", CombatPreviewView.HealthLoss => "Health lost at each start · fresh native run per point · capped by initial HP", _ => "Green: no damage observed in completed tests · one seed and duration" };
        Text(dc, legend, new(18, 63), 11, Brush("#8D9D95"));
        if (report.Reference.IncompleteMotion || report.Points.Any(p => p.Sample?.IncompleteMotion == true)) Text(dc, "Partial motion coverage · omitted projectile paths are not tested", new(18, 83), 11, Brush("#C5A384"));
        else if (report.Player.Invulnerable || report.Player.Spectator) Text(dc, "Test-player hit overrides are enabled · zero damage is not a safety result", new(18, 83), 11, Brush("#C5A384"));
        Text(dc, string.Create(CultureInfo.InvariantCulture, $"Reference: {report.Reference.EmittedProjectiles} emitted · peak {report.Reference.PeakIncoming} incoming · {report.Reference.HealthLoss} HP lost"), new(18, ActualHeight - 68), 11, Brush("#9FB0A8"));
        Text(dc, "Prototype arena · no movement or firing input · targeting reruns for damage tests", new(18, ActualHeight - 48), 10, Brush("#74817D"));
        Text(dc, "Dots test exact start points; intervening positions and other seeds remain untested", new(18, ActualHeight - 28), 10, Brush("#74817D"));
    }
}
