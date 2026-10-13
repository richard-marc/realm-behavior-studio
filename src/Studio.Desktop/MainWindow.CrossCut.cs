using System.Windows;
using System.Windows.Controls;
using Studio.Core;
namespace Studio.Desktop;
public partial class MainWindow
{
    bool crossCutEditing;Button? crossCutEditorButton;
    void ClearCrossCutEditor(){crossCutEditing=false;Viewport.CrossCut=null;if(crossCutEditorButton!=null)crossCutEditorButton.Content="Edit origin layout";}
    void RefreshCrossCutSelection(){if(!crossCutEditing)return;if(selectedNode==null||adapter.ReadCrossCut(document,selectedNode) is not{}pattern||!adapter.HasKnownProjectileOwner(document,selectedEntity!.Id,selectedNode)){ClearCrossCutEditor();return;}Viewport.CrossCut=new(selectedNode.Id,document.Revision,pattern,RegionEditable(selectedNode));}
    void ApplyCrossCutEdit(CrossCutSelection selection,IReadOnlyDictionary<string,double> values){Try(()=>CommitCrossCutEdit(selection,values));RefreshCrossCutSelection();}
    void CommitCrossCutEdit(CrossCutSelection selection,IReadOnlyDictionary<string,double> values)
    {
        var node=document.Find(selection.NodeId);if(!adapter.IsCrossCut(document,node)||!adapter.HasKnownProjectileOwner(document,selectedEntity!.Id,node)||values.Keys.Any(k=>k is not("radius" or "spacing"))||values.Values.Any(v=>!double.IsFinite(v)))throw new ArgumentException("Choose native crosscut origin radius or spacing.");
        document.Apply(new(selection.Revision,"Adjust crosscut origins",values.Select(p=>new EditOperation(node.Id,p.Key,p.Value.ToString("R",System.Globalization.CultureInfo.InvariantCulture))).ToArray()),ValidateAuthoring);Changed(node.Id);
    }
    void AddCrossCutControls(BehaviorNode node)
    {
        crossCutEditorButton=null;if(!adapter.IsCrossCut(document,node))return;
        if(adapter.ReadCrossCut(document,node)==null||!adapter.HasKnownProjectileOwner(document,selectedEntity!.Id,node)){Inspector.Children.Add(new TextBlock{Text="Source inputs define these origins. Edit the values below; Play executes the native controller.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,10,0,6)});return;}
        crossCutEditorButton=new(){Content=crossCutEditing?"Close origin editor":"Edit origin layout",Style=(Style)FindResource("QuietButton"),Margin=new(0,10,0,0),IsEnabled=RegionEditable(node)};
        crossCutEditorButton.Click+=(_,_)=>{if(crossCutEditing){ClearCrossCutEditor();return;}ClearViewportEditors();Stop(this,new());crossCutEditing=true;RefreshCrossCutSelection();crossCutEditorButton!.Content="Close origin editor";WorkspaceTabs.SelectedIndex=0;UpdateLayout();Viewport.FitCrossCut();Viewport.Focus();};Inspector.Children.Add(crossCutEditorButton);
        var body=new TextBlock{Text="Native edges: left → right → top → bottom → four diagonals. Each origin fires inward from the state-entry anchor. Suppressed origins advance the sequence; stun does not. The dashed editor limit is an origin bound, not an exported telegraph.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#939F98"),Margin=new(0,8,0,6)};
        Inspector.Children.Add(new Expander{Header="Edge sequence",Content=body,Margin=new(0,10,0,6)});
    }
}
