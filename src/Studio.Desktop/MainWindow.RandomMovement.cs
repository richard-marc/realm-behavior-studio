using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? randomMovePanel;
    ComboBox? swirlTargeting;
    void AddRandomMovementControls(BehaviorNode node)
    {
        randomMovePanel = null; swirlTargeting = null;
        if (!adapter.IsRandomMovement(document, node)) return;
        var body = new StackPanel();
        void Note(string text) => body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new(0, 8, 0, 6), Foreground = EncounterViewport.Brush("#A9B1AE") });
        var canvas = new Canvas { Width = 250, Height = 150, ClipToBounds = true, Background = EncounterViewport.Brush("#141818"), Margin = new(0, 8, 0, 4) };
        void Host(double x, double y)
        {
            var image = new Image { Source = sprites.Get(selectedEntity!.Id), Width = 22, Height = 22 };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor); Canvas.SetLeft(image, x - 11); Canvas.SetTop(image, y - 11); canvas.Children.Add(image);
        }
        if (node.Kind == "Swirl")
        {
            var settings = adapter.ReadSwirl(document, node);
            if (settings != null)
            {
                var expectedDocument = document; var revision = document.Revision;
                swirlTargeting = new() { ItemsSource = new[] { "Acquire a player", "Center on entry position" }, SelectedIndex = settings.Targeted ? 0 : 1, IsEnabled = RegionEditable(node), Margin = new(0, 8, 0, 0) };
                swirlTargeting.SelectionChanged += (_, _) => Try(() =>
                {
                    if (document != expectedDocument || document.Revision != revision) throw new InvalidOperationException("The movement changed. Select it again before changing its center.");
                    document.Apply(new(revision, "Set swirl center", [new(node.Id, "targeted", swirlTargeting.SelectedIndex == 0 ? "true" : "false")]), ValidateAuthoring); Changed(node.Id);
                });
                body.Children.Add(swirlTargeting);
                if (!settings.Targeted && settings.Radius != 0)
                {
                    var ring = new Ellipse { Width = 100, Height = 100, Stroke = EncounterViewport.Brush("#9FD6BB"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } };
                    Canvas.SetLeft(ring, 75); Canvas.SetTop(ring, 25); canvas.Children.Add(ring); Host(125, 75); body.Children.Add(canvas);
                    Note($"Entry center · {settings.Radius:0.###} tile signed radius\nRing shows the steering goal, not a predicted path.");
                }
                else Note($"{settings.Radius:0.###} tile signed radius · {settings.AcquireRange:0.###} tile acquisition range");
            }
            else Note("Source expressions define this movement. The editor does not execute getters or factories to draw a path.");
            Note("Acquisition requires both player coordinates to differ from the host. The native center stays fixed during a cycle. Without acquisition, steering continues at one fifth speed around the retained center (initially world zero). Paralysis pauses movement and its timer. Nearby-target reset rules use the original period calculation.");
            Note("Swirl reports InProgress after steering and does not complete a cycle. Prioritize keeps it selected until native status changes; a paralyzed tick reports NotStarted. Entry resets its per-actor center and timer.");
        }
        else
        {
            var settings = adapter.ReadJumpOffset(document, node);
            if (settings != null && settings.MinX <= settings.MaxX && settings.MinY <= settings.MaxY)
            {
                // Normalize in double precision; even the complete Int32 span must stay finite.
                var minX = Math.Min(0, settings.MinX); var maxX = Math.Max(0, settings.LastX);
                var minY = Math.Min(0, settings.MinY); var maxY = Math.Max(0, settings.LastY);
                var scale = Math.Min(200 / Math.Max(1, (double)maxX - minX), 110 / Math.Max(1, (double)maxY - minY));
                double X(double value) => 25 + (value - minX) * scale;
                double Y(double value) => 18 + (value - minY) * scale;
                var box = new Rectangle { Width = Math.Max(2, ((double)settings.LastX - settings.MinX) * scale), Height = Math.Max(2, ((double)settings.LastY - settings.MinY) * scale), Stroke = EncounterViewport.Brush("#9FD6BB"), StrokeThickness = 1 };
                Canvas.SetLeft(box, X(settings.MinX)); Canvas.SetTop(box, Y(settings.MinY)); canvas.Children.Add(box);
                if (settings.Columns <= 15 && settings.Rows <= 10)
                    for (long x = settings.MinX; x <= settings.LastX; x++) for (long y = settings.MinY; y <= settings.LastY; y++)
                    {
                        var dot = new Ellipse { Width = 5, Height = 5, Fill = EncounterViewport.Brush("#9FD6BB") }; Canvas.SetLeft(dot, X(x) - 2.5); Canvas.SetTop(dot, Y(y) - 2.5); canvas.Children.Add(dot);
                    }
                canvas.Children.Insert(0, new Line { X1 = X(0), Y1 = Y(0), X2 = X(((double)settings.MinX + settings.LastX) / 2), Y2 = Y(((double)settings.MinY + settings.LastY) / 2), Stroke = EncounterViewport.Brush("#62746B"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } });
                Host(X(0), Y(0)); body.Children.Add(canvas);
                Note($"X: {settings.MinX} … {settings.LastX} · Y: {settings.MinY} … {settings.LastY}\nInteger landing offsets from the entry position. Maximum inputs are exclusive; equal bounds give one fixed offset.");
            }
            else Note("A source expression or invalid range prevents a landing preview. The original compiler/runtime remains authoritative.");
            Note("Teleports once on entry using native Move. It bypasses movement collision validation and still jumps while paralyzed. Ordinary ticks do nothing. Re-entry applies another offset from the current position; it does not return to the spawn point.");
            Note("The original cycle status stays NotStarted. A Prioritize wrapper enters all children (so this jump still happens), then can fall through to another child on its first tick.");
        }
        randomMovePanel = new() { Header = node.Kind == "Swirl" ? "Swirl movement" : "Entry jump offsets", Content = body, Margin = new(0, 10, 0, 6) }; Inspector.Children.Add(randomMovePanel);
    }
}
