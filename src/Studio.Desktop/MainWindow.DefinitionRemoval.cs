using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    Button? removeDefinitionButton;
    void OpenDefinitionBody(string file,Guid body)
    {
        file=System.IO.Path.GetFullPath(file);var primary=PrimaryDocument;
        if(!file.Equals(primary.SourceFile,StringComparison.OrdinalIgnoreCase)&&!dependencyDocuments.ContainsKey(file))
        {
            var source=SourceDependencies().Single(s=>s.SourceFile.Equals(file,StringComparison.OrdinalIgnoreCase));var loaded=new BehaviorDocument(source.Source,adapter,source.Identities,file);loaded.LockedNodes.UnionWith(source.LockedNodes);dependencyDocuments[file]=loaded;collapsedByDocument[loaded]=source.CollapsedNodes.ToHashSet();
        }
        FocusWorkspaceSource(file,body);var definition=document.Definitions.Single(d=>d.Root.Id==body);EditDefinition(definition.EntityId);
    }
    IEnumerable<AuthoringCommand> DefinitionCommands()
    {
        foreach(var source in Snapshot().AdditionalSources.Prepend(new StudioSourceDocument(PrimaryDocument.SourceFile!,PrimaryDocument.Source){Identities=PrimaryDocument.Identities}))
        {
            var path=source.SourceFile;var editing=path.Equals(PrimaryDocument.SourceFile,StringComparison.OrdinalIgnoreCase)?PrimaryDocument:dependencyDocuments.GetValueOrDefault(path);
            if(editing==null){editing=new BehaviorDocument(source.Source,adapter,source.Identities,path);editing.LockedNodes.UnionWith(source.LockedNodes);dependencyDocuments[path]=editing;collapsedByDocument[editing]=source.CollapsedNodes.ToHashSet();}foreach(var definition in editing.Definitions)
            {var id=definition.Root.Id;yield return new("Edit definition "+definition.Definition!.Label,System.IO.Path.GetFileName(path)+" · "+definition.Definition.Encounters.Count+" known callers",()=>OpenDefinitionBody(path,id));}
        }
    }
    void AddDefinitionRemovalControls(BehaviorNode node)
    {
        removeDefinitionButton=null;var definition=document.Definitions.SingleOrDefault(d=>d.Root.Id==node.Id);if(definition==null)return;
        var panel=new StackPanel();panel.Children.Add(new TextBlock{Text="Remove this private declaration after its compiler-bound references have been removed. Referenced dependencies retain their source. One Undo restores the complete declaration and its view.",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=EncounterViewport.Brush("#979DA7"),Margin=new(0,8,0,8)});
        removeDefinitionButton=new(){Content="Remove unused definition",Style=(Style)FindResource("QuietButton"),IsEnabled=RegionEditable(node)};removeDefinitionButton.Click+=(_,_)=>Try(()=>
        {
            var plan=SourceDefinitionRemoval.Remove(adapter,Snapshot(),document.SourceFile!,node.Id);ApplyWorkspacePhaseEdit(plan,plan.Selection[0],"Removed definition");
        });panel.Children.Add(removeDefinitionButton);Inspector.Children.Add(new Expander{Header="Definition source",Content=panel,Margin=new(0,12,0,6)});
    }
}
