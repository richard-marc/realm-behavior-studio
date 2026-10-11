using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? ownedEmitterPanel;
    ComboBox? ownedLaneAxes;
    TextBlock? ownedEmitterInfo;
    void AddOwnedEmitterControls(BehaviorNode node)
    {
        ownedEmitterPanel=null;ownedLaneAxes=null;ownedEmitterInfo=null;
        if(!adapter.IsOwnedLaneEmitter(document,node))return;
        var body=new StackPanel();var resource=adapter.ReadOwnedEmitterResource(document,node);var revision=document.Revision;
        ownedEmitterInfo=new TextBlock{Text=resource==null?"The emitter's entity or required XML projectile #0 comes from unresolved source.":resource.EntityId+"\nXML projectile #0 · "+resource.Projectile.Summary+(resource.Projectile.Traits.Length==0?"":"\n"+resource.Projectile.Traits)+(resource.Projectile.PreviewLimitation==null?"":"\n"+resource.Projectile.PreviewLimitation),TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#929D96"),Margin=new(0,8,0,8)};
        if(resource!=null)body.Children.Add(new Image{Source=sprites.Get(resource.Projectile.TextureId),Width=32,Height=32,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,8,0,0)});
        body.Children.Add(ownedEmitterInfo);body.Children.Add(new TextBlock{Text="Lane axes",Margin=new(0,6,0,6)});
        ownedLaneAxes=new ComboBox{ItemsSource=adapter.LaneAxes(document,node),DisplayMemberPath="Name",IsEnabled=RegionEditable(node),ToolTip="Native helper arrangement. The source supplies launch positions; the projectile index is fixed at 0."};
        ownedLaneAxes.SelectedItem=ownedLaneAxes.Items.Cast<LaneAxisChoice>().FirstOrDefault(c=>c.Name==adapter.ReadLaneAxis(document,node));body.Children.Add(ownedLaneAxes);
        var axes=ownedLaneAxes;axes.SelectionChanged+=(_,_)=>Try(()=>{if(axes.SelectedItem is not LaneAxisChoice choice)return;adapter.ApplyLaneAxis(document,selectedEntity!.Id,node.Id,revision,choice.Name);Changed(node.Id);});
        body.Children.Add(new TextBlock{Text="Up to two helper enemies fire their own XML projectile #0. Choose a helper with a flat starting state; Play checks its loaded behavior. The game cleans them up on phase exit, owner death or removal. Launch points and warning effects come from the native controller.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#929D96"),Margin=new(0,10,0,0)});
        ownedEmitterPanel=new Expander{Header="Lane emission",Content=body,Margin=new(0,10,0,0)};Inspector.Children.Add(ownedEmitterPanel);
    }
}
