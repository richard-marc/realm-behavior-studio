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
    int combatScenarioIndex;
    public int CombatScenarioIndex { get => combatScenarioIndex; set { combatScenarioIndex = Math.Max(0, value); combatSelected = -1; InvalidateVisual(); } }
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
            combatScenarioIndex = 0;
            combatReport = value;
            if (value != null)
            {
                FitCombatPreview(value);
            }
            InvalidateVisual();
        }
    }
    void FitCombatPreview(CombatPreviewReport report)
    {
        var scenarios = report.Scenarios.IsDefaultOrEmpty ? new[] { report.Scenario() } : report.Scenarios.ToArray();
        var positions = scenarios.SelectMany(s => s.Points.Select(p => new Point(p.X, p.Y))
            .Concat(s.Reference.Path.Select(p => new Point(p.X, p.Y)))
            .Concat(s.Points.SelectMany(p => p.Sample?.Path ?? []).Select(p => new Point(p.X, p.Y))))
            .Append(new Point(report.BossX, report.BossY)).ToArray();
        var minX = positions.Min(p => p.X) - 1; var maxX = positions.Max(p => p.X) + 1;
        var minY = positions.Min(p => p.Y) - 1; var maxY = positions.Max(p => p.Y) + 1;
        scale = Math.Clamp(Math.Min(Math.Max(80, ActualWidth - 100) / Math.Max(4, maxX - minX),
            Math.Max(80, ActualHeight - 190) / Math.Max(4, maxY - minY)), .5, 48);
        pan = new(-((minX + maxX) / 2 - 64.5) * scale, -((minY + maxY) / 2 - 64.5) * scale + scale * .5 + 12);
    }
    internal Point CombatPoint(double x, double y) => HitScreen(x, y);
    internal bool SelectCombatPoint(Point cursor)
    {
        if (!combatOpen || combatReport == null) return false;
        var nearest = combatReport.Scenario(combatScenarioIndex).Points.Select((test, index) => (index, Distance: (HitScreen(test.X, test.Y) - cursor).Length)).OrderBy(p => p.Distance).FirstOrDefault();
        if (nearest.Distance > 12) return false;
        combatSelected = nearest.index; CombatPointSelected?.Invoke(combatSelected); InvalidateVisual(); return true;
    }
    void DrawCombatPreview(DrawingContext dc)
    {
        Text(dc, "Attack preview", new(18, 18), 13, Brush("#C4D5CF"));
        if (combatReport is not { } report) { Text(dc, CombatProgress, new(18, 42), 12, Brush("#9FB0A8")); return; }
        var scenario = report.Scenario(combatScenarioIndex); var reference = scenario.Reference; var points = scenario.Points;
        var options = report.Options; var count = options.Columns; var values = reference.ExposureSamples;
        var max = CombatView == CombatPreviewView.Density ? Math.Max(1, values.Max()) : Math.Max(1, points.Max(p => p.Sample?.HealthLoss ?? 0));
        DrawMovementMarker(dc, report.EntityId, report.BossX, report.BossY, 0);
        DrawMovementMarker(dc, reference.PlayerClass, reference.StartX, reference.StartY, Math.PI);
        var path = combatSelected >= 0 && combatSelected < points.Length ? points[combatSelected].Sample?.Path ?? [] : reference.Path;
        for (var i = 1; i < path.Length; i++) dc.DrawLine(new Pen(Brush("#79B9A5"), 1.5), HitScreen(path[i - 1].X, path[i - 1].Y), HitScreen(path[i].X, path[i].Y));
        for (var i = 0; i < points.Length; i++)
        {
            var test = points[i]; var point = HitScreen(test.X, test.Y); var sample = test.Sample;
            var zeroObserved = report.NoDamageObserved(i);
            var value = CombatView == CombatPreviewView.Density ? values[i] : sample?.HealthLoss ?? 0;
            var intensity = Math.Clamp((double)value / max, 0, 1);
            var fill = sample == null ? Brush("#353C39") : CombatView == CombatPreviewView.NoDamage ? Brush(zeroObserved ? "#7AB39B" : "#3D4A43") : value == 0 ? Brush("#35423B") : Brush(intensity > .66 ? "#D39177" : intensity > .33 ? "#A97E68" : "#725F51");
            dc.DrawEllipse(fill, new Pen(Brush("#65746C"), .5), point, 6, 6);
            if (i == combatSelected) dc.DrawEllipse(null, new Pen(Brush("#C8DDD1"), 1.5), point, 10, 10);
            if (sample == null) dc.DrawLine(new Pen(Brush("#747D77"), 1), point + new Vector(-4, -4), point + new Vector(4, 4));
        }
        Text(dc, string.Create(CultureInfo.InvariantCulture, $"{options.DurationMs / 1000d:0.#} s per run · seed {scenario.Seed} · {points.Length} starts · {options.InputLabel}") + (report.ForcedPhase == null ? " · full encounter" : " · forced " + report.ForcedPhase), new(18, 42), 11, Brush("#9FB0A8"));
        var legend = CombatView switch { CombatPreviewView.Density => "Density at point centers · selected seed reference · five-millisecond projectile samples", CombatPreviewView.HealthLoss => "Health lost at each start · selected seed · capped by initial HP", _ => "Green: no damage observed in completed tests for every tested seed" };
        Text(dc, legend, new(18, 63), 11, Brush("#8D9D95"));
        if (reference.IncompleteMotion || points.Any(p => p.Sample?.IncompleteMotion == true)) Text(dc, "Partial motion coverage · omitted projectile paths are not tested", new(18, 83), 11, Brush("#C5A384"));
        else if (report.Player.Invulnerable || report.Player.Spectator) Text(dc, "Test-player hit overrides are enabled · zero damage is not a safety result", new(18, 83), 11, Brush("#C5A384"));
        Text(dc, string.Create(CultureInfo.InvariantCulture, $"Reference: {reference.EmittedProjectiles} emitted · {reference.PlayerProjectiles} player shots · {reference.HealthLoss} HP lost"), new(18, ActualHeight - 68), 11, Brush("#9FB0A8"));
        Text(dc, "Native inputs rerun from each start · path sampled every 100 ms · collision sampled every 5 ms", new(18, ActualHeight - 48), 10, Brush("#74817D"));
        Text(dc, "Exact tested starts, inputs and seeds only; intervening positions remain untested", new(18, ActualHeight - 28), 10, Brush("#74817D"));
    }
}
