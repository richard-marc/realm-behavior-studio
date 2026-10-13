using System.Windows;
using System.Windows.Controls;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    TextBox? extractNameInput;ComboBox? extractDestination;Button? extractApplyButton;
    void AddExtractionControls(Studio.Core.BehaviorNode node,StackPanel panel)
    {
        extractNameInput=null;extractDestination=null;extractApplyButton=null;
        if(node.Kind is "Spread" or "Opaque"||node.Kind=="ArgumentGroup"&&(node.ArgumentFromCall||node.ArgumentRequiresSource||node.Argument?.IsMember==true))return;
        var form=new StackPanel();form.Children.Add(new TextBlock{Text="Move this expression into a reusable helper. Its phase names and native hierarchy stay intact.",TextWrapping=TextWrapping.Wrap,Foreground=EncounterViewport.Brush("#92969F"),FontSize=11});
        form.Children.Add(new TextBlock{Text="Helper name",Margin=new(0,8,0,4)});extractNameInput=new TextBox{Text="EncounterPart"};form.Children.Add(extractNameInput);
        form.Children.Add(new TextBlock{Text="Source file",Margin=new(0,8,0,4)});extractDestination=new ComboBox{ItemsSource=SourceFiles.ItemsSource,SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).FirstOrDefault(s=>s.Path.Equals(document.SourceFile,StringComparison.OrdinalIgnoreCase))};form.Children.Add(extractDestination);
        extractApplyButton=new Button{Content="Extract helper",Margin=new(0,8,0,0),IsEnabled=RegionEditable(node)};
        extractApplyButton.Click+=(_,_)=>Try(()=>ExtractSelectedSource());form.Children.Add(extractApplyButton);
        panel.Children.Add(new Expander{Header="Extract helper",Content=form,Margin=new(0,10,0,0)});
    }
    void ExtractSelectedSource()
    {
        if(selectedNode==null||extractNameInput==null||extractDestination?.SelectedItem is not SourceFileChoice destination)return;
        var ids=nodeSelection.Count>1?nodeSelection.ToArray():new[]{selectedNode.Id};
        var plan=SourceDefinitionRefactor.Extract(adapter,Snapshot(),document.SourceFile!,ids,destination.Path,extractNameInput.Text);
        ApplyWorkspacePhaseEdit(plan,plan.Selection[0],"Extracted");
    }
}
