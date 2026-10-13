using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? sequentialSweepPanel;
    void AddSequentialSweepControls(BehaviorNode node)
    {
        sequentialSweepPanel=null;if(!adapter.IsSequentialSweep(document,node))return;
        var pattern=adapter.ReadSequentialSweep(document,node);var panel=new StackPanel();
        panel.Children.Add(new TextBlock{Text=pattern==null?"The burst uses source inputs. Edit its expressions below; Play executes the native controller.":$"{pattern.Count} projectiles · {pattern.Volleys} volleys\nVolley sizes: {string.Join(" · ",pattern.VolleyCounts)}\nNative firing interval: {pattern.NativeIntervalMs} ms",TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,8)});
        panel.Children.Add(new TextBlock{Text="Activates when a player enters trigger range, then tracks that player's current position. One shot traces the sweep; larger volleys alternate full and inset fans. The last firing interval plus recovery finishes before reacquisition. Native ticks emit at most one volley each. Stun, target validity and phase changes can interrupt this sequence.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#979DA7")});
        sequentialSweepPanel=new(){Header="Burst sequence",Content=panel,Margin=new(0,10,0,6)};Inspector.Children.Add(sequentialSweepPanel);
    }
}
