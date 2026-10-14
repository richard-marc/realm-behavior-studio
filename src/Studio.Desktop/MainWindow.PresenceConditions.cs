using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? presencePanel;ListBox? presenceTargetRows,presenceResourceChoices;TextBox? presenceResourceSearch;Button? presenceAdd,presenceUse,presenceRemove,presenceUp,presenceDown;
    Expander? presenceGroundPanel;ListBox? presenceGroundChoices;TextBox? presenceGroundSearch;Button? presenceGroundUse;
    sealed record GroundConditionChoice(GroundResource Resource,ImageSource? Image){public string Title=>Resource.Id;public string Summary=>Resource.Summary;}
    bool HasPresenceTargetControls(string parameter)=>presenceTargetRows!=null&&parameter.Split('[')[0]=="targets";
    void AddPresenceControls(BehaviorNode node)
    {
        presencePanel=null;presenceTargetRows=null;presenceResourceChoices=null;presenceAdd=null;presenceUse=null;presenceRemove=null;presenceUp=null;presenceDown=null;presenceGroundPanel=null;presenceGroundChoices=null;presenceGroundUse=null;
        if(!adapter.IsPresenceCondition(document,node))return;
        if(node.Kind=="OnGroundTransition")AddPresenceGroundControls(node);
        var body=new StackPanel();var revision=document.Revision;Action? loadTargets=null;
        if(node.Kind=="EntitiesNotExistsTransition")
        {
            var targets=adapter.PresenceTargets(document,node);
            if(targets==null)body.Children.Add(new TextBlock{Text="The target list uses source expressions. Edit its definition in Source; this inspector never executes a getter or factory.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,8,0,8)});
            else
            {
                presenceTargetRows=new(){ItemsSource=targets,MaxHeight=130,Margin=new(0,8,0,8)};body.Children.Add(presenceTargetRows);
                presenceResourceSearch=new(){Tag="Search XML targets",Margin=new(0,0,0,6)};body.Children.Add(presenceResourceSearch);
                presenceResourceChoices=new(){MaxHeight=180,ItemTemplate=(DataTemplate)FindResource("EntityChoiceTemplate")};body.Children.Add(presenceResourceChoices);
                var actions=new WrapPanel{Margin=new(0,8,0,0)};body.Children.Add(actions);var loaded=false;IReadOnlyList<EntityChoice> resources=[];
                void Filter(){if(!loaded)return;var previous=(presenceResourceChoices.SelectedItem as EntityChoice)?.Id??targets.FirstOrDefault();var choices=resources.Where(r=>(r.Title+" "+r.Summary).Contains(presenceResourceSearch.Text,StringComparison.OrdinalIgnoreCase)).ToArray();presenceResourceChoices.ItemsSource=choices;presenceResourceChoices.SelectedItem=choices.FirstOrDefault(r=>r.Id==previous);if(presenceResourceChoices.SelectedItem!=null)presenceResourceChoices.ScrollIntoView(presenceResourceChoices.SelectedItem);UpdatePresenceControls();}
                Button Action(string title,PhaseChoiceEdit edit)
                {
                    var button=new Button{Content=title,Style=(Style)FindResource("QuietButton"),Margin=new(0,0,4,4)};button.Click+=(_,_)=>Try(()=>{var index=presenceTargetRows!.SelectedIndex;var target=(presenceResourceChoices?.SelectedItem as EntityChoice)?.Id;adapter.EditPresenceTarget(document,selectedEntity!.Id,node.Id,revision,edit,index,target);Changed(node.Id);presencePanel!.IsExpanded=true;presenceTargetRows!.SelectedIndex=edit switch{PhaseChoiceEdit.Add=>presenceTargetRows.Items.Count-1,PhaseChoiceEdit.Up=>index-1,PhaseChoiceEdit.Down=>index+1,_=>Math.Min(index,presenceTargetRows.Items.Count-1)};});actions.Children.Add(button);return button;
                }
                presenceAdd=Action("Add",PhaseChoiceEdit.Add);presenceUse=Action("Use XML",PhaseChoiceEdit.Replace);presenceUp=Action("↑",PhaseChoiceEdit.Up);presenceDown=Action("↓",PhaseChoiceEdit.Down);presenceRemove=Action("Remove",PhaseChoiceEdit.Remove);
                presenceTargetRows.SelectionChanged+=(_,_)=>UpdatePresenceControls();presenceResourceChoices.SelectionChanged+=(_,_)=>UpdatePresenceControls();presenceResourceSearch.TextChanged+=(_,_)=>Filter();
                loadTargets=()=>{if(loaded)return;resources=EntityResources.Catalog(EntityResourceScope.Queryable).Select(r=>new EntityChoice(r.Id,r.Title,r.SpriteId,r.Summary)).ToArray();loaded=true;Filter();};
            }
        }
        var summary=node.Kind switch
        {
            "AnyEntityWithinTransition" or "NoEntityWithinTransition"=>"Strict integer-radius proximity to other enemies and players that are not Hidden. Invisible players still count. This host and static objects are excluded; ownership is not checked.",
            "NoPlayerWithinTransition"=>"Native nearest-player query with strict distance. Hidden and Invisible players are excluded. This differs from the nearby-player rule.",
            "EntityWithinTransition"=>"Case-insensitive XML IdName substring in the enemy/static collision index. A matching host counts. Null matches any descriptor name; display-name overrides do not affect it.",
            "EntitiesNotExistsTransition"=>"Every selected XML object type must be absent at strict distance. A matching host or static object counts. Empty lists never match; unknown IDs use the native Pirate fallback. There is no owned-minion filter.",
            "OnGroundTransition"=>"Reads the host's actual map cell using integer coordinates. The exact XML ground ID resolves on the first tick; a missing ID fails native lookup.",
            _=>"Captures position on the first tick, then compares exact Vector2 positions after countdown checks. Movement that returns to the same position can match. Delay is not a continuous stationary-duration timer; native execution order determines the sampled position."
        };
        body.Children.Add(new TextBlock{Text=summary,TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#939F98"),Margin=new(0,10,0,8)});
        presencePanel=new(){Header=node.Kind=="EntitiesNotExistsTransition"?"Absent XML targets":"Native condition details",Content=body,Margin=new(0,10,0,6)};presencePanel.Expanded+=(_,_)=>loadTargets?.Invoke();Inspector.Children.Add(presencePanel);UpdatePresenceControls();
    }
    Expander GroundConditionPicker(string header,Action<string> use,out ListBox list,out TextBox search,out Button button)
    {
        var body=new StackPanel();search=new(){Tag="Search ground XML",Margin=new(0,8,0,6)};body.Children.Add(search);list=new(){MaxHeight=220};var template=new DataTemplate();var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);var sprite=new FrameworkElementFactory(typeof(Image));sprite.SetValue(FrameworkElement.WidthProperty,24d);sprite.SetValue(FrameworkElement.HeightProperty,24d);sprite.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,8,0));sprite.SetBinding(Image.SourceProperty,new System.Windows.Data.Binding("Image"));row.AppendChild(sprite);var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Title"));row.AppendChild(label);template.VisualTree=row;list.ItemTemplate=template;body.Children.Add(list);button=new(){Content="Use ground",Style=(Style)FindResource("QuietButton"),Margin=new(0,8,0,0),IsEnabled=false};body.Children.Add(button);var panel=new Expander{Header=header,Content=body,Margin=new(0,10,0,6)};var localList=list;var localSearch=search;var localButton=button;var loaded=false;GroundConditionChoice[] choices=[];
        void Filter(){if(!loaded)return;var selected=(localList.SelectedItem as GroundConditionChoice)?.Resource.Id;localList.ItemsSource=choices.Where(g=>(g.Title+" "+g.Summary).Contains(localSearch.Text,StringComparison.OrdinalIgnoreCase)).ToArray();localList.SelectedItem=((GroundConditionChoice[])localList.ItemsSource).FirstOrDefault(g=>g.Resource.Id==selected);}
        panel.Expanded+=(_,_)=>{if(loaded)return;choices=NativeArena.Grounds().Select(g=>new GroundConditionChoice(g,sprites.GetGround(g.Type))).ToArray();loaded=true;Filter();};localSearch.TextChanged+=(_,_)=>Filter();localList.SelectionChanged+=(_,_)=>localButton.IsEnabled=localList.SelectedItem!=null&&(selectedNode==null||RegionEditable(selectedNode));localButton.Click+=(_,_)=>Try(()=>{if(localList.SelectedItem is GroundConditionChoice choice)use(choice.Resource.Id);});return panel;
    }
    Expander CreateGroundConditionInputPicker(TextBox input)=>GroundConditionPicker("Choose ground XML",id=>input.Text=PhaseLiteral(id),out _,out _,out _);
    void AddPresenceGroundControls(BehaviorNode node)
    {
        var revision=document.Revision;presenceGroundPanel=GroundConditionPicker("Ground condition · "+(adapter.PresenceGround(document,node)??"source value"),id=>{adapter.ApplyPresenceGround(document,selectedEntity!.Id,node.Id,revision,id);Changed(node.Id);},out var list,out var search,out var use);presenceGroundChoices=list;presenceGroundSearch=search;presenceGroundUse=use;Inspector.Children.Add(presenceGroundPanel);
    }
    void UpdatePresenceControls()
    {
        if(selectedNode==null)return;var editable=RegionEditable(selectedNode);
        if(presenceTargetRows!=null){var index=presenceTargetRows.SelectedIndex;var chosen=presenceResourceChoices?.SelectedItem is EntityChoice;presenceTargetRows.IsEnabled=editable;presenceResourceChoices!.IsEnabled=editable;presenceAdd!.IsEnabled=editable&&chosen;presenceUse!.IsEnabled=editable&&chosen&&index>=0;presenceRemove!.IsEnabled=editable&&index>=0;presenceUp!.IsEnabled=editable&&index>0;presenceDown!.IsEnabled=editable&&index>=0&&index<presenceTargetRows.Items.Count-1;}
        if(presenceGroundChoices!=null){presenceGroundChoices.IsEnabled=editable;presenceGroundUse!.IsEnabled=editable&&presenceGroundChoices.SelectedItem!=null;}
    }
}
