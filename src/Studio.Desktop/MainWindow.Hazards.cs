using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? hazardPanel;ComboBox? hazardLayout;CheckBox? hazardOutward;
    void AddHazardControls(BehaviorNode node)
    {
        hazardPanel=null;hazardLayout=null;hazardOutward=null;if(!adapter.IsDelayedHazard(document,node))return;
        var pattern=adapter.ReadHazardPattern(document,node);var revision=document.Revision;var body=new StackPanel();
        body.Children.Add(new TextBlock{Text="Layout",FontSize=11,Margin=new(0,8,0,5)});
        hazardLayout=new(){ItemsSource=new[]{"Trail","Lattice"},SelectedIndex=pattern==null?-1:pattern.Layout=="Trail"?0:1,IsEnabled=RegionEditable(node)};
        var layout=hazardLayout;layout.SelectionChanged+=(_,_)=>Try(()=>{if(layout.SelectedItem is string chosen)CommitHazardParameter(node.Id,revision,"layout","global::TKR.WorldServer.logic.behaviors.HazardLayout."+chosen);});body.Children.Add(layout);
        hazardOutward=new(){Content="Lattice starts outward",IsChecked=pattern?.OutwardFirst,IsThreeState=pattern==null,IsEnabled=RegionEditable(node)&&pattern?.Layout=="Lattice",Margin=new(0,10,0,0)};
        var outward=hazardOutward;outward.Click+=(_,_)=>Try(()=>{if(outward.IsChecked is bool first)CommitHazardParameter(node.Id,revision,"outwardFirst",first?"true":"false");});body.Children.Add(outward);
        if(pattern!=null&&adapter.HasKnownProjectileOwner(document,selectedEntity!.Id,node))
        {
            var projectile=adapter.HazardProjectiles(selectedEntity.Id).SingleOrDefault(p=>p.Index==pattern.Projectile);
            var diagram=new Canvas{Width=230,Height=160,ClipToBounds=true,Margin=new(0,10,0,8),Background=EncounterViewport.Brush("#101516")};body.Children.Add(diagram);
            const double cx=115,cy=80,scale=11;
            var marker=new Image{Source=sprites.Get(selectedEntity.Id),Width=28,Height=28,Stretch=Stretch.Uniform};Canvas.SetLeft(marker,cx-14);Canvas.SetTop(marker,cy-20);diagram.Children.Add(marker);
            var sites=pattern.Layout=="Lattice"?new[]{(-5d,-5d),(-5d,5d),(5d,-5d),(5d,5d)}:new[]{(-3d,0d)};
            foreach(var (x,y) in sites)
            {
                var point=new Point(cx+x*scale,cy+y*scale);var cue=new Ellipse{Width=.65*scale*2,Height=.65*scale*2,Stroke=EncounterViewport.Brush("#DCC793"),StrokeThickness=1};Canvas.SetLeft(cue,point.X-.65*scale);Canvas.SetTop(cue,point.Y-.65*scale);diagram.Children.Add(cue);
                var heading=pattern.Layout=="Lattice"?Math.Atan2(y,x)+(pattern.OutwardFirst?0:Math.PI):Math.PI;var count=pattern.Layout=="Lattice"?2:3;
                for(var i=0;i<count;i++){var angle=heading+(i-(count-1)/2d)*Math.PI/9;diagram.Children.Add(new Line{X1=point.X,Y1=point.Y,X2=point.X+Math.Cos(angle)*22,Y2=point.Y+Math.Sin(angle)*22,Stroke=EncounterViewport.Brush("#637A70"),StrokeThickness=1});}
            }
            body.Children.Add(new TextBlock{Text=pattern.Layout=="Lattice"?"Fixed sites at ±5 tiles from original spawn. First volley shown; later volleys alternate.":"Trail prototype: three rays face opposite the movement since the last attempt. Actual origins follow the moving host.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#979DA7")});
            body.Children.Add(new TextBlock{Text=string.Create(CultureInfo.InvariantCulture,$"First attempt after {pattern.NativeStartupMs} ms · {pattern.NativeIntervalMs} ms between attempts\n")+(pattern.VolleyLimit==int.MaxValue?"No practical attempt limit":pattern.VolleyLimit+" unstunned attempts")+(projectile==null?"\nChoose compatible dormant XML.":string.Create(CultureInfo.InvariantCulture,$"\nXML acceleration after {projectile.AccelerationDelayMs:0.###} ms\nArms after {projectile.ArmingDelayMs:0.###} ms · expires at {projectile.LifetimeMs:0.###} ms")),TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#A5B2AC"),Margin=new(0,10,0,0)});
        }
        else body.Children.Add(new TextBlock{Text="Source expressions define this layout or its owner. Play executes the native controller; known inputs show its timing and fixed sites here.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,10,0,0)});
        body.Children.Add(new TextBlock{Text="Warning circles mark arming cues, not collision areas. Native arena fit, 14-tile host distance, stun and visibility can suppress packets.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#83958B"),Margin=new(0,10,0,6)});
        hazardPanel=new(){Header="Hazard layout & arming",Content=body,Margin=new(0,10,0,6)};Inspector.Children.Add(hazardPanel);
    }
    void CommitHazardParameter(Guid nodeId,long revision,string parameter,string expression)
    {
        var node=document.Find(nodeId);if(!adapter.IsDelayedHazard(document,node)||parameter is not ("layout" or "outwardFirst"))throw new ArgumentException("Choose a native hazard layout input.");
        document.Apply(new(revision,"Adjust hazard "+parameter,[new(nodeId,parameter,expression)]),ValidateAuthoring);Changed(nodeId);
    }
}
