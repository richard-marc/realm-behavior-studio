using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? ringPatternPanel;
    bool ringEditing;Button? ringEditorButton;
    void ClearRingEditor(){ringEditing=false;Viewport.Ring=null;if(ringEditorButton!=null)ringEditorButton.Content="Edit ring layout";}
    void RefreshRingSelection()
    {if(!ringEditing)return;if(selectedNode==null||adapter.ReadRingPattern(document,selectedNode) is not {} pattern||!adapter.HasKnownProjectileOwner(document,selectedEntity!.Id,selectedNode)){ClearRingEditor();return;}Viewport.Ring=new(selectedNode.Id,document.Revision,pattern,RegionEditable(selectedNode));}
    void ApplyRingEdit(RingSelection selection,IReadOnlyDictionary<string,double> values){Try(()=>CommitRingEdit(selection,values));RefreshRingSelection();}
    void CommitRingEdit(RingSelection selection,IReadOnlyDictionary<string,double> values)
    {
        var node=document.Find(selection.NodeId);if(!adapter.IsRingPattern(document,node)||!adapter.HasKnownProjectileOwner(document,selectedEntity!.Id,node))throw new ArgumentException("Choose a ring with a known native host.");
        var allowed=node.Kind=="GapRing"?new[]{"gapAngle","slots","missing"}:new[]{"angle","spacing"};if(values.Keys.Any(key=>!allowed.Contains(key))||values.Values.Any(v=>!double.IsFinite(v)))throw new ArgumentException("Choose a native ring layout parameter.");
        document.Apply(new(selection.Revision,"Adjust ring layout",values.Select(p=>new EditOperation(selection.NodeId,p.Key,p.Value.ToString("R",System.Globalization.CultureInfo.InvariantCulture))).ToArray()),ValidateAuthoring);Changed(selection.NodeId);
    }
    void AddRingPatternControls(BehaviorNode node)
    {
        ringPatternPanel=null;ringEditorButton=null;
        if(node.Kind=="GordoRotation")
        {Inspector.Children.Add(new TextBlock{Text="Chooses a seeded rotation sign on entry. GordoSpiral and GapRing read it only from the actor's current leaf behaviors; a parent-only rotation falls back to +1.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#979DA7"),Margin=new(0,10,0,6)});return;}
        if(!adapter.IsRingPattern(document,node))return;var pattern=adapter.ReadRingPattern(document,node);var panel=new StackPanel();
        if(pattern!=null&&adapter.HasKnownProjectileOwner(document,selectedEntity!.Id,node))
        {ringEditorButton=new(){Content=ringEditing?"Close ring editor":"Edit ring layout",Style=(Style)FindResource("QuietButton"),Margin=new(0,10,0,0),IsEnabled=RegionEditable(node)};ringEditorButton.Click+=(_,_)=>{if(ringEditing){ClearRingEditor();return;}ClearViewportEditors();Stop(this,new());ringEditing=true;RefreshRingSelection();ringEditorButton!.Content="Close ring editor";WorkspaceTabs.SelectedIndex=0;UpdateLayout();Viewport.FitRing();Viewport.Focus();};Inspector.Children.Add(ringEditorButton);}
        panel.Children.Add(new TextBlock{Text=pattern==null?"This ring uses source inputs. Edit its expressions below; Play runs the original controller.":$"{pattern.Count} projectiles · {pattern.Spacing:0.###}° spacing\n{pattern.Delay} ms initial countdown · {pattern.Interval} ms interval"+(pattern.Slots is {} slots?$"\n{slots} divisions · {pattern.Missing} omitted":"")+(pattern.WarnFirstVolley?"\nNative first-volley warning enabled":""),TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,8)});
        panel.Children.Add(new TextBlock{Text="Rotation and cooldown belong to each enemy. Native ticks attempt one volley at a time and advance rotation only after a successful shot. Play applies Dazed/Weak/Stunned, seeded offsets, alternating sides and the current leaf's GordoRotation. Warning packets use the original marked ray count; continuous drift may shift the later first volley.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#979DA7")});
        ringPatternPanel=new(){Header="Ring pattern",Content=panel,Margin=new(0,10,0,6)};Inspector.Children.Add(ringPatternPanel);
    }
}
