using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? snarePanel;ComboBox? snareAnchor,snareGap;Button? snareEditorButton;bool snareEditing;
    void ClearSnareEditor(){snareEditing=false;Viewport.Snare=null;if(snareEditorButton!=null)snareEditorButton.Content="Edit snare layout";}
    void RefreshSnareSelection()
    {
        if(!snareEditing)return;
        if(selectedNode==null||adapter.ReadSnare(document,selectedNode) is not{} pattern||!adapter.HasKnownProjectileOwner(document,selectedEntity!.Id,selectedNode)||adapter.SnareProjectiles(selectedEntity.Id,pattern.Radius).SingleOrDefault(p=>p.Index==pattern.Projectile) is not{} projectile){ClearSnareEditor();return;}
        Viewport.Snare=new(selectedNode.Id,document.Revision,pattern,projectile.LifetimeMs*projectile.Speed/10000,RegionEditable(selectedNode));
    }
    void ApplySnareEdit(SnareSelection selection,IReadOnlyDictionary<string,double> values){Try(()=>CommitSnareEdit(selection,values));RefreshSnareSelection();}
    void CommitSnareEdit(SnareSelection selection,IReadOnlyDictionary<string,double> values)
    {
        var node=document.Find(selection.NodeId);var pattern=adapter.ReadSnare(document,node);
        if(pattern==null||!adapter.HasKnownProjectileOwner(document,selectedEntity!.Id,node)||values.Count==0||values.Keys.Any(k=>k is not("radius" or "count" or "gapSlots")&&k!=(pattern.Echo?"clampRange":"targetRange")&&(k!="gapAngle"||pattern.Echo||pattern.Gap is not("Fixed" or "Rotating")))||values.Values.Any(v=>!double.IsFinite(v)))throw new ArgumentException("Choose the native snare radius, clamp, slots or fixed/rotating opening.");
        document.Apply(new(selection.Revision,"Adjust snare layout",values.Select(p=>new EditOperation(node.Id,p.Key,p.Value.ToString("R",CultureInfo.InvariantCulture))).ToArray()),ValidateAuthoring);Changed(node.Id);
    }
    bool SnareOptionVisible(string name)=>name=="anchor"&&snareAnchor!=null||name=="gap"&&snareGap!=null;
    void AddSnareControls(BehaviorNode node)
    {
        snarePanel=null;snareAnchor=null;snareGap=null;snareEditorButton=null;if(!adapter.IsSnare(document,node))return;
        var pattern=adapter.ReadSnare(document,node);var revision=document.Revision;var body=new StackPanel();
        if(pattern!=null&&adapter.HasKnownProjectileOwner(document,selectedEntity!.Id,node)&&adapter.SnareProjectiles(selectedEntity.Id,pattern.Radius).Any(p=>p.Index==pattern.Projectile))
        {
            snareEditorButton=new(){Content=snareEditing?"Close snare editor":"Edit snare layout",Style=(Style)FindResource("QuietButton"),IsEnabled=RegionEditable(node),Margin=new(0,10,0,0)};
            snareEditorButton.Click+=(_,_)=>{if(snareEditing){ClearSnareEditor();return;}ClearViewportEditors();Stop(this,new());snareEditing=true;RefreshSnareSelection();snareEditorButton!.Content="Close snare editor";WorkspaceTabs.SelectedIndex=0;UpdateLayout();Viewport.FitSnare();Viewport.Focus();};Inspector.Children.Add(snareEditorButton);
        }
        if(pattern is{Echo:false})
        {
            ComboBox Choice(string title,string name,string type,string[] choices,string selected)
            {
                body.Children.Add(new TextBlock{Text=title,FontSize=11,Margin=new(0,8,0,4)});var control=new ComboBox{ItemsSource=choices,SelectedItem=selected,IsEnabled=RegionEditable(node)};
                control.SelectionChanged+=(_,_)=>Try(()=>{if(control.SelectedItem is string value)CommitSnareOption(node.Id,revision,name,"global::TKR.WorldServer.logic.behaviors."+type+"."+value);});body.Children.Add(control);return control;
            }
            snareAnchor=Choice("Anchor","anchor","SnareAnchor",["Target","Host","Spawn"],pattern.Anchor);snareGap=Choice("Opening","gap","SnareGap",["AwayFromHost","TowardHost","Fixed","Rotating"],pattern.Gap);
        }
        if(pattern!=null)
        {
            var diagram=new Canvas{Width=230,Height=175,ClipToBounds=true,Background=EncounterViewport.Brush("#101516"),Margin=new(0,10,0,8)};var offset=pattern.Anchor is "Target" or "History"?Math.Min(pattern.TargetRange,3):0;var scale=Math.Min(68/pattern.Radius,105/(pattern.Radius+offset));var ax=115d;var ay=88d;var hx=ax-offset*scale;
            var gap=pattern.GapAngle*Math.PI/180;if(pattern.Gap is "AwayFromHost" or "Travel")gap=0;else if(pattern.Gap=="TowardHost")gap=Math.PI;
            var ring=new Ellipse{Width=pattern.Radius*scale*2,Height=pattern.Radius*scale*2,Stroke=EncounterViewport.Brush("#3F554B"),StrokeThickness=1};Canvas.SetLeft(ring,ax-pattern.Radius*scale);Canvas.SetTop(ring,ay-pattern.Radius*scale);diagram.Children.Add(ring);
            foreach(var (x,y,heading) in SnarePattern.Origins(0,0,gap,pattern.Count,pattern.GapSlots,pattern.Radius))
            {
                var px=ax+x*scale;var py=ay+y*scale;var dot=new Ellipse{Width=4,Height=4,Fill=EncounterViewport.Brush("#AFC7BA")};Canvas.SetLeft(dot,px-2);Canvas.SetTop(dot,py-2);diagram.Children.Add(dot);diagram.Children.Add(new Line{X1=px,Y1=py,X2=px+Math.Cos(heading)*13,Y2=py+Math.Sin(heading)*13,Stroke=EncounterViewport.Brush("#718D7D"),StrokeThickness=1});
            }
            var marker=new Image{Source=sprites.Get(selectedEntity!.Id),Width=28,Height=28,Stretch=Stretch.Uniform};Canvas.SetLeft(marker,hx-14);Canvas.SetTop(marker,ay-20);diagram.Children.Add(marker);
            var center=new Ellipse{Width=5,Height=5,Fill=EncounterViewport.Brush("#DCC793")};Canvas.SetLeft(center,ax-2.5);Canvas.SetTop(center,ay-2.5);diagram.Children.Add(center);body.Children.Add(diagram);
            var projectile=adapter.SnareProjectiles(selectedEntity.Id,pattern.Radius).SingleOrDefault(p=>p.Index==pattern.Projectile);
            body.Children.Add(new TextBlock{Text=(pattern.Count-pattern.GapSlots)+" inward origins · "+pattern.GapSlots+" omitted slots\n"+(pattern.Echo?"History anchor · "+pattern.LagMs+" ms lag":pattern.Anchor+" anchor · "+pattern.Gap+" opening")+(projectile==null?"\nChoose compatible straight XML.":string.Create(CultureInfo.InvariantCulture,$"\nXML travel time {projectile.ArrivalMs(pattern.Radius):0.###} ms · lifetime {projectile.LifetimeMs:0.###} ms")),TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#A5B2AC")});
        }
        else body.Children.Add(new TextBlock{Text="Source expressions define this snare. Known constants show its layout; factories and getters are never executed by the inspector.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,10,0,0)});
        body.Children.Add(new TextBlock{Text=node.Kind=="EchoSnare"?"Prototype ring and travel opening shown. Native Play observes the player's path, clamps its past position and faces the opening toward current travel. Stationary targets use the host bearing or fallback angle. Marks clear on cleanup; projectile clears retain player history.":"Prototype opening layout shown. Native Play selects the anchor and opening each attempt. Map passability and host visibility suppress individual origins; native warning markers do not define hitboxes.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#83958B"),Margin=new(0,10,0,6)});
        snarePanel=new(){Header="Snare layout",Content=body,Margin=new(0,10,0,6)};Inspector.Children.Add(snarePanel);
    }
    void CommitSnareOption(Guid nodeId,long revision,string name,string expression)
    {
        var node=document.Find(nodeId);if(!adapter.IsSnare(document,node)||node.Kind!="ConvergingSnare"||name is not("anchor" or "gap"))throw new ArgumentException("Choose a native snare anchor or opening.");
        document.Apply(new(revision,"Adjust snare "+name,[new(nodeId,name,expression)]),ValidateAuthoring);Changed(nodeId);
    }
    void UpdateSnareControls(){if(selectedNode==null)return;foreach(var option in new[]{snareAnchor,snareGap})if(option!=null)option.IsEnabled=RegionEditable(selectedNode);if(snareEditorButton!=null)snareEditorButton.IsEnabled=RegionEditable(selectedNode);RefreshSnareSelection();}
}
