using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    sealed record TextureChoice(int Index,string Title,BitmapSource? Image);
    ComboBox? textureChoices;
    Button? useTextureButton;
    void AddTransformationControls(BehaviorNode node)
    {
        textureChoices=null;useTextureButton=null;
        if(node.Kind is "Transform" or "TransformOnDeath")Inspector.Children.Add(new TextBlock{Text=node.Kind=="Transform"?"Replaces this actor through the game's entity resolver. The replacement runs its own behavior and appears in Actors. Original-actor health controls stop when this actor leaves the world.":"Runs the game's replacement callback on actual death. Count and probability retain native behavior; removing an actor for a test bypasses death callbacks.",TextWrapping=TextWrapping.Wrap,Foreground=EncounterViewport.Brush("#92969F"),FontSize=11,Margin=new(0,8,0,0)});
        if(node.Kind is "SizeTransform" or "ChangeSize" or "SizeTransformComplete")
        {
            var text=node.Kind=="SizeTransform"?"Uses the native growth/shrink animation. Entry size, glow and projectile settings are restored on completion or exit.":node.Kind=="ChangeSize"?"Native size pulses persist after leaving this phase. The supplied rate and its sign control clamping.":"Completion reads the actor's state for the same SizeTransform object. Open its shared reference to edit the running transform.";
            Inspector.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Foreground=EncounterViewport.Brush("#92969F"),FontSize=11,Margin=new(0,8,0,0)});
        }
        if(node.Kind!="SetAltTexture")return;
        var items=AlternateTextures.Indices(selectedEntity!.Id).Select(index=>new TextureChoice(index,index==0?"Original texture":"Texture "+index,sprites.Get(selectedEntity.Id,altTexture:index))).ToArray();
        var list=new ComboBox{ItemsSource=items,DisplayMemberPath="Title",Margin=new(0,4,0,8),IsEnabled=RegionEditable(node)};
        var preview=new Image{Width=72,Height=72,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,8)};
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(preview,System.Windows.Media.BitmapScalingMode.NearestNeighbor);
        var minimum=node.Parameters.FirstOrDefault(p=>p.Name=="minValue");
        list.SelectedItem=items.FirstOrDefault(t=>t.Index.ToString(CultureInfo.InvariantCulture)==minimum?.Expression)??items.FirstOrDefault();
        list.SelectionChanged+=(_,_)=>preview.Source=(list.SelectedItem as TextureChoice)?.Image;preview.Source=(list.SelectedItem as TextureChoice)?.Image;
        var revision=document.Revision;var apply=new Button{Content="Use as first texture",IsEnabled=RegionEditable(node)&&minimum!=null};
        apply.Click+=(_,_)=>Try(()=>{if(list.SelectedItem is not TextureChoice selected||minimum==null)return;document.Apply(new(revision,"Change first native texture",[new(node.Id,"minValue",selected.Index.ToString(CultureInfo.InvariantCulture))]),ValidateAuthoring);Changed(node.Id);});
        var content=new StackPanel();content.Children.Add(list);content.Children.Add(preview);content.Children.Add(apply);
        content.Children.Add(new TextBlock{Text="The game advances integer IDs between the first and last texture. Undefined IDs retain the previous visible texture. Exit does not reset it.",TextWrapping=TextWrapping.Wrap,Foreground=EncounterViewport.Brush("#92969F"),FontSize=11,Margin=new(0,10,0,0)});
        Inspector.Children.Add(new Expander{Header="Textures · "+items.Length,Content=content,Margin=new(0,8,0,0)});textureChoices=list;useTextureButton=apply;
    }
}
