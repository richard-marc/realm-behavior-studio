using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? trailPanel;Slider? recountAge;TextBlock? trailSummary;Button? trailAim;
    void AddTrailLedgerControls(BehaviorNode node)
    {
        trailPanel=null;recountAge=null;trailSummary=null;trailAim=null;if(!adapter.IsTrailLedger(document,node))return;var settings=adapter.ReadTrailLedger(document,node);var body=new StackPanel();var revision=document.Revision;
        if(settings!=null)
        {
            var canvas=new Canvas{Width=250,Height=140,ClipToBounds=true,Background=EncounterViewport.Brush("#141818")};body.Children.Add(canvas);
            void Dot(double x,double y,string color,double size=6){var dot=new Ellipse{Width=size,Height=size,Fill=EncounterViewport.Brush(color)};Canvas.SetLeft(dot,125+x*10-size/2);Canvas.SetTop(dot,70+y*10-size/2);canvas.Children.Add(dot);}
            var host=new Image{Source=sprites.Get(selectedEntity!.Id),Width=18,Height=18};Canvas.SetLeft(host,116);Canvas.SetTop(host,61);canvas.Children.Add(host);
            if(settings.Kind=="TrailLedger")
            {
                if(settings["boundsRadius"]>0){var radius=settings["boundsRadius"]*10;var circle=new Ellipse{Width=radius*2,Height=radius*2,Stroke=EncounterViewport.Brush("#637066"),StrokeThickness=1,StrokeDashArray=new DoubleCollection{3,3}};Canvas.SetLeft(circle,125-radius);Canvas.SetTop(circle,70-radius);canvas.Children.Add(circle);}
                Dot(6,0,"#CB99E8");var outside=RapleAdapter.OutsideLedgerBounds(6,0,0,0,settings["boundsRadius"]);trailSummary=new(){Text="Sample target six tiles from spawn · "+(outside?"penalty fuse "+settings["penaltyFuseMs"]:"normal fuse "+settings["fuseMs"])+" ms\nMerge radius "+settings["mergeRadius"]+" · per-player cap "+settings["perPlayerCap"]+" · total cap "+settings["totalCap"],TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,8,0,8)};body.Children.Add(trailSummary);
                trailAim=new Button{Content=settings.Flags["interestAimed"]?"Interest fan: aimed":"Interest ring: uniform",Style=(Style)FindResource("QuietButton"),IsEnabled=RegionEditable(node),Margin=new(0,4,0,8)};trailAim.Click+=(_,_)=>Try(()=>{document.Apply(new(revision,"Set native interest geometry",[new(node.Id,"interestAimed",settings.Flags["interestAimed"]?"false":"true")]),ValidateAuthoring);Changed(node.Id);trailPanel!.IsExpanded=true;});body.Children.Add(trailAim);
                body.Children.Add(new TextBlock{Text="Stamps current tracked positions. Merge checks live marks owned by the same player. The bounds preview shows the penalty rule; a native bug suppresses the boundary animation, while the penalty fuse still applies.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#939F98"),Margin=new(0,6,0,8)});
            }
            else
            {
                try
                {
                    var window=(int)settings["windowMs"];var path=new List<(long T,float X,float Y)>{(0,2,-2),(window/3,4,-2),(window*2L/3,4,2),(window,7,2)};var points=RapleAdapter.RecountLayout(settings,path,window);var line=new Polyline{Stroke=EncounterViewport.Brush("#52665A"),StrokeThickness=1};foreach(var p in path)line.Points.Add(new(125+p.X*10,70+p.Y*10));canvas.Children.Add(line);
                    trailSummary=new(){Text="Native resample of an illustrative path · "+points.Count+" points\nSpacing widens to retain the whole path under the per-player cap. Fuses rise from oldest to newest history.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,8,0,8)};body.Children.Add(trailSummary);recountAge=new(){Minimum=0,Maximum=settings["maxFuseMs"],Value=settings["minFuseMs"]};body.Children.Add(recountAge);
                    var dots=new List<(Ellipse Dot,long Fuse)>();foreach(var p in points){var dot=new Ellipse{Width=6,Height=6,ToolTip=$"Recorded {p.RecordedMs} ms · fuse {p.FuseMs} ms"};Canvas.SetLeft(dot,125+p.X*10-3);Canvas.SetTop(dot,70+p.Y*10-3);canvas.Children.Add(dot);dots.Add((dot,p.FuseMs));}void Draw(){foreach(var (dot,fuse) in dots)dot.Fill=EncounterViewport.Brush(recountAge.Value>=fuse?"#E8A481":"#9ED2B6");}recountAge.ValueChanged+=(_,_)=>Draw();Draw();
                }catch(ArgumentException error){body.Children.Add(new TextBlock{Text=error.Message,TextWrapping=TextWrapping.Wrap,FontSize=11});}
            }
        }
        body.Children.Add(new TextBlock{Text="Native Play supplies actual histories, passable origins, caps, live-host range and delayed XML bursts. Pending marks clear on phase exit, death, encounter removal and projectile generation changes. A recount retains its historical path; this illustration does not predict player movement.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#939F98"),Margin=new(0,8,0,8)});trailPanel=new(){Header=node.Kind=="TrailLedger"?"Native trail ledger":"Native movement recount",Content=body,Margin=new(0,10,0,6)};Inspector.Children.Add(trailPanel);
    }
    void UpdateTrailControls(){if(trailAim!=null&&selectedNode!=null)trailAim.IsEnabled=RegionEditable(selectedNode);}
}
