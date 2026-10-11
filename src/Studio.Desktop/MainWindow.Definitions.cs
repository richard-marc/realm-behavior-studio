using System.Windows;
using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    string? definitionViewKey;
    void EditDefinition(string key)
    {
        var definition=document.Definitions.Single(d=>d.EntityId==key);definitionViewKey=key;
        nodeSelection.Clear();WorkspaceTabs.SelectedIndex=1;RefreshDocument(definition.Root.Id);
    }
    void ReturnToEncounter(object sender,RoutedEventArgs e)
    {
        definitionViewKey=null;nodeSelection.Clear();RefreshDocument();
    }
    void AddDefinitionControls(BehaviorNode node)
    {
        var definitions=document.ReferencedDefinitions(node);if(definitions.Count==0)return;
        var panel=new StackPanel();
        foreach(var definition in definitions)
        {
            var button=new Button{Content="Edit "+definition.Definition!.Label,Style=(Style)FindResource("QuietButton"),Margin=new(0,6,0,0),ToolTip=definition.Definition.Boundary};
            button.Click+=(_,_)=>EditDefinition(definition.EntityId);panel.Children.Add(button);
        }
        if(definitions.Count==1)Inspector.Children.Add(panel);
        else Inspector.Children.Add(new Expander{Header="Definitions · "+definitions.Count,Content=panel,Margin=new(0,8,0,0)});
    }
}
