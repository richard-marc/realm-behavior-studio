using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    readonly Dictionary<string,TextBox> entryCreationInputs=[];
    readonly Dictionary<string,TextBox> entryCreationChildren=[];
    TextBox? entryCreationChild;Button? entryCreationButton;
    void AddEntryCreationControls(BehaviorNode node)
    {
        entryCreationInputs.Clear();entryCreationChildren.Clear();entryCreationChild=null;entryCreationButton=null;
        var form=new StackPanel();form.Children.Add(new TextBlock{Text="Create an entry using this Add overload. The collection controls key constraints and runtime order.",TextWrapping=System.Windows.TextWrapping.Wrap,Foreground=EncounterViewport.Brush("#92969F"),FontSize=11});
        foreach(var parameter in node.Parameters)
        {
            form.Children.Add(new TextBlock{Text=parameter.Name,Margin=new(0,8,0,4)});var input=new TextBox{Text=parameter.Expression};entryCreationInputs[parameter.Name]=input;form.Children.Add(input);
        }
        var slots=document.SchemaAt(node)!.ChildArguments(document.Expression(node));
        foreach(var slot in slots)
        {
            var child=slots.Count==1?node.Children.Single():node.Children.Single(n=>n.Argument?.Name==slot.Name).Children.Single();
            form.Children.Add(new TextBlock{Text=slot.Name+" · "+slot.ElementType,Margin=new(0,8,0,4)});
            var input=new TextBox{Text=document.Expression(child),TextWrapping=System.Windows.TextWrapping.Wrap,AcceptsReturn=true};entryCreationChildren[slot.Name]=input;form.Children.Add(input);
        }
        entryCreationChild=entryCreationChildren.Values.First();
        entryCreationButton=new Button{Content="Create entry",Margin=new(0,8,0,0),IsEnabled=RegionEditable(node)};entryCreationButton.Click+=(_,_)=>Try(()=>
        {
            var id=document.CreateInitializerEntry(node.Id,entryCreationInputs.ToDictionary(p=>p.Key,p=>p.Value.Text),entryCreationChildren.ToDictionary(p=>p.Key,p=>p.Value.Text),ValidateAuthoring);Changed(id);
        });form.Children.Add(entryCreationButton);Inspector.Children.Add(new Expander{Header="New entry",Content=form,Margin=new(0,12,0,4)});
    }
}
