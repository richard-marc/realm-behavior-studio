using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    bool NavigateRuntimeSource(EncounterEvent ev)
    {
        if(ev.PacketOrigin!=null&&ev.PacketOrigin.Source==null&&ev.PacketOrigin.Timer?.SchedulingSource==null){Status.Text=ev.PacketOrigin.Timer==null?"Emitting callback construction source is unavailable.":"Timer scheduling source is unavailable.";return false;}
        if((ev.PacketOrigin?.Source??ev.PacketOrigin?.Timer?.SchedulingSource??ev.PhaseOrigin) is not {} origin)
        {
            if(ev.NodeId is not {} id)return false;
            if(document.AllNodes.Any(n=>n.Id==id)||PrimaryDocument.AllNodes.Any(n=>n.Id==id)){SelectNode(id);return true;}
            return false;
        }
        var project=Snapshot();
        if(!origin.TryResolve(project,out var accepted)){Status.Text="Recorded source has changed or is unavailable.";return false;}
        RememberSourceDraft();
        if(sourceDrafts.TryGetValue(origin.SourceFile,out var draft)&&draft!=accepted)
        {Status.Text="Apply or discard this file's draft before navigating its recorded source.";return false;}
        var primary=origin.SourceFile.Equals(project.SourceFile,StringComparison.OrdinalIgnoreCase);
        BehaviorDocument target;
        if(primary)target=PrimaryDocument;
        else if(!dependencyDocuments.TryGetValue(origin.SourceFile,out target!))
        {
            var record=project.AdditionalSources.Single(s=>s.SourceFile.Equals(origin.SourceFile,StringComparison.OrdinalIgnoreCase));
            target=new(record.Source,adapter,record.Identities,record.SourceFile);target.LockedNodes.UnionWith(record.LockedNodes);
            dependencyDocuments[record.SourceFile]=target;collapsedByDocument[target]=record.CollapsedNodes.ToHashSet();
        }
        var candidates=target.AllNodes.Where(n=>n.Kind is not ("ArgumentGroup" or "Factory" or "Reference" or "Spread" or "Opaque")&&n.Start==origin.Start&&n.Length==origin.Length).ToArray();
        var node=candidates.FirstOrDefault(n=>n.Id==origin.NodeId)??(candidates.Length==1?candidates[0]:null);
        var owner=node==null?null:target.AllRoots.First(r=>r.Root.Descendants().Any(n=>n.Id==node.Id));
        if(node!=null&&owner!=null&&(owner.EntityId==selectedEntity!.Id||owner.Definition?.Encounters.Contains(selectedEntity.Id)==true))
        {
            if(primary)LeaveExternalDefinition();else{encounterDocument??=document;document=target;}
            definitionViewKey=owner.Definition==null?null:owner.EntityId;
            WorkspaceTabs.SelectedIndex=1;RefreshDocument(node.Id);return true;
        }
        // Generated construction sites and other actors retain exact source navigation without changing the encounter.
        WorkspaceTabs.SelectedItem=SourceWorkspace;
        SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(s=>s.Path.Equals(origin.SourceFile,StringComparison.OrdinalIgnoreCase));
        SourceEditor.Select(origin.Start,origin.Length);WorkspaceTabs.UpdateLayout();SourceEditor.UpdateLayout();
        var line=accepted[..origin.Start].Count(c=>c=='\n');
        SourceEditor.Focus();SourceEditor.ScrollToLine(line);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
        {
            if(displayedSourceFile==origin.SourceFile&&SourceEditor.Text==accepted&&SourceEditor.SelectionStart==origin.Start)
            {SourceEditor.UpdateLayout();SourceEditor.ScrollToLine(line);}
        }));return true;
    }
}
