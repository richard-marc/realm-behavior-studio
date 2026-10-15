using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? healingPanel;
    void AddHealingControls(BehaviorNode node)
    {
        healingPanel=null;if(adapter.ReadHealing(document,node) is not{} settings)return;
        var body=new StackPanel();
        void Note(string text)=>body.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#A6AEA9"),Margin=new(0,8,0,6)});
        Note(settings.AmountKnown?settings.Amount is{} amount?$"Adds {amount} HP to each matching enemy, capped at its maximum HP.":"Restores every matching enemy to its maximum HP.":"The healing amount is defined by source. Play evaluates it in the original game.");
        if(settings.Range is{} range)
        {
            Note($"Strictly within {range:0.###} tiles of the host. A matching host is included.");
            if(range>0)
            {
                var canvas=new Canvas{Width=250,Height=156,ClipToBounds=true,Background=EncounterViewport.Brush("#141818"),Margin=new(0,8,0,4)};
                var ring=new Ellipse{Width=128,Height=128,Stroke=EncounterViewport.Brush("#9FD6BB"),StrokeThickness=1};Canvas.SetLeft(ring,61);Canvas.SetTop(ring,14);canvas.Children.Add(ring);
                var host=new Image{Source=sprites.Get(selectedEntity!.Id),Width=24,Height=24};RenderOptions.SetBitmapScalingMode(host,BitmapScalingMode.NearestNeighbor);Canvas.SetLeft(host,113);Canvas.SetTop(host,66);canvas.Children.Add(host);body.Children.Add(canvas);
                Note("Targeting radius only. Native Play determines which enemies are present and eligible.");
            }
        }
        else Note("The range comes from source; the inspector does not run getters to predict it.");
        Note(settings.Kind=="HealGroup"?"Matches the exact XML group without case sensitivity. Null matches no group; a blank group can match blank XML groups.":"Matches XML descriptor name substrings without case sensitivity. Null matches all enemy names. A display-name override does not change the match.");
        Note("Only Enemy instances are healed. Hidden, Invisible and Invulnerable do not exclude an otherwise matching enemy. Entry resets a per-actor countdown to zero. Stun holds a ready heal; a positive countdown still decreases while stunned. After an attempt, the native cooldown resets even when no HP changed. Expiry and the next attempt occur on separate ticks.");
        healingPanel=new(){Header="Nearby healing",Content=body,Margin=new(0,10,0,6)};Inspector.Children.Add(healingPanel);
    }
}
