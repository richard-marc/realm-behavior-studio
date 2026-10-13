using System.Windows.Controls;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    TextBox? cloneNameInput;Button? cloneDefinitionButton;
    void AddDefinitionCloneControls(Studio.Core.BehaviorNode node,StackPanel panel)
    {
        cloneNameInput=null;cloneDefinitionButton=null;if(node.Kind!="Factory")return;
        var content=new StackPanel();content.Children.Add(new TextBlock{Text="Create independent helper declarations and a new call beside this one. Existing calls retain their original source.",TextWrapping=System.Windows.TextWrapping.Wrap,Foreground=EncounterViewport.Brush("#92969F"),FontSize=11});
        content.Children.Add(new TextBlock{Text="Clone name",Margin=new(0,8,0,4)});cloneNameInput=new TextBox{Text="IndependentEncounterPart"};content.Children.Add(cloneNameInput);
        cloneDefinitionButton=new Button{Content="Clone definition",Margin=new(0,8,0,0),IsEnabled=RegionEditable(node)};cloneDefinitionButton.Click+=(_,_)=>Try(CloneSelectedDefinition);content.Children.Add(cloneDefinitionButton);
        panel.Children.Add(new Expander{Header="Independent clone",Content=content,Margin=new(0,10,0,0)});
    }
    void CloneSelectedDefinition()
    {
        if(selectedNode==null||cloneNameInput==null)return;
        var plan=SourcePhaseRefactor.CloneFactory(adapter,Snapshot(),document.SourceFile!,selectedNode.Id,cloneNameInput.Text);
        ApplyWorkspacePhaseEdit(plan,plan.Selection[0],"Cloned");
    }
}
