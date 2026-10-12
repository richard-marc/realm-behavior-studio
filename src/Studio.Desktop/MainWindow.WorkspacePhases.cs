using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    sealed record WorkspacePhaseEdit(SourcePhaseEdit Plan,IReadOnlyList<BehaviorDocument> Documents);
    readonly Stack<WorkspacePhaseEdit> workspacePhaseUndo=[],workspacePhaseRedo=[];
    void DiscardWorkspacePhaseRedo()
    {
        foreach(var editing in workspacePhaseRedo.SelectMany(e=>e.Documents).Distinct())
        {
            editing.DiscardRedoHistory();if(editing.SourceFile!=null)sourceRedo.Remove(editing.SourceFile);
        }
        workspacePhaseRedo.Clear();
    }
    void InvalidateWorkspacePhaseRedo()
    {
        if(workspacePhaseRedo.TryPeek(out var edit)&&SourceVerification.EditedSourceHash(Snapshot())!=SourceVerification.EditedSourceHash(edit.Plan.Before))DiscardWorkspacePhaseRedo();
    }
    void RenameWorkspacePhase(Guid id,string name)
    {
        var before=Snapshot();var plan=SourcePhaseRefactor.Rename(adapter,before,document.SourceFile!,id,name);
        ApplyWorkspacePhaseEdit(plan,id,"Renamed");
    }
    void DeleteWorkspacePhase(Guid id)
        =>DeleteWorkspacePhases([id]);
    void GroupWorkspacePhases(IEnumerable<Guid> ids,string name)
    {
        var selected=document.NormalizeSelection(ids);var parent=document.Parent(selected[0].Id)??throw new ArgumentException("Select child nodes to group.");
        var plan=SourcePhaseRefactor.Group(adapter,Snapshot(),document.SourceFile!,selected.Select(n=>n.Id),name,chooseUniqueName:true);
        var record=plan.After.AdditionalSources.FirstOrDefault(s=>s.SourceFile.Equals(document.SourceFile,StringComparison.OrdinalIgnoreCase));
        var grouped=new BehaviorDocument(record?.Source??plan.After.Source,adapter.ForProject(plan.After),record?.Identities??plan.After.Identities,document.SourceFile);
        var oldIds=document.Identities.Select(i=>i.Id).ToHashSet();
        var group=grouped.Find(parent.Id).Children.Single(n=>n.Kind=="State"&&!oldIds.Contains(n.Id));
        ApplyWorkspacePhaseEdit(plan,group.Id,"Grouped");
    }
    void DeleteWorkspacePhases(IEnumerable<Guid> ids)
    {
        var nodes=document.NormalizeSelection(ids);var removed=nodes.SelectMany(n=>n.Descendants()).Select(n=>n.Id).ToHashSet();
        var parent=document.Parent(nodes[0].Id);while(parent!=null&&removed.Contains(parent.Id))parent=document.Parent(parent.Id);
        var fallback=parent?.Id??throw new ArgumentException("Select child phases.");
        var plan=SourcePhaseRefactor.Delete(adapter,Snapshot(),document.SourceFile!,nodes.Select(n=>n.Id));
        ApplyWorkspacePhaseEdit(plan,fallback,"Deleted");
    }
    void ApplyWorkspacePhaseEdit(SourcePhaseEdit plan,Guid selection,string action)
    {
        var before=plan.Before;
        if(plan.Changes.Count==0)return;
        RememberSourceDraft();
        if(plan.Changes.Any(c=>sourceDrafts.TryGetValue(c.SourceFile,out var draft)&&draft!=c.Before))throw new InvalidOperationException("Apply or discard drafts in the affected source files before changing this phase.");
        var binding=adapter.ForProject(plan.After);var affected=new List<BehaviorDocument>();
        foreach(var change in plan.Changes)
        {
            BehaviorDocument editing;
            if(change.SourceFile.Equals(before.SourceFile,StringComparison.OrdinalIgnoreCase))editing=PrimaryDocument;
            else if(!dependencyDocuments.TryGetValue(change.SourceFile,out editing!))
            {
                editing=new(change.Before,adapter.ForProject(before),change.Identities,change.SourceFile);
                var record=before.AdditionalSources.FirstOrDefault(s=>s.SourceFile.Equals(change.SourceFile,StringComparison.OrdinalIgnoreCase));
                if(record!=null){editing.LockedNodes.UnionWith(record.LockedNodes);collapsedByDocument[editing]=record.CollapsedNodes.ToHashSet();}
                dependencyDocuments[change.SourceFile]=editing;
            }
            if(editing.Source!=change.Before)throw new InvalidOperationException("A source file changed during phase checking. Retry with the current revision.");
            affected.Add(editing);
        }
        var committed=new List<BehaviorDocument>();
        try
        {
            for(var index=0;index<affected.Count;index++)
            {
                var editing=affected[index];var change=plan.Changes[index];
                if(change.AfterIdentities is {} ids)editing.ApplySourceRevision(editing.Revision,change.Before,change.After,ids,binding);
                else editing.ApplyLiteralEdits(editing.Revision,change.Edits,binding);
                committed.Add(editing);
            }
        }
        catch
        {
            foreach(var editing in committed.AsEnumerable().Reverse())editing.Undo();
            var originalBinding=adapter.ForProject(before);foreach(var editing in affected)editing.RebindSchema(originalBinding);throw;
        }
        InstallPhaseWorkspace(plan.After,plan.Changes);
        DiscardWorkspacePhaseRedo();foreach(var change in plan.Changes)sourceRedo.Remove(change.SourceFile);
        workspacePhaseUndo.Push(new(plan,affected));scenarioRevision++;Changed(selection);
        Status.Text=action+" phase across "+plan.Changes.Count+" source files · Undo restores the complete edit";
    }
    void InstallPhaseWorkspace(StudioProject project,IReadOnlyList<SourcePhaseChange> changes)
    {
        adapter=adapter.ForProject(project);
        sourceDependencies.Clear();foreach(var source in project.AdditionalSources)sourceDependencies[source.SourceFile]=source;
        PrimaryDocument.RebindSchema(adapter);
        foreach(var editing in dependencyDocuments.Values)editing.RebindSchema(adapter);
        foreach(var change in changes)
        {
            var editing=change.SourceFile.Equals(project.SourceFile,StringComparison.OrdinalIgnoreCase)?PrimaryDocument:dependencyDocuments[change.SourceFile];
            var record=change.SourceFile.Equals(project.SourceFile,StringComparison.OrdinalIgnoreCase)?new StudioSourceDocument(project.SourceFile,project.Source){LockedNodes=project.LockedNodes,CollapsedNodes=project.CollapsedNodes}:project.AdditionalSources.Single(s=>s.SourceFile.Equals(change.SourceFile,StringComparison.OrdinalIgnoreCase));
            editing.LockedNodes.Clear();editing.LockedNodes.UnionWith(record.LockedNodes);
            collapsedByDocument[editing]=record.CollapsedNodes.Where(id=>editing.AllNodes.Any(n=>n.Id==id)).ToHashSet();
        }
        foreach(var change in changes)sourceDrafts.Remove(change.SourceFile);
        if(displayedSourceFile!=null){displayedSource=displayedSourceFile.Equals(project.SourceFile,StringComparison.OrdinalIgnoreCase)?project.Source:project.AdditionalSources.FirstOrDefault(s=>s.SourceFile.Equals(displayedSourceFile,StringComparison.OrdinalIgnoreCase))?.Source??"";SourceEditor.Text=sourceDrafts.GetValueOrDefault(displayedSourceFile,displayedSource);}
    }
    bool RestoreWorkspacePhase(bool redo)
    {
        var from=redo?workspacePhaseRedo:workspacePhaseUndo;var to=redo?workspacePhaseUndo:workspacePhaseRedo;
        if(from.Count==0)return false;
        var edit=from.Peek();var expected=redo?edit.Plan.Before:edit.Plan.After;var target=redo?edit.Plan.After:edit.Plan.Before;
        if(SourceVerification.EditedSourceHash(Snapshot())!=SourceVerification.EditedSourceHash(expected))return false;
        if(edit.Documents.Where((d,index)=>!d.CanRestoreSourceHistory(redo?edit.Plan.Changes[index].After:edit.Plan.Changes[index].Before,redo)).Any())
        {
            if(redo)DiscardWorkspacePhaseRedo();return false;
        }
        RememberSourceDraft();
        if(edit.Plan.Changes.Any(c=>sourceDrafts.TryGetValue(c.SourceFile,out var draft)&&draft!=(redo?c.Before:c.After)))throw new InvalidOperationException("Apply or discard drafts in the affected source files before restoring the coordinated phase edit.");
        if(edit.Documents.Any(d=>d.LockedNodes.Count>0))throw new InvalidOperationException("Unlock the affected sources before restoring the coordinated phase edit.");
        var restored=new List<BehaviorDocument>();
        try
        {
            foreach(var editing in edit.Documents)
            {
                if(!(redo?editing.Redo():editing.Undo()))throw new InvalidOperationException("The coordinated phase history is unavailable.");restored.Add(editing);
            }
        }
        catch{foreach(var editing in restored.AsEnumerable().Reverse()){if(redo)editing.Undo();else editing.Redo();}throw;}
        InstallPhaseWorkspace(target,edit.Plan.Changes);from.Pop();to.Push(edit);scenarioRevision++;Changed(selectedNode?.Id);return true;
    }
}
