using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Studio.Raple;

namespace Studio.Desktop;

public sealed class EncounterTimeline : FrameworkElement
{
    public EncounterReplay? Replay { get; set; }
    public long CursorMs { get; set; }
    public EncounterEvent? Selected { get; set; }
    public event Action<long, EncounterEvent?>? SeekRequested;
    DrawingGroup? plot;
    EncounterReplay? plotReplay;
    double plotWidth, plotHeight, plotDpi;
    static readonly string[] lanes = ["Phases", "Attacks", "Movement", "Health/tests", "Actors"];
    static int Lane(EncounterEvent e) => e.Kind switch { "transition" or "forced-phase" => 0, "attack" or "player-attack" => 1, "movement" => 2, "hit" or "weapon-hit" or "override" => 3, _ => 4 };
    static Brush Color(int lane) => new SolidColorBrush(lane switch { 0 => System.Windows.Media.Color.FromRgb(119, 197, 172), 1 => System.Windows.Media.Color.FromRgb(175, 157, 218), 3 => System.Windows.Media.Color.FromRgb(216, 171, 112), _ => System.Windows.Media.Color.FromRgb(123, 153, 184) });
    public static long EventSeekMs(EncounterEvent e, long duration) => Math.Min(duration, e.Ms + (e.Kind is "start" or "forced-phase" or "override" ? 0 : EncounterSession.SampleMs));
    double Width => Math.Max(1, ActualWidth - 104);
    double X(long ms) => 88 + ms / (double)Math.Max(5, Replay?.DurationMs ?? 5) * Width;
    FormattedText Text(string text, Brush color, double size = 11) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if(plot==null || !ReferenceEquals(plotReplay,Replay) || plotWidth!=ActualWidth || plotHeight!=ActualHeight || plotDpi!=dpi)
        {
            plot=new DrawingGroup(); using(var chart=plot.Open()) DrawPlot(chart);
            plot.Freeze(); plotReplay=Replay; plotWidth=ActualWidth; plotHeight=ActualHeight; plotDpi=dpi;
        }
        dc.DrawDrawing(plot);
        if(Replay==null)return;
        if (Selected != null) dc.DrawEllipse(null,new Pen(Brushes.White,1),new(X(Selected.Ms),56+Lane(Selected)*35),7,13);
        dc.DrawLine(new Pen(Brushes.White,1),new(X(CursorMs),28),new(X(CursorMs),206));
    }
    void DrawPlot(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(16,18,21)), null, new(0,0,ActualWidth,ActualHeight));
        var muted = new SolidColorBrush(System.Windows.Media.Color.FromRgb(142,148,157)); var line = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromRgb(41,45,51)), 1);
        if (Replay == null) { dc.DrawText(Text("Play an encounter to record its native events.", muted, 12), new(16,24)); return; }
        for (var lane = 0; lane < lanes.Length; lane++)
        { var y = 56 + lane * 35; dc.DrawText(Text(lanes[lane], muted), new(12,y-8)); dc.DrawLine(line, new(88,y),new(ActualWidth-16,y)); }
        for (var i=0;i<=5;i++)
        { var ms=Replay.DurationMs*i/5; var x=X(ms); dc.DrawLine(line,new(x,28),new(x,206)); dc.DrawText(Text((ms/1000d).ToString("0.00")+" s",muted),new(Math.Clamp(x-12,88,Math.Max(88,ActualWidth-52)),8)); }
        // Coalesce pixels only for display; all original events remain in the list.
        foreach (var group in Replay.Events.GroupBy(e => (Lane(e), Pixel: (int)X(e.Ms))))
        { var e=group.First(); var y=56+Lane(e)*35; dc.DrawLine(new Pen(Color(Lane(e)), 3),new(X(e.Ms),y-9),new(X(e.Ms),y+9)); }
        foreach(var group in Replay.Movements.GroupBy(m=>(int)X(m.Ms)))
        { var movement=group.First(); dc.DrawLine(new Pen(Color(2),3),new(X(movement.Ms),118),new(X(movement.Ms),134)); }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e); if (Replay == null || Replay.DurationMs == 0) return;
        var p=e.GetPosition(this); var closest=Replay.Events.OrderBy(ev => Math.Abs(X(ev.Ms)-p.X)+Math.Abs(56+Lane(ev)*35-p.Y)).FirstOrDefault();
        var movement=Replay.Movements.OrderBy(m=>Math.Abs(X(m.Ms)-p.X)).FirstOrDefault();
        if(movement!=null && Math.Abs(p.Y-126)<14 && Math.Abs(X(movement.Ms)-p.X)<8)
        {
            var ev=new EncounterEvent(movement.Ms,"movement",$"{movement.Entity}: ({movement.FromX:0.000}, {movement.FromY:0.000}) → ({movement.ToX:0.000}, {movement.ToY:0.000}) · native {movement.DurationMs} ms step"+(movement.Phase!=""?" · observed phase "+movement.Phase:""),movement.PhaseNodeId,movement.ActorId);
            SeekRequested?.Invoke(EventSeekMs(ev,Replay.DurationMs),ev);
        }
        else if (closest != null && Math.Abs(X(closest.Ms)-p.X)<8 && Math.Abs(56+Lane(closest)*35-p.Y)<14) SeekRequested?.Invoke(EventSeekMs(closest,Replay.DurationMs),closest);
        else SeekRequested?.Invoke(Math.Clamp((long)Math.Round((p.X-88)/Width*Replay.DurationMs/5)*5,0,Replay.DurationMs),null);
        e.Handled=true;
    }
}
