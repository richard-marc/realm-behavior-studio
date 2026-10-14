using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    Expander? terrainPanel;ListBox? terrainRows,terrainSourceChoices,terrainDestinationChoices;Button? terrainAdd,terrainUse,terrainRemove,terrainUp,terrainDown,terrainDestinationUse;
    CheckBox? terrainRestore,terrainLab;Slider? terrainAge;
    bool HasTerrainControls(string name)=>terrainPanel!=null&&(name=="to"||name=="restoreOnExit"&&terrainRestore!=null||name=="allowBossLab"&&terrainLab!=null||name=="from"&&terrainRows!=null);
    void AddTerrainWaveControls(BehaviorNode node)
    {
        terrainPanel=null;terrainRows=null;terrainSourceChoices=null;terrainDestinationChoices=null;terrainAdd=terrainUse=terrainRemove=terrainUp=terrainDown=terrainDestinationUse=null;terrainRestore=null;terrainLab=null;terrainAge=null;
        if(!adapter.IsTerrainWave(document,node))return;var revision=document.Revision;var pattern=adapter.ReadTerrainWave(document,node);var body=new StackPanel();
        if(pattern!=null)
        {
            var canvas=new Canvas{Width=250,Height=130,ClipToBounds=true,Background=EncounterViewport.Brush("#141818")};body.Children.Add(canvas);var caption=new TextBlock{FontSize=11,Margin=new(0,6,0,8),Foreground=EncounterViewport.Brush("#9CAAA3")};body.Children.Add(caption);terrainAge=new Slider{Minimum=0,Maximum=(double)pattern.Delay+pattern.Duration,Value=((double)pattern.Delay+pattern.Duration)/2};body.Children.Add(terrainAge);
            void Draw()
            {
                canvas.Children.Clear();var age=(long)terrainAge.Value;var radius=RapleAdapter.TerrainWaveFrontRadius(pattern,age);var scale=54/Math.Max(1,Math.Max(pattern.Start,pattern.End));
                void Circle(double r,string color,bool dash){var line=new Ellipse{Width=r*scale*2,Height=r*scale*2,Stroke=EncounterViewport.Brush(color),StrokeThickness=1.5};if(dash)line.StrokeDashArray=new DoubleCollection{3,3};Canvas.SetLeft(line,125-r*scale);Canvas.SetTop(line,65-r*scale);canvas.Children.Add(line);}
                Circle(pattern.Start,"#59645F",true);Circle(pattern.End,"#59645F",true);Circle(radius,"#83CBB0",false);var anchor=new Ellipse{Width=5,Height=5,Fill=EncounterViewport.Brush("#DCE7E1")};Canvas.SetLeft(anchor,122.5);Canvas.SetTop(anchor,62.5);canvas.Children.Add(anchor);
                caption.Text=$"{(pattern.End>=pattern.Start?"Outward":"Inward")} · age {age} ms · radius {radius:0.##} tiles";
            }
            terrainAge.ValueChanged+=(_,_)=>Draw();Draw();
            terrainRestore=new CheckBox{Content="Restore ground on exit or death",IsChecked=pattern.Restore,Margin=new(0,10,0,0)};terrainLab=new CheckBox{Content="Enable in the test arena",IsChecked=pattern.AllowBossLab,Margin=new(0,8,0,8)};
            foreach(var (control,name) in new[]{(terrainRestore,"restoreOnExit"),(terrainLab,"allowBossLab")}){control.IsEnabled=RegionEditable(node);control.Click+=(_,_)=>Try(()=>{document.Apply(new(revision,"Set terrain wave option",[new(node.Id,name,control.IsChecked==true?"true":"false")]),ValidateAuthoring);Changed(node.Id);terrainPanel!.IsExpanded=true;});body.Children.Add(control);}
        }
        body.Children.Add(GroundConditionPicker("Destination ground · "+(pattern?.To??"source"),ground=>{adapter.SetTerrainWaveGround(document,selectedEntity!.Id,node.Id,revision,ground);Changed(node.Id);terrainPanel!.IsExpanded=true;},out var destination,out _,out var destinationUse));terrainDestinationChoices=destination;terrainDestinationUse=destinationUse;
        if(adapter.TerrainWaveGrounds(document,node) is{} sources)
        {
            terrainRows=new ListBox{ItemsSource=sources,MaxHeight=100,Margin=new(0,10,0,4)};body.Children.Add(new TextBlock{Text="Replace these ground types",Margin=new(0,10,0,0)});body.Children.Add(terrainRows);
            body.Children.Add(GroundConditionPicker("Choose source ground",ground=>Edit(PhaseChoiceEdit.Replace,ground),out var choices,out _,out var use));terrainSourceChoices=choices;terrainUse=use;var actions=new WrapPanel();body.Children.Add(actions);
            void Edit(PhaseChoiceEdit edit,string? ground=null){adapter.EditTerrainWaveGround(document,selectedEntity!.Id,node.Id,revision,edit,terrainRows!.SelectedIndex,ground);Changed(node.Id);terrainPanel!.IsExpanded=true;}
            Button Action(string text,PhaseChoiceEdit edit){var button=new Button{Content=text,Style=(Style)FindResource("QuietButton"),Margin=new(0,0,4,4)};button.Click+=(_,_)=>Try(()=>Edit(edit,(terrainSourceChoices?.SelectedItem as GroundConditionChoice)?.Resource.Id));actions.Children.Add(button);return button;}
            terrainAdd=Action("Add",PhaseChoiceEdit.Add);terrainUp=Action("Up",PhaseChoiceEdit.Up);terrainDown=Action("Down",PhaseChoiceEdit.Down);terrainRemove=Action("Remove",PhaseChoiceEdit.Remove);terrainRows.SelectionChanged+=(_,_)=>UpdateTerrainControls();choices.SelectionChanged+=(_,_)=>UpdateTerrainControls();
        }
        body.Children.Add(new TextBlock{Text="The front crosses tile centers around the original spawn point. Only matching source grounds change. Restoration uses recorded original grounds and can overwrite later edits. Wrapper callbacks control exit/death dispatch.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#939F98"),Margin=new(0,12,0,8)});
        terrainPanel=new Expander{Header="Terrain wave",Content=body,Margin=new(0,10,0,6)};Inspector.Children.Add(terrainPanel);UpdateTerrainControls();
    }
    void UpdateTerrainControls()
    {
        if(terrainPanel==null||selectedNode==null)return;var editable=RegionEditable(selectedNode);if(terrainRestore!=null)terrainRestore.IsEnabled=editable;if(terrainLab!=null)terrainLab.IsEnabled=editable;
        if(terrainDestinationChoices!=null){terrainDestinationChoices.IsEnabled=editable;terrainDestinationUse!.IsEnabled=editable&&terrainDestinationChoices.SelectedItem!=null;}
        if(terrainRows==null)return;var index=terrainRows.SelectedIndex;terrainRows.IsEnabled=editable;terrainSourceChoices!.IsEnabled=editable;terrainAdd!.IsEnabled=editable&&terrainSourceChoices.SelectedItem!=null;terrainUse!.IsEnabled=editable&&index>=0&&terrainSourceChoices.SelectedItem!=null;terrainRemove!.IsEnabled=editable&&index>=0&&terrainRows.Items.Count>1;terrainUp!.IsEnabled=editable&&index>0;terrainDown!.IsEnabled=editable&&index>=0&&index<terrainRows.Items.Count-1;
    }
}
