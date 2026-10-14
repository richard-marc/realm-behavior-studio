using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? dashPanel;
    Button? addDashFlankButton;
    void AddDashControls(BehaviorNode node)
    {
        dashPanel = null; addDashFlankButton = null;
        var references = adapter.DashReferences(document, node);
        if (references.Count > 0)
        {
            var choice = new ComboBox { ItemsSource = references, SelectedIndex = 0, ToolTip = "Actual Dash values already used by this phase" };
            var revision = document.Revision; var expectedDocument = document; var entityId = selectedEntity!.Id;
            addDashFlankButton = new() { Content = "Add flank volley", Style = (Style)FindResource("QuietButton"), IsEnabled = RegionEditable(node) && EnemyProjectiles.ForEntity(selectedEntity!.Id).Count > 0, Margin = new(0, 6, 0, 0) };
            addDashFlankButton.Click += (_, _) => Try(() =>
            {
                if (document != expectedDocument || document.Revision != revision || selectedEntity?.Id != entityId) throw new ArgumentException("The phase changed. Select it again before adding the volley.");
                var expression = "new " + adapter.CreationTypeName("DashFlankVolley") + "(" + (string)choice.SelectedItem + ", " + EnemyProjectiles.ForEntity(selectedEntity!.Id).First().Index + ")";
                document.AddChild(node.Id, expression, ValidateAuthoring); Changed(document.Find(node.Id).Children.Last().Id);
            });
            var body = new StackPanel(); body.Children.Add(choice); body.Children.Add(addDashFlankButton);
            Inspector.Children.Add(new Expander { Header = "Shared dash attack", Content = body, Margin = new(0, 10, 0, 6) });
        }
        if (!adapter.IsDashController(document, node)) return;
        var panel = new StackPanel(); string summary;
        if (node.Kind == "Dash")
        {
            var dash = adapter.ReadDash(document, node);
            summary = dash == null ? "Dash parameters come from source. Play executes their original values." :
                $"{dash.Speed:0.###} native speed · {dash.Distance:0.###} tiles\n{dash.TargetRange:0.###} tile target range · {dash.InitialDelayMs} ms initial delay\n" +
                (dash.CooldownAfterEndMs >= 0 ? $"{dash.CooldownAfterEndMs} ms cooldown after ending" : $"{dash.IntervalMs} ms start-to-start cadence") + (dash.FullDistance ? "\nFull-distance heading lock" : "\nStops at the original target position or distance limit");
            panel.Children.Add(new TextBlock { Text = "Locks the nearest visible target heading when a dash starts. Walls can end movement early; ordinary child movement runs between dashes. Paralysis/Stasis cancels the dash and advances scheduling. Entry/exit forward to the child movement. Ability-triggered dodge needs actual offensive-ability events.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new(0, 8, 0, 0), Foreground = EncounterViewport.Brush("#979DA7") });
        }
        else
        {
            var flank = adapter.ReadDashFlank(document, node);
            summary = flank == null ? "Fan geometry comes from source. Play uses the original Dash relationship." : $"{flank.CountPerSide} shots per side · {flank.SpreadDegrees:0.###}° spacing\n±{flank.SideDegrees:0.###}° from locked heading · {flank.CooldownMs} ms cooldown";
            if (flank != null)
            {
                try
                {
                    var fans = RapleAdapter.DashFans(flank, 0);
                    var canvas = new Canvas { Width = 250, Height = 132, ClipToBounds = true, Background = EncounterViewport.Brush("#141818"), Margin = new(0, 10, 0, 6), ToolTip = "Native fan geometry at a 0° dash heading; rays show directions, not projectile travel distance" };
                    canvas.Children.Add(new Line { X1 = 45, Y1 = 66, X2 = 215, Y2 = 66, Stroke = EncounterViewport.Brush("#62746B"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } });
                    foreach (var first in new[] { fans.Left, fans.Right }) for (var i = 0; i < flank.CountPerSide; i++)
                    {
                        var angle = first + i * fans.Increment;
                        if (!double.IsFinite(angle)) continue;
                        canvas.Children.Add(new Line { X1 = 125, Y1 = 66, X2 = 125 + 52 * Math.Cos(angle), Y2 = 66 + 52 * Math.Sin(angle), Stroke = EncounterViewport.Brush("#9FD6BB"), StrokeThickness = 1 });
                    }
                    var host = new Image { Source = sprites.Get(selectedEntity!.Id), Width = 22, Height = 22 };
                    RenderOptions.SetBitmapScalingMode(host, BitmapScalingMode.NearestNeighbor); Canvas.SetLeft(host, 114); Canvas.SetTop(host, 55); canvas.Children.Add(host); panel.Children.Add(canvas);
                    panel.Children.Add(new TextBlock { Text = "Heading 0° example · actual direction and origin follow native movement", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = EncounterViewport.Brush("#979DA7") });
                }
                catch (ArgumentException) { panel.Children.Add(new TextBlock { Text = "Native angle values exceed the finite diagram range.", TextWrapping = TextWrapping.Wrap, FontSize = 11 }); }
            }
            panel.Children.Add(new TextBlock { Text = "Uses the same Dash object executed by this phase. Source order determines which movement tick it observes. Native cooldown scaling applies. If the first moving tick is stunned, the original timer boundary can keep that dash silent; a later dash resets detection. This action does not drive movement.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new(0, 8, 0, 0), Foreground = EncounterViewport.Brush("#979DA7") });
        }
        panel.Children.Insert(0, new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        dashPanel = new() { Header = node.Kind == "Dash" ? "Dash movement" : "Flank volley", Content = panel, Margin = new(0, 10, 0, 6) }; Inspector.Children.Add(dashPanel);
    }
}
