using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;
namespace Studio.Desktop;
public partial class MainWindow
{
    bool customMoveEditing;Button? customMoveEditorButton;ComboBox? patrolDirection;Expander? customMovePanel;
    void ClearCustomMoveEditor(){customMoveEditing=false;Viewport.CustomMove=null;if(customMoveEditorButton!=null)customMoveEditorButton.Content="Edit movement layout";}
    void RefreshCustomMoveSelection()
    {
        if(!customMoveEditing)return;if(selectedNode==null||adapter.ReadCustomMovement(document,selectedNode) is not{} p||p.Kind=="Protect"&&(p.Radius<0||p.Acquire<0||p.Arrival<0)){ClearCustomMoveEditor();return;}Viewport.CustomMove=new(selectedNode.Id,document.Revision,p,RegionEditable(selectedNode));
    }
    void ApplyCustomMoveEdit(CustomMovementSelection selection,IReadOnlyDictionary<string,double> values){Try(()=>{adapter.ApplyCustomMovement(document,selectedEntity!.Id,selection.NodeId,selection.Revision,values);Changed(selection.NodeId);});RefreshCustomMoveSelection();}
    void AddCustomMoveControls(BehaviorNode node)
    {
        customMoveEditorButton=null;patrolDirection=null;customMovePanel=null;if(!adapter.IsCustomMovement(document,node))return;var p=adapter.ReadCustomMovement(document,node);var revision=document.Revision;
        if(p!=null&&(p.Kind!="Protect"||p.Radius>=0&&p.Acquire>=0&&p.Arrival>=0))
        {
            customMoveEditorButton=new(){Content=customMoveEditing?"Close movement editor":"Edit movement layout",Style=(Style)FindResource("QuietButton"),IsEnabled=RegionEditable(node),Margin=new(0,10,0,0)};customMoveEditorButton.Click+=(_,_)=>{if(customMoveEditing){ClearCustomMoveEditor();return;}ClearViewportEditors();Stop(this,new());customMoveEditing=true;RefreshCustomMoveSelection();customMoveEditorButton!.Content="Close movement editor";WorkspaceTabs.SelectedIndex=0;UpdateLayout();Viewport.FitCustomMove();Viewport.Focus();};Inspector.Children.Add(customMoveEditorButton);
        }
        var body=new StackPanel();var text=node.Kind switch{"Protect"=>"Nearest matching XML type is selected each tick, including this host if it matches. Approach stops at reprotectRange; after arrival, protectionRange decides when to restart. Fixed steps can overshoot, and losing the target resets acquisition. This is movement rather than a defense buff or parent assignment.","BackAndForth"=>"Positive segment budgets alternate right and left. Attempted travel consumes the budget even when blocked; steps are not clamped to the segment endpoint. Negative budgets can restart leftward segments indefinitely. Zero still attempts a step. Paralysis pauses the attempted movement.",_=>"Circle uses original Enemy.SpawnPoint. Each tick steers toward the next point rather than teleporting onto the ring. Positive angle is clockwise on the screen. Native speed/tick scaling, map passability and paralysis determine actual movement."};body.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,8,0,8)});
        if(node.Kind=="PatrolCircle"&&p!=null){patrolDirection=new(){ItemsSource=new[]{"Clockwise","Counterclockwise"},SelectedIndex=p.Clockwise?0:1,IsEnabled=RegionEditable(node)};patrolDirection.SelectionChanged+=(_,_)=>Try(()=>{if(patrolDirection.SelectedIndex>=0){document.Apply(new(revision,"Set patrol direction",[new(node.Id,"clockwise",patrolDirection.SelectedIndex==0?"true":"false")]),ValidateAuthoring);Changed(node.Id);}});body.Children.Add(patrolDirection);}
        if(p==null)body.Children.Add(new TextBlock{Text="Source values define this movement. The inspector does not execute getters or factories; edit its inputs in Source.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,8,0,8)});customMovePanel=new(){Header="Native movement details",Content=body,Margin=new(0,10,0,6)};Inspector.Children.Add(customMovePanel);
    }
    void UpdateCustomMoveControls(){if(selectedNode==null)return;var editable=RegionEditable(selectedNode);if(customMoveEditorButton!=null)customMoveEditorButton.IsEnabled=editable;if(patrolDirection!=null)patrolDirection.IsEnabled=editable;RefreshCustomMoveSelection();}
}
