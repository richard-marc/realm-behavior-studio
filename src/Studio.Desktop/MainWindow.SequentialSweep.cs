using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? sequentialSweepPanel;
    bool sweepEditing;
    Button? sweepEditorButton;
    void ClearSweepEditor(){sweepEditing=false;Viewport.Sweep=null;if(sweepEditorButton!=null)sweepEditorButton.Content="Edit cone & ranges";}
    void RefreshSweepSelection()
    {
        if(!sweepEditing)return;
        if(selectedNode==null||adapter.ReadSequentialSweep(document,selectedNode) is not {} pattern||!adapter.HasKnownProjectileOwner(document,selectedEntity!.Id,selectedNode)){ClearSweepEditor();return;}
        var refit=Viewport.Sweep is {} previous&&previous.Pattern.AimRange!=pattern.AimRange;
        Viewport.Sweep=new(selectedNode.Id,document.Revision,pattern,RegionEditable(selectedNode));if(refit)Viewport.FitSweep();
    }
    void ApplySweepEdit(SweepSelection selection,IReadOnlyDictionary<string,double> values){Try(()=>CommitSweepEdit(selection,values));RefreshSweepSelection();}
    void CommitSweepEdit(SweepSelection selection,IReadOnlyDictionary<string,double> values)
    {
        var node=document.Find(selection.NodeId);if(!adapter.IsSequentialSweep(document,node)||!adapter.HasKnownProjectileOwner(document,selectedEntity!.Id,node))throw new ArgumentException("Choose a sweep with a known native host before editing its diagram.");
        if(values.Keys.Any(name=>name is not ("sweepDegrees" or "triggerRange" or "aimRange"))||values.Values.Any(value=>!double.IsFinite(value)))throw new ArgumentException("Choose a native sweep cone or targeting range.");
        document.Apply(new(selection.Revision,"Adjust burst sweep",values.Select(p=>new EditOperation(selection.NodeId,p.Key,p.Value.ToString("R",System.Globalization.CultureInfo.InvariantCulture))).ToArray()),ValidateAuthoring);Changed(selection.NodeId);
    }
    void AddSequentialSweepControls(BehaviorNode node)
    {
        sequentialSweepPanel=null;sweepEditorButton=null;if(!adapter.IsSequentialSweep(document,node))return;
        var pattern=adapter.ReadSequentialSweep(document,node);var panel=new StackPanel();
        if(pattern!=null&&adapter.HasKnownProjectileOwner(document,selectedEntity!.Id,node))
        {
            sweepEditorButton=new(){Content=sweepEditing?"Close sweep editor":"Edit cone & ranges",Style=(Style)FindResource("QuietButton"),Margin=new(0,10,0,0),IsEnabled=RegionEditable(node)};
            sweepEditorButton.Click+=(_,_)=>{if(sweepEditing){ClearSweepEditor();return;}ClearViewportEditors();Stop(this,new());sweepEditing=true;RefreshSweepSelection();sweepEditorButton!.Content="Close sweep editor";WorkspaceTabs.SelectedIndex=0;UpdateLayout();Viewport.FitSweep();Viewport.Focus();Status.Text="Sweep editor · cone and native targeting ranges · Play tests the burst";};Inspector.Children.Add(sweepEditorButton);
        }
        panel.Children.Add(new TextBlock{Text=pattern==null?"The burst uses source inputs. Edit its expressions below; Play executes the native controller.":$"{pattern.Count} projectiles · {pattern.Volleys} volleys\nVolley sizes: {string.Join(" · ",pattern.VolleyCounts)}\nNative firing interval: {pattern.NativeIntervalMs} ms",TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,8)});
        panel.Children.Add(new TextBlock{Text="Activates when a player enters trigger range, then tracks that player's current position. One shot traces the sweep; larger volleys alternate full and inset fans. The last firing interval plus recovery finishes before reacquisition. Native ticks emit at most one volley each. Stun, target validity and phase changes can interrupt this sequence.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#979DA7")});
        sequentialSweepPanel=new(){Header="Burst sequence",Content=panel,Margin=new(0,10,0,6)};Inspector.Children.Add(sequentialSweepPanel);
    }
}
