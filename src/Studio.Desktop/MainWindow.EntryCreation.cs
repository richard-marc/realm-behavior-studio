using System.Windows.Controls;
using Studio.Core;

namespace Studio.Desktop;
public partial class MainWindow
{
    readonly Dictionary<string,TextBox> entryCreationInputs=[];
    readonly Dictionary<string,TextBox> entryCreationChildren=[];
    TextBox? entryCreationChild;Button? entryCreationButton;
    ComboBox? entryOverloadChoices;
    void AddCollectionEntryCreationControls(BehaviorNode node)
    {
        entryOverloadChoices=null;var contracts=document.EntryOverloads(node.Id);if(contracts.Count==0)return;
        var form=new StackPanel();form.Children.Add(new TextBlock{Text="Choose this collection's Add overload and prepare its complete inputs.",TextWrapping=System.Windows.TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#92969F")});
        entryOverloadChoices=new(){ItemsSource=contracts,DisplayMemberPath="Label",Margin=new(0,8,0,8)};form.Children.Add(entryOverloadChoices);var inputs=new StackPanel();form.Children.Add(inputs);
        entryOverloadChoices.SelectionChanged+=(_,_)=>
        {
            entryCreationInputs.Clear();entryCreationChildren.Clear();entryCreationChild=null;inputs.Children.Clear();
            if(entryOverloadChoices.SelectedItem is not InitializerEntryOverload contract)return;
            foreach(var parameter in contract.Metadata.Parameters)
            {
                var child=contract.Metadata.Children.FirstOrDefault(c=>c.Name==parameter.Name);inputs.Children.Add(new TextBlock{Text=parameter.Name+" · "+parameter.Type+(parameter.Default=="required"?"":" (optional)"),Margin=new(0,8,0,4)});
                var input=new TextBox{Text=child==null?contract.Seeds[parameter.Name]:"",TextWrapping=System.Windows.TextWrapping.Wrap,AcceptsReturn=child!=null,ToolTip=parameter.Default=="required"?"Required native input.":"Blank uses the original Add default."};(child==null?entryCreationInputs:entryCreationChildren)[parameter.Name]=input;inputs.Children.Add(input);
            }
            entryCreationChild=entryCreationChildren.Values.FirstOrDefault();var revision=document.Revision;
            entryCreationButton=new(){Content="Create entry",Margin=new(0,8,0,0),IsEnabled=RegionEditable(node)};entryCreationButton.Click+=(_,_)=>Try(()=>
            {
                if(document.Revision!=revision)throw new InvalidOperationException("The source changed. Select the collection again to refresh its Add contracts.");
                var id=document.CreateCollectionEntry(node.Id,contract.Signature,entryCreationInputs.ToDictionary(p=>p.Key,p=>p.Value.Text),entryCreationChildren.ToDictionary(p=>p.Key,p=>p.Value.Text),ValidateAuthoring);Changed(id);
            });inputs.Children.Add(entryCreationButton);
        };
        entryOverloadChoices.SelectedIndex=0;Inspector.Children.Add(new Expander{Header="New entry",Content=form,Margin=new(0,12,0,4)});
    }
    void AddEntryCreationControls(BehaviorNode node)
    {
        entryCreationInputs.Clear();entryCreationChildren.Clear();entryCreationChild=null;entryCreationButton=null;
        var form=new StackPanel();form.Children.Add(new TextBlock{Text="Create an entry using this Add overload. The collection controls key constraints and runtime order.",TextWrapping=System.Windows.TextWrapping.Wrap,Foreground=EncounterViewport.Brush("#92969F"),FontSize=11});
        foreach(var parameter in node.Parameters)
        {
            form.Children.Add(new TextBlock{Text=parameter.Name,Margin=new(0,8,0,4)});var input=new TextBox{Text=parameter.Expression};entryCreationInputs[parameter.Name]=input;form.Children.Add(input);
        }
        var metadata=document.SchemaAt(node)!.DescribeExpression(document.Expression(node))!;var slots=metadata.Children;
        foreach(var slot in slots)
        {
            var container=node.Children.FirstOrDefault(n=>n.Argument?.Name==slot.Name)??node;var child=slot.IsCollection?null:container.Children.SingleOrDefault();
            form.Children.Add(new TextBlock{Text=slot.Name+" · "+slot.ElementType+(slot.IsCollection?" list":"")+(metadata.Parameters.Single(p=>p.Name==slot.Name).Default!="required"?" (optional)":""),Margin=new(0,8,0,4)});
            var input=new TextBox{Text=slot.IsCollection?(container.ArgumentFromCall?(slot.IsParams?document.SchemaAt(node)!.ChildList(document.Expression(node),slot.Name,container.Children.Select(c=>document.Expression(c)).ToArray()):""):document.Expression(container)):child==null?"":document.Expression(child),ToolTip=slot.IsCollection?"Complete native collection expression. Blank uses the default or an empty required list.":slot.Minimum==0?"Leave blank to use the native default.":"Required native expression.",TextWrapping=System.Windows.TextWrapping.Wrap,AcceptsReturn=true};entryCreationChildren[slot.Name]=input;form.Children.Add(input);
        }
        entryCreationChild=entryCreationChildren.Values.First();
        entryCreationButton=new Button{Content="Create entry",Margin=new(0,8,0,0),IsEnabled=RegionEditable(node)};entryCreationButton.Click+=(_,_)=>Try(()=>
        {
            var id=document.CreateInitializerEntry(node.Id,entryCreationInputs.ToDictionary(p=>p.Key,p=>p.Value.Text),entryCreationChildren.ToDictionary(p=>p.Key,p=>p.Value.Text),ValidateAuthoring);Changed(id);
        });form.Children.Add(entryCreationButton);Inspector.Children.Add(new Expander{Header="New entry",Content=form,Margin=new(0,12,0,4)});
    }
}
