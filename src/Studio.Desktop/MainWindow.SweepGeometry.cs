using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Studio.Core;
namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? sweepGeometryPanel;Button? sweepGeometryApply;readonly Dictionary<string,TextBox> sweepGeometryInputs=[];
    void AddSweepGeometryControls(BehaviorNode node)
    {
        sweepGeometryPanel=null;sweepGeometryApply=null;sweepGeometryInputs.Clear();if(!adapter.IsSweepController(document,node))return;var body=new StackPanel();var location=adapter.ReadSweepGeometry(document,node);var revision=document.Revision;
        if(location==null){body.Children.Add(new TextBlock{Text="Shared geometry uses source inputs. Open Source to inspect its constructor or values; this view does not execute factories or getters.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,8,0,8)});}
        else
        {
            var geometry=location.Geometry;body.Children.Add(new TextBlock{Text=geometry.Projectiles.Count+" radial XML bands · "+geometry.Arms+" arms\nProjectiles: "+string.Join(", ",geometry.Projectiles)+"\n"+System.IO.Path.GetFileName(location.SourceFile)+" · shared constructor",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,8,0,8)});
            var diagram=new Canvas{Width=230,Height=150,ClipToBounds=true,Background=EncounterViewport.Brush("#101516")};const double cx=115,cy=75;var scale=60/Math.Max(1,geometry.OuterRadius);
            foreach(var radius in Enumerable.Range(0,geometry.Projectiles.Count+1).Select(i=>geometry.InnerRadius+i*geometry.BandSpacing)){var ring=new Ellipse{Width=radius*scale*2,Height=radius*scale*2,Stroke=EncounterViewport.Brush("#465B52"),StrokeThickness=1};Canvas.SetLeft(ring,cx-radius*scale);Canvas.SetTop(ring,cy-radius*scale);diagram.Children.Add(ring);}
            for(var i=0;i<geometry.Arms;i++){var heading=geometry.Heading(0)+i*Math.Tau/geometry.Arms;diagram.Children.Add(new Line{X1=cx+Math.Cos(heading)*geometry.InnerRadius*scale,Y1=cy+Math.Sin(heading)*geometry.InnerRadius*scale,X2=cx+Math.Cos(heading)*geometry.OuterRadius*scale,Y2=cy+Math.Sin(heading)*geometry.OuterRadius*scale,Stroke=EncounterViewport.Brush("#9DBAAA"),StrokeThickness=1.5});}
            var sprite=new Image{Source=sprites.Get(selectedEntity!.Id),Width=28,Height=28,Stretch=Stretch.Uniform};Canvas.SetLeft(sprite,cx-14);Canvas.SetTop(sprite,cy-20);diagram.Children.Add(sprite);body.Children.Add(diagram);
            body.Children.Add(new TextBlock{Text=string.Create(CultureInfo.InvariantCulture,$"XML geometry · {geometry.InnerRadius:0.###}–{geometry.OuterRadius:0.###} tiles · {geometry.Rate:0.###}°/s\nLifetime {geometry.LifetimeMs} ms · opening heading shown"),TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#939F98"),Margin=new(0,8,0,8)});
            foreach(var (name,title,value) in new[]{("armCount","Arms",geometry.Arms.ToString(CultureInfo.InvariantCulture)),("startAngle","Starting angle · degrees",geometry.Angle.ToString("R",CultureInfo.InvariantCulture)),("refreshMs","Sweep refresh · ms",geometry.RefreshMs.ToString(CultureInfo.InvariantCulture)),("startupMs","Sweep startup · ms",geometry.StartupMs.ToString(CultureInfo.InvariantCulture))})
            {body.Children.Add(new TextBlock{Text=title,FontSize=11,Margin=new(0,8,0,4)});var input=new TextBox{Text=value,IsEnabled=RegionEditable(node)};sweepGeometryInputs[name]=input;body.Children.Add(input);}
            sweepGeometryApply=new(){Content="Apply shared geometry",Style=(Style)FindResource("QuietButton"),IsEnabled=RegionEditable(node),Margin=new(0,10,0,0)};sweepGeometryApply.Click+=(_,_)=>Try(()=>CommitSweepGeometry(node.Id,revision,sweepGeometryInputs.ToDictionary(p=>p.Key,p=>p.Value.Text)));body.Children.Add(sweepGeometryApply);
            body.Children.Add(new TextBlock{Text="Edits change the native constructor for its accepted consumers. Band radii, rate and lifetime must match the owner's XML. Pulse and echo keep their own attempt timing; echo uses the sweep heading from an earlier age. Native origins stay at the actor's entry position.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#83958B"),Margin=new(0,10,0,8)});
        }
        sweepGeometryPanel=new(){Header="Shared sweep geometry",Content=body,Margin=new(0,10,0,6)};Inspector.Children.Add(sweepGeometryPanel);
    }
    void CommitSweepGeometry(Guid controller,long revision,IReadOnlyDictionary<string,string> values)
    {if(document.Revision!=revision)throw new InvalidOperationException("This geometry proposal is stale.");var plan=Studio.Raple.SourceSweepGeometryRefactor.Apply(adapter,Snapshot(),document.SourceFile!,controller,values);ApplyWorkspacePhaseEdit(plan,controller,"Adjusted sweep geometry");}
    void UpdateSweepGeometryControls(){if(selectedNode==null)return;var editable=RegionEditable(selectedNode);if(sweepGeometryApply!=null)sweepGeometryApply.IsEnabled=editable;foreach(var input in sweepGeometryInputs.Values)input.IsEnabled=editable;}
}
