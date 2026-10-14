using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? strikePanel;ComboBox? strikeLayout;TextBlock? strikeSummary;
    bool HasStrikeControls(string name)=>strikeLayout!=null&&name=="layout";
    void AddSkyStrikeControls(BehaviorNode node)
    {
        strikePanel=null;strikeLayout=null;strikeSummary=null;if(!adapter.IsSkyStrike(document,node))return;var pattern=adapter.ReadSkyStrike(document,node);var body=new StackPanel();var revision=document.Revision;
        strikeLayout=new ComboBox{ItemsSource=new[]{"Targets","Grid","Chain","Halo"},SelectedItem=pattern?.Layout,IsEnabled=RegionEditable(node),Margin=new(0,6,0,8)};body.Children.Add(strikeLayout);
        strikeLayout.SelectionChanged+=(_,_)=>Try(()=>{if(strikeLayout?.SelectedItem is not string layout||adapter.ReadSkyStrike(document,node)?.Layout==layout)return;adapter.SetSkyStrikeLayout(document,selectedEntity!.Id,node.Id,revision,layout);Changed(node.Id);strikePanel!.IsExpanded=true;});
        if(pattern!=null)
        {
            var canvas=new Canvas{Width=250,Height=150,ClipToBounds=true,Background=EncounterViewport.Brush("#141818")};body.Children.Add(canvas);var scale=60/Math.Max(1,pattern["maxRange"]);
            var ring=new Ellipse{Width=pattern["maxRange"]*scale*2,Height=pattern["maxRange"]*scale*2,Stroke=EncounterViewport.Brush("#53615A"),StrokeThickness=1,StrokeDashArray=new DoubleCollection{3,3}};Canvas.SetLeft(ring,125-ring.Width/2);Canvas.SetTop(ring,75-ring.Height/2);canvas.Children.Add(ring);
            try
            {
                foreach(var point in RapleAdapter.SkyStrikeLayout(pattern).Take((int)pattern["totalCap"]))
                {
                    var radius=pattern["aoeRadius"]>0?pattern["aoeRadius"]:pattern["markRadius"];var size=Math.Clamp(radius*scale*2,4,100);var marker=new Ellipse{Width=size,Height=size,Stroke=EncounterViewport.Brush("#E79986"),StrokeThickness=1,ToolTip=$"({point.X:0.##}, {point.Y:0.##}) · fuse + {point.DelayMs} ms"};Canvas.SetLeft(marker,125+point.X*scale-size/2);Canvas.SetTop(marker,75+point.Y*scale-size/2);canvas.Children.Add(marker);
                }
            }catch(ArgumentException error){body.Children.Add(new TextBlock{Text=error.Message,TextWrapping=TextWrapping.Wrap,FontSize=11});}
            var host=new Image{Source=sprites.Get(selectedEntity!.Id),Width=18,Height=18};Canvas.SetLeft(host,116);Canvas.SetTop(host,66);canvas.Children.Add(host);
            var target=new Ellipse{Width=5,Height=5,Fill=EncounterViewport.Brush("#9FD6BB")};Canvas.SetLeft(target,125+6*scale-2.5);Canvas.SetTop(target,72.5);canvas.Children.Add(target);
            strikeSummary=new TextBlock{Text=(pattern["aoeRadius"]>0?"Native area impact · "+pattern["aoeRadius"]+" tile radius · "+pattern["aoeDamage"]+" damage before native scaling/defense":"XML core projectile impact")+(pattern["coreIndex"]>=0?" · core #"+pattern["coreIndex"]:"")+"\nFuse "+pattern["fuseMs"]+" ms · stagger "+pattern["stepMs"]+" ms · cap "+pattern["totalCap"],TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,10,0,8)};body.Children.Add(strikeSummary);
        }
        body.Children.Add(new TextBlock{Text="Opening layout for one target six tiles from the host. Native Play applies current player tracking, passability, range, pending-mark caps and later volley rotation. Area hits use current positions at impact; the ring matches the area radius. Player teardown remains the prototype death boundary.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#939F98"),Margin=new(0,8,0,8)});
        strikePanel=new Expander{Header="Sky-strike layout",Content=body,Margin=new(0,10,0,6)};Inspector.Children.Add(strikePanel);
    }
    void UpdateStrikeControls(){if(strikeLayout!=null&&selectedNode!=null)strikeLayout.IsEnabled=RegionEditable(selectedNode);}
}
