using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    readonly Dictionary<string,TextBox> entryCreationInputs=[];
    TextBox? entryCreationChild;Button? entryCreationButton;
    void AddEntryCreationControls(BehaviorNode node)
    {
        entryCreationInputs.Clear();entryCreationChild=null;entryCreationButton=null;
        var form=new StackPanel();form.Children.Add(new TextBlock{Text="Create a new pair using this Add overload. The collection controls key constraints and runtime order.",TextWrapping=System.Windows.TextWrapping.Wrap,Foreground=EncounterViewport.Brush("#92969F"),FontSize=11});
        foreach(var parameter in node.Parameters)
        {
            form.Children.Add(new TextBlock{Text=parameter.Name,Margin=new(0,8,0,4)});var input=new TextBox{Text=parameter.Expression};entryCreationInputs[parameter.Name]=input;form.Children.Add(input);
        }
        form.Children.Add(new TextBlock{Text="Behavior expression",Margin=new(0,8,0,4)});entryCreationChild=new TextBox{Text=document.Expression(node.Children.Single()),TextWrapping=System.Windows.TextWrapping.Wrap,AcceptsReturn=true};form.Children.Add(entryCreationChild);
        entryCreationButton=new Button{Content="Create entry",Margin=new(0,8,0,0),IsEnabled=RegionEditable(node)};entryCreationButton.Click+=(_,_)=>Try(()=>
        {
            var id=document.CreateInitializerEntry(node.Id,entryCreationInputs.ToDictionary(p=>p.Key,p=>p.Value.Text),entryCreationChild.Text,ValidateAuthoring);Changed(id);
        });form.Children.Add(entryCreationButton);Inspector.Children.Add(new Expander{Header="New entry",Content=form,Margin=new(0,12,0,4)});
    }
}
