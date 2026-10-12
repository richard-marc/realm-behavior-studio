using System.Windows;
using System.Windows.Controls;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    string? definitionViewKey;
    BehaviorDocument? encounterDocument;
    BehaviorDocument PrimaryDocument=>encounterDocument??document;
    readonly Dictionary<string,BehaviorDocument> dependencyDocuments=new(StringComparer.OrdinalIgnoreCase);
    void LeaveExternalDefinition()
    {
        if(encounterDocument==null)return;
        RememberSourceDraft();CaptureVisualDependencyEdit();document=encounterDocument;encounterDocument=null;definitionViewKey=null;nodeSelection.Clear();
    }
    void EditExternalDefinition(SourceDefinitionReference reference)
    {
        var path=System.IO.Path.GetFullPath(reference.SourceFile);
        if(!sourceDependencies.ContainsKey(path))AttachSourceFile(path);
        var source=SourceDependencies().Single(s=>s.SourceFile.Equals(path,StringComparison.OrdinalIgnoreCase));
        if(!dependencyDocuments.TryGetValue(path,out var dependency)||dependency.Source!=source.Source)
        {
            dependency=new(source.Source,adapter,source.Identities,path);dependency.LockedNodes.UnionWith(source.LockedNodes);
            dependencyDocuments[path]=dependency;collapsedByDocument[dependency]=source.CollapsedNodes.ToHashSet();
        }
        dependency.RebindSchema(adapter);
        sourceDependencies[path]=source with{Identities=dependency.Identities,LockedNodes=dependency.LockedNodes.ToList(),CollapsedNodes=collapsedByDocument.GetValueOrDefault(dependency,[]).ToList()};
        var definition=dependency.Definitions.Single(d=>d.Root.Start==reference.Start&&d.Root.Length==reference.Length);
        encounterDocument??=document;document=dependency;EditDefinition(definition.EntityId);
    }
    IEnumerable<DocumentDiagnostic> ValidateAuthoring(BehaviorDocument candidate)
    {
        if(encounterDocument==null)return adapter.Validate(candidate,selectedEntity!.Id);
        if(PrimaryDocument.LockedNodes.Count!=0)throw new InvalidOperationException("Unlock the encounter before editing a shared definition.");
        var project=ProjectForDocument(candidate);
        var scoped=adapter.ForProject(project);
        return scoped.Validate(new BehaviorDocument(project.Source,scoped,project.Identities,project.SourceFile),project.EntityId).Concat(adapter.Validate(candidate,project.EntityId));
    }
    StudioProject ProjectForDocument(BehaviorDocument candidate)
    {
        var project=Snapshot();
        return encounterDocument==null?project with{Source=candidate.Source,Identities=candidate.Identities}:
            project with{AdditionalSources=SourceDependencies().Select(s=>s.SourceFile.Equals(candidate.SourceFile,StringComparison.OrdinalIgnoreCase)?s with{Source=candidate.Source,Identities=candidate.Identities}:s).ToList()};
    }
    void CaptureVisualDependencyEdit()
    {
        if(encounterDocument==null||document.SourceFile==null)return;
        var previous=sourceDependencies[document.SourceFile];
        if(previous.Source!=document.Source)
        {
            if(!sourceUndo.TryGetValue(previous.SourceFile,out var history))sourceUndo[previous.SourceFile]=history=new();
            history.Push(previous);sourceRedo.Remove(previous.SourceFile);scenarioRevision++;
        }
        sourceDependencies[previous.SourceFile]=previous with{Source=document.Source,Identities=document.Identities,LockedNodes=document.LockedNodes.ToList(),CollapsedNodes=CollapsedNodes.ToList()};
    }
    void EditDefinition(string key)
    {
        var definition=document.Definitions.Single(d=>d.EntityId==key);definitionViewKey=key;
        nodeSelection.Clear();WorkspaceTabs.SelectedIndex=1;RefreshDocument(definition.Root.Id);
    }
    void ReturnToEncounter(object sender,RoutedEventArgs e)
    {
        LeaveExternalDefinition();definitionViewKey=null;nodeSelection.Clear();RefreshDocument();
    }
    void AddDefinitionControls(BehaviorNode node)
    {
        var definitions=document.ReferencedDefinitions(node);var external=document.ExternalDefinitions(node);if(definitions.Count==0&&external.Count==0)return;
        var panel=new StackPanel();
        foreach(var definition in definitions)
        {
            var button=new Button{Content="Edit "+definition.Definition!.Label,Style=(Style)FindResource("QuietButton"),Margin=new(0,6,0,0),ToolTip=definition.Definition.Boundary};
            button.Click+=(_,_)=>EditDefinition(definition.EntityId);panel.Children.Add(button);
        }
        foreach(var reference in external)
        {
            var button=new Button{Content="Edit "+reference.Label,Style=(Style)FindResource("QuietButton"),Margin=new(0,6,0,0),ToolTip=System.IO.Path.GetFileName(reference.SourceFile)};
            button.Click+=(_,_)=>Try(()=>EditExternalDefinition(reference));panel.Children.Add(button);
        }
        if(definitions.Count+external.Count==1)Inspector.Children.Add(panel);
        else Inspector.Children.Add(new Expander{Header="Definitions · "+(definitions.Count+external.Count),Content=panel,Margin=new(0,8,0,0)});
    }
}
