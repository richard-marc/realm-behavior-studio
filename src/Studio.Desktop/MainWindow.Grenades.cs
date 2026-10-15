using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? grenadePanel;
    ComboBox? grenadeTargeting;
    void AddGrenadeControls(BehaviorNode node)
    {
        grenadePanel=null;grenadeTargeting=null;if(!adapter.IsGrenade(document,node))return;
        var body=new StackPanel();var settings=adapter.ReadGrenade(document,node);
        void Note(string text)=>body.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#A6AEA9"),Margin=new(0,8,0,6)});
        if(settings!=null)
        {
            var revision=document.Revision;var expected=document;
            grenadeTargeting=new(){ItemsSource=new[]{"Aim at player","Fixed bearing"},SelectedIndex=settings.FixedAngle==null?0:1,IsEnabled=RegionEditable(node),Margin=new(0,8,0,0)};
            var chooser=grenadeTargeting;chooser.SelectionChanged+=(_,_)=>Try(()=>
            {
                if(document!=expected||document.Revision!=revision)throw new InvalidOperationException("The grenade changed. Select it again before changing its target mode.");
                document.Apply(new(revision,"Set grenade targeting",[new(node.Id,"fixedAngle",chooser.SelectedIndex==0?"null":(settings.FixedAngle??0).ToString("R",System.Globalization.CultureInfo.InvariantCulture))]),ValidateAuthoring);Changed(node.Id);
            });body.Children.Add(chooser);
            Note($"{settings.Count} throws · {settings.Radius:0.###} tile blast radius · {settings.Damage} base damage");
            if(settings.FixedAngle!=null)
            {
                try
                {
                    var targets=settings.FixedTargets(0,0);var scale=68/Math.Max(.1,Math.Abs(settings.Range)+settings.Radius);
                    var canvas=new Canvas{Width=250,Height=180,ClipToBounds=true,Background=EncounterViewport.Brush("#141818"),Margin=new(0,8,0,4)};
                    foreach(var point in targets.Take(64))
                    {
                        var x=125+point.X*scale;var y=90+point.Y*scale;var radius=settings.Radius*scale;
                        canvas.Children.Add(new Line{X1=125,Y1=90,X2=x,Y2=y,Stroke=EncounterViewport.Brush("#586F62"),StrokeThickness=1});
                        var ring=new Ellipse{Width=radius*2,Height=radius*2,Stroke=EncounterViewport.Brush("#9FD6BB"),StrokeThickness=1};Canvas.SetLeft(ring,x-radius);Canvas.SetTop(ring,y-radius);canvas.Children.Add(ring);
                        var dot=new Ellipse{Width=4,Height=4,Fill=EncounterViewport.Brush("#9FD6BB")};Canvas.SetLeft(dot,x-2);Canvas.SetTop(dot,y-2);canvas.Children.Add(dot);
                    }
                    var host=new Image{Source=sprites.Get(selectedEntity!.Id),Width=22,Height=22};RenderOptions.SetBitmapScalingMode(host,BitmapScalingMode.NearestNeighbor);Canvas.SetLeft(host,114);Canvas.SetTop(host,79);canvas.Children.Add(host);body.Children.Add(canvas);
                    Note($"First activation · {settings.NativeSpacing:0.###}° native spacing\nLanding footprints relative to the host; later activations use the original rotation counter."+(targets.Count>64?" First 64 landing markers shown.":""));
                }
                catch(ArgumentException){Note("Native throw coordinates exceed the finite diagram range. Source values remain unchanged.");}
            }
            else Note("Each aimed throw captures the actual target position at launch. Multiple throws land at that same captured point; spacing and rotation do not spread aimed landings. An explicit AttackTarget takes priority over the nearest-player search.");
        }
        else Note("Source expressions define this grenade. The inspector does not execute getters or factories to predict its landing points.");
        Note("Native world timer: 1500 ms, then the original timer-expiry tick. Phase exit does not cancel a pending impact. Native Expunge/removal can leave the host world reference intact, so its pending impact still fires. A retired owner, whole-encounter cleanup or hostile-projectile clear invalidates the timer. Moving after launch can avoid the captured location.");
        Note("Damage and condition effects run in the original server. Impact packets appear on the timeline with their actual fields. Delayed timer emissions have an explicit unavailable source link when no native callback context is present. The rotation counter is shared by the behavior object; its cooldown is stored per actor.");
        grenadePanel=new(){Header="Grenade area attack",Content=body,Margin=new(0,10,0,6)};Inspector.Children.Add(grenadePanel);
    }
}
