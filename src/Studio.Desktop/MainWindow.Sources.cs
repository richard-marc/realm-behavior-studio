using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Studio.Core;
using Studio.Raple;

namespace Studio.Desktop;
public partial class MainWindow
{
    sealed record SourceFileChoice(string Path,string Title)
    {
        public override string ToString()=>Title;
    }
    readonly Dictionary<string,StudioSourceDocument> sourceDependencies=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,Stack<StudioSourceDocument>> sourceUndo=new(StringComparer.OrdinalIgnoreCase),sourceRedo=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> sourceDrafts=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> sourceDraftBases=new(StringComparer.OrdinalIgnoreCase);
    string? displayedSourceFile;
    string displayedSource="";
    bool refreshingSourceFiles;

    List<StudioSourceDocument> SourceDependencies()=>sourceDependencies.Values.Where(s=>!s.SourceFile.Equals(selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase))
        .Select(s=>dependencyDocuments.TryGetValue(s.SourceFile,out var d)?s with{Source=d.Source,Identities=d.Identities,LockedNodes=d.LockedNodes.ToList(),CollapsedNodes=collapsedByDocument.GetValueOrDefault(d,[]).ToList()}:s).ToList();
    void LoadSourceDependencies(StudioProject project,RapleAdapter scoped)
    {
        workspacePhaseUndo.Clear();workspacePhaseRedo.Clear();encounterDocument=null;dependencyDocuments.Clear();sourceDependencies.Clear();sourceUndo.Clear();sourceRedo.Clear();sourceDrafts.Clear();sourceDraftBases.Clear();displayedSourceFile=null;
        foreach(var source in project.AdditionalSources)sourceDependencies.Add(source.SourceFile,source);
        foreach(var draft in project.SourceDrafts){sourceDrafts.Add(draft.SourceFile,draft.Source);sourceDraftBases.Add(draft.SourceFile,draft.BaseHash);}
        adapter=scoped;
    }
    List<StudioSourceDraft> SourceDraftSnapshot()
    {
        RememberSourceDraft();
        var sources=SourceDependencies().Prepend(new(selectedEntity!.SourceFile,PrimaryDocument.Source)).ToDictionary(s=>s.SourceFile,s=>s.Source,StringComparer.OrdinalIgnoreCase);
        return sourceDrafts.Where(p=>sources.TryGetValue(p.Key,out var accepted)&&p.Value!=accepted)
            .Select(p=>new StudioSourceDraft(p.Key,p.Value,sourceDraftBases.GetValueOrDefault(p.Key,StudioSourceDraft.Hash(sources[p.Key])))).ToList();
    }
    void RebindSourceWorkspace()
    {
        if(sourceDependencies.Count==0&&!adapter.HasAdditionalSources)return;
        adapter=adapter.ForProject(Snapshot());
        PrimaryDocument.RebindSchema(adapter);
        if(encounterDocument!=null)document.RebindSchema(adapter);
    }
    void RememberSourceDraft()
    {
        if(displayedSourceFile==null)return;
        if(SourceEditor.Text==displayedSource){sourceDrafts.Remove(displayedSourceFile);sourceDraftBases.Remove(displayedSourceFile);return;}
        if(!sourceDrafts.ContainsKey(displayedSourceFile))sourceDraftBases[displayedSourceFile]=StudioSourceDraft.Hash(displayedSource);
        sourceDrafts[displayedSourceFile]=SourceEditor.Text;
    }
    void RefreshSourceFiles()
    {
        RememberSourceDraft();refreshingSourceFiles=true;
        var path=displayedSourceFile??selectedEntity!.SourceFile;
        var choices=new[]{selectedEntity!.SourceFile}.Concat(SourceDependencies().Select(s=>s.SourceFile))
            .Select(p=>new SourceFileChoice(p,Path.GetRelativePath(Path.Combine(adapter.ServerRoot,"TKR.WorldServer"),p))).ToArray();
        SourceFiles.ItemsSource=choices;SourceFiles.SelectedItem=choices.FirstOrDefault(c=>c.Path.Equals(path,StringComparison.OrdinalIgnoreCase))??choices[0];
        refreshingSourceFiles=false;DisplaySourceFile();
    }
    void SourceFileSelected(object sender,SelectionChangedEventArgs e)
    {
        if(refreshingSourceFiles||SourceFiles.SelectedItem==null)return;
        RememberSourceDraft();DisplaySourceFile();
    }
    void ShowEncounterSource()
    {
        WorkspaceTabs.SelectedItem=SourceWorkspace;
        SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(c=>c.Path.Equals(selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase));
    }
    void DisplaySourceFile()
    {
        if(SourceFiles.SelectedItem is not SourceFileChoice choice)return;
        displayedSourceFile=choice.Path;
        var primary=choice.Path.Equals(selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase);
        displayedSource=primary?PrimaryDocument.Source:SourceDependencies().Single(s=>s.SourceFile.Equals(choice.Path,StringComparison.OrdinalIgnoreCase)).Source;
        SourceEditor.Text=sourceDrafts.GetValueOrDefault(choice.Path,displayedSource);
        UpdateSourceDraftNotice();
    }
    void SourceDraftChanged(object sender,TextChangedEventArgs e)=>UpdateSourceDraftNotice();
    void UpdateSourceDraftNotice()
    {
        if(SourceFileNotice==null||DiscardSourceDraftButton==null||displayedSourceFile==null)return;
        var dirty=SourceEditor.Text!=displayedSource;
        DiscardSourceDraftButton.Visibility=dirty?Visibility.Visible:Visibility.Collapsed;
        var stale=dirty&&sourceDraftBases.TryGetValue(displayedSourceFile,out var hash)&&hash!=StudioSourceDraft.Hash(displayedSource);
        SourceFileNotice.Text=dirty?(stale?"Draft · accepted source changed since this draft began. Review before Apply.":"Draft · saved with your project and recovery. Apply to use in Play or export."):
            "Accepted source · Play and export use this version. Apply checks all project files.";
    }
    void DiscardSourceDraft(object sender,RoutedEventArgs e)
    {
        if(displayedSourceFile==null)return;
        sourceDrafts.Remove(displayedSourceFile);sourceDraftBases.Remove(displayedSourceFile);SourceEditor.Text=displayedSource;UpdateSourceDraftNotice();
    }
    void AddSourceFile(object sender,RoutedEventArgs e)=>Try(()=>
    {
        var dialog=new OpenFileDialog{Filter="Server C# source|*.cs",InitialDirectory=Path.Combine(adapter.ServerRoot,"TKR.WorldServer/logic")};
        if(dialog.ShowDialog()==true)AttachSourceFile(dialog.FileName);
    });
    void AttachSourceFile(string path)
    {
        path=Path.GetFullPath(path);
        if(!path.Equals(selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase)&&!sourceDependencies.ContainsKey(path))
        {
            var project=Snapshot();var placeholder=new StudioSourceDocument(path,"");
            SourceVerification.SourceSet(adapter.ServerRoot,project with{AdditionalSources=[..project.AdditionalSources,placeholder]});
            var source=placeholder with{Source=File.ReadAllText(path)};
            ValidateSourceWorkspace(project with{AdditionalSources=[..project.AdditionalSources,source]});
            sourceDependencies.Add(path,source);scenarioRevision++;Changed();
        }
        SourceFiles.SelectedItem=((SourceFileChoice[])SourceFiles.ItemsSource).Single(s=>s.Path.Equals(path,StringComparison.OrdinalIgnoreCase));
        WorkspaceTabs.SelectedItem=SourceWorkspace;
    }
    RapleAdapter ValidateSourceWorkspace(StudioProject candidate)
    {
        var scoped=adapter.ForProject(candidate);
        var primary=new BehaviorDocument(candidate.Source,scoped,candidate.Identities,candidate.SourceFile);
        var errors=adapter.Validate(primary,candidate.EntityId).Where(d=>d.IsError).Select(d=>d.Message).Distinct().ToArray();
        if(errors.Length!=0)throw new ArgumentException(string.Join("\n",errors));
        return scoped;
    }
    StudioProject WithDependency(StudioSourceDocument replacement)=>Snapshot() with{AdditionalSources=SourceDependencies().Select(s=>s.SourceFile.Equals(replacement.SourceFile,StringComparison.OrdinalIgnoreCase)?replacement:s).ToList()};
    void ApplySourceBuffer()
    {
        if(displayedSourceFile==null||displayedSourceFile.Equals(selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase))
        {
            PrimaryDocument.ReplaceSource(SourceEditor.Text,d=>adapter.Validate(d,selectedEntity!.Id));
            sourceDrafts.Remove(selectedEntity!.SourceFile);displayedSource=SourceEditor.Text;Changed();return;
        }
        var previous=SourceDependencies().Single(s=>s.SourceFile.Equals(displayedSourceFile,StringComparison.OrdinalIgnoreCase));
        if(previous.Source==SourceEditor.Text){sourceDrafts.Remove(previous.SourceFile);sourceDraftBases.Remove(previous.SourceFile);UpdateSourceDraftNotice();return;}
        // Cross-file caller ownership is not complete yet. Do not bypass existing caller locks.
        if(PrimaryDocument.LockedNodes.Count!=0)throw new InvalidOperationException("Unlock the encounter before changing a shared source file; cross-file lock ownership is still pending.");
        var candidate=previous with{Source=SourceEditor.Text};var scoped=ValidateSourceWorkspace(WithDependency(candidate));
        var dependency=new BehaviorDocument(previous.Source,scoped,previous.Identities,previous.SourceFile);
        dependency.LockedNodes.UnionWith(previous.LockedNodes);
        dependency.ReplaceSource(candidate.Source);
        candidate=candidate with{Identities=dependency.Identities};
        if(!sourceUndo.TryGetValue(previous.SourceFile,out var history))sourceUndo[previous.SourceFile]=history=new();
        history.Push(previous);sourceRedo.Remove(previous.SourceFile);
        InstallDependencySource(candidate);sourceDrafts.Remove(previous.SourceFile);displayedSource=candidate.Source;scenarioRevision++;Changed();
    }
    bool UndoSourceBuffer(bool redo)
    {
        var file=SourceWorkspace.IsSelected?displayedSourceFile:encounterDocument!=null?document.SourceFile:displayedSourceFile;
        if(file==null||file.Equals(selectedEntity!.SourceFile,StringComparison.OrdinalIgnoreCase))return false;
        var from=redo?sourceRedo:sourceUndo;var to=redo?sourceUndo:sourceRedo;
        if(!from.TryGetValue(file,out var history)||history.Count==0)return true;
        RememberSourceDraft();if(sourceDrafts.ContainsKey(file))throw new InvalidOperationException("Apply or discard this source draft before restoring an accepted source edit.");
        var candidate=history.Peek();ValidateSourceWorkspace(WithDependency(candidate));
        if(PrimaryDocument.LockedNodes.Count!=0||SourceDependencies().Single(s=>s.SourceFile.Equals(file,StringComparison.OrdinalIgnoreCase)).LockedNodes.Count!=0)throw new InvalidOperationException("Unlock the encounter and shared source before restoring this edit.");
        if(!to.TryGetValue(file,out var other))to[file]=other=new();
        other.Push(SourceDependencies().Single(s=>s.SourceFile.Equals(file,StringComparison.OrdinalIgnoreCase)));history.Pop();InstallDependencySource(candidate);
        sourceDrafts.Remove(file);if(displayedSourceFile==file){displayedSource=candidate.Source;SourceEditor.Text=candidate.Source;}scenarioRevision++;Changed();return true;
    }
    void InstallDependencySource(StudioSourceDocument source)
    {
        sourceDependencies[source.SourceFile]=source;
        if(dependencyDocuments.TryGetValue(source.SourceFile,out var retained)&&
            (retained.Source==source.Source||retained.RestoreSourceHistory(source.Source,false)||retained.RestoreSourceHistory(source.Source,true)))
        {
            retained.RebindSchema(adapter.ForProject(WithDependency(source)));retained.LockedNodes.Clear();retained.LockedNodes.UnionWith(source.LockedNodes);collapsedByDocument[retained]=source.CollapsedNodes.ToHashSet();return;
        }
        if(!dependencyDocuments.TryGetValue(source.SourceFile,out var previous))return;
        previous.RebindSchema(adapter.ForProject(WithDependency(source)));previous.ReplaceSource(source.Source);
        previous.LockedNodes.UnionWith(source.LockedNodes);collapsedByDocument[previous]=source.CollapsedNodes.ToHashSet();
    }
    void ExportSourceWorkspace(string path,StudioProject project,NativeRuntimeStamp stamp)
    {
        if(project.AdditionalSources.Count==0)
        {
            File.WriteAllText(path,project.Source);
            File.WriteAllText(path+".verification.json",JsonSerializer.Serialize(stamp,BehaviorDocument.JsonOptions));return;
        }
        var sources=SourceVerification.SourceSet(adapter.ServerRoot,project);
        using var archive=new ZipArchive(File.Create(path),ZipArchiveMode.Create);
        foreach(var source in sources)
        {
            var entry=archive.CreateEntry(Path.GetRelativePath(Path.Combine(adapter.ServerRoot,"TKR.WorldServer"),source.Key).Replace('\\','/'));
            using var writer=new StreamWriter(entry.Open());writer.Write(source.Value);
        }
        using var manifest=new StreamWriter(archive.CreateEntry("studio-verification.json").Open());manifest.Write(JsonSerializer.Serialize(stamp,BehaviorDocument.JsonOptions));
    }
}
