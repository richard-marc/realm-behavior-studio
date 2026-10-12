using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    sealed record PhaseChoiceRow(int Index,string Name,int Slots,int Total)
    {public override string ToString()=>$"{Index+1}.  {Name}   ·   {Slots}/{Total} slots";}
    Expander? phaseChoicePanel;
    ListBox? phaseChoiceRows;
    ComboBox? phaseChoiceTarget;
    Button? phaseChoiceUse,phaseChoiceAdd,phaseChoiceUp,phaseChoiceDown,phaseChoiceRemove;
    void AddPhaseChoiceControls(BehaviorNode node)
    {
        var expanded=phaseChoicePanel?.Tag is Guid previous&&previous==node.Id&&phaseChoicePanel.IsExpanded;
        phaseChoicePanel=null;phaseChoiceRows=null;phaseChoiceTarget=null;
        phaseChoiceUse=phaseChoiceAdd=phaseChoiceUp=phaseChoiceDown=phaseChoiceRemove=null;
        if(node.Kind!="TimedRandomTransition")return;
        var choices=document.PhaseChoices(node.Id);var body=new StackPanel();
        body.Children.Add(new TextBlock{Text=choices==null?"Destinations come from source. Edit that expression in Source.":"The game chooses one list entry. Repeated phase names increase its chance. Choices retain their source order.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#92969F"),Margin=new(0,8,0,8)});
        phaseChoicePanel=new Expander{Header="Phase choices"+(choices==null?" · source":" · "+choices.Count),Tag=node.Id,IsExpanded=expanded,Content=body,Margin=new(0,10,0,0)};Inspector.Children.Add(phaseChoicePanel);if(choices==null)return;
        var revision=document.Revision;
        phaseChoiceRows=new ListBox{ItemsSource=choices.Select((name,index)=>new PhaseChoiceRow(index,name,choices.Count(c=>c==name),choices.Count)).ToArray(),MaxHeight=190,Margin=new(0,0,0,8)};body.Children.Add(phaseChoiceRows);
        var phaseNames=AuthoringRoot.Descendants().Where(n=>n.Kind=="State").Select(n=>document.StateName(n.Id)).OfType<string>().Distinct().Order().ToArray();
        phaseChoiceTarget=new ComboBox{ItemsSource=phaseNames,ToolTip="Choose an existing named phase",Margin=new(0,0,0,8)};body.Children.Add(phaseChoiceTarget);
        var edits=new WrapPanel();body.Children.Add(edits);
        Button Action(string label,PhaseChoiceEdit edit)
        {
            var button=new Button{Content=label,Margin=new(0,0,4,4),Style=(Style)FindResource("QuietButton")};
            button.Click+=(_,_)=>Try(()=>{var index=(phaseChoiceRows?.SelectedItem as PhaseChoiceRow)?.Index??-1;var name=phaseChoiceTarget?.SelectedItem as string;document.EditPhaseChoice(node.Id,revision,edit,index,name,d=>adapter.Validate(d,selectedEntity!.Id));Changed(node.Id);phaseChoicePanel!.IsExpanded=true;phaseChoiceRows!.SelectedIndex=edit switch {PhaseChoiceEdit.Add=>phaseChoiceRows.Items.Count-1,PhaseChoiceEdit.Up=>index-1,PhaseChoiceEdit.Down=>index+1,_=>Math.Min(index,phaseChoiceRows.Items.Count-1)};});edits.Children.Add(button);return button;
        }
        phaseChoiceUse=Action("Use phase",PhaseChoiceEdit.Replace);phaseChoiceAdd=Action("Add",PhaseChoiceEdit.Add);phaseChoiceUp=Action("↑",PhaseChoiceEdit.Up);phaseChoiceDown=Action("↓",PhaseChoiceEdit.Down);phaseChoiceRemove=Action("Remove",PhaseChoiceEdit.Remove);
        phaseChoiceRows.SelectionChanged+=(_,_)=>{if(phaseChoiceRows.SelectedItem is PhaseChoiceRow row)phaseChoiceTarget.SelectedItem=row.Name;UpdatePhaseChoiceControls();};phaseChoiceTarget.SelectionChanged+=(_,_)=>UpdatePhaseChoiceControls();
        if(choices.Count>0)phaseChoiceRows.SelectedIndex=0;UpdatePhaseChoiceControls();
    }
    void UpdatePhaseChoiceControls()
    {
        if(phaseChoiceRows==null||selectedNode==null)return;var editable=RegionEditable(selectedNode);var index=phaseChoiceRows.SelectedIndex;var count=phaseChoiceRows.Items.Count;var target=phaseChoiceTarget?.SelectedItem is string;
        phaseChoiceRows.IsEnabled=editable;phaseChoiceTarget!.IsEnabled=editable;
        phaseChoiceUse!.IsEnabled=editable&&index>=0&&target;phaseChoiceAdd!.IsEnabled=editable&&target;
        phaseChoiceUp!.IsEnabled=editable&&index>0;phaseChoiceDown!.IsEnabled=editable&&index>=0&&index<count-1;phaseChoiceRemove!.IsEnabled=editable&&index>=0&&count>1;
    }
}
