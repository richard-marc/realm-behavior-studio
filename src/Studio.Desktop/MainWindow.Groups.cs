using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? groupPicker;TextBox? groupSearch;ListBox? groupChoices;Button? useGroupButton;TextBlock? groupInfo;ListBox? groupMembers;
    Expander? creationGroupPicker;ListBox? creationGroupChoices;
    Expander CreateGroupInputPicker(TextBox input)
    {
        var body=new StackPanel();var search=new TextBox{Tag="Search XML groups",Margin=new(0,8,0,6)};var list=new ListBox{MaxHeight=180};var info=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,8,0,0)};var use=new Button{Content="Use group",IsEnabled=false,Margin=new(0,8,0,0)};body.Children.Add(search);body.Children.Add(list);body.Children.Add(info);body.Children.Add(use);
        var picker=new Expander{Header="Choose from native XML groups",Content=body,Margin=new(0,4,0,0)};creationGroupPicker=picker;creationGroupChoices=list;var loaded=false;
        void Filter(){if(!loaded)return;list.ItemsSource=NativeGroups.Catalog().Where(g=>(g.Title+" "+g.Summary+" "+string.Join(" ",g.Members.Select(m=>m.Id))).Contains(search.Text,StringComparison.OrdinalIgnoreCase)).ToArray();}
        picker.Expanded+=(_,_)=>{loaded=true;Filter();};search.TextChanged+=(_,_)=>Filter();list.SelectionChanged+=(_,_)=>{use.IsEnabled=list.SelectedItem is NativeEntityGroup;info.Text=(list.SelectedItem as NativeEntityGroup)?.Summary??"Choose a group to inspect its native members.";};use.Click+=(_,_)=>{if(list.SelectedItem is NativeEntityGroup group){input.Text=group.Name==null?"null":Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Literal(group.Name).ToFullString();picker.IsExpanded=false;}};return picker;
    }
    void UpdateGroupControls()
    {if(useGroupButton!=null&&selectedNode!=null)useGroupButton.IsEnabled=RegionEditable(selectedNode)&&groupChoices?.SelectedItem is NativeEntityGroup group&&adapter.ReadGroupReference(document,selectedNode) is {} current&&(!current.Known||current.Name!=group.Name);}
    void AddGroupControls(BehaviorNode node)
    {
        groupPicker=null;groupSearch=null;groupChoices=null;useGroupButton=null;groupInfo=null;groupMembers=null;
        if(!adapter.IsGroupPrimitive(document,node))
        {
            if(node.Kind=="Reproduce")Inspector.Children.Add(new TextBlock{Text="Checks actual local population and reproduces only without a nearby non-Hidden player. Null children means this host's XML type. Native children give no XP and are not parent-owned.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#939F98"),Margin=new(0,10,0,6)});return;
        }
        var current=adapter.ReadGroupReference(document,node);var revision=document.Revision;var body=new StackPanel();groupSearch=new(){Tag="Search XML groups",Margin=new(0,8,0,6)};groupChoices=new(){MaxHeight=150};groupMembers=new(){MaxHeight=150};groupInfo=new(){TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#9AA99F"),Margin=new(0,8,0,6)};useGroupButton=new(){Content="Use group",Style=(Style)FindResource("QuietButton"),Margin=new(0,8,0,0)};
        var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);var art=new FrameworkElementFactory(typeof(Image));art.SetValue(FrameworkElement.WidthProperty,22d);art.SetValue(FrameworkElement.HeightProperty,22d);art.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,8,0));art.SetBinding(Image.SourceProperty,new System.Windows.Data.Binding("ObjectType"){Converter=new ObjectSpriteConverter()});row.AppendChild(art);var title=new FrameworkElementFactory(typeof(TextBlock));title.SetValue(TextBlock.FontSizeProperty,11d);title.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);title.SetValue(FrameworkElement.MaxWidthProperty,215d);title.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);title.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Id"));title.SetBinding(FrameworkElement.ToolTipProperty,new System.Windows.Data.Binding("Summary"));row.AppendChild(title);groupMembers.ItemTemplate=new(){VisualTree=row};VirtualizingPanel.SetIsVirtualizing(groupMembers,true);VirtualizingPanel.SetVirtualizationMode(groupMembers,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(groupMembers,true);
        body.Children.Add(groupSearch);body.Children.Add(groupChoices);body.Children.Add(groupInfo);body.Children.Add(groupMembers);body.Children.Add(useGroupButton);
        var list=groupChoices;var search=groupSearch;var info=groupInfo;var members=groupMembers;var loaded=false;
        void Filter(){if(!loaded)return;var previous=list.SelectedItem as NativeEntityGroup;var groups=NativeGroups.Catalog().Where(g=>(g.Title+" "+g.Summary+" "+string.Join(" ",g.Members.Select(m=>m.Id))).Contains(search.Text,StringComparison.OrdinalIgnoreCase)).ToArray();list.ItemsSource=groups;list.SelectedItem=groups.FirstOrDefault(g=>previous!=null?g.Name==previous.Name:current.Known&&g.Name==current.Name);if(list.SelectedItem==null){info.Text=groups.Length==0?"No matching XML groups.":"Select a group to inspect its native members.";members.ItemsSource=null;}else list.ScrollIntoView(list.SelectedItem);UpdateGroupControls();}
        list.SelectionChanged+=(_,_)=>
        {
            members.ItemsSource=null;if(list.SelectedItem is NativeEntityGroup group)
            {
                info.Text=group.Summary+(group.Members.Any(m=>m.RuntimeKind!="Enemy")?"\nIncludes other native object kinds; additional world context can be required.":"");
                members.ItemsSource=group.Members;
            }
            UpdateGroupControls();
        };
        search.TextChanged+=(_,_)=>Filter();useGroupButton.Click+=(_,_)=>Try(()=>{if(list.SelectedItem is NativeEntityGroup group){adapter.ApplyGroupReference(document,selectedEntity!.Id,node.Id,revision,group.Name);Changed(node.Id);}});
        body.Children.Add(new TextBlock{Text=node.Kind switch{"SpawnGroup"=>"Per-entry attempt budget. Blocked placements count; removed children do not replenish it. Positive radius samples a positive-quadrant square. Children are not parent-owned.","ReproduceGroup"=>"Replenishes below native local group density, including this host if it matches. No nearby-player gate. Region filtering retains the supplied source rule; children give no XP and are not parent-owned.",_=>"Group absence uses invariant case-insensitive matching and strict radius. A matching host counts; blank groups never trigger."},TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#83958B"),Margin=new(0,10,0,6)});
        groupPicker=new(){Header="XML group · "+(current.Known?current.Name??"null":"source expression"),Content=body,Margin=new(0,10,0,6)};groupPicker.Expanded+=(_,_)=>{if(loaded)return;loaded=true;Filter();};Inspector.Children.Add(groupPicker);
    }
}
